using System.Text.RegularExpressions;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Cost;
using DBUDoctor.Core.Model;

namespace DBUDoctor.Core.Rules;

public sealed record SuppressedFinding(Finding Finding, Suppression Suppression);

public sealed class RuleResults
{
    /// <summary>Findings to report: new ones, plus existing ones with --report-existing.</summary>
    public required IReadOnlyList<Finding> Findings { get; init; }

    public required IReadOnlyList<SuppressedFinding> Suppressed { get; init; }

    /// <summary>Findings that already exist in base and were not reported.</summary>
    public required int ExistingHidden { get; init; }
}

/// <summary>
/// Runs every enabled rule on the head configuration, and on the base to find what already
/// existed. Adding a rule means adding it to <see cref="RuleRegistry"/>; the engine, the diff and the
/// reporters do not change.
/// </summary>
public static class RuleEngine
{
    public static RuleResults Evaluate(
        string bundle,
        ResolvedBundle baseBundle,
        ResolvedBundle headBundle,
        PricingContext basePrices,
        PricingContext headPrices,
        DateTimeOffset reference,
        DoctorConfig config,
        bool reportExisting,
        IReadOnlyList<IRule>? rules = null,
        DateOnly? today = null)
    {
        var headFindings = new List<Finding>();
        // Identity → the highest severity it has in the base: a finding is new when it is absent there
        // or more severe now.
        var baseline = new Dictionary<string, Severity>(StringComparer.Ordinal);

        foreach (var rule in rules ?? RuleRegistry.All)
        {
            var settings = config.Rules.GetValueOrDefault(rule.Metadata.Id);
            if (settings?.Enabled == false)
            {
                continue;
            }

            var severity = settings?.Severity ?? rule.Metadata.DefaultSeverity;
            var parameters = settings?.Parameters ?? new Dictionary<string, decimal>();

            RuleContext Context(ResolvedBundle current, ResolvedBundle? previous, PricingContext prices) => new()
            {
                Bundle = bundle,
                Current = current,
                Previous = previous,
                Prices = prices,
                Reference = reference,
                Today = today ?? DateOnly.FromDateTime(reference.UtcDateTime),
                Parameters = parameters,
                Lists = settings?.Lists ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
                Severity = severity,
                SeverityConfigured = settings?.Severity is not null,
                Metadata = rule.Metadata,
            };

            headFindings.AddRange(rule.Evaluate(Context(headBundle, baseBundle, headPrices)));
            if (!baseBundle.IsEmpty)
            {
                foreach (var finding in rule.Evaluate(Context(baseBundle, null, basePrices)))
                {
                    baseline[finding.Identity] = baseline.TryGetValue(finding.Identity, out var seen) && seen > finding.Severity ? seen : finding.Severity;
                }
            }
        }

        var reported = new List<Finding>();
        var suppressed = new List<SuppressedFinding>();
        var existingHidden = 0;
        foreach (var finding in headFindings.DistinctBy(f => f.Identity).Order(FindingOrder.Instance))
        {
            var existing = baseline.TryGetValue(finding.Identity, out var baseSeverity) && baseSeverity >= finding.Severity;
            if (existing && !reportExisting)
            {
                existingHidden++;
                continue;
            }

            var suppression = config.Suppressions.FirstOrDefault(s => Matches(s, finding));
            if (suppression is not null)
            {
                suppressed.Add(new SuppressedFinding(finding, suppression));
                continue;
            }

            reported.Add(existing ? finding with { Existing = true } : finding);
        }

        return new RuleResults { Findings = reported, Suppressed = suppressed, ExistingHidden = existingHidden };
    }

    public static bool Matches(Suppression suppression, Finding finding)
    {
        if (!string.Equals(suppression.Rule, finding.RuleId, StringComparison.Ordinal))
        {
            return false;
        }

        var pattern = "^" + Regex.Escape(suppression.Resource).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch($"{finding.ResourceType}.{finding.ResourceKey}", pattern, RegexOptions.CultureInvariant);
    }
}

/// <summary>Ordinal order: rule, resource type, resource key, field path.</summary>
public sealed class FindingOrder : IComparer<Finding>
{
    public static readonly FindingOrder Instance = new();

    public int Compare(Finding? x, Finding? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        var c = string.CompareOrdinal(x.Bundle, y.Bundle);
        if (c == 0) c = string.CompareOrdinal(x.RuleId, y.RuleId);
        if (c == 0) c = string.CompareOrdinal(x.ResourceType, y.ResourceType);
        if (c == 0) c = string.CompareOrdinal(x.ResourceKey, y.ResourceKey);
        if (c == 0) c = string.CompareOrdinal(x.FieldPath, y.FieldPath);
        return c;
    }
}
