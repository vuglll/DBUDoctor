using System.Globalization;
using System.Text;
using DBUDoctor.Core.Diff;

namespace DBUDoctor.Core.Calibration;

/// <summary>A named parameter of the SQL Statement Execution API (<c>:name</c> in the statement).</summary>
public sealed record SqlParameter(string Name, string? Value, string Type = "STRING");

public sealed record SqlStatement(string Purpose, string Text, IReadOnlyList<SqlParameter> Parameters)
{
    /// <summary>A stable text for cache keys: the statement and its parameters.</summary>
    public string Fingerprint
    {
        get
        {
            var builder = new StringBuilder(Text);
            foreach (var p in Parameters)
            {
                builder.Append('\n').Append(p.Name).Append('=').Append(p.Type).Append(':').Append(p.Value);
            }

            return builder.ToString();
        }
    }
}

/// <summary>A failed statement. <see cref="ErrorCode"/> is the API's code, such as PERMISSION_DENIED.</summary>
public sealed class SqlQueryException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    /// <summary>The failure is a missing grant or table, which the user fixes with a GRANT.</summary>
    public bool IsPermission =>
        ErrorCode is "PERMISSION_DENIED" or "INSUFFICIENT_PERMISSIONS" or "TABLE_OR_VIEW_NOT_FOUND" or "SCHEMA_NOT_FOUND" or "CATALOG_NOT_FOUND" ||
        Message.Contains("PERMISSION_DENIED", StringComparison.Ordinal) ||
        Message.Contains("INSUFFICIENT_PERMISSIONS", StringComparison.Ordinal) ||
        Message.Contains("TABLE_OR_VIEW_NOT_FOUND", StringComparison.Ordinal);
}

/// <summary>Runs one read-only statement and returns its rows as strings (JSON_ARRAY results).</summary>
public interface ISqlRunner
{
    Task<IReadOnlyList<IReadOnlyList<string?>>> QueryAsync(SqlStatement statement, CancellationToken cancellationToken);
}

/// <summary>The read-only statements calibration runs (design C1). IDs are validated, then passed as parameters.</summary>
public static class UsageQueries
{
    /// <summary>
    /// Usage per resource, run and day. Columns: id, run_id, sku_name, serverless, performance_target,
    /// day, dbus. Corrections are RETRACTION rows with negative quantities, so the sum is the truth.
    /// </summary>
    public static SqlStatement Usage(string type, IReadOnlyList<string> ids, int windowDays)
    {
        var (idColumn, runColumn) = type switch
        {
            ResourceTypes.Jobs => ("usage_metadata.job_id", "usage_metadata.job_run_id"),
            ResourceTypes.Pipelines => ("usage_metadata.dlt_pipeline_id", "usage_metadata.dlt_update_id"),
            ResourceTypes.SqlWarehouses => ("usage_metadata.warehouse_id", "CAST(NULL AS STRING)"),
            ResourceTypes.Clusters => ("usage_metadata.cluster_id", "CAST(NULL AS STRING)"),
            _ => throw new ArgumentException($"No usage query for {type}", nameof(type)),
        };

        var (markers, parameters) = IdParameters(type, ids);
        parameters.Insert(0, new SqlParameter("window_days", windowDays.ToString(CultureInfo.InvariantCulture), "INT"));
        var text = $"""
            SELECT {idColumn} AS id,
                   {runColumn} AS run_id,
                   sku_name,
                   product_features.is_serverless AS serverless,
                   product_features.performance_target AS performance_target,
                   usage_date AS day,
                   SUM(usage_quantity) AS dbus
            FROM system.billing.usage
            WHERE usage_unit = 'DBU'
              AND usage_date >= date_sub(current_date(), :window_days)
              AND {idColumn} IN ({markers})
            GROUP BY ALL
            HAVING SUM(usage_quantity) <> 0
            """;
        return new SqlStatement($"usage of {type}", text, parameters);
    }

    /// <summary>Current list prices for the cloud. Columns: sku_name, price, price_start_time.</summary>
    public static SqlStatement ListPrices(string cloud) => new(
        "list prices",
        """
        SELECT sku_name, pricing.default AS price, price_start_time
        FROM system.billing.list_prices
        WHERE cloud = :cloud
          AND currency_code = 'USD'
          AND usage_unit = 'DBU'
          AND price_start_time <= current_timestamp()
          AND (price_end_time IS NULL OR price_end_time > current_timestamp())
        """,
        [new SqlParameter("cloud", cloud)]);

    /// <summary>
    /// DBUs and node-hours per job cluster of the given jobs (design C8). Columns: cluster_id,
    /// node_type, photon, dbus, node_hours.
    /// </summary>
    public static SqlStatement NodeTimeline(IReadOnlyList<string> jobIds, int windowDays)
    {
        var (markers, parameters) = IdParameters(ResourceTypes.Jobs, jobIds);
        parameters.Insert(0, new SqlParameter("window_days", windowDays.ToString(CultureInfo.InvariantCulture), "INT"));
        var text = $"""
            WITH c AS (
              SELECT usage_metadata.cluster_id AS cluster_id,
                     BOOL_OR(COALESCE(product_features.is_photon, false)) AS photon,
                     SUM(usage_quantity) AS dbus
              FROM system.billing.usage
              WHERE usage_unit = 'DBU'
                AND usage_date >= date_sub(current_date(), :window_days)
                AND usage_metadata.job_id IN ({markers})
                AND usage_metadata.cluster_id IS NOT NULL
              GROUP BY 1
              HAVING SUM(usage_quantity) <> 0
            ),
            n AS (
              SELECT cluster_id, node_type,
                     SUM(unix_timestamp(end_time) - unix_timestamp(start_time)) / 3600.0 AS node_hours
              FROM system.compute.node_timeline
              WHERE start_time >= date_sub(current_date(), :window_days)
                AND cluster_id IN (SELECT cluster_id FROM c)
              GROUP BY ALL
            )
            SELECT c.cluster_id, n.node_type, c.photon, c.dbus, n.node_hours
            FROM c JOIN n ON c.cluster_id = n.cluster_id
            """;
        return new SqlStatement("node timeline", text, parameters);
    }

    private static (string Markers, List<SqlParameter> Parameters) IdParameters(string type, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            throw new ArgumentException("At least one ID is needed.", nameof(ids));
        }

        var parameters = new List<SqlParameter>();
        var markers = new List<string>();
        for (var i = 0; i < ids.Count; i++)
        {
            if (!ResourceIds.IsValid(type, ids[i]))
            {
                throw new ArgumentException($"`{ids[i]}` is not a valid {type} ID.", nameof(ids));
            }

            parameters.Add(new SqlParameter($"id_{i}", ids[i]));
            markers.Add($":id_{i}");
        }

        return (string.Join(", ", markers), parameters);
    }

    public static decimal? Decimal(string? value) =>
        value is not null && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
