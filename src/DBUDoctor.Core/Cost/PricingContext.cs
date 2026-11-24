using DBUDoctor.Core.Config;
using DBUDoctor.Core.Pricing;

namespace DBUDoctor.Core.Cost;

/// <summary>
/// The prices that apply to one bundle: its cloud and region, the catalog entries for them, and the
/// user's discounts, surcharge, overrides and VM commitment. Records the oldest catalog date it used.
/// </summary>
public sealed class PricingContext
{
    private DateOnly? _oldestAsOf;

    public PricingContext(PriceCatalog catalog, Cloud? cloud, string? region, PricingSettings pricing, EstimationSettings estimation)
    {
        Catalog = catalog;
        Cloud = cloud;
        Region = region;
        Pricing = pricing;
        Estimation = estimation;
        Sheet = cloud is { } c ? catalog.PricesFor(c, region) : null;
        NodeTypes = cloud is { } c2 ? catalog.NodeTypesFor(c2) : null;
        InfraIncluded = pricing.IncludeCloudInfra switch
        {
            InfraMode.True => true,
            InfraMode.False => false,
            _ => Sheet?.HasVmPrices ?? false,
        };
    }

    public PriceCatalog Catalog { get; }

    public Cloud? Cloud { get; }

    public string? Region { get; }

    public PricingSettings Pricing { get; }

    public EstimationSettings Estimation { get; }

    public PriceSheet? Sheet { get; }

    public NodeTypeTable? NodeTypes { get; }

    /// <summary>Cloud VM cost is added to classic compute.</summary>
    public bool InfraIncluded { get; }

    public DateOnly? OldestAsOf => _oldestAsOf;

    /// <summary>List $/DBU by SKU from <c>system.billing.list_prices</c>, which win over the catalog (calibration).</summary>
    public IReadOnlyDictionary<string, decimal> LiveListPrices { get; private set; } = new Dictionary<string, decimal>(StringComparer.Ordinal);

    /// <summary>At least one price came from <see cref="LiveListPrices"/>.</summary>
    public bool LivePricesUsed { get; private set; }

    /// <summary>
    /// Takes current list prices keyed by the workspace's billing SKU names, which carry the plan tier
    /// as a prefix and sometimes a region suffix. When several names map to one SKU with different
    /// prices, the one the workspace actually used wins; otherwise the catalog price stays.
    /// </summary>
    public void UseListPrices(IReadOnlyDictionary<string, decimal> billingPrices, IEnumerable<string> observedSkus)
    {
        var tierPrefix = Pricing.Tier == PlanTier.Enterprise ? "ENTERPRISE_" : "PREMIUM_";
        var observed = observedSkus.ToHashSet(StringComparer.Ordinal);
        var candidates = new Dictionary<string, List<(string Name, decimal Price)>>(StringComparer.Ordinal);
        foreach (var (name, price) in billingPrices)
        {
            if (!name.StartsWith(tierPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = name[tierPrefix.Length..];
            var sku = Skus.All
                .Where(s => s.StartsWith("PREMIUM_", StringComparison.Ordinal))
                .Select(s => (Sku: s, Suffix: s["PREMIUM_".Length..]))
                .Where(s => rest == s.Suffix || rest.StartsWith(s.Suffix + "_", StringComparison.Ordinal))
                .OrderByDescending(s => s.Suffix.Length)
                .Select(s => s.Sku)
                .FirstOrDefault();
            if (sku is not null)
            {
                (candidates.TryGetValue(sku, out var list) ? list : candidates[sku] = []).Add((name, price));
            }
        }

        var live = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var (sku, list) in candidates)
        {
            var used = list.Where(c => observed.Contains(c.Name)).Select(c => c.Price).Distinct().ToList();
            var all = list.Select(c => c.Price).Distinct().ToList();
            if (used.Count == 1)
            {
                live[sku] = used[0];
            }
            else if (used.Count == 0 && all.Count == 1)
            {
                live[sku] = all[0];
            }
        }

        LiveListPrices = live;
    }

    /// <summary>Why no prices apply at all, or null when a price sheet was found.</summary>
    public string? MissingPricesReason => Cloud is null
        ? "cloud unknown: set `cloud` in .dbudoctor.yml or `workspace.host` in the bundle"
        : Region is null
            ? $"region not set: set `region` in .dbudoctor.yml for {EnumNames.Display(Cloud.Value)} prices"
            : Sheet is null
                ? $"no catalog prices for {EnumNames.Display(Cloud.Value)} region {Region}"
                : null;

    public void Used(Provenance provenance)
    {
        if (_oldestAsOf is null || provenance.AsOf < _oldestAsOf)
        {
            _oldestAsOf = provenance.AsOf;
        }
    }

    /// <summary>List $/DBU from the catalog for the configured tier, before discounts.</summary>
    public decimal? ListPrice(string sku)
    {
        if (LiveListPrices.TryGetValue(sku, out var live))
        {
            LivePricesUsed = true;
            return live;
        }

        if (Sheet?.DbuFor(Pricing.Tier).TryGetValue(sku, out var value) == true)
        {
            Used(value.Provenance);
            return value.Value;
        }

        return null;
    }

    /// <summary>
    /// Effective $/DBU = list × (1 − discount + surcharge) (design D12). A SKU override is exact: no
    /// discount or surcharge applies to it.
    /// </summary>
    public decimal? EffectivePrice(string sku, out string? reason)
    {
        reason = null;
        if (Pricing.Overrides.TryGetValue(sku, out var overridden))
        {
            return overridden;
        }

        if (MissingPricesReason is { } missing)
        {
            reason = missing;
            return null;
        }

        if (Pricing.Tier == PlanTier.Enterprise && Sheet!.DbuEnterprise.Count == 0 && !LiveListPrices.ContainsKey(sku))
        {
            reason = Cloud == Config.Cloud.Azure
                ? "Azure has the Premium tier only: remove `pricing.tier: enterprise`"
                : $"the catalog has no Enterprise-tier prices for {EnumNames.Display(Cloud!.Value)} {Region}";
            return null;
        }

        var list = ListPrice(sku);
        if (list is null)
        {
            var tier = Pricing.Tier == PlanTier.Enterprise ? "Enterprise " : "";
            reason = $"no {tier}{Skus.Label(sku)} price in the catalog for {EnumNames.Display(Cloud!.Value)} {Region}";
            return null;
        }

        return list.Value * (1 - Discount(Skus.FamilyOf(sku)) + Pricing.Surcharge);
    }

    public decimal Discount(string family) =>
        Pricing.Discounts.TryGetValue(family, out var specific) ? specific
        : Pricing.Discounts.TryGetValue(ProductFamily.All, out var all) ? all
        : 0m;

    public NodeType? Node(string id)
    {
        if (NodeTypes?.NodeTypes.TryGetValue(id, out var node) == true)
        {
            Used(node.Provenance);
            return node;
        }

        return null;
    }

    public decimal? VmPrice(string nodeType)
    {
        if (Sheet?.Vm.TryGetValue(nodeType, out var vm) == true && vm.For(Pricing.Commitment) is { } price)
        {
            Used(vm.Provenance);
            return price;
        }

        return null;
    }
}
