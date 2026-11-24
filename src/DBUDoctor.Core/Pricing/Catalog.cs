using DBUDoctor.Core.Config;

namespace DBUDoctor.Core.Pricing;

/// <summary>Every catalog value carries the date it was checked and where it came from.</summary>
public sealed record Provenance(DateOnly AsOf, string Source);

public sealed record CatalogValue(decimal Value, Provenance Provenance);

/// <summary>VM price per hour by commitment column. A missing column is null.</summary>
public sealed record VmPrice(IReadOnlyDictionary<Commitment, decimal> Columns, Provenance Provenance)
{
    public decimal? For(Commitment commitment) => Columns.TryGetValue(commitment, out var price) ? price : null;
}

/// <summary><c>prices/&lt;cloud&gt;/&lt;region&gt;.yaml</c>: list $/DBU by SKU, and VM prices per node type where available.</summary>
public sealed class PriceSheet
{
    public required Cloud Cloud { get; init; }

    public required string Region { get; init; }

    public required string Currency { get; init; }

    /// <summary>Premium-tier list $/DBU by SKU.</summary>
    public required IReadOnlyDictionary<string, CatalogValue> Dbu { get; init; }

    /// <summary>Enterprise-tier list $/DBU by SKU (AWS and GCP). Empty where the cloud has no Enterprise tier.</summary>
    public IReadOnlyDictionary<string, CatalogValue> DbuEnterprise { get; init; } = new Dictionary<string, CatalogValue>();

    public required IReadOnlyDictionary<string, VmPrice> Vm { get; init; }

    /// <summary>Where the VM prices came from, when that differs from the DBU prices (AWS: the AWS Price List).</summary>
    public Provenance? VmProvenance { get; init; }

    public IReadOnlyDictionary<string, CatalogValue> DbuFor(PlanTier tier) => tier == PlanTier.Enterprise ? DbuEnterprise : Dbu;

    public bool HasVmPrices => Vm.Count > 0;
}

public enum PhotonMethod
{
    /// <summary>The catalog lists a Photon DBU count per node type and workload (Azure).</summary>
    DbuCount,

    /// <summary>A DBU multiplier per node family.</summary>
    Multiplier,

    /// <summary>Photon has its own $/DBU SKU and the DBU count is unchanged.</summary>
    Sku,

    /// <summary>The catalog has no Photon pricing for this cloud.</summary>
    None,
}

public sealed record NodeType(
    string Id,
    decimal Dbu,
    decimal? DbuAllPurpose,
    decimal? PhotonDbuJobs,
    decimal? PhotonDbuAllPurpose,
    string? Family,
    Provenance Provenance);

/// <summary><c>node-types/&lt;cloud&gt;.yaml</c>: DBU per hour per instance type, and the cloud's Photon method.</summary>
public sealed class NodeTypeTable
{
    public required Cloud Cloud { get; init; }

    public required PhotonMethod PhotonMethod { get; init; }

    public required IReadOnlyDictionary<string, decimal> PhotonMultipliers { get; init; }

    public required IReadOnlyDictionary<string, NodeType> NodeTypes { get; init; }

    public required Provenance Provenance { get; init; }
}

public sealed record WarehouseVmLayout(string Driver, string Worker, int Workers);

/// <summary><c>sql-warehouses.yaml</c>: DBU per hour per cluster size, and the VMs behind classic and pro sizes.</summary>
public sealed class WarehouseTable
{
    public required IReadOnlyDictionary<string, CatalogValue> Sizes { get; init; }

    public required IReadOnlyDictionary<Cloud, IReadOnlyDictionary<string, WarehouseVmLayout>> VmLayouts { get; init; }

    public required Provenance Provenance { get; init; }
}

/// <summary><c>not-modeled.yaml</c>: cost categories reports list as not modeled.</summary>
public sealed class NotModeledTable
{
    public required IReadOnlyList<string> Everywhere { get; init; }

    public required IReadOnlyDictionary<Cloud, IReadOnlyList<string>> ByCloud { get; init; }

    public required IReadOnlyList<string> NotVisible { get; init; }

    public required string WithoutVmPrices { get; init; }

    public IReadOnlyList<string> For(Cloud? cloud, bool hasVmPrices)
    {
        var items = new List<string>();
        if (cloud is { } c && ByCloud.TryGetValue(c, out var specific))
        {
            items.AddRange(specific);
        }

        items.AddRange(Everywhere.Where(e => !items.Contains(e, StringComparer.Ordinal)));
        if (!hasVmPrices)
        {
            items.Add(WithoutVmPrices);
        }

        items.AddRange(NotVisible);
        return items;
    }
}

/// <summary>Platform defaults that apply when a bundle omits a field (<c>defaults.yaml</c>).</summary>
public sealed class CatalogDefaults
{
    public static CatalogDefaults None { get; } = new();

    /// <summary>The SQL Warehouses API default for <c>auto_stop_mins</c>. Null when the catalog has none.</summary>
    public CatalogValue? WarehouseAutoStopMinutes { get; init; }
}

/// <summary>Serverless products priced by size (<c>serverless.yaml</c>): Databricks Apps.</summary>
public sealed class ServerlessTable
{
    public static ServerlessTable None { get; } = new() { AppSizes = new Dictionary<string, CatalogValue>(StringComparer.OrdinalIgnoreCase), AppDefaultSize = "MEDIUM" };

    /// <summary>DBU per hour per app instance, by compute size (MEDIUM, LARGE, XLARGE).</summary>
    public required IReadOnlyDictionary<string, CatalogValue> AppSizes { get; init; }

    public required string AppDefaultSize { get; init; }

    /// <summary>Lakebase: DBU per compute unit (CU) per hour, by cloud.</summary>
    public IReadOnlyDictionary<Cloud, CatalogValue> LakebaseDbuPerCu { get; init; } = new Dictionary<Cloud, CatalogValue>();
}

/// <summary>Databricks Runtime end-of-support dates by <c>major.minor</c> (<c>runtimes.yaml</c>, LTS releases).</summary>
public sealed class RuntimeTable
{
    public static RuntimeTable None { get; } = new() { EndOfSupport = new Dictionary<Version, (DateOnly, Provenance)>() };

    public required IReadOnlyDictionary<Version, (DateOnly Date, Provenance Provenance)> EndOfSupport { get; init; }

    /// <summary>The oldest listed version; older runtimes are all past end of support.</summary>
    public Version? Oldest => EndOfSupport.Keys.Order().FirstOrDefault();
}

public sealed class PriceCatalog
{
    public required IReadOnlyDictionary<(Cloud Cloud, string Region), PriceSheet> Prices { get; init; }

    public required IReadOnlyDictionary<Cloud, NodeTypeTable> NodeTypes { get; init; }

    public required WarehouseTable Warehouses { get; init; }

    public required NotModeledTable NotModeled { get; init; }

    public CatalogDefaults Defaults { get; init; } = CatalogDefaults.None;

    public RuntimeTable Runtimes { get; init; } = RuntimeTable.None;

    public ServerlessTable Serverless { get; init; } = ServerlessTable.None;

    public PriceSheet? PricesFor(Cloud cloud, string? region) =>
        region is not null && Prices.TryGetValue((cloud, region), out var sheet) ? sheet : null;

    public NodeTypeTable? NodeTypesFor(Cloud cloud) => NodeTypes.GetValueOrDefault(cloud);

    public IEnumerable<string> RegionsFor(Cloud cloud) =>
        Prices.Keys.Where(k => k.Cloud == cloud).Select(k => k.Region).Order(StringComparer.Ordinal);

    /// <summary>Returns a copy with one price sheet replaced, for <c>--refresh-prices</c>.</summary>
    public PriceCatalog WithPrices(PriceSheet sheet)
    {
        var prices = Prices.ToDictionary(kv => kv.Key, kv => kv.Value);
        prices[(sheet.Cloud, sheet.Region)] = sheet;
        return new PriceCatalog { Prices = prices, NodeTypes = NodeTypes, Warehouses = Warehouses, NotModeled = NotModeled, Defaults = Defaults, Runtimes = Runtimes, Serverless = Serverless };
    }
}
