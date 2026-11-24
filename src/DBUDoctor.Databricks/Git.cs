using System.ComponentModel;
using System.Text.Json;
using DBUDoctor.Core;

namespace DBUDoctor.Databricks;

/// <summary>The refs to compare. A null head means the current working tree.</summary>
public sealed record RefPair(string Base, string? Head, string Source);

public sealed class Git(IProcessRunner runner, string workingDirectory)
{
    public string Run(params string[] arguments)
    {
        var result = TryRun(arguments);
        if (result.ExitCode != 0)
        {
            throw new DoctorException($"`git {string.Join(' ', arguments)}` failed: {result.StandardError.Trim()}");
        }

        return result.StandardOutput.Trim();
    }

    public ProcessResult TryRun(params string[] arguments)
    {
        try
        {
            return runner.Run("git", arguments, workingDirectory);
        }
        catch (Win32Exception ex)
        {
            throw new DoctorException("`git` was not found on PATH. DBUDoctor needs git to resolve the base ref (or pass --base-json and --head-json).", ex);
        }
    }

    public string RepositoryRoot()
    {
        var result = TryRun("rev-parse", "--show-toplevel");
        return result.ExitCode == 0
            ? Path.GetFullPath(result.StandardOutput.Trim())
            : throw new DoctorException($"{workingDirectory} is not inside a git repository. Run DBUDoctor in a repository, or pass --base-json and --head-json.");
    }

    /// <summary>
    /// Base and head: <c>--base</c>/<c>--head</c> when given; in a GitHub Actions pull request, the
    /// event's base and head SHAs; otherwise the merge-base of HEAD and the remote default branch,
    /// against the working tree.
    /// </summary>
    public RefPair ResolveRefs(string? baseRef, string? headRef, IReadOnlyDictionary<string, string?> environment)
    {
        if (baseRef is not null)
        {
            return new RefPair(baseRef, headRef, "--base");
        }

        if (PullRequestShas(environment) is { } shas)
        {
            return new RefPair(shas.Base, headRef ?? shas.Head, "pull request event");
        }

        var defaultBranch = RemoteDefaultBranch();
        var mergeBase = TryRun("merge-base", "HEAD", defaultBranch);
        if (mergeBase.ExitCode != 0)
        {
            throw new DoctorException($"Could not find the merge-base of HEAD and {defaultBranch}. Pass --base <ref>.");
        }

        return new RefPair(mergeBase.StandardOutput.Trim(), headRef, $"merge-base with {defaultBranch}");
    }

    public static (string Base, string Head)? PullRequestShas(IReadOnlyDictionary<string, string?> environment)
    {
        var eventName = environment.GetValueOrDefault("GITHUB_EVENT_NAME");
        var eventPath = environment.GetValueOrDefault("GITHUB_EVENT_PATH");
        if (eventName is not ("pull_request" or "pull_request_target") || string.IsNullOrEmpty(eventPath) || !File.Exists(eventPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(eventPath));
        if (document.RootElement.TryGetProperty("pull_request", out var pr) &&
            pr.TryGetProperty("base", out var b) && b.TryGetProperty("sha", out var baseSha) &&
            pr.TryGetProperty("head", out var h) && h.TryGetProperty("sha", out var headSha))
        {
            return (baseSha.GetString()!, headSha.GetString()!);
        }

        return null;
    }

    public string RemoteDefaultBranch()
    {
        var symbolic = TryRun("symbolic-ref", "--quiet", "refs/remotes/origin/HEAD");
        if (symbolic.ExitCode == 0 && symbolic.StandardOutput.Trim() is { Length: > 0 } reference)
        {
            return reference.Replace("refs/remotes/", "", StringComparison.Ordinal);
        }

        foreach (var candidate in new[] { "origin/main", "origin/master", "main", "master" })
        {
            if (TryRun("rev-parse", "--verify", "--quiet", candidate + "^{commit}").ExitCode == 0)
            {
                return candidate;
            }
        }

        throw new DoctorException("Could not find the remote default branch (origin/HEAD, origin/main or origin/master). Pass --base <ref>.");
    }

    /// <summary>Makes sure the commit is available, fetching it from origin in a shallow clone.</summary>
    public void EnsureCommit(string reference)
    {
        if (TryRun("cat-file", "-e", reference + "^{commit}").ExitCode == 0)
        {
            return;
        }

        var fetch = TryRun("fetch", "--no-tags", "--depth=1", "origin", reference);
        if (fetch.ExitCode != 0 || TryRun("cat-file", "-e", reference + "^{commit}").ExitCode != 0)
        {
            throw new DoctorException($"The ref `{reference}` is not available and could not be fetched: {fetch.StandardError.Trim()}");
        }
    }

    /// <summary>Checks a ref out into a temporary worktree, removed again on dispose.</summary>
    public TemporaryWorktree AddWorktree(string reference)
    {
        EnsureCommit(reference);
        var path = Path.Combine(Path.GetTempPath(), "dbudoctor-" + Guid.NewGuid().ToString("N")[..12]);
        Run("worktree", "add", "--detach", "--quiet", path, reference);
        return new TemporaryWorktree(this, path);
    }
}

/// <summary>A <c>git worktree</c> that is removed on dispose, also after a failure.</summary>
public sealed class TemporaryWorktree(Git git, string path) : IDisposable
{
    public string Path { get; } = path;

    public void Dispose()
    {
        try
        {
            git.TryRun("worktree", "remove", "--force", Path);
        }
        finally
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best effort; prune below forgets the worktree either way.
            }
            catch (UnauthorizedAccessException)
            {
            }

            git.TryRun("worktree", "prune");
        }
    }
}
