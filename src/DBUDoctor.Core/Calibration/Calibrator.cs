using DBUDoctor.Core.Config;
using DBUDoctor.Core.Cost;
using DBUDoctor.Core.Diff;
using DBUDoctor.Core.Model;
using DBUDoctor.Core.Reporting;

namespace DBUDoctor.Core.Calibration;

/// <summary>
/// Turns observed usage into monthly figures (design C7). History belongs to the deployed resource,
/// which is the base side; the head side scales it by what the pull request changes.
/// </summary>
public static class Calibrator
{
    private const string Source = "system.billing.usage";

    /// <summary>Calibrates the row in place. Returns true when at least the base side was calibrated.</summary>
    public static bool Apply(CostComparison row, UsageHistory history, EstimationSettings estimation, ResolvedBundle baseBundle, ResolvedBundle headBundle)
    {
        if (!history.Enabled || row.Before is not { } before)
        {
            return false;
        }

        if (history.NotFound.Contains((row.Type, row.Key)))
        {
            before.Assumptions.Add($"not found in calibration target `{history.Target}`: estimated without history");
            return false;
        }

        if (!history.Resources.TryGetValue((row.Type, row.Key), out var h))
        {
            return false;
        }

        if (h.TotalDbus <= 0)
        {
            before.Assumptions.Add($"no usage in the last {Plural(history.WindowDays, "day")}: estimated without history");
            return false;
        }

        if (before.PricedElsewhere || before.PricePerDbu is not { } basePrice)
        {
            return false;
        }

        var confidence = h.RunCount >= history.MinRuns || h.Days.Count >= 7 ? Confidence.High : Confidence.Low;
        var info = new CalibrationInfo(h.RunCount, h.Days.Count, h.WindowDays, h.MedianDbusPerRun, h.MonthlyDbus);
        var observed = h.MedianDbusPerRun is { } median && row.Type is ResourceTypes.Jobs
            ? $"{Plural(h.RunCount, "run")}, median {Dbu(median)} DBUs per run"
            : $"{Format.Number(h.MonthlyDbus)} DBUs per month over {Plural(h.Days.Count, "day")} of usage";
        var sourceNote = $"calibrated from {observed} in the last {Plural(h.WindowDays, "day")} ({Source})";

        // Per-run figures for jobs with run history and a finite schedule; month totals otherwise.
        var perRun = row.Type == ResourceTypes.Jobs && h.MedianDbusPerRun is not null && before.Frequency?.AlwaysOn != true;
        decimal baseUnits;
        string? runsNote = null;
        if (perRun)
        {
            if (before.Frequency?.RunsPerMonth is { } scheduled)
            {
                baseUnits = scheduled;
            }
            else
            {
                baseUnits = h.MonthlyRuns;
                runsNote = $"runs per month from observed runs ({Format.Number(h.MonthlyRuns)})";
            }
        }
        else
        {
            baseUnits = 1;
        }

        var unitDbus = perRun ? h.MedianDbusPerRun!.Value : h.MonthlyDbus;
        Set(before, Monthly(before, unitDbus, baseUnits, basePrice, before.DbuPerHour), confidence, info, sourceNote, runsNote);

        if (row.After is not { } after || after.PricePerDbu is not { } headPrice || after.Serverless != before.Serverless)
        {
            // A switch between classic and serverless changes the compute: history does not carry over.
            return true;
        }

        decimal? headUnits = 1;
        if (perRun)
        {
            headUnits = after.Frequency?.AlwaysOn == true ? null
                : after.Frequency?.RunsPerMonth ?? (before.Frequency?.RunsPerMonth is null ? h.MonthlyRuns : null);
        }
        else if (row.Type == ResourceTypes.Jobs && after.Frequency?.AlwaysOn != before.Frequency?.AlwaysOn)
        {
            headUnits = null;
        }

        if (headUnits is null)
        {
            after.Assumptions.Add("schedule type changed: estimated without history");
            return true;
        }

        var headConfidence = confidence;
        var headDbus = unitDbus;
        var assumptions = new List<string> { sourceNote };
        if (runsNote is not null && after.Frequency?.RunsPerMonth is null)
        {
            assumptions.Add(runsNote);
        }

        decimal? headDbuRate = before.DbuPerHour;
        if (!before.Serverless || row.Type == ResourceTypes.SqlWarehouses)
        {
            if (before.DbuPerHour is not { } baseRate || baseRate <= 0 || after.DbuPerHour is not { } headRate)
            {
                after.Assumptions.Add("compute size unknown: estimated without history");
                return true;
            }

            if (headRate != baseRate)
            {
                if (estimation.Scaling == ScalingAssumption.ConstantRuntime || row.Type != ResourceTypes.Jobs)
                {
                    headDbus = unitDbus * headRate / baseRate;
                    assumptions.Add($"observed DBUs scaled by {Dbu(headRate)} / {Dbu(baseRate)} DBU per hour ({(row.Type == ResourceTypes.Jobs ? "constant runtime" : "constant uptime")})");
                }
                else
                {
                    assumptions.Add("linear speedup: DBUs per run unchanged");
                }
            }

            headDbuRate = estimation.Scaling == ScalingAssumption.ConstantRuntime || row.Type != ResourceTypes.Jobs ? baseRate : headRate;
        }
        else if (row.Type == ResourceTypes.Jobs)
        {
            var baseTarget = Target(baseBundle, row.Key);
            var headTarget = Target(headBundle, row.Key);
            if (baseTarget != headTarget)
            {
                var factor = headTarget == "STANDARD" ? estimation.StandardModeFactor : 1 / estimation.StandardModeFactor;
                headDbus = unitDbus * factor;
                headConfidence = Confidence.Low;
                assumptions.Add($"performance mode {baseTarget} → {headTarget}: DBUs × {Format.Number(factor)} (estimation.standard_mode_factor)");
            }
        }

        // Runtime per run stays the base's under constant runtime: hours = observed DBUs / base DBU rate.
        var hours = headDbuRate is { } r && r > 0 ? unitDbus / r : (decimal?)null;
        var monthly = (headDbus * headUnits.Value * headPrice) + (after.InfraIncluded && after.VmCostPerHour is { } vm && hours is { } hr ? hr * vm * headUnits.Value : 0);
        DropHistoryCaveats(after);
        after.Monthly = monthly;
        after.Confidence = headConfidence;
        after.History = info;
        after.MonthlyNote = null;
        after.Assumptions.AddRange(assumptions);
        return true;
    }

    /// <summary>A warning per calibrated node type whose observed DBU per node-hour drifts more than 10% from the catalog.</summary>
    public static IEnumerable<string> DriftWarnings(UsageHistory history, PricingContext prices)
    {
        foreach (var node in history.Nodes.OrderBy(n => n.NodeType, StringComparer.Ordinal))
        {
            if (prices.NodeTypes?.NodeTypes.TryGetValue(node.NodeType, out var catalog) != true || catalog!.Dbu <= 0)
            {
                continue;
            }

            var rated = catalog.Dbu;

            var observed = node.DbuPerNodeHour;
            if (Math.Abs(observed - rated) / rated > 0.10m)
            {
                yield return $"Catalog drift: `{node.NodeType}` is rated {Dbu(rated)} DBU per hour in the catalog, but observed usage shows {Dbu(observed)}. Please open a catalog pull request.";
            }
        }
    }

    private static decimal Monthly(CostEstimate side, decimal unitDbus, decimal units, decimal price, decimal? dbuRate)
    {
        var dbuCost = unitDbus * units * price;
        if (side.InfraIncluded && side.VmCostPerHour is { } vm && dbuRate is { } rate && rate > 0)
        {
            dbuCost += unitDbus / rate * vm * units;
        }

        return dbuCost;
    }

    /// <summary>History answers what configuration could not: drop the "needs history" caveats.</summary>
    private static void DropHistoryCaveats(CostEstimate side) =>
        side.Assumptions.RemoveAll(a => a.Contains("cannot be derived from configuration", StringComparison.Ordinal));

    private static void Set(CostEstimate side, decimal monthly, Confidence confidence, CalibrationInfo info, string sourceNote, string? runsNote)
    {
        DropHistoryCaveats(side);
        side.Monthly = monthly;
        side.Confidence = confidence;
        side.History = info;
        side.MonthlyNote = null;
        side.Assumptions.Add(sourceNote);
        if (runsNote is not null)
        {
            side.Assumptions.Add(runsNote);
        }
    }

    private static string Target(ResolvedBundle bundle, string key) =>
        (bundle.Document.Resources?.Jobs?.GetValueOrDefault(key)?.PerformanceTarget ?? "PERFORMANCE_OPTIMIZED").ToUpperInvariant();

    /// <summary>DBU figures keep two decimals: node ratings such as 0.75 must not round to 0.8.</summary>
    private static string Dbu(decimal value) => decimal.Round(value, 2).ToString("#,0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
