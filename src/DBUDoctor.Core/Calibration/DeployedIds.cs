using System.Text.Json;
using DBUDoctor.Core.Diff;

namespace DBUDoctor.Core.Calibration;

/// <summary>
/// Maps bundle resource keys to deployed IDs from <c>databricks bundle summary --output json</c>
/// (design C2). The CLI writes each deployed resource's ID as a string at
/// <c>resources.&lt;type&gt;.&lt;key&gt;.id</c>; resources it has not deployed have no ID.
/// </summary>
public static class DeployedIds
{
    public static Dictionary<(string Type, string Key), string> FromSummary(string json)
    {
        var ids = new Dictionary<(string Type, string Key), string>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Object)
        {
            return ids;
        }

        foreach (var type in ResourceTypes.Priced)
        {
            if (!resources.TryGetProperty(type, out var byKey) || byKey.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var resource in byKey.EnumerateObject())
            {
                if (resource.Value.ValueKind == JsonValueKind.Object &&
                    resource.Value.TryGetProperty("id", out var id) &&
                    id.ValueKind is JsonValueKind.String or JsonValueKind.Number &&
                    (id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText()) is { Length: > 0 } value &&
                    !(resource.Value.TryGetProperty("modified_status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "created") &&
                    ResourceIds.IsValid(type, value))
                {
                    ids[(type, resource.Name)] = value;
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// The name-matching fallback for jobs: <c>usage_metadata.job_name</c> recorded for each job ID.
    /// A name that maps to exactly one ID resolves; anything else stays unresolved.
    /// </summary>
    public static SqlStatement JobsByName(IReadOnlyList<string> names, int windowDays)
    {
        var parameters = new List<SqlParameter> { new("window_days", windowDays.ToString(System.Globalization.CultureInfo.InvariantCulture), "INT") };
        var markers = new List<string>();
        for (var i = 0; i < names.Count; i++)
        {
            parameters.Add(new SqlParameter($"name_{i}", names[i]));
            markers.Add($":name_{i}");
        }

        return new SqlStatement("jobs by name", $"""
            SELECT DISTINCT usage_metadata.job_name AS name, usage_metadata.job_id AS id
            FROM system.billing.usage
            WHERE usage_date >= date_sub(current_date(), :window_days)
              AND usage_metadata.job_name IN ({string.Join(", ", markers)})
            """, parameters);
    }

    public static Dictionary<string, string> ReadJobsByName(IReadOnlyList<IReadOnlyList<string?>> rows) =>
        rows.Where(r => r.Count >= 2 && r[0] is not null && ResourceIds.IsJobId(r[1]))
            .GroupBy(r => r[0]!, StringComparer.Ordinal)
            .Where(g => g.Select(r => r[1]).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(g => g.Key, g => g.First()[1]!, StringComparer.Ordinal);
}
