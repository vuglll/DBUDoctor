using DBUDoctor.Core.Config;
using DBUDoctor.Core.Cost;
using DBUDoctor.Core.Diff;
using DBUDoctor.Core.Rules;

namespace DBUDoctor.Core.Reporting;

public sealed record PolicyViolation(string Policy, string Message);

/// <summary>How calibration ran for one bundle. Null on the report when calibration is off.</summary>
public sealed record CalibrationSummary(string? Target, int WindowDays, int Calibrated, int NotFound, bool LivePrices, IReadOnlyList<string> Notes);

/// <summary>Everything a renderer needs for one bundle and target. Built by <see cref="Analysis.BundleAnalyzer"/>.</summary>
public sealed class BundleReport
{
    public required string Bundle { get; init; }

    public required string Target { get; init; }

    /// <summary>The bundle root relative to the repository, or null for JSON input.</summary>
    public string? BundleRoot { get; init; }

    public Cloud? Cloud { get; init; }

    public string? Region { get; init; }

    public required string Currency { get; init; }

    public DateOnly? CatalogAsOf { get; init; }

    public required bool InfraIncluded { get; init; }

    public required Commitment Commitment { get; init; }

    public PlanTier Tier { get; init; } = PlanTier.Premium;

    public required AutoscaleAssumption Autoscale { get; init; }

    public required ScalingAssumption Scaling { get; init; }

    public decimal? DefaultRunMinutes { get; init; }

    public CalibrationSummary? Calibration { get; init; }

    /// <summary>Changed priced resources, in type then key order.</summary>
    public required IReadOnlyList<CostComparison> Changes { get; init; }

    public required IReadOnlyList<PossibleRename> PossibleRenames { get; init; }

    public required IReadOnlyList<UnpricedChange> Unpriced { get; init; }

    public required IReadOnlyList<Finding> Findings { get; init; }

    public required int SuppressedCount { get; init; }

    public required int ExistingHidden { get; init; }

    public required IReadOnlyList<string> NotModeled { get; init; }

    /// <summary>Report-level notes, such as a missing region or a failed price refresh.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public List<PolicyViolation> Violations { get; } = [];

    public List<string> PolicyNotes { get; } = [];

    /// <summary>Rows with a monthly figure: added, removed or modified resources whose both sides are known.</summary>
    public IEnumerable<CostComparison> MonthlyRows => Changes.Where(c => c.HasMonthly);

    /// <summary>Rows in the headline: monthly figures of Medium or High confidence.</summary>
    public IEnumerable<CostComparison> HeadlineRows => MonthlyRows.Where(c => c.Confidence >= Confidence.Medium);

    /// <summary>Changes with no monthly figure but a known rate on at least one side.</summary>
    public IEnumerable<CostComparison> RateRows => Changes.Where(c => !c.HasMonthly && !c.HasUnknownRate && (c.RateBefore is not null || c.RateAfter is not null));

    /// <summary>Serverless jobs and pipelines: $/DBU and runs are known, DBUs per run need history.</summary>
    public IEnumerable<CostComparison> HistoryRows => Changes.Where(c => !c.HasMonthly && !c.HasUnknownRate && c.RateBefore is null && c.RateAfter is null);

    /// <summary>Changes whose rate is unknown on a side that exists.</summary>
    public IEnumerable<CostComparison> UnknownRows => Changes.Where(c => c.HasUnknownRate);

    public decimal HeadlineDelta => HeadlineRows.Sum(r => r.MonthlyDelta!.Value);

    public decimal HeadlineBaseTotal => HeadlineRows.Sum(r => r.MonthlyBefore!.Value);

    /// <summary>The delta as a fraction of the base total of changed resources; null when that total is 0.</summary>
    public decimal? HeadlinePercent => HeadlineBaseTotal == 0 ? null : HeadlineDelta / HeadlineBaseTotal;

    /// <summary>Changed priced resources that are not in the headline.</summary>
    public int ExcludedFromHeadline => Changes.Count - HeadlineRows.Count();

    public bool HasCostRelevantChanges => Changes.Count > 0 || Findings.Count > 0;

    public string Marker => $"<!-- dbudoctor:bundle={Bundle};target={Target} -->";

    /// <summary>Assumptions that apply to the whole report.</summary>
    public IEnumerable<string> GlobalAssumptions
    {
        get
        {
            var prices = Cloud is { } c
                ? $"Prices: {ReportText.CloudName(c)} {Region ?? "(region not set)"}, {(Tier == PlanTier.Enterprise ? "Enterprise" : "Premium")} tier, " +
                  (InfraIncluded ? $"{ReportText.CommitmentName(Commitment)} VMs" : "DBUs only (cloud VMs not included)") +
                  (Calibration?.LivePrices == true ? ", list prices from system.billing.list_prices" : "") +
                  (CatalogAsOf is { } d ? $", catalog {d:yyyy-MM-dd}" : "") + "."
                : "Prices: cloud unknown.";
            yield return prices;
            yield return $"Scaling: {(Scaling == ScalingAssumption.ConstantRuntime ? "constant runtime" : "linear speedup")}. Autoscale: at {EnumNames.Of(Autoscale)}.";
            yield return DefaultRunMinutes is { } m
                ? $"Run duration: {Format.Number(m)} minutes (estimation.default_run_minutes)."
                : Calibration is null
                    ? "Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes)."
                    : "Run duration: not set, so scheduled classic jobs without history show rates only (set estimation.default_run_minutes).";
            if (Calibration is { } cal)
            {
                var found = cal.NotFound > 0 ? $"; {cal.NotFound} not found in the target" : "";
                yield return $"Calibration: history from system.billing.usage over the last {cal.WindowDays} days in target `{cal.Target}`, {cal.Calibrated} calibrated{found}. High confidence assumes the next month looks like the last one.";
                foreach (var note in cal.Notes)
                {
                    yield return $"Calibration: {note}.";
                }
            }
        }
    }
}

/// <summary>The result of one <c>dbudoctor diff</c> run: one report per bundle.</summary>
public sealed class RunReport
{
    public required IReadOnlyList<BundleReport> Bundles { get; init; }

    public bool PolicyViolated => Bundles.Any(b => b.Violations.Count > 0);

    public int ExitCode => PolicyViolated ? ExitCodes.PolicyViolated : ExitCodes.Ok;
}
