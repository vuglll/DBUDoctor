using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBUDoctor.Core.Config;

namespace DBUDoctor.Core.Pricing.Databricks;

/// <summary>One row of the Databricks pricing calculator data: an instance type, a workload and a plan.</summary>
public sealed class PricingRow
{
    [JsonPropertyName("cloud")] public string? Cloud { get; set; }
    [JsonPropertyName("instance")] public string? Instance { get; set; }
    [JsonPropertyName("compute")] public string? Compute { get; set; }
    [JsonPropertyName("plan")] public string? Plan { get; set; }
    [JsonPropertyName("baserate")] public string? BaseRate { get; set; }
    [JsonPropertyName("dburate")] public string? DbuRate { get; set; }
    [JsonPropertyName("region")] public string? Region { get; set; }
}

[JsonSerializable(typeof(List<PricingRow>))]
internal sealed partial class DatabricksPricingJsonContext : JsonSerializerContext;

/// <summary>
/// Reads the public JSON files behind the Databricks pricing calculator
/// (<c>https://www.databricks.com/en-pricing-assets/data/pricing/&lt;CLOUD&gt;.json</c>). The files are
/// undocumented, so the mapping tables below are guarded by contract tests and every refresh goes
/// through a reviewed pull request.
/// </summary>
public sealed class DatabricksPricingClient(HttpClient http)
{
    public const string BaseUrl = "https://www.databricks.com/en-pricing-assets/data/pricing/";

    public static string UrlFor(Cloud cloud) => BaseUrl + (cloud == Config.Cloud.Aws ? "AWS" : "GCP") + ".json";

    public async Task<List<PricingRow>> RowsAsync(Cloud cloud, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(UrlFor(cloud), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(stream, DatabricksPricingJsonContext.Default.ListPricingRow, cancellationToken).ConfigureAwait(false) ?? [];
    }
}

public static class DatabricksPricingNormalizer
{
    /// <summary>Calculator workload names by SKU.</summary>
    public static readonly IReadOnlyDictionary<string, string> ComputeToSku = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Jobs Compute"] = Skus.JobsCompute,
        ["Jobs Compute Photon"] = Skus.JobsComputePhoton,
        ["All-Purpose Compute"] = Skus.AllPurposeCompute,
        ["All-Purpose Compute Photon"] = Skus.AllPurposeComputePhoton,
        ["DLT Core Compute"] = Skus.DltCore,
        ["DLT Core Compute Photon"] = Skus.DltCorePhoton,
        ["DLT Pro Compute"] = Skus.DltPro,
        ["DLT Pro Compute Photon"] = Skus.DltProPhoton,
        ["DLT Advanced Compute"] = Skus.DltAdvanced,
        ["DLT Advanced Compute Photon"] = Skus.DltAdvancedPhoton,
        ["SQL Compute"] = Skus.SqlClassic,
        ["SQL Pro Compute"] = Skus.SqlPro,
        ["SQL Serverless Compute"] = Skus.SqlServerless,
        ["Jobs Serverless Compute"] = Skus.JobsServerless,
        ["Interactive Serverless Compute"] = Skus.AllPurposeServerless,
        ["Lakebase"] = Skus.DatabaseServerless,
    };

    /// <summary>Workloads deliberately not mapped: not priced by this version.</summary>
    public static readonly IReadOnlyList<string> IgnoredComputes =
    [
        "Jobs Light Compute", "DLT Serverless Compute", "Fine Tuning - On Demand",
        "Model Serving/Serverless Real-Time Inference", "Serverless Real-Time Inference",
    ];

    /// <summary>Calculator region names by region code. GCP lists most regions under two spellings.</summary>
    public static readonly IReadOnlyDictionary<Cloud, IReadOnlyDictionary<string, string>> RegionCodes = new Dictionary<Cloud, IReadOnlyDictionary<string, string>>
    {
        [Config.Cloud.Aws] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AP (Jakarta)"] = "ap-southeast-3",
            ["AP (Mumbai)"] = "ap-south-1",
            ["AP (Seoul)"] = "ap-northeast-2",
            ["AP (Singapore)"] = "ap-southeast-1",
            ["AP (Sydney)"] = "ap-southeast-2",
            ["AP (Tokyo)"] = "ap-northeast-1",
            ["Canada"] = "ca-central-1",
            ["Europe (France)"] = "eu-west-3",
            ["Europe (Frankfurt)"] = "eu-central-1",
            ["Europe (Ireland)"] = "eu-west-1",
            ["Europe (London)"] = "eu-west-2",
            ["Europe (Stockholm)"] = "eu-north-1",
            ["SA (Brazil)"] = "sa-east-1",
            ["US East (N. Virginia)"] = "us-east-1",
            ["US East (Ohio)"] = "us-east-2",
            ["US West (California)"] = "us-west-1",
            ["US West (N. California)"] = "us-west-1",
            ["US West (Oregon)"] = "us-west-2",
        },
        [Config.Cloud.Gcp] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Asia (Mumbai)"] = "asia-south1",
            ["India (Mumbai)"] = "asia-south1",
            ["Asia (Singapore)"] = "asia-southeast1",
            ["Asia-Southeast (Singapore)"] = "asia-southeast1",
            ["Asia (Tokyo)"] = "asia-northeast1",
            ["Asia-Northeast (Tokyo)"] = "asia-northeast1",
            ["Australia (Sydney)"] = "australia-southeast1",
            ["Australia-Southeast (Sydney)"] = "australia-southeast1",
            ["Canada (Quebec)"] = "northamerica-northeast1",
            ["Europe (Belgium)"] = "europe-west1",
            ["Europe-West (Belgium)"] = "europe-west1",
            ["Europe (England)"] = "europe-west2",
            ["Europe-West (England)"] = "europe-west2",
            ["Europe (Frankfurt)"] = "europe-west3",
            ["Europe-West (Frankfurt)"] = "europe-west3",
            ["ME (Dammam)"] = "me-central2",
            ["SA (Brazil)"] = "southamerica-east1",
            ["US (Iowa)"] = "us-central1",
            ["US-Central (Iowa)"] = "us-central1",
            ["US (Nevada)"] = "us-west4",
            ["US-West (Nevada)"] = "us-west4",
            ["USA (Nevada)"] = "us-west4",
            ["US (Oregon)"] = "us-west1",
            ["US-West (Oregon)"] = "us-west1",
            ["US (South Carolina)"] = "us-east1",
            ["US-East (South Carolina)"] = "us-east1",
            ["US (Virginia)"] = "us-east4",
            ["US-East (Virginia)"] = "us-east4",
        },
    };

    private const string PhotonSuffix = " (Photon)";

    public sealed record Result(
        IReadOnlyDictionary<string, PriceSheet> Regions,
        IReadOnlyDictionary<string, NodeType> NodeTypes,
        IReadOnlyList<string> Conflicts,
        IReadOnlyList<string> UnmappedComputes,
        IReadOnlyList<string> UnmappedRegions);

    /// <summary>
    /// Builds one price sheet per region and the node-type table. Rows without a region apply to every
    /// region; regional rows override them. Gov and SAP rows are left out. A SKU with two different
    /// prices for one tier and region is dropped and reported, not guessed.
    /// </summary>
    public static Result Normalize(Cloud cloud, IEnumerable<PricingRow> rows, Provenance provenance)
    {
        var regionCodes = RegionCodes[cloud];
        var unmappedComputes = new SortedSet<string>(StringComparer.Ordinal);
        var unmappedRegions = new SortedSet<string>(StringComparer.Ordinal);

        // (tier, sku, region or "*") -> prices seen
        var prices = new Dictionary<(PlanTier Tier, string Sku, string Region), SortedSet<decimal>>();
        var regions = new SortedSet<string>(StringComparer.Ordinal);
        var dbu = new Dictionary<(string Instance, string Workload), SortedSet<decimal>>();

        foreach (var row in rows)
        {
            if (row.Compute is not { } compute || row.Plan is not ("Premium" or "Enterprise") ||
                compute.EndsWith(" - Gov", StringComparison.Ordinal) || compute.EndsWith(" - SAP", StringComparison.Ordinal))
            {
                continue;
            }

            if (!ComputeToSku.TryGetValue(compute, out var sku))
            {
                if (!IgnoredComputes.Contains(compute, StringComparer.Ordinal))
                {
                    unmappedComputes.Add(compute);
                }

                continue;
            }

            // No region, or "n/a", means the price applies in every region.
            string region = "*";
            if (!string.IsNullOrEmpty(row.Region) && row.Region != "n/a")
            {
                if (row.Region.Contains("Gov", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!regionCodes.TryGetValue(row.Region, out var code))
                {
                    unmappedRegions.Add(row.Region);
                    continue;
                }

                region = code;
                regions.Add(code);
            }

            var tier = row.Plan == "Enterprise" ? PlanTier.Enterprise : PlanTier.Premium;
            if (Number(row.BaseRate) is { } price and > 0)
            {
                Add(prices, (tier, sku, region), price);
            }

            // DBU per hour per instance comes from the Premium classic rows, which every instance has.
            if (tier == PlanTier.Premium && region == "*" && row.Instance is { } instance && Number(row.DbuRate) is { } rate and > 0 &&
                (sku is Skus.JobsCompute or Skus.JobsComputePhoton or Skus.AllPurposeCompute or Skus.AllPurposeComputePhoton))
            {
                var photon = instance.EndsWith(PhotonSuffix, StringComparison.Ordinal);
                var name = photon ? instance[..^PhotonSuffix.Length] : instance;
                var workload = sku is Skus.JobsCompute or Skus.JobsComputePhoton ? "jobs" : "all_purpose";
                Add(dbu, (name, (photon ? "photon_" : "") + workload), rate);
            }
        }

        var conflicts = new List<string>();
        decimal? Single<TKey>(Dictionary<TKey, SortedSet<decimal>> source, TKey key, string label)
            where TKey : notnull
        {
            if (!source.TryGetValue(key, out var values))
            {
                return null;
            }

            if (values.Count == 1)
            {
                return values.Min;
            }

            conflicts.Add($"{label}: {string.Join(" vs ", values.Select(v => v.ToString(CultureInfo.InvariantCulture)))}");
            return null;
        }

        var sheets = new SortedDictionary<string, PriceSheet>(StringComparer.Ordinal);
        foreach (var region in regions)
        {
            var premium = new Dictionary<string, CatalogValue>(StringComparer.Ordinal);
            var enterprise = new Dictionary<string, CatalogValue>(StringComparer.Ordinal);
            foreach (var sku in Skus.All)
            {
                foreach (var (tier, target) in new[] { (PlanTier.Premium, premium), (PlanTier.Enterprise, enterprise) })
                {
                    var label = $"{EnumNames.Of(cloud)} {region} {EnumNames.Of(tier)} {sku}";
                    var value = prices.ContainsKey((tier, sku, region))
                        ? Single(prices, (tier, sku, region), label)
                        : Single(prices, (tier, sku, "*"), $"{EnumNames.Of(cloud)} {EnumNames.Of(tier)} {sku}");
                    if (value is { } v)
                    {
                        target[sku] = new CatalogValue(v, provenance);
                    }
                }
            }

            sheets[region] = new PriceSheet
            {
                Cloud = cloud,
                Region = region,
                Currency = "USD",
                Dbu = premium,
                DbuEnterprise = enterprise,
                Vm = new Dictionary<string, VmPrice>(),
            };
        }

        var nodes = new SortedDictionary<string, NodeType>(StringComparer.Ordinal);
        foreach (var instance in dbu.Keys.Select(k => k.Instance).Distinct().Order(StringComparer.Ordinal))
        {
            var jobs = Single(dbu, (instance, "jobs"), $"{EnumNames.Of(cloud)} {instance} DBU/h");
            if (jobs is null)
            {
                continue;
            }

            var allPurpose = Single(dbu, (instance, "all_purpose"), $"{EnumNames.Of(cloud)} {instance} all-purpose DBU/h");
            nodes[instance] = new NodeType(
                instance,
                jobs.Value,
                allPurpose is { } ap && ap != jobs ? ap : null,
                Single(dbu, (instance, "photon_jobs"), $"{EnumNames.Of(cloud)} {instance} Photon DBU/h"),
                Single(dbu, (instance, "photon_all_purpose"), $"{EnumNames.Of(cloud)} {instance} Photon all-purpose DBU/h"),
                null,
                provenance);
        }

        return new Result(sheets, nodes, conflicts.Distinct(StringComparer.Ordinal).ToList(), unmappedComputes.ToList(), unmappedRegions.ToList());
    }

    private static void Add<TKey>(Dictionary<TKey, SortedSet<decimal>> map, TKey key, decimal value)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var set))
        {
            map[key] = set = [];
        }

        // 0.15 and 0.150 are the same price.
        set.Add(value / 1.0000000000000000000000000000m);
    }

    private static decimal? Number(string? text) =>
        decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value) ? value : null;
}
