using System.Text;
using DBUDoctor.Core.Diff;

namespace DBUDoctor.Core.Reporting;

/// <summary>Plain-text report for terminals (the default format).</summary>
public static class ConsoleRenderer
{
    public static string Render(RunReport run) => string.Join("\n", run.Bundles.Select(Render));

    public static string Render(BundleReport report)
    {
        var b = new StringBuilder();
        var currency = report.Currency;
        var headline = ReportText.Headline(report).Replace("**", "", StringComparison.Ordinal);
        b.Append("DBUDoctor · ").Append(report.Bundle).Append(" → ").Append(report.Target).Append(": ").Append(headline).Append('\n');
        foreach (var warning in report.Warnings)
        {
            b.Append("  ! ").Append(warning).Append('\n');
        }

        b.Append('\n');
        if (!report.HasCostRelevantChanges)
        {
            b.Append("No cost-relevant changes.\n\n");
        }

        var monthly = report.MonthlyRows.ToList();
        if (monthly.Count > 0)
        {
            var rows = monthly.Select(r => new[]
            {
                Plain(ReportText.Resource(r.Type, r.Key)),
                r.Summary,
                ReportText.MonthlyCell(r.MonthlyBefore, r.Before is null, currency),
                ReportText.MonthlyCell(r.MonthlyAfter, r.After is null, currency),
                Format.SignedMoney(r.MonthlyDelta!.Value, currency),
                ReportText.ConfidenceDetail(r, report),
            }).ToList();
            Table(b, ["Resource", "Change", "Before", "After", "Δ / month", "Confidence"], rows);
            if (ReportText.ExcludedNote(report) is { } excluded)
            {
                b.Append(excluded).Append('\n');
            }

            b.Append('\n');
        }

        Section(b, report.Calibration is null ? "Rate changes (no monthly figure until calibration)" : "Rate changes (no monthly figure: no history)", report.RateRows.Select(r => Plain(ReportText.RateLine(r, currency))));
        Section(b, "Needs history (DBUs per run are not in configuration)", report.HistoryRows.Select(r => Plain(ReportText.HistoryLine(r, currency))));
        Section(b, "Unknown cost", report.UnknownRows.Select(r => Plain(ReportText.UnknownLine(r))));
        Section(b, "New findings", report.Findings.Where(f => !f.Existing).Select(f => Plain(ReportText.FindingLine(f, markdown: false))));
        Section(b, "Existing findings", report.Findings.Where(f => f.Existing).Select(f => Plain(ReportText.FindingLine(f, markdown: false))));
        if (report.SuppressedCount > 0)
        {
            b.Append(report.SuppressedCount).Append(report.SuppressedCount == 1 ? " finding" : " findings").Append(" suppressed in .dbudoctor.yml.\n\n");
        }

        Section(b, "Possible renames", report.PossibleRenames.Select(r => $"{ResourceTypes.Singular(r.Type)} {r.OldKey} → {r.NewKey}"));
        Section(b, "Not priced in this version", report.Unpriced.Select(u => $"{u.Type} {u.Key} ({u.Kind.ToString().ToLowerInvariant()})"));
        Section(b, "Policy violated", report.Violations.Select(v => $"{v.Policy}: {v.Message}"));
        foreach (var note in report.PolicyNotes)
        {
            b.Append(note).Append('\n');
        }

        Section(b, "Assumptions", report.GlobalAssumptions.Concat(ReportText.ResourceAssumptions(report)).Select(Plain));
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
            b.Append("  - ").Append(line).Append('\n');
        }

        b.Append('\n');
    }

    private static void Table(StringBuilder b, string[] header, List<string[]> rows)
    {
        var widths = header.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();
        void Line(string[] cells) => b.Append("  ").Append(string.Join("  ", cells.Select((c, i) => i == cells.Length - 1 ? c : c.PadRight(widths[i])))).Append('\n');
        Line(header);
        Line(widths.Select(w => new string('-', w)).ToArray());
        foreach (var row in rows)
        {
            Line(row);
        }
    }

    private static string Plain(string text) => text.Replace("`", "", StringComparison.Ordinal);
}
