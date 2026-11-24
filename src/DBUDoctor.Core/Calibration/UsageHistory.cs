using DBUDoctor.Core.Scheduling;

namespace DBUDoctor.Core.Calibration;

/// <summary>DBUs of one job run or pipeline update, summed over the usage rows of the window.</summary>
public sealed record RunUsage(string RunId, decimal Dbus);

/// <summary>What <c>system.billing.usage</c> recorded for one deployed resource in the window.</summary>
public sealed class ResourceHistory
{
    public required string Type { get; init; }

    public required string Key { get; init; }

    public required string Id { get; init; }

    public required int WindowDays { get; init; }

    public decimal TotalDbus { get; set; }

    /// <summary>Distinct UTC days with usage.</summary>
    public HashSet<DateOnly> Days { get; } = [];

    /// <summary>Jobs and pipelines: DBUs per run (job run or pipeline update).</summary>
    public Dictionary<string, decimal> Runs { get; } = new(StringComparer.Ordinal);

    /// <summary>DBUs per billing SKU name, as the workspace records it (it may carry a region suffix).</summary>
    public Dictionary<string, decimal> Skus { get; } = new(StringComparer.Ordinal);

    public bool? Serverless { get; set; }

    /// <summary><c>product_features.performance_target</c> of the most recent usage.</summary>
    public string? PerformanceTarget { get; set; }

    public int RunCount => Runs.Count(r => r.Value > 0);

    /// <summary>Median DBUs per run, over runs with usage. Null without runs.</summary>
    public decimal? MedianDbusPerRun
    {
        get
        {
            var values = Runs.Values.Where(v => v > 0).Order().ToArray();
            if (values.Length == 0)
            {
                return null;
            }

            var middle = values.Length / 2;
            return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
        }
    }

    /// <summary>The window's DBUs scaled to a 730-hour month.</summary>
    public decimal MonthlyDbus => TotalDbus * RunFrequency.HoursPerMonth / (24m * WindowDays);

    /// <summary>Observed runs scaled to a 730-hour month.</summary>
    public decimal MonthlyRuns => RunCount * RunFrequency.HoursPerMonth / (24m * WindowDays);
}

/// <summary>DBUs and node-hours of one node type on calibrated job clusters (design C8).</summary>
public sealed record NodeObservation(string NodeType, decimal Dbus, decimal NodeHours)
{
    public decimal DbuPerNodeHour => NodeHours > 0 ? Dbus / NodeHours : 0;
}

/// <summary>Everything calibration learned for one bundle. Empty when calibration is off or failed.</summary>
public sealed class UsageHistory
{
    public static UsageHistory None { get; } = new() { Enabled = false };

    public bool Enabled { get; init; } = true;

    public string? Target { get; init; }

    public int WindowDays { get; init; } = 30;

    public int MinRuns { get; init; } = 5;

    public Dictionary<(string Type, string Key), ResourceHistory> Resources { get; } = [];

    /// <summary>Resources with no deployed ID in the calibration target.</summary>
    public HashSet<(string Type, string Key)> NotFound { get; } = [];

    /// <summary>Current list price per billing SKU name, from <c>system.billing.list_prices</c>.</summary>
    public Dictionary<string, decimal> ListPrices { get; } = new(StringComparer.Ordinal);

    public List<NodeObservation> Nodes { get; } = [];

    /// <summary>Assumption lines, such as "drift check skipped".</summary>
    public List<string> Notes { get; } = [];

    /// <summary>Problems the user should fix, such as a missing grant.</summary>
    public List<string> Warnings { get; } = [];
}
