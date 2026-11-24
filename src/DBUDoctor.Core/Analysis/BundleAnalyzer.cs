using DBUDoctor.Core.Calibration;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Cost;
using DBUDoctor.Core.Diff;
using DBUDoctor.Core.Model;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Reporting;
using DBUDoctor.Core.Rules;

namespace DBUDoctor.Core.Analysis;

public sealed record BundleInput(string Name, string Target, ResolvedBundle Base, ResolvedBundle Head, string? Root = null);

/// <summary>
/// Diff, cost and rules for one bundle. A pure function of the two documents, the configuration and
/// the catalog: tests run it offline with an injected reference instant.
/// </summary>
public static class BundleAnalyzer
{
    public static BundleReport Analyze(
        BundleInput input,
        DoctorConfig config,
        PriceCatalog catalog,
        DateTimeOffset reference,
        bool reportExisting,
        string? fallbackHost = null,
        IReadOnlyList<string>? extraWarnings = null,
        UsageHistory? history = null,
        DateOnly? today = null)
    {
        var warnings = new List<string>(extraWarnings ?? []);
        var host = input.Head.WorkspaceHost ?? input.Base.WorkspaceHost ?? fallbackHost;
        var cloud = config.Cloud ?? SettingsResolver.InferCloud(host);
        var region = config.Region;

        var basePrices = new PricingContext(catalog, cloud, region, config.Pricing, config.Estimation);
        var headPrices = new PricingContext(catalog, cloud, region, config.Pricing, config.Estimation);
        history ??= UsageHistory.None;
        if (history.Enabled && history.ListPrices.Count > 0)
        {
            var observedSkus = history.Resources.Values.SelectMany(r => r.Skus.Keys).ToList();
            basePrices.UseListPrices(history.ListPrices, observedSkus);
            headPrices.UseListPrices(history.ListPrices, observedSkus);
        }

        warnings.AddRange(history.Warnings);
        if (headPrices.MissingPricesReason is { } missing)
        {
            warnings.Add(cloud is not null && region is null
                ? $"Rates are Unknown because `region` is not set. Set `region` in .dbudoctor.yml (for example `region: {catalog.RegionsFor(cloud.Value).FirstOrDefault() ?? "westeurope"}`)."
                : $"Rates are Unknown: {missing}.");
        }
        else if (!headPrices.InfraIncluded && cloud is not null)
        {
            warnings.Add(headPrices.Sheet?.HasVmPrices == true
                ? "Cloud VMs are not included in classic rates (pricing.include_cloud_infra: false)."
                : $"Cloud VMs are not included in classic rates: the catalog has no VM prices for {EnumNames.Display(cloud.Value)}.");
        }

        var diff = BundleDiffer.Diff(input.Base, input.Head);
        var comparisons = CostComparer.Compare(
            diff,
            input.Base,
            input.Head,
            new CostEngine(basePrices, reference),
            new CostEngine(headPrices, reference),
            history.Enabled ? history : null);
        if (history.Enabled)
        {
            warnings.AddRange(Calibrator.DriftWarnings(history, headPrices));
        }

        var rules = RuleEngine.Evaluate(input.Name, input.Base, input.Head, basePrices, headPrices, reference, config, reportExisting, today: today);

        // The oldest catalog date used; when nothing was priced, the date of the bundle's price sheet.
        var asOf = new[] { basePrices.OldestAsOf, headPrices.OldestAsOf }.Where(d => d is not null).Min()
            ?? headPrices.Sheet?.Dbu.Values.Select(v => (DateOnly?)v.Provenance.AsOf).Min();
        var notModeled = catalog.NotModeled.For(cloud, headPrices.InfraIncluded);

        var report = new BundleReport
        {
            Bundle = input.Name,
            Target = input.Target,
            BundleRoot = input.Root,
            Cloud = cloud,
            Region = region,
            Currency = config.Currency,
            CatalogAsOf = asOf,
            InfraIncluded = headPrices.InfraIncluded,
            Commitment = config.Pricing.Commitment,
            Tier = config.Pricing.Tier,
            Autoscale = config.Estimation.Autoscale,
            Scaling = config.Estimation.Scaling,
            DefaultRunMinutes = config.Estimation.DefaultRunMinutes,
            Calibration = history.Enabled
                ? new CalibrationSummary(
                    history.Target,
                    history.WindowDays,
                    comparisons.Count(c => c.Before?.History is not null || c.After?.History is not null),
                    history.NotFound.Count,
                    headPrices.LivePricesUsed || basePrices.LivePricesUsed,
                    history.Notes)
                : null,
            Changes = comparisons,
            PossibleRenames = diff.PossibleRenames,
            Unpriced = diff.Unpriced,
            Findings = rules.Findings,
            SuppressedCount = rules.Suppressed.Count,
            ExistingHidden = rules.ExistingHidden,
            NotModeled = notModeled,
            Warnings = warnings,
        };

        PolicyEvaluator.Evaluate(report, config.FailOn);
        return report;
    }
}
