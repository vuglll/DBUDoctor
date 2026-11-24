using DBUDoctor.Core.Config;
using DBUDoctor.Core.Cost;
using DBUDoctor.Core.Diff;
using DBUDoctor.Core.Pricing;

namespace DBUDoctor.Core.Reporting;

/// <summary>Wording shared by the console and Markdown renderers.</summary>
internal static class ReportText
{
    public const string Disclaimer = "List-price estimate. Not an invoice.";

    public static string Resource(string type, string key) => $"{ResourceTypes.Singular(type)} `{key}`";

    public static string Label(Confidence confidence) => confidence switch
    {
        Confidence.High => "High",
        Confidence.Medium => "Medium",
        Confidence.Low => "Low",
        _ => "Unknown",
    };

    public static string CommitmentName(Commitment commitment) => commitment switch
    {
        Commitment.Payg => "pay-as-you-go",
        Commitment.Spot => "spot",
        Commitment.Reserved1y => "1-year reserved",
        Commitment.Reserved3y => "3-year reserved",
        Commitment.SavingsPlan1y => "1-year savings plan",
        Commitment.SavingsPlan3y => "3-year savings plan",
        _ => EnumNames.Of(commitment),
    };

    public static string CloudName(Cloud? cloud) => cloud switch
    {
        Cloud.Azure => "Azure",
        Cloud.Aws => "AWS",
        Cloud.Gcp => "GCP",
        _ => "this cloud",
    };

    /// <summary>"Medium · 20-min runs (configured)", "Medium · runs all month", "Low · …".</summary>
    public static string ConfidenceDetail(CostComparison row, BundleReport report)
    {
        var label = Label(row.Confidence);
        if (row.Notes.Count > 0)
        {
            return $"{label} · {row.Notes[0]}";
        }

        var side = row.After ?? row.Before;
        if ((row.Before?.History ?? row.After?.History) is { } history)
        {
            return history.Runs > 0
                ? $"{label} · {Format.Number(history.Runs)} runs observed"
                : $"{label} · {Format.Number(history.Days)} days observed";
        }

        if (side?.Frequency?.AlwaysOn == true || side?.Assumptions.Any(a => a.StartsWith("continuous", StringComparison.Ordinal)) == true)
        {
            return $"{label} · runs all month";
        }

        if (report.DefaultRunMinutes is { } m && side?.Frequency?.RunsPerMonth > 0)
        {
            return $"{label} · {Format.Number(m)}-min runs (configured)";
        }

        return label;
    }

    public static string MonthlyCell(decimal? value, bool absent, string currency) =>
        absent ? "—" : value is { } v ? Format.Money(v, currency) : "?";

    public static string RateCell(CostEstimate? estimate, string currency)
    {
        if (estimate is null)
        {
            return "—";
        }

        if (estimate.RatePerHour is { } rate)
        {
            return Format.Money(rate, currency);
        }

        return estimate.PricedElsewhere ? "priced elsewhere" : estimate.Serverless ? "needs history" : "unknown";
    }

    /// <summary>The rate-change line: "sql_warehouse `bi_wh`: cluster_size Small → Medium, $8.40 → $16.80 per active hour".</summary>
    public static string RateLine(CostComparison row, string currency)
    {
        var before = RateCell(row.Before, currency);
        var after = RateCell(row.After, currency);
        // "per active hour" belongs to amounts only: "$0.75 per active hour → needs history".
        var amountsOnly = (row.Before is null || row.Before.RatePerHour is not null) && (row.After is null || row.After.RatePerHour is not null);
        var rates = amountsOnly
            ? $"{before} → {after} per active hour"
            : $"{PerHour(row.Before, before)} → {PerHour(row.After, after)}";
        var text = $"{Resource(row.Type, row.Key)}: {row.Summary}, {rates}";
        if (row.Confidence == Confidence.Low && row.Notes.Count > 0)
        {
            text += $" (Low: {row.Notes[0]})";
        }

        return text;
    }

    private static string PerHour(CostEstimate? estimate, string cell) =>
        estimate?.RatePerHour is not null ? cell + " per active hour" : cell;

    /// <summary>The needs-history line for serverless jobs and pipelines.</summary>
    public static string HistoryLine(CostComparison row, string currency)
    {
        var side = row.After ?? row.Before!;
        var details = new List<string>();
        if (side.Sku is { } sku && side.PricePerDbu is { } price)
        {
            details.Add($"{Skus.Label(sku)} at {Format.Money(price, currency)} per DBU");
        }

        if (side.Frequency?.RunsPerMonth is { } runs)
        {
            details.Add($"{Format.Number(runs)} runs / month");
        }

        details.Add(side.PricedElsewhere ? side.MonthlyNote! : "monthly cost needs history");
        return $"{Resource(row.Type, row.Key)}: {row.Summary}. {string.Join(", ", details)}";
    }

    public static string UnknownLine(CostComparison row) =>
        $"{Resource(row.Type, row.Key)}: {row.Summary}. {string.Join("; ", row.UnknownReasons)}";

    public static string Headline(BundleReport report)
    {
        if (!report.HasCostRelevantChanges)
        {
            return "no cost-relevant changes";
        }

        if (!report.HeadlineRows.Any())
        {
            return "no monthly estimate";
        }

        var delta = $"{Format.SignedMoney(report.HeadlineDelta, report.Currency)} / month";
        return report.HeadlinePercent is { } p
            ? $"**{delta}** ({Format.SignedPercent(p)} on changed resources)"
            : $"**{delta}**";
    }

    public static string? ExcludedNote(BundleReport report) =>
        report.ExcludedFromHeadline > 0 && report.HeadlineRows.Any()
            ? $"Excludes {report.ExcludedFromHeadline} change{(report.ExcludedFromHeadline == 1 ? "" : "s")} without a Medium or High monthly estimate."
            : null;

    public static IEnumerable<string> ResourceAssumptions(BundleReport report)
    {
        foreach (var row in report.Changes)
        {
            var items = (row.Before?.Assumptions ?? []).Concat(row.After?.Assumptions ?? []).Distinct(StringComparer.Ordinal).ToList();
            if (items.Count > 0)
            {
                yield return $"{Resource(row.Type, row.Key)}: {string.Join("; ", items)}.";
            }
        }
    }

    public static string NotModeledLine(BundleReport report) =>
        $"Not modeled on {CloudName(report.Cloud)}: {string.Join(", ", report.NotModeled)}.";

    public static string FindingLine(Rules.Finding finding, bool markdown)
    {
        var id = markdown ? $"[{finding.RuleId}]({finding.DocsUrl})" : finding.RuleId;
        var location = finding.Location is { } l ? $" ({l.File}:{l.Line})" : "";
        var existing = finding.Existing ? " (existing)" : "";
        return $"{EnumNames.Of(finding.Severity)} · {id} · {finding.Message}{location}{existing}";
    }
}
