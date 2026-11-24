using System.Text.Json;

namespace DBUDoctor.Core.Diff;

/// <summary>
/// The fields whose change counts as a cost change, per resource type (design: "Cost-relevant fields").
/// A change to any other field does not. The list includes fields only later rules read, so the diff
/// does not change when those rules ship.
/// </summary>
public static class CostRelevantFields
{
    // Paths use '*' for a keyed array element and '**' for "anything below".
    private static readonly string[] ClusterFields =
    [
        "node_type_id", "driver_node_type_id", "num_workers", "autoscale.**", "runtime_engine",
        "spark_version", "instance_pool_id", "driver_instance_pool_id", "aws_attributes.**",
        "azure_attributes.**", "gcp_attributes.**", "custom_tags.**",
    ];

    private static readonly Dictionary<string, string[]> Patterns = new(StringComparer.Ordinal)
    {
        [ResourceTypes.Jobs] =
        [
            .. ClusterFields.Select(f => "job_clusters[*].new_cluster." + f),
            "tasks[*].existing_cluster_id",
            "tasks[*].job_cluster_key",
            .. ClusterFields.Select(f => "tasks[*].new_cluster." + f),
            "tasks[*].environment_key",
            "tasks[*].timeout_seconds",
            "tasks[*].pipeline_task.**",
            "schedule.**",
            "trigger.**",
            "continuous.**",
            "performance_target",
            "timeout_seconds",
            "max_concurrent_runs",
            "tags.**",
            "budget_policy_id",
        ],
        [ResourceTypes.Pipelines] =
        [
            "clusters[*].**",
            "continuous",
            "development",
            "edition",
            "photon",
            "serverless",
            "budget_policy_id",
        ],
        [ResourceTypes.Clusters] = [.. ClusterFields, "autotermination_minutes"],
        [ResourceTypes.Apps] = ["compute_size", "compute_min_instances", "compute_max_instances", "budget_policy_id", "usage_policy_id"],
        [ResourceTypes.PostgresEndpoints] = ["autoscaling_limit_min_cu", "autoscaling_limit_max_cu", "no_suspension", "suspend_timeout_duration", "disabled", "endpoint_type", "group.**"],
        [ResourceTypes.SqlWarehouses] =
        [
            "cluster_size", "min_num_clusters", "max_num_clusters", "auto_stop_mins", "warehouse_type",
            "enable_serverless_compute", "spot_instance_policy", "tags.**",
        ],
    };

    /// <summary>
    /// Flattens a resource into cost-relevant leaf paths and compact JSON values. Arrays of tasks,
    /// job clusters and pipeline clusters are keyed by <c>task_key</c>, <c>job_cluster_key</c> and
    /// <c>label</c>, so reordering them is not a change.
    /// </summary>
    public static Dictionary<string, string> Flatten(string type, JsonElement resource)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Patterns.TryGetValue(type, out var patterns))
        {
            return result;
        }

        Walk(resource, "", "", patterns, result);
        return result;
    }

    public static bool IsCostRelevant(string type, string path)
    {
        return Patterns.TryGetValue(type, out var patterns) && patterns.Any(p => Matches(p, Normalize(path)));
    }

    private static void Walk(JsonElement element, string path, string pattern, string[] patterns, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var any = false;
                foreach (var property in element.EnumerateObject())
                {
                    any = true;
                    var childPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                    var childPattern = pattern.Length == 0 ? property.Name : $"{pattern}.{property.Name}";
                    if (patterns.Any(p => CouldMatch(p, childPattern)))
                    {
                        Walk(property.Value, childPath, childPattern, patterns, result);
                    }
                }

                if (!any && patterns.Any(p => Matches(p, pattern)))
                {
                    result[path] = "{}";
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                var scalar = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        scalar = false;
                        var key = ArrayKey(item) ?? index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        Walk(item, $"{path}[{key}]", $"{pattern}[*]", patterns, result);
                    }

                    index++;
                }

                if (scalar && patterns.Any(p => Matches(p, pattern)))
                {
                    result[path] = element.GetRawText();
                }

                break;

            default:
                if (patterns.Any(p => Matches(p, pattern)))
                {
                    result[path] = element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();
                }

                break;
        }
    }

    private static string? ArrayKey(JsonElement item)
    {
        foreach (var name in new[] { "task_key", "job_cluster_key", "label", "environment_key" })
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        // Pipeline clusters without a label are the default cluster.
        return item.TryGetProperty("node_type_id", out _) || item.TryGetProperty("num_workers", out _) || item.TryGetProperty("autoscale", out _)
            ? "default"
            : null;
    }

    private static string Normalize(string path) =>
        System.Text.RegularExpressions.Regex.Replace(path, @"\[[^\]]*\]", "[*]");

    private static bool Matches(string pattern, string path)
    {
        if (pattern.EndsWith(".**", StringComparison.Ordinal))
        {
            var prefix = pattern[..^3];
            return path == prefix || path.StartsWith(prefix + ".", StringComparison.Ordinal) || path.StartsWith(prefix + "[", StringComparison.Ordinal);
        }

        return pattern == path;
    }

    /// <summary>True when <paramref name="pathPrefix"/> is on the way to, or inside, a match of <paramref name="pattern"/>.</summary>
    private static bool CouldMatch(string pattern, string pathPrefix)
    {
        if (Matches(pattern, pathPrefix))
        {
            return true;
        }

        var bare = pattern.EndsWith(".**", StringComparison.Ordinal) ? pattern[..^3] : pattern;
        return bare.StartsWith(pathPrefix + ".", StringComparison.Ordinal) ||
               bare.StartsWith(pathPrefix + "[", StringComparison.Ordinal);
    }
}
