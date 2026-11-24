using System.Text;
using DBUDoctor.Core.Diff;

namespace DBUDoctor.Core.Reporting;

/// <summary>
/// The pull request comment. Deterministic: the same inputs and catalog give byte-identical output.
/// The first line is the hidden marker that identifies the sticky comment (design D9).
/// </summary>
public static class MarkdownRenderer
{
    public static string Render(RunReport run) =>
        string.Join("\n---\n\n", run.Bundles.Select(Render));

    public static string Render(BundleReport report)
    {
        var b = new StringBuilder();
        var currency = report.Currency;
        b.Append(report.Marker).Append('\n');
        b.Append("### DBUDoctor · `").Append(report.Bundle).Append("` → `").Append(report.Target).Append("`: ").Append(ReportText.Headline(report)).Append("\n\n");

        foreach (var warning in report.Warnings)
        {
            b.Append("> ").Append(warning).Append('\n');
        }

        if (report.Warnings.Count > 0)
        {
            b.Append('\n');
        }

        if (!report.HasCostRelevantChanges)
        {
            b.Append("No cost-relevant changes.\n\n");
        }

        var monthly = report.MonthlyRows.ToList();
        if (monthly.Count > 0)
        {
            b.Append("| Resource | Change | Before | After | Δ / month | Confidence |\n");
            b.Append("|---|---|---|---|---|---|\n");
            foreach (var row in monthly)
            {
                var delta = Format.SignedMoney(row.MonthlyDelta!.Value, currency);
                if (row.Confidence >= Cost.Confidence.Medium)
                {
                    delta = $"**{delta}**";
                }

                b.Append("| ").Append(Cell(ReportText.Resource(row.Type, row.Key)))
                 .Append(" | ").Append(Cell(row.Summary))
                 .Append(" | ").Append(ReportText.MonthlyCell(row.MonthlyBefore, row.Before is null, currency))
                 .Append(" | ").Append(ReportText.MonthlyCell(row.MonthlyAfter, row.After is null, currency))
                 .Append(" | ").Append(delta)
                 .Append(" | ").Append(Cell(ReportText.ConfidenceDetail(row, report)))
                 .Append(" |\n");
            }

            b.Append('\n');
            if (ReportText.ExcludedNote(report) is { } excluded)
            {
                b.Append('_').Append(excluded).Append("_\n\n");
            }
        }

        Section(b, report.Calibration is null ? "**Rate changes** (no monthly figure until calibration)" : "**Rate changes** (no monthly figure: no history)", report.RateRows.Select(r => ReportText.RateLine(r, currency)));
        Section(b, "**Needs history** (DBUs per run are not in configuration)", report.HistoryRows.Select(r => ReportText.HistoryLine(r, currency)));
        Section(b, "**Unknown cost**", report.UnknownRows.Select(ReportText.UnknownLine));

        var newFindings = report.Findings.Where(f => !f.Existing).Select(f => ReportText.FindingLine(f, markdown: true));
        Section(b, "**New findings**", newFindings);
        Section(b, "**Existing findings** (--report-existing)", report.Findings.Where(f => f.Existing).Select(f => ReportText.FindingLine(f, markdown: true)));
        if (report.SuppressedCount > 0)
        {
            b.Append('_').Append(report.SuppressedCount).Append(report.SuppressedCount == 1 ? " finding" : " findings").Append(" suppressed in .dbudoctor.yml._\n\n");
        }

        Section(b, "**Possible renames** (a removal and an addition with the same settings)", report.PossibleRenames.Select(r => $"{ResourceTypes.Singular(r.Type)} `{r.OldKey}` → `{r.NewKey}`"));
        Section(b, "**Not priced in this version**", report.Unpriced.Select(u => $"{u.Type} `{u.Key}` ({u.Kind.ToString().ToLowerInvariant()})"));

        if (report.Violations.Count > 0)
        {
            Section(b, "**Policy violated**", report.Violations.Select(v => $"`{v.Policy}`: {v.Message}"));
        }

        foreach (var note in report.PolicyNotes)
        {
            b.Append('_').Append(note).Append("_\n\n");
        }

        b.Append("<details><summary>Assumptions</summary>\n\n");
        foreach (var line in report.GlobalAssumptions.Concat(ReportText.ResourceAssumptions(report)))
        {
            b.Append("- ").Append(line).Append('\n');
        }

        b.Append("</details>\n\n");
        b.Append(ReportText.NotModeledLine(report)).Append('\n');
        b.Append(ReportText.Disclaimer).Append('\n');
        return b.ToString();
    }

    private static void Section(StringBuilder b, string title, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0)
        {
            return;
        }

        b.Append(title).Append('\n');
        foreach (var line in list)
        {
            b.Append("- ").Append(line).Append('\n');
        }

        b.Append('\n');
    }

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
