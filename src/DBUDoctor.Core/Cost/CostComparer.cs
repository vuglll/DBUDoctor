using DBUDoctor.Core.Config;
using DBUDoctor.Core.Diff;
using DBUDoctor.Core.Model;

namespace DBUDoctor.Core.Cost;

/// <summary>Builds the base-versus-head cost comparison for every changed priced resource.</summary>
public static class CostComparer
{
    public const string CrossModeNote = "classic/pro cost excludes cloud VMs; serverless includes them";

    public static List<CostComparison> Compare(BundleDiff diff, ResolvedBundle baseBundle, ResolvedBundle headBundle, CostEngine baseEngine, CostEngine headEngine, Calibration.UsageHistory? history = null)
    {
        var result = new List<CostComparison>();
        foreach (var change in diff.Changed)
        {
            var before = change.Kind == ChangeKind.Added ? null : baseEngine.Estimate(baseBundle, change.Type, change.Key);
            var after = change.Kind == ChangeKind.Removed ? null : headEngine.Estimate(headBundle, change.Type, change.Key);
            var comparison = new CostComparison
            {
                Type = change.Type,
                Key = change.Key,
                Kind = change.Kind,
                Before = before,
                After = after,
                Summary = Summarize(change, before, after, headBundle),
            };

            // Calibration applies its own scaling to observed figures.
            if (history is null || !Calibration.Calibrator.Apply(comparison, history, headEngine.Prices.Estimation, baseBundle, headBundle))
            {
                ApplyScaling(comparison, headEngine.Prices.Estimation.Scaling);
            }

            comparison.Confidence = RowConfidence(comparison);
            result.Add(comparison);
        }

        return result;
    }

    private static Confidence RowConfidence(CostComparison row)
    {
        var sides = new[] { row.Before, row.After }.Where(s => s is not null).Select(s => s!).ToList();
        var confidence = sides.Count == 0 ? Confidence.Unknown : sides.Min(s => s.Confidence);

        // Classic and serverless are only comparable when the classic side includes its VMs.
        if (row.Before is { } b && row.After is { } a && b.Serverless != a.Serverless)
        {
            var classic = b.Serverless ? a : b;
            if (!classic.InfraIncluded)
            {
                row.Notes.Add(CrossModeNote);
                if (confidence > Confidence.Low || (row.RateBefore is not null && row.RateAfter is not null))
                {
                    confidence = Confidence.Low;
                }
            }
        }

        return confidence;
    }

    /// <summary>
    /// <c>linear_speedup</c>: when a classic job's compute size changes, its cost per run stays the
    /// same, so the head monthly figure is the base cost per run times the head runs (design D5).
    /// </summary>
    private static void ApplyScaling(CostComparison row, ScalingAssumption scaling)
    {
        if (row.Kind != ChangeKind.Modified || row.Before is not { } before || row.After is not { } after ||
            before.DbuPerHour is null || after.DbuPerHour is null || before.DbuPerHour == after.DbuPerHour ||
            before.Serverless || after.Serverless)
        {
            return;
        }

        if (scaling == ScalingAssumption.LinearSpeedup &&
            before.Frequency is { AlwaysOn: false, RunsPerMonth: > 0 } bf &&
            after.Frequency is { AlwaysOn: false, RunsPerMonth: { } headRuns } &&
            before.Monthly is { } baseMonthly && after.Monthly is not null)
        {
            var costPerRun = baseMonthly / bf.RunsPerMonth!.Value;
            after.Monthly = costPerRun * headRuns;
            after.Assumptions.Add("linear speedup: compute size changed, cost per run unchanged");
        }
        else if (scaling == ScalingAssumption.ConstantRuntime)
        {
            after.Assumptions.Add("constant runtime: compute size changed, run duration unchanged");
        }
    }

    private static string Summarize(ResourceChange change, CostEstimate? before, CostEstimate? after, ResolvedBundle head)
    {
        if (change.Kind == ChangeKind.Removed)
        {
            return "removed";
        }

        if (change.Kind == ChangeKind.Added)
        {
            var details = new List<string> { "added" };
            if (change.Type == ResourceTypes.Pipelines && head.Document.Resources?.Pipelines?.GetValueOrDefault(change.Key)?.Continuous == true)
            {
                details.Add("continuous");
            }
            else if (after?.Frequency is { } f)
            {
                details.Add(f.Description);
            }

            if (after?.Serverless == true)
            {
                details.Add("serverless");
            }

            return string.Join(", ", details);
        }

        var parts = new List<string>();
        // Lead with the fields a reviewer recognizes; cluster details come last.
        var fields = change.Fields.OrderBy(f => f.Path.Contains("new_cluster.", StringComparison.Ordinal) ? 1 : 0).ToList();
        var shown = 0;
        if (before is not null && after is not null && before.Serverless != after.Serverless)
        {
            // The switch explains the task fields it touches (environment_key, job_cluster_key, ...).
            parts.Add(after.Serverless ? "classic → serverless" : "serverless → classic");
            var toggles = fields.Where(f => f.Path is "serverless" or "enable_serverless_compute").ToList();
            shown += toggles.Count;
            fields = fields.Except(toggles).ToList();
        }

        var scheduleFields = fields.Where(f => IsScheduleField(f.Path)).ToList();
        if (before?.Frequency is { } bf && after?.Frequency is { } af && scheduleFields.Count > 0)
        {
            parts.Add(bf.Description == af.Description ? $"schedule changed, still {af.Description}" : $"schedule {bf.Description} → {af.Description}");
            shown += scheduleFields.Count;
            fields = fields.Except(scheduleFields).ToList();
        }

        foreach (var field in fields.Take(Math.Max(0, 2 - parts.Count)))
        {
            parts.Add($"{field.Path} {Value(field.OldValue)} → {Value(field.NewValue)}");
            shown++;
        }

        if (change.Fields.Count > shown)
        {
            parts.Add($"+{change.Fields.Count - shown} more");
        }

        return string.Join("; ", parts);
    }

    private static string Value(string? value) => value is null ? "unset" : $"`{value}`";

    private static bool IsScheduleField(string path) =>
        path.StartsWith("schedule", StringComparison.Ordinal) || path.StartsWith("trigger", StringComparison.Ordinal) || path.StartsWith("continuous", StringComparison.Ordinal);
}
