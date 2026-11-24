using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DBUDoctor.Core.Calibration;

namespace DBUDoctor.Databricks;

/// <summary>
/// Caches query results per workspace, statement and UTC day (design C4), so repeated runs on the same
/// day do not start the warehouse. Older days are deleted. The cache holds usage figures, never tokens.
/// </summary>
public sealed class CachingSqlRunner(Func<ISqlRunner> inner, string cacheDirectory, string workspaceHost, TimeProvider time) : ISqlRunner
{
    private ISqlRunner? _inner;

    public int Executed { get; private set; }

    public async Task<IReadOnlyList<IReadOnlyList<string?>>> QueryAsync(SqlStatement statement, CancellationToken cancellationToken)
    {
        var day = time.GetUtcNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var directory = Path.Combine(cacheDirectory, "calibration", day);
        var file = Path.Combine(directory, Key(statement) + ".json");
        if (Read(file) is { } cached)
        {
            return cached;
        }

        _inner ??= inner();
        var rows = await _inner.QueryAsync(statement, cancellationToken).ConfigureAwait(false);
        Executed++;
        Write(directory, file, rows);
        Prune(Path.Combine(cacheDirectory, "calibration"), day);
        return rows;
    }

    private string Key(SqlStatement statement)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(workspaceHost.ToLowerInvariant() + "\n" + statement.Fingerprint));
        return Convert.ToHexStringLower(hash)[..32];
    }

    private static IReadOnlyList<IReadOnlyList<string?>>? Read(string file)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            return document.RootElement.EnumerateArray()
                .Select(row => (IReadOnlyList<string?>)row.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Null ? null : v.GetString()).ToArray())
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void Write(string directory, string file, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        try
        {
            Directory.CreateDirectory(directory);
            using var stream = File.Create(file);
            using var w = new Utf8JsonWriter(stream);
            w.WriteStartArray();
            foreach (var row in rows)
            {
                w.WriteStartArray();
                foreach (var value in row)
                {
                    if (value is null)
                    {
                        w.WriteNullValue();
                    }
                    else
                    {
                        w.WriteStringValue(value);
                    }
                }

                w.WriteEndArray();
            }

            w.WriteEndArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cache that cannot be written only costs a query next time.
        }
    }

    private static void Prune(string root, string today)
    {
        try
        {
            foreach (var old in Directory.EnumerateDirectories(root).Where(d => string.CompareOrdinal(Path.GetFileName(d), today) < 0))
            {
                Directory.Delete(old, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
