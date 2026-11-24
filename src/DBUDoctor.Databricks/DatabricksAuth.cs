using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DBUDoctor.Databricks;

/// <summary>A workspace URL and a bearer token. The token is never logged or printed.</summary>
public sealed record DatabricksCredentials(string Host, string Token)
{
    public override string ToString() => $"DatabricksCredentials {{ Host = {Host} }}";
}

/// <summary>Calibration cannot reach the workspace; the run continues without history.</summary>
public sealed class CalibrationUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Workspace auth for calibration (design C5), in order: a personal access token in
/// <c>DATABRICKS_TOKEN</c>; token federation (<c>DATABRICKS_AUTH_TYPE</c> <c>github-oidc</c> or
/// <c>env-oidc</c> with <c>DATABRICKS_CLIENT_ID</c>); OAuth M2M with <c>DATABRICKS_CLIENT_ID</c> and
/// <c>DATABRICKS_CLIENT_SECRET</c>; then the Databricks CLI's own auth through <c>databricks auth token</c>.
/// </summary>
public static class DatabricksAuth
{
    public static async Task<DatabricksCredentials> ResolveAsync(
        string? bundleHost,
        IReadOnlyDictionary<string, string?> env,
        HttpClient http,
        IProcessRunner runner,
        string? cliExecutable,
        CancellationToken cancellationToken)
    {
        var host = Normalize(Get(env, "DATABRICKS_HOST") ?? bundleHost)
            ?? throw new CalibrationUnavailableException("no workspace host: set DATABRICKS_HOST or `workspace.host` in the bundle");

        if (Get(env, "DATABRICKS_TOKEN") is { } token)
        {
            return new DatabricksCredentials(host, token);
        }

        var authType = Get(env, "DATABRICKS_AUTH_TYPE")?.ToLowerInvariant();
        if (authType is "github-oidc" or "env-oidc")
        {
            var federated = authType == "github-oidc"
                ? await GitHubTokenAsync(http, env, cancellationToken).ConfigureAwait(false)
                : Get(env, Get(env, "DATABRICKS_OIDC_TOKEN_ENV") ?? "DATABRICKS_OIDC_TOKEN")
                    ?? throw new CalibrationUnavailableException("DATABRICKS_AUTH_TYPE is env-oidc but DATABRICKS_OIDC_TOKEN is not set");
            return new DatabricksCredentials(host, await ExchangeAsync(http, host, federated, Get(env, "DATABRICKS_CLIENT_ID"), cancellationToken).ConfigureAwait(false));
        }

        if (Get(env, "DATABRICKS_CLIENT_ID") is { } clientId && Get(env, "DATABRICKS_CLIENT_SECRET") is { } secret)
        {
            return new DatabricksCredentials(host, await ClientCredentialsAsync(http, host, clientId, secret, cancellationToken).ConfigureAwait(false));
        }

        if (cliExecutable is not null)
        {
            var arguments = new List<string> { "auth", "token", "--host", host };
            if (Get(env, "DATABRICKS_CONFIG_PROFILE") is { } profile)
            {
                arguments = ["auth", "token", "--profile", profile];
            }

            ProcessResult result;
            try
            {
                result = runner.Run(cliExecutable, arguments, Environment.CurrentDirectory);
            }
            catch (Win32Exception ex)
            {
                throw new CalibrationUnavailableException($"could not run the Databricks CLI for auth: {ex.Message}", ex);
            }

            if (result.ExitCode == 0 && ReadAccessToken(result.StandardOutput) is { } cliToken)
            {
                return new DatabricksCredentials(host, cliToken);
            }
        }

        throw new CalibrationUnavailableException(
            "no Databricks credentials: set DATABRICKS_TOKEN, or DATABRICKS_CLIENT_ID and DATABRICKS_CLIENT_SECRET, or sign in with `databricks auth login`");
    }

    /// <summary>OAuth M2M: the workspace token endpoint with the client credentials grant.</summary>
    private static async Task<string> ClientCredentialsAsync(HttpClient http, string host, string clientId, string secret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, host + "/oidc/v1/token")
        {
            Content = new FormUrlEncodedContent([new("grant_type", "client_credentials"), new("scope", "all-apis")]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}")));
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || ReadAccessToken(body) is not { } token)
            {
                throw new CalibrationUnavailableException($"OAuth M2M sign-in to {host} failed (HTTP {(int)response.StatusCode})");
            }

            return token;
        }
        catch (HttpRequestException ex)
        {
            throw new CalibrationUnavailableException($"could not reach {host}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// The GitHub Actions OIDC token: <c>ACTIONS_ID_TOKEN_REQUEST_URL</c> with the bearer
    /// <c>ACTIONS_ID_TOKEN_REQUEST_TOKEN</c> (the workflow needs <c>permissions: id-token: write</c>).
    /// The audience must match the federation policy: <c>DATABRICKS_TOKEN_AUDIENCE</c>, else
    /// <c>DATABRICKS_ACCOUNT_ID</c> (the policy default), else GitHub's default.
    /// </summary>
    private static async Task<string> GitHubTokenAsync(HttpClient http, IReadOnlyDictionary<string, string?> env, CancellationToken cancellationToken)
    {
        if (Get(env, "ACTIONS_ID_TOKEN_REQUEST_URL") is not { } url || Get(env, "ACTIONS_ID_TOKEN_REQUEST_TOKEN") is not { } requestToken)
        {
            throw new CalibrationUnavailableException("DATABRICKS_AUTH_TYPE is github-oidc but the workflow has no OIDC token: add `permissions: id-token: write`");
        }

        if ((Get(env, "DATABRICKS_TOKEN_AUDIENCE") ?? Get(env, "DATABRICKS_ACCOUNT_ID")) is { } audience)
        {
            url += (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "audience=" + Uri.EscapeDataString(audience);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", requestToken);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = response.IsSuccessStatusCode ? JsonDocument.Parse(body) : null;
            if (document?.RootElement.TryGetProperty("value", out var value) == true && value.GetString() is { Length: > 0 } token)
            {
                return token;
            }

            throw new CalibrationUnavailableException($"GitHub did not issue an OIDC token (HTTP {(int)response.StatusCode})");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new CalibrationUnavailableException($"could not get the GitHub OIDC token: {ex.Message}", ex);
        }
    }

    /// <summary>Token federation (RFC 8693): exchange a federated JWT at the workspace <c>/oidc/v1/token</c>.</summary>
    private static async Task<string> ExchangeAsync(HttpClient http, string host, string subjectToken, string? clientId, CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "urn:ietf:params:oauth:grant-type:token-exchange"),
            new("subject_token", subjectToken),
            new("subject_token_type", "urn:ietf:params:oauth:token-type:jwt"),
            new("scope", "all-apis"),
        };
        if (clientId is not null)
        {
            // Omitted for account-wide federation policies.
            fields.Add(new("client_id", clientId));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, host + "/oidc/v1/token") { Content = new FormUrlEncodedContent(fields) };
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode && ReadAccessToken(body) is { } token
                ? token
                : throw new CalibrationUnavailableException($"token federation with {host} failed (HTTP {(int)response.StatusCode}): check the service principal's federation policy");
        }
        catch (HttpRequestException ex)
        {
            throw new CalibrationUnavailableException($"could not reach {host}: {ex.Message}", ex);
        }
    }

    internal static string? ReadAccessToken(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("access_token", out var token) && token.ValueKind == JsonValueKind.String
                ? token.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? Normalize(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var value = host.Trim().TrimEnd('/');
        return value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> env, string name) =>
        env.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
