using DBUDoctor.Core.Scheduling;

namespace DBUDoctor.Core.Cost;

/// <summary>How a monthly figure was obtained (design D4). Only calibration produces High.</summary>
public enum Confidence
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>The cost of one resource on one side (base or head).</summary>
public sealed class CostEstimate
{
    /// <summary>The DBU SKU, such as PREMIUM_JOBS_COMPUTE. Null when the resource has no compute of its own.</summary>
    public string? Sku { get; init; }

    public decimal? PricePerDbu { get; init; }

    public decimal? DbuPerHour { get; init; }

    public decimal? DbuCostPerHour { get; init; }

    public decimal? VmCostPerHour { get; init; }

    /// <summary>Cost per active hour. Null when it cannot be computed (see <see cref="UnknownReasons"/>).</summary>
    public decimal? RatePerHour { get; init; }

    public bool Serverless { get; init; }

    public bool InfraIncluded { get; init; }

    /// <summary>The resource has no compute of its own: its tasks run on clusters, pipelines or warehouses priced as their own resources.</summary>
    public bool PricedElsewhere { get; init; }

    public RunFrequency? Frequency { get; set; }

    /// <summary>Run duration in hours, when configured.</summary>
    public decimal? HoursPerRun { get; init; }

    public decimal? Monthly { get; set; }

    /// <summary>The most a month can cost at the configured maximum size, when that differs from <see cref="Monthly"/>.</summary>
    public decimal? MonthlyCeiling { get; set; }

    public Confidence Confidence { get; set; }

    /// <summary>Why the monthly figure is missing, such as "needs history".</summary>
    public string? MonthlyNote { get; set; }

    public List<string> Assumptions { get; } = [];

    public List<string> UnknownReasons { get; } = [];

    /// <summary>The history behind a calibrated monthly figure. Null when the figure comes from configuration.</summary>
    public CalibrationInfo? History { get; set; }

    /// <summary>
    /// The rate cannot be computed for a reason the user can fix or should know about (a missing
    /// node type, price or region). Serverless jobs and pipelines without history are not unknown:
    /// their DBUs per run simply need history.
    /// </summary>
    public bool RateUnknown => UnknownReasons.Count > 0 || (RatePerHour is null && !Serverless && !PricedElsewhere);
}

/// <summary>Observed usage behind a calibrated estimate.</summary>
public sealed record CalibrationInfo(int Runs, int Days, int WindowDays, decimal? DbusPerRun, decimal MonthlyDbus);

/// <summary>Base and head estimates for one changed priced resource.</summary>
public sealed class CostComparison
{
    public required string Type { get; init; }

    public required string Key { get; init; }

    public required Diff.ChangeKind Kind { get; init; }

    public CostEstimate? Before { get; init; }

    public CostEstimate? After { get; init; }

    public required string Summary { get; init; }

    /// <summary>Both sides have a monthly figure (an absent side counts as 0).</summary>
    public bool HasMonthly =>
        (Before is null || Before.Monthly is not null) && (After is null || After.Monthly is not null) && (Before ?? After) is not null;

    public decimal? MonthlyBefore => Before is null ? 0 : Before.Monthly;

    public decimal? MonthlyAfter => After is null ? 0 : After.Monthly;

    public decimal? MonthlyDelta => HasMonthly ? MonthlyAfter - MonthlyBefore : null;

    public decimal? RateBefore => Before?.RatePerHour;

    public decimal? RateAfter => After?.RatePerHour;

    public Confidence Confidence { get; set; }

    public List<string> Notes { get; } = [];

    /// <summary>The rate is unknown on a side that exists.</summary>
    public bool HasUnknownRate => (Before?.RateUnknown ?? false) || (After?.RateUnknown ?? false);

    public IEnumerable<string> UnknownReasons =>
        (Before?.UnknownReasons ?? []).Concat(After?.UnknownReasons ?? []).Distinct(StringComparer.Ordinal);
}
