namespace DBUDoctor.Core.Config;

public enum Cloud
{
    Azure,
    Aws,
    Gcp,
}

public enum InfraMode
{
    Auto,
    True,
    False,
}

public enum PlanTier
{
    Premium,
    Enterprise,
}

public enum Commitment
{
    Payg,
    Spot,
    Reserved1y,
    Reserved3y,
    SavingsPlan1y,
    SavingsPlan3y,
}

public enum AutoscaleAssumption
{
    Max,
    Mid,
    Min,
}

public enum ScalingAssumption
{
    ConstantRuntime,
    LinearSpeedup,
}

public enum Severity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public sealed record Suppression(string Rule, string Resource, string Reason, int Line);

public sealed record RuleSettings(bool? Enabled, Severity? Severity, IReadOnlyDictionary<string, decimal> Parameters, IReadOnlyDictionary<string, IReadOnlyList<string>>? Lists = null);

public sealed class PricingSettings
{
    /// <summary>Fraction off list price by product family; <c>all</c> is the default for every family.</summary>
    public Dictionary<string, decimal> Discounts { get; init; } = new(StringComparer.Ordinal);

    public decimal Surcharge { get; set; }

    /// <summary>Exact $/DBU by SKU. Discounts and surcharges do not apply to an override.</summary>
    public Dictionary<string, decimal> Overrides { get; init; } = new(StringComparer.Ordinal);

    public InfraMode IncludeCloudInfra { get; set; } = InfraMode.Auto;

    public Commitment Commitment { get; set; } = Commitment.Payg;

    /// <summary>The list price column: Premium (default) or Enterprise. Azure has Premium only.</summary>
    public PlanTier Tier { get; set; } = PlanTier.Premium;
}

public sealed class EstimationSettings
{
    public AutoscaleAssumption Autoscale { get; set; } = AutoscaleAssumption.Max;

    public ScalingAssumption Scaling { get; set; } = ScalingAssumption.ConstantRuntime;

    /// <summary>Run duration that turns schedules into monthly figures. Null means not set.</summary>
    public decimal? DefaultRunMinutes { get; set; }

    /// <summary>DBUs in serverless standard performance mode relative to performance-optimized (calibration).</summary>
    public decimal StandardModeFactor { get; set; } = 0.7m;

    /// <summary>Node type per instance pool ID: pools are workspace objects, so the bundle does not show them.</summary>
    public Dictionary<string, string> InstancePools { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Reading usage history from system tables (add-usage-calibration). Off by default.</summary>
public sealed class CalibrationSettings
{
    public bool Enabled { get; set; }

    /// <summary>The SQL warehouse that runs the read-only history queries. Required when enabled.</summary>
    public string? WarehouseId { get; set; }

    /// <summary>The bundle target whose deployed resources hold the history. Default: the estimation target.</summary>
    public string? Target { get; set; }

    public int WindowDays { get; set; } = 30;

    /// <summary>Runs needed for confidence High (7 days of usage also qualify).</summary>
    public int MinRuns { get; set; } = 5;
}

public sealed class FailOnSettings
{
    public decimal? MonthlyIncrease { get; set; }

    public decimal? PercentIncrease { get; set; }

    public Severity? Severity { get; set; }

    public List<string> Rules { get; init; } = [];

    public bool UnknownCost { get; set; }
}

/// <summary>The settings from <c>.dbudoctor.yml</c>, before flags and environment variables are applied.</summary>
public sealed class DoctorConfig
{
    public string? Target { get; set; }

    public List<string>? Bundles { get; set; }

    public Cloud? Cloud { get; set; }

    public string? Region { get; set; }

    public string Currency { get; set; } = "USD";

    public PricingSettings Pricing { get; init; } = new();

    public EstimationSettings Estimation { get; init; } = new();

    public CalibrationSettings Calibration { get; init; } = new();

    public Dictionary<string, RuleSettings> Rules { get; init; } = new(StringComparer.Ordinal);

    public List<Suppression> Suppressions { get; init; } = [];

    public bool CommentOnNoChange { get; set; }

    public FailOnSettings FailOn { get; init; } = new();

    /// <summary>The file it was read from, or null for defaults.</summary>
    public string? SourcePath { get; set; }
}

public static class EnumNames
{
    public static string Of(Commitment value) => value switch
    {
        Commitment.Payg => "payg",
        Commitment.Spot => "spot",
        Commitment.Reserved1y => "reserved_1y",
        Commitment.Reserved3y => "reserved_3y",
        Commitment.SavingsPlan1y => "savings_plan_1y",
        Commitment.SavingsPlan3y => "savings_plan_3y",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Of(PlanTier value) => value switch
    {
        PlanTier.Premium => "premium",
        PlanTier.Enterprise => "enterprise",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Of(Cloud value) => value switch
    {
        Cloud.Azure => "azure",
        Cloud.Aws => "aws",
        Cloud.Gcp => "gcp",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Display name for reports: Azure, AWS, GCP.</summary>
    public static string Display(Cloud value) => value switch
    {
        Cloud.Azure => "Azure",
        Cloud.Aws => "AWS",
        Cloud.Gcp => "GCP",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Of(Severity value) => value switch
    {
        Severity.Info => "info",
        Severity.Warning => "warning",
        Severity.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Of(AutoscaleAssumption value) => value switch
    {
        AutoscaleAssumption.Max => "max",
        AutoscaleAssumption.Mid => "mid",
        AutoscaleAssumption.Min => "min",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Of(ScalingAssumption value) => value switch
    {
        ScalingAssumption.ConstantRuntime => "constant_runtime",
        ScalingAssumption.LinearSpeedup => "linear_speedup",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static string Of(InfraMode value) => value switch
    {
        InfraMode.Auto => "auto",
        InfraMode.True => "true",
        InfraMode.False => "false",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static readonly IReadOnlyDictionary<string, Commitment> Commitments = Enum.GetValues<Commitment>().ToDictionary(Of, v => v);
    public static readonly IReadOnlyDictionary<string, PlanTier> Tiers = Enum.GetValues<PlanTier>().ToDictionary(Of, v => v);
    public static readonly IReadOnlyDictionary<string, Cloud> Clouds = Enum.GetValues<Cloud>().ToDictionary(Of, v => v);
    public static readonly IReadOnlyDictionary<string, Severity> Severities = Enum.GetValues<Severity>().ToDictionary(Of, v => v);
    public static readonly IReadOnlyDictionary<string, AutoscaleAssumption> Autoscales = Enum.GetValues<AutoscaleAssumption>().ToDictionary(Of, v => v);
    public static readonly IReadOnlyDictionary<string, ScalingAssumption> Scalings = Enum.GetValues<ScalingAssumption>().ToDictionary(Of, v => v);
    public static readonly IReadOnlyDictionary<string, InfraMode> InfraModes = Enum.GetValues<InfraMode>().ToDictionary(Of, v => v);
}
