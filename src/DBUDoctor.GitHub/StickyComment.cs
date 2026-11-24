using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBUDoctor.Core;

namespace DBUDoctor.GitHub;

public sealed class IssueComment
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
}

public sealed class CommentBody
{
    [JsonPropertyName("body")] public string Body { get; set; } = "";
}

[JsonSerializable(typeof(List<IssueComment>))]
[JsonSerializable(typeof(IssueComment))]
[JsonSerializable(typeof(CommentBody))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;

public enum CommentOutcome
{
    Created,
    Updated,
    Skipped,
    WroteJobSummary,
}

/// <summary>Where to comment: the repository and pull request from the GitHub Actions environment.</summary>
public sealed record PullRequestTarget(string ApiUrl, string Repository, int Number, string? Token, string? StepSummaryPath)
{
    public static PullRequestTarget FromEnvironment(IReadOnlyDictionary<string, string?> env)
    {
        var repository = env.GetValueOrDefault("GITHUB_REPOSITORY");
        var eventPath = env.GetValueOrDefault("GITHUB_EVENT_PATH");
        if (string.IsNullOrEmpty(repository) || string.IsNullOrEmpty(eventPath) || !File.Exists(eventPath))
        {
            throw new DoctorException("--github-comment needs a GitHub Actions pull_request run (GITHUB_REPOSITORY and GITHUB_EVENT_PATH are not set).");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(eventPath));
        var root = document.RootElement;
        int? number = root.TryGetProperty("pull_request", out var pr) && pr.TryGetProperty("number", out var n) ? n.GetInt32()
            : root.TryGetProperty("number", out var n2) && n2.ValueKind == JsonValueKind.Number ? n2.GetInt32()
            : null;
        if (number is null)
        {
            throw new DoctorException("--github-comment needs a pull request event; this event has no pull request number.");
        }

        var token = env.GetValueOrDefault("DBUDOCTOR_GITHUB_TOKEN") is { Length: > 0 } t ? t : env.GetValueOrDefault("GITHUB_TOKEN");
        return new PullRequestTarget(
            env.GetValueOrDefault("GITHUB_API_URL") is { Length: > 0 } api ? api.TrimEnd('/') : "https://api.github.com",
            repository,
            number.Value,
            string.IsNullOrEmpty(token) ? null : token,
            env.GetValueOrDefault("GITHUB_STEP_SUMMARY"));
    }
}

/// <summary>
/// Keeps one pull request comment per bundle and target up to date (design D9). When the token cannot
/// write (a fork, a read-only token), the report goes to the job summary instead, without failing.
/// </summary>
public sealed class StickyComment(HttpClient http, TextWriter log)
{
    public async Task<CommentOutcome> PublishAsync(PullRequestTarget target, string marker, string markdown, bool hasChanges, bool commentOnNoChange, CancellationToken cancellationToken = default)
    {
        if (target.Token is null)
        {
            log.WriteLine("warning: no GITHUB_TOKEN; writing the report to the job summary instead of a comment.");
            return WriteSummary(target, markdown);
        }

        IssueComment? existing;
        try
        {
            existing = await FindAsync(target, marker, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            log.WriteLine($"warning: cannot read pull request comments ({(int)ex.StatusCode!}); writing the job summary instead.");
            return WriteSummary(target, markdown);
        }

        if (existing is null && !hasChanges && !commentOnNoChange)
        {
            return CommentOutcome.Skipped;
        }

        try
        {
            if (existing is not null)
            {
                await SendAsync(HttpMethod.Patch, $"{target.ApiUrl}/repos/{target.Repository}/issues/comments/{existing.Id}", target.Token, markdown, cancellationToken).ConfigureAwait(false);
                return CommentOutcome.Updated;
            }

            await SendAsync(HttpMethod.Post, $"{target.ApiUrl}/repos/{target.Repository}/issues/{target.Number}/comments", target.Token, markdown, cancellationToken).ConfigureAwait(false);
            return CommentOutcome.Created;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            log.WriteLine($"warning: the token cannot write pull request comments ({(int)ex.StatusCode!}), for example on a pull request from a fork. Writing the job summary instead. Grant `pull-requests: write` to comment.");
            return WriteSummary(target, markdown);
        }
    }

    private async Task<IssueComment?> FindAsync(PullRequestTarget target, string marker, CancellationToken cancellationToken)
    {
        for (var page = 1; page <= 50; page++)
        {
            using var request = Request(HttpMethod.Get, $"{target.ApiUrl}/repos/{target.Repository}/issues/{target.Number}/comments?per_page=100&page={page}", target.Token!);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var comments = await JsonSerializer.DeserializeAsync(stream, GitHubJsonContext.Default.ListIssueComment, cancellationToken).ConfigureAwait(false) ?? [];
            var match = comments.FirstOrDefault(c => c.Body?.Contains(marker, StringComparison.Ordinal) == true);
            if (match is not null)
            {
                return match;
            }

            if (comments.Count < 100)
            {
                return null;
            }
        }

        return null;
    }

    private async Task SendAsync(HttpMethod method, string url, string token, string markdown, CancellationToken cancellationToken)
    {
        using var request = Request(method, url, token);
        var payload = JsonSerializer.Serialize(new CommentBody { Body = markdown }, GitHubJsonContext.Default.CommentBody);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("DBUDoctor");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GitHub API returned {(int)response.StatusCode} for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}.", null, response.StatusCode);
        }
    }

    private CommentOutcome WriteSummary(PullRequestTarget target, string markdown)
    {
        if (string.IsNullOrEmpty(target.StepSummaryPath))
        {
            log.WriteLine("warning: GITHUB_STEP_SUMMARY is not set; the report was not published.");
            return CommentOutcome.Skipped;
        }

        File.AppendAllText(target.StepSummaryPath, markdown + "\n");
        return CommentOutcome.WroteJobSummary;
    }
}
