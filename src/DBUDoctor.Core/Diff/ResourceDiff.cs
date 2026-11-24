using System.Text.Json;
using DBUDoctor.Core.Model;

namespace DBUDoctor.Core.Diff;

public enum ChangeKind
{
    Unchanged,
    Added,
    Removed,
    Modified,
}

public static class ResourceTypes
{
    public const string Jobs = "jobs";
    public const string Pipelines = "pipelines";
    public const string Clusters = "clusters";
    public const string SqlWarehouses = "sql_warehouses";

    /// <summary>The resource types this version prices, in report order (ordinal).</summary>
    public const string Apps = "apps";
    public const string PostgresEndpoints = "postgres_endpoints";

    public static readonly IReadOnlyList<string> Priced = [Apps, Clusters, Jobs, Pipelines, PostgresEndpoints, SqlWarehouses];

    public static bool IsPriced(string type) => Priced.Contains(type, StringComparer.Ordinal);

    /// <summary>Singular label for reports: job, pipeline, cluster, sql_warehouse.</summary>
    public static string Singular(string type) => type switch
    {
        Jobs => "job",
        Pipelines => "pipeline",
        Clusters => "cluster",
        SqlWarehouses => "sql_warehouse",
        Apps => "app",
        PostgresEndpoints => "postgres_endpoint",
        _ => type,
    };
}

/// <param name="Path">The field path inside the resource, such as <c>tasks[ingest].existing_cluster_id</c>.</param>
/// <param name="OldValue">The base value as compact JSON, or null when absent.</param>
/// <param name="NewValue">The head value as compact JSON, or null when absent.</param>
public sealed record FieldChange(string Path, string? OldValue, string? NewValue);

public sealed record ResourceChange(
    string Type,
    string Key,
    ChangeKind Kind,
    IReadOnlyList<FieldChange> Fields)
{
    public string Id => $"{Type}.{Key}";
}

public sealed record PossibleRename(string Type, string OldKey, string NewKey);

public sealed record UnpricedChange(string Type, string Key, ChangeKind Kind);

public sealed class BundleDiff
{
    public required IReadOnlyList<ResourceChange> Resources { get; init; }

    public required IReadOnlyList<PossibleRename> PossibleRenames { get; init; }

    public required IReadOnlyList<UnpricedChange> Unpriced { get; init; }

    public IEnumerable<ResourceChange> Changed => Resources.Where(r => r.Kind != ChangeKind.Unchanged);

    public ResourceChange? Find(string type, string key) =>
        Resources.FirstOrDefault(r => r.Type == type && r.Key == key);
}

public static class BundleDiffer
{
    public static BundleDiff Diff(ResolvedBundle baseBundle, ResolvedBundle headBundle)
    {
        var resources = new List<ResourceChange>();
        var renames = new List<PossibleRename>();
        var unpriced = new List<UnpricedChange>();

        var baseTypes = ResourceMap(baseBundle.RawResources);
        var headTypes = ResourceMap(headBundle.RawResources);

        foreach (var type in ResourceTypes.Priced)
        {
            var baseItems = baseTypes.GetValueOrDefault(type) ?? [];
            var headItems = headTypes.GetValueOrDefault(type) ?? [];
            var removed = new List<(string Key, Dictionary<string, string> Fields)>();
            var added = new List<(string Key, Dictionary<string, string> Fields)>();

            foreach (var key in baseItems.Keys.Union(headItems.Keys).Order(StringComparer.Ordinal))
            {
                var hasBase = baseItems.TryGetValue(key, out var baseElement);
                var hasHead = headItems.TryGetValue(key, out var headElement);
                var baseFields = hasBase ? CostRelevantFields.Flatten(type, baseElement) : [];
                var headFields = hasHead ? CostRelevantFields.Flatten(type, headElement) : [];

                if (!hasBase)
                {
                    resources.Add(new ResourceChange(type, key, ChangeKind.Added, Changes(baseFields, headFields)));
                    added.Add((key, headFields));
                }
                else if (!hasHead)
                {
                    resources.Add(new ResourceChange(type, key, ChangeKind.Removed, Changes(baseFields, headFields)));
                    removed.Add((key, baseFields));
                }
                else
                {
                    var changes = Changes(baseFields, headFields);
                    resources.Add(new ResourceChange(type, key, changes.Count == 0 ? ChangeKind.Unchanged : ChangeKind.Modified, changes));
                }
            }

            foreach (var (oldKey, oldFields) in removed)
            {
                foreach (var (newKey, newFields) in added)
                {
                    if (oldFields.Count > 0 && SameFields(oldFields, newFields))
                    {
                        renames.Add(new PossibleRename(type, oldKey, newKey));
                    }
                }
            }
        }

        foreach (var type in baseTypes.Keys.Union(headTypes.Keys).Where(t => !ResourceTypes.IsPriced(t)).Order(StringComparer.Ordinal))
        {
            var baseItems = baseTypes.GetValueOrDefault(type) ?? [];
            var headItems = headTypes.GetValueOrDefault(type) ?? [];
            foreach (var key in baseItems.Keys.Union(headItems.Keys).Order(StringComparer.Ordinal))
            {
                var hasBase = baseItems.TryGetValue(key, out var b);
                var hasHead = headItems.TryGetValue(key, out var h);
                var kind = !hasBase ? ChangeKind.Added
                    : !hasHead ? ChangeKind.Removed
                    : JsonElement.DeepEquals(b, h) ? ChangeKind.Unchanged : ChangeKind.Modified;
                if (kind != ChangeKind.Unchanged)
                {
                    unpriced.Add(new UnpricedChange(type, key, kind));
                }
            }
        }

        return new BundleDiff { Resources = resources, PossibleRenames = renames, Unpriced = unpriced };
    }

    private static Dictionary<string, Dictionary<string, JsonElement>> ResourceMap(JsonElement? resources)
    {
        var map = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        if (resources is not { ValueKind: JsonValueKind.Object } root)
        {
            return map;
        }

        foreach (var type in root.EnumerateObject())
        {
            if (type.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var items = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var item in type.Value.EnumerateObject())
            {
                items[item.Name] = item.Value;
            }

            map[type.Name] = items;
        }

        return map;
    }

    private static List<FieldChange> Changes(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var changes = new List<FieldChange>();
        foreach (var path in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            before.TryGetValue(path, out var oldValue);
            after.TryGetValue(path, out var newValue);
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                changes.Add(new FieldChange(path, oldValue, newValue));
            }
        }

        return changes;
    }

    private static bool SameFields(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);
}
