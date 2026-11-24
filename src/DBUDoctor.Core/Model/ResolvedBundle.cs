using System.Text.Json;

namespace DBUDoctor.Core.Model;

public sealed record SourceLocation(string File, int Line);

/// <summary>
/// One side (base or head) of a bundle: the typed document, the raw resources for the field diff,
/// and optional source locations from <c>--include-locations</c>.
/// </summary>
public sealed class ResolvedBundle
{
    private ResolvedBundle(BundleDocument document, JsonElement? resources, IReadOnlyDictionary<string, SourceLocation> locations)
    {
        Document = document;
        RawResources = resources;
        Locations = locations;
    }

    public static ResolvedBundle Empty { get; } = new(new BundleDocument(), null, new Dictionary<string, SourceLocation>());

    public BundleDocument Document { get; }

    /// <summary>The <c>resources</c> object as JSON, or null when the bundle has none.</summary>
    public JsonElement? RawResources { get; }

    /// <summary>Source locations keyed by configuration path, such as <c>resources.jobs.orders</c>.</summary>
    public IReadOnlyDictionary<string, SourceLocation> Locations { get; }

    public bool IsEmpty => RawResources is null && Document.Bundle is null;

    public string? Name => Document.Bundle?.Name;

    public string? Target => Document.Bundle?.Target;

    public string? WorkspaceHost => Document.Workspace?.Host;

    public static ResolvedBundle Parse(string json, string sourceName)
    {
        JsonDocument? parsed = null;
        try
        {
            parsed = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new DoctorException($"{sourceName}: expected a JSON object from `databricks bundle validate --output json`.");
            }

            var document = root.Deserialize(BundleJsonContext.Default.BundleDocument) ?? new BundleDocument();
            JsonElement? resources = root.TryGetProperty("resources", out var r) && r.ValueKind == JsonValueKind.Object ? r.Clone() : null;
            var locations = root.TryGetProperty("__locations", out var l) ? ParseLocations(l) : new Dictionary<string, SourceLocation>();
            return new ResolvedBundle(document, resources, locations);
        }
        catch (JsonException ex)
        {
            // validate prints the bundle unresolved when it stops early (for example without workspace
            // auth); a `${var.x}` left in a boolean or number field then fails the typed read.
            if (parsed is not null && ex.Path is { } path && Find(parsed.RootElement, path) is { ValueKind: JsonValueKind.String } value
                && value.GetString() is { } text && text.Contains("${", StringComparison.Ordinal))
            {
                throw new DoctorException(
                    $"{sourceName}: `{path.TrimStart('$', '.')}` is the unresolved reference `{text}`. "
                    + "`databricks bundle validate` did not resolve the bundle's variables; fix the error it reports (often missing workspace authentication) and run it again.",
                    ex);
            }

            var where = ex.LineNumber is { } line ? $" (line {line + 1})" : "";
            throw new DoctorException($"{sourceName}: not valid bundle JSON{where}: {ex.Message}", ex);
        }
        finally
        {
            parsed?.Dispose();
        }
    }

    /// <summary>Follows a System.Text.Json error path such as <c>$.resources.jobs['a.b'].tasks[0].x</c>.</summary>
    internal static JsonElement? Find(JsonElement root, string path)
    {
        var current = root;
        var i = path.StartsWith('$') ? 1 : 0;
        while (i < path.Length)
        {
            string? name = null;
            int? index = null;
            if (path[i] == '.')
            {
                var end = path.IndexOfAny(['.', '['], i + 1);
                end = end < 0 ? path.Length : end;
                name = path[(i + 1)..end];
                i = end;
            }
            else if (path[i] == '[' && i + 1 < path.Length && path[i + 1] == '\'')
            {
                var end = path.IndexOf("']", i + 2, StringComparison.Ordinal);
                if (end < 0) return null;
                name = path[(i + 2)..end];
                i = end + 2;
            }
            else if (path[i] == '[')
            {
                var end = path.IndexOf(']', i + 1);
                if (end < 0 || !int.TryParse(path.AsSpan(i + 1, end - i - 1), System.Globalization.CultureInfo.InvariantCulture, out var n)) return null;
                index = n;
                i = end + 1;
            }
            else
            {
                return null;
            }

            if (name is not null)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return null;
            }
            else if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
            {
                return null;
            }
            else
            {
                current = current[index!.Value];
            }
        }

        return current;
    }

    public static ResolvedBundle Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new DoctorException($"File not found: {path}");
        }

        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>
    /// Reads the <c>__locations</c> map: <c>{ "version": 1, "files": [...], "locations": { "path": [[file, line, column], ...] } }</c>.
    /// The flag is hidden and may change, so anything unexpected yields no locations rather than an error.
    /// </summary>
    internal static Dictionary<string, SourceLocation> ParseLocations(JsonElement element)
    {
        var result = new Dictionary<string, SourceLocation>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array ||
            !element.TryGetProperty("locations", out var locations) || locations.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        var fileNames = files.EnumerateArray().Select(f => f.ValueKind == JsonValueKind.String ? f.GetString() ?? "" : "").ToArray();
        foreach (var entry in locations.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var tuple in entry.Value.EnumerateArray())
            {
                if (tuple.ValueKind == JsonValueKind.Array && tuple.GetArrayLength() >= 2 &&
                    tuple[0].TryGetInt32(out var fileIndex) && tuple[1].TryGetInt32(out var line) &&
                    fileIndex >= 0 && fileIndex < fileNames.Length)
                {
                    result[entry.Name] = new SourceLocation(fileNames[fileIndex], line);
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>The location of a path, or of its nearest ancestor that has one.</summary>
    public SourceLocation? FindLocation(string path)
    {
        var current = path;
        while (true)
        {
            if (Locations.TryGetValue(current, out var location))
            {
                return location;
            }

            var cut = Math.Max(current.LastIndexOf('.'), current.LastIndexOf('['));
            if (cut <= 0)
            {
                return null;
            }

            current = current[..cut];
        }
    }
}
