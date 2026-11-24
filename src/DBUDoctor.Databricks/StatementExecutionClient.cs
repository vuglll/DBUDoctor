using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DBUDoctor.Core.Calibration;

namespace DBUDoctor.Databricks;

/// <summary>
/// The SQL Statement Execution API (<c>/api/2.0/sql/statements</c>) with JSON_ARRAY results inline:
/// submit, poll until a terminal state, then follow result chunks. Plain <see cref="HttpClient"/> and
/// <see cref="JsonDocument"/>, so it stays AOT-safe.
/// </summary>
public sealed class StatementExecutionClient(HttpClient http, DatabricksCredentials credentials, string warehouseId) : ISqlRunner
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Scales retry delays; tests set it to zero.</summary>
    public double RetryDelayScale { get; init; } = 1;

    public async Task<IReadOnlyList<IReadOnlyList<string?>>> QueryAsync(SqlStatement statement, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + Timeout;
        using var response = await SendAsync(HttpMethod.Post, "/api/2.0/sql/statements", Body(statement), cancellationToken).ConfigureAwait(false);
        var root = response.RootElement;
        var id = root.TryGetProperty("statement_id", out var idElement) ? idElement.GetString() : null;
        var current = response;
        var owned = new List<JsonDocument>();
        try
        {
            while (true)
            {
                var state = State(current.RootElement, out var errorCode, out var message);
                switch (state)
                {
                    case "SUCCEEDED":
                        return await ReadRowsAsync(current.RootElement, owned, cancellationToken).ConfigureAwait(false);
                    case "FAILED" or "CANCELED" or "CLOSED":
                        throw new SqlQueryException(errorCode ?? state, $"{statement.Purpose}: {message ?? state}");
                }

                if (id is null)
                {
                    throw new SqlQueryException("NO_STATEMENT_ID", $"{statement.Purpose}: the API returned no statement ID.");
                }

                if (DateTimeOffset.UtcNow > deadline)
                {
                    await CancelAsync(id).ConfigureAwait(false);
                    throw new SqlQueryException("TIMEOUT", $"{statement.Purpose}: no result after {Timeout.TotalMinutes:0} minutes.");
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                current = await SendAsync(HttpMethod.Get, $"/api/2.0/sql/statements/{id}", null, cancellationToken).ConfigureAwait(false);
                owned.Add(current);
            }
        }
        finally
        {
            foreach (var document in owned)
            {
                document.Dispose();
            }
        }
    }

    private string Body(SqlStatement statement)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("warehouse_id", warehouseId);
            w.WriteString("statement", statement.Text);
            w.WriteString("wait_timeout", "30s");
            w.WriteString("on_wait_timeout", "CONTINUE");
            w.WriteString("format", "JSON_ARRAY");
            w.WriteString("disposition", "INLINE");
            w.WriteStartArray("parameters");
            foreach (var p in statement.Parameters)
            {
                w.WriteStartObject();
                w.WriteString("name", p.Name);
                if (p.Value is null)
                {
                    w.WriteNull("value");
                }
                else
                {
                    w.WriteString("value", p.Value);
                }

                w.WriteString("type", p.Type);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private async Task<IReadOnlyList<IReadOnlyList<string?>>> ReadRowsAsync(JsonElement root, List<JsonDocument> owned, CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyList<string?>>();
        var chunk = root.TryGetProperty("result", out var result) ? result : default;
        while (chunk.ValueKind == JsonValueKind.Object)
        {
            if (chunk.TryGetProperty("data_array", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in data.EnumerateArray())
                {
                    rows.Add(row.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? null : v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()).ToArray());
                }
            }

            if (!chunk.TryGetProperty("next_chunk_internal_link", out var next) || next.GetString() is not { Length: > 0 } link)
            {
                break;
            }

            var page = await SendAsync(HttpMethod.Get, link, null, cancellationToken).ConfigureAwait(false);
            owned.Add(page);
            chunk = page.RootElement;
        }

        return rows;
    }

    private static string? State(JsonElement root, out string? errorCode, out string? message)
    {
        errorCode = null;
        message = null;
        if (!root.TryGetProperty("status", out var status))
        {
            return null;
        }

        if (status.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            errorCode = error.TryGetProperty("error_code", out var code) ? code.GetString() : null;
            message = error.TryGetProperty("message", out var text) ? text.GetString() : null;
        }

        return status.TryGetProperty("state", out var state) ? state.GetString() : null;
    }

    private async Task CancelAsync(string id)
    {
        try
        {
            using var _ = await SendAsync(HttpMethod.Post, $"/api/2.0/sql/statements/{id}/cancel", "{}", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SqlQueryException or HttpRequestException)
        {
            // Best effort: the statement times out on the server anyway.
        }
    }

    /// <summary>Sends with retries on 429 and 5xx. Other HTTP errors become <see cref="SqlQueryException"/>.</summary>
    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, credentials.Host + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.Token);
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return JsonDocument.Parse(text);
            }

            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            if (retryable && attempt < 4)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(TimeSpan.FromMilliseconds(delay.TotalMilliseconds * RetryDelayScale), cancellationToken).ConfigureAwait(false);
                continue;
            }

            var (code, message) = ErrorBody(text);
            code ??= response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "PERMISSION_DENIED",
                HttpStatusCode.NotFound => "NOT_FOUND",
                _ => "HTTP_" + (int)response.StatusCode,
            };
            throw new SqlQueryException(code, $"HTTP {(int)response.StatusCode}: {message ?? response.ReasonPhrase}");
        }
    }

    private static (string? Code, string? Message) ErrorBody(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            return (
                root.TryGetProperty("error_code", out var code) ? code.GetString() : null,
                root.TryGetProperty("message", out var message) ? message.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
