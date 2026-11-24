using System.Text.RegularExpressions;
using DBUDoctor.Core.Diff;

namespace DBUDoctor.Core.Calibration;

/// <summary>
/// Formats of deployed resource IDs (design C3). Every ID is checked before it reaches a query, even
/// though queries pass IDs as named parameters: an ID that does not look like one is never sent.
/// </summary>
public static partial class ResourceIds
{
    /// <summary>Job IDs are positive 64-bit integers.</summary>
    public static bool IsJobId(string? id) => id is not null && JobId().IsMatch(id);

    /// <summary>Pipeline IDs are UUIDs.</summary>
    public static bool IsPipelineId(string? id) => id is not null && Uuid().IsMatch(id);

    /// <summary>SQL warehouse IDs are 16 hexadecimal characters.</summary>
    public static bool IsWarehouseId(string? id) => id is not null && WarehouseId().IsMatch(id);

    /// <summary>Cluster IDs look like <c>0601-182128-dcbte59m</c>.</summary>
    public static bool IsClusterId(string? id) => id is not null && ClusterId().IsMatch(id);

    public static bool IsValid(string type, string? id) => type switch
    {
        ResourceTypes.Jobs => IsJobId(id),
        ResourceTypes.Pipelines => IsPipelineId(id),
        ResourceTypes.SqlWarehouses => IsWarehouseId(id),
        ResourceTypes.Clusters => IsClusterId(id),
        _ => false,
    };

    [GeneratedRegex(@"\A[1-9][0-9]{0,18}\z")]
    private static partial Regex JobId();

    [GeneratedRegex(@"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z")]
    private static partial Regex Uuid();

    [GeneratedRegex(@"\A[0-9a-f]{16}\z")]
    private static partial Regex WarehouseId();

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{6}-[a-z0-9]{8}\z")]
    private static partial Regex ClusterId();
}
