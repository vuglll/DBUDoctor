using System.Globalization;
using System.Text;
using System.Text.Json;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Cost;

namespace DBUDoctor.Core.Reporting;

/// <summary>
/// <c>--format json</c>: a public contract (schemas/report.schema.json), changed only additively.
/// Written with <see cref="Utf8JsonWriter"/> for a fixed property order and no reflection. Amounts
/// are unrounded decimals.
/// </summary>
public static class JsonRenderer
{
    public const int SchemaVersion = 1;

    public static string Render(RunReport run, string toolVersion)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", SchemaVersion);
            w.WriteStartObject("tool");
            w.WriteString("name", "dbudoctor");
            w.WriteString("version", toolVersion);
            w.WriteEndObject();
            w.WriteNumber("exitCode", run.ExitCode);
            w.WriteBoolean("policyViolated", run.PolicyViolated);
            w.WriteStartArray("bundles");
            foreach (var bundle in run.Bundles)
            {
                WriteBundle(w, bundle);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static void WriteBundle(Utf8JsonWriter w, BundleReport r)
    {
        w.WriteStartObject();
        w.WriteString("bundle", r.Bundle);
        w.WriteString("target", r.Target);
        StringOrNull(w, "root", r.BundleRoot);
        StringOrNull(w, "cloud", r.Cloud is { } c ? EnumNames.Of(c) : null);
        StringOrNull(w, "region", r.Region);
        w.WriteString("currency", r.Currency);
        StringOrNull(w, "catalogAsOf", r.CatalogAsOf?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        w.WriteBoolean("cloudInfraIncluded", r.InfraIncluded);
        w.WriteString("commitment", EnumNames.Of(r.Commitment));
        w.WriteString("tier", EnumNames.Of(r.Tier));
        w.WriteString("scaling", EnumNames.Of(r.Scaling));
        w.WriteString("autoscale", EnumNames.Of(r.Autoscale));
        NumberOrNull(w, "defaultRunMinutes", r.DefaultRunMinutes);
        if (r.Calibration is { } cal)
        {
            w.WriteStartObject("calibration");
            StringOrNull(w, "target", cal.Target);
            w.WriteNumber("windowDays", cal.WindowDays);
            w.WriteNumber("calibratedChanges", cal.Calibrated);
            w.WriteNumber("notFound", cal.NotFound);
            w.WriteBoolean("livePrices", cal.LivePrices);
            Strings(w, "notes", cal.Notes);
            w.WriteEndObject();
        }
        else
        {
            w.WriteNull("calibration");
        }

        w.WriteBoolean("hasCostRelevantChanges", r.HasCostRelevantChanges);

        w.WriteStartObject("totals");
        w.WriteNumber("monthlyDelta", Format.Normalize(r.HeadlineDelta));
        w.WriteNumber("baseMonthly", Format.Normalize(r.HeadlineBaseTotal));
        NumberOrNull(w, "percentChange", r.HeadlinePercent is { } p ? p * 100 : null);
        w.WriteNumber("includedChanges", r.HeadlineRows.Count());
        w.WriteNumber("excludedChanges", r.ExcludedFromHeadline);
        w.WriteEndObject();

        w.WriteStartArray("changes");
        foreach (var row in r.Changes)
        {
            w.WriteStartObject();
            w.WriteString("type", row.Type);
            w.WriteString("key", row.Key);
            w.WriteString("kind", row.Kind.ToString().ToLowerInvariant());
            w.WriteString("summary", row.Summary);
            w.WriteString("category", row.HasUnknownRate ? "unknown" : row.HasMonthly ? "monthly" : (row.RateBefore is not null || row.RateAfter is not null) ? "rate" : "needs_history");
            w.WriteString("confidence", row.Confidence.ToString());
            w.WriteBoolean("inTotals", row.HasMonthly && row.Confidence >= Confidence.Medium);
            NumberOrNull(w, "monthlyDelta", row.MonthlyDelta);
            Strings(w, "notes", row.Notes);
            Estimate(w, "before", row.Before);
            Estimate(w, "after", row.After);
            w.WriteEndObject();
        }

        w.WriteEndArray();

        w.WriteStartArray("findings");
        foreach (var f in r.Findings)
        {
            w.WriteStartObject();
            w.WriteString("ruleId", f.RuleId);
            w.WriteString("severity", EnumNames.Of(f.Severity));
            w.WriteString("resourceType", f.ResourceType);
            w.WriteString("resourceKey", f.ResourceKey);
            w.WriteString("fieldPath", f.FieldPath);
            w.WriteString("message", f.Message);
            w.WriteString("docsUrl", f.DocsUrl);
            w.WriteBoolean("existing", f.Existing);
            if (f.Location is { } l)
            {
                w.WriteStartObject("location");
                w.WriteString("file", l.File);
                w.WriteNumber("line", l.Line);
                w.WriteEndObject();
            }
            else
            {
                w.WriteNull("location");
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteNumber("suppressedFindings", r.SuppressedCount);
        w.WriteNumber("existingFindingsHidden", r.ExistingHidden);

        w.WriteStartArray("possibleRenames");
        foreach (var rename in r.PossibleRenames)
        {
            w.WriteStartObject();
            w.WriteString("type", rename.Type);
            w.WriteString("from", rename.OldKey);
            w.WriteString("to", rename.NewKey);
            w.WriteEndObject();
        }

        w.WriteEndArray();

        w.WriteStartArray("notPriced");
        foreach (var u in r.Unpriced)
        {
            w.WriteStartObject();
            w.WriteString("type", u.Type);
            w.WriteString("key", u.Key);
            w.WriteString("kind", u.Kind.ToString().ToLowerInvariant());
            w.WriteEndObject();
        }

        w.WriteEndArray();
        Strings(w, "assumptions", r.GlobalAssumptions);
        Strings(w, "notModeled", r.NotModeled);
        Strings(w, "warnings", r.Warnings);

        w.WriteStartObject("policy");
        w.WriteBoolean("violated", r.Violations.Count > 0);
        w.WriteStartArray("violations");
        foreach (var v in r.Violations)
        {
            w.WriteStartObject();
            w.WriteString("policy", v.Policy);
            w.WriteString("message", v.Message);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        Strings(w, "notes", r.PolicyNotes);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void Estimate(Utf8JsonWriter w, string name, CostEstimate? e)
    {
        if (e is null)
        {
            w.WriteNull(name);
            return;
        }

        w.WriteStartObject(name);
        StringOrNull(w, "sku", e.Sku);
        NumberOrNull(w, "pricePerDbu", e.PricePerDbu);
        NumberOrNull(w, "dbuPerHour", e.DbuPerHour);
        NumberOrNull(w, "dbuCostPerHour", e.DbuCostPerHour);
        NumberOrNull(w, "vmCostPerHour", e.VmCostPerHour);
        NumberOrNull(w, "ratePerHour", e.RatePerHour);
        w.WriteBoolean("serverless", e.Serverless);
        w.WriteBoolean("cloudInfraIncluded", e.InfraIncluded);
        NumberOrNull(w, "runsPerMonth", e.Frequency?.RunsPerMonth);
        StringOrNull(w, "frequency", e.Frequency?.Description);
        NumberOrNull(w, "monthly", e.Monthly);
        NumberOrNull(w, "monthlyCeiling", e.MonthlyCeiling);
        w.WriteString("confidence", e.Confidence.ToString());
        StringOrNull(w, "monthlyNote", e.MonthlyNote);
        Strings(w, "assumptions", e.Assumptions);
        Strings(w, "unknownReasons", e.UnknownReasons);
        if (e.History is { } h)
        {
            w.WriteStartObject("history");
            w.WriteNumber("runs", h.Runs);
            w.WriteNumber("days", h.Days);
            w.WriteNumber("windowDays", h.WindowDays);
            NumberOrNull(w, "dbusPerRun", h.DbusPerRun);
            w.WriteNumber("monthlyDbus", Format.Normalize(h.MonthlyDbus));
            w.WriteEndObject();
        }
        else
        {
            w.WriteNull("history");
        }

        w.WriteEndObject();
    }

    private static void StringOrNull(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null) w.WriteNull(name);
        else w.WriteString(name, value);
    }

    private static void NumberOrNull(Utf8JsonWriter w, string name, decimal? value)
    {
        if (value is { } v) w.WriteNumber(name, Format.Normalize(v));
        else w.WriteNull(name);
    }

    private static void Strings(Utf8JsonWriter w, string name, IEnumerable<string> values)
    {
        w.WriteStartArray(name);
        foreach (var value in values)
        {
            w.WriteStringValue(value);
        }

        w.WriteEndArray();
    }
}
