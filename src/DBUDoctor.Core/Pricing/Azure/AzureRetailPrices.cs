using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBUDoctor.Core.Config;

namespace DBUDoctor.Core.Pricing.Azure;

public sealed class RetailPriceItem
{
    [JsonPropertyName("currencyCode")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("retailPrice")] public decimal RetailPrice { get; set; }
    [JsonPropertyName("armRegionName")] public string? ArmRegionName { get; set; }
    [JsonPropertyName("meterName")] public string? MeterName { get; set; }
    [JsonPropertyName("productName")] public string? ProductName { get; set; }
    [JsonPropertyName("skuName")] public string? SkuName { get; set; }
    [JsonPropertyName("serviceName")] public string? ServiceName { get; set; }
    [JsonPropertyName("unitOfMeasure")] public string? UnitOfMeasure { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("armSkuName")] public string? ArmSkuName { get; set; }
    [JsonPropertyName("reservationTerm")] public string? ReservationTerm { get; set; }
    [JsonPropertyName("effectiveStartDate")] public string? EffectiveStartDate { get; set; }
    [JsonPropertyName("savingsPlan")] public List<SavingsPlanPrice>? SavingsPlan { get; set; }
}

public sealed class SavingsPlanPrice
{
    [JsonPropertyName("retailPrice")] public decimal RetailPrice { get; set; }
    [JsonPropertyName("term")] public string? Term { get; set; }
}

public sealed class RetailPricesPage
{
    [JsonPropertyName("Items")] public List<RetailPriceItem>? Items { get; set; }
    [JsonPropertyName("NextPageLink")] public string? NextPageLink { get; set; }
}

[JsonSerializable(typeof(RetailPricesPage))]
internal sealed partial class AzurePricesJsonContext : JsonSerializerContext;

/// <summary>
/// Client for the public, unauthenticated Azure Retail Prices API. The preview API version is the
/// one that returns savings-plan prices; filter values are case-sensitive in it.
/// </summary>
public sealed class AzureRetailPricesClient(HttpClient http)
{
    public const string Endpoint = "https://prices.azure.com/api/retail/prices";
    public const string ApiVersion = "2023-01-01-preview";

    public async Task<List<RetailPriceItem>> QueryAsync(string filter, CancellationToken cancellationToken = default)
    {
        var items = new List<RetailPriceItem>();
        string? url = $"{Endpoint}?api-version={ApiVersion}&$filter={Uri.EscapeDataString(filter)}";
        while (url is not null)
        {
            using var response = await GetWithRetryAsync(url, cancellationToken).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync(stream, AzurePricesJsonContext.Default.RetailPricesPage, cancellationToken).ConfigureAwait(false);
            items.AddRange(page?.Items ?? []);
            url = page?.NextPageLink;
        }

        return items;
    }

    /// <summary>Retries throttling (429) and server errors, honoring Retry-After, with exponential backoff.</summary>
    private async Task<HttpResponseMessage> GetWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode || attempt >= MaxAttempts || (status != 429 && status < 500))
            {
                response.EnsureSuccessStatusCode();
                return response;
            }

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));
            response.Dispose();
            await Task.Delay(delay * RetryDelayScale, cancellationToken).ConfigureAwait(false);
        }
    }

    public int MaxAttempts { get; init; } = 8;

    /// <summary>Multiplies retry delays; tests set it to 0.</summary>
    public double RetryDelayScale { get; init; } = 1;

    public Task<List<RetailPriceItem>> DatabricksMetersAsync(string? region, CancellationToken cancellationToken = default) =>
        QueryAsync(region is null
            ? "serviceName eq 'Azure Databricks'"
            : $"serviceName eq 'Azure Databricks' and armRegionName eq '{region}'", cancellationToken);

    public Task<List<RetailPriceItem>> VirtualMachineMetersAsync(string region, CancellationToken cancellationToken = default) =>
        QueryAsync($"serviceName eq 'Virtual Machines' and armRegionName eq '{region}'", cancellationToken);
}

/// <summary>Turns Retail Prices API items into a catalog price sheet.</summary>
public static class AzurePriceNormalizer
{
    /// <summary>
    /// Premium DBU meter names by SKU. Meter names are compared after collapsing whitespace and
    /// ignoring case, because the API mixes "All-purpose" and "All-Purpose".
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> MeterToSku = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Premium Jobs Compute DBU"] = Skus.JobsCompute,
        ["Premium Jobs Compute Photon DBU"] = Skus.JobsComputePhoton,
        ["Premium All-purpose Compute DBU"] = Skus.AllPurposeCompute,
        ["Premium All-Purpose Photon DBU"] = Skus.AllPurposeComputePhoton,
        ["Premium Core Compute Delta Live Tables DBU"] = Skus.DltCore,
        ["Premium Core Compute Photon Delta Live Tables DBU"] = Skus.DltCorePhoton,
        ["Premium Pro Compute Delta Live Tables DBU"] = Skus.DltPro,
        ["Premium Pro Compute Photon Delta Live Tables DBU"] = Skus.DltProPhoton,
        ["Premium Advanced Compute Delta Live Tables DBU"] = Skus.DltAdvanced,
        ["Premium Advanced Compute Photon Delta Live Tables DBU"] = Skus.DltAdvancedPhoton,
        ["Premium SQL Analytics DBU"] = Skus.SqlClassic,
        ["Premium SQL Compute Pro DBU"] = Skus.SqlPro,
        ["Premium Serverless SQL DBU"] = Skus.SqlServerless,
        ["Premium Automated Serverless Compute DBU"] = Skus.JobsServerless,
        ["Premium Interactive Serverless Compute DBU"] = Skus.AllPurposeServerless,
        ["Premium Database Serverless Compute DBU"] = Skus.DatabaseServerless,

        // US Gov regions name the serverless meters differently; no region has both names.
        ["Premium Jobs Serverless Compute DBU"] = Skus.JobsServerless,
        ["Premium All-Purpose Serverless Compute DBU"] = Skus.AllPurposeServerless,
    };

    /// <summary>Premium DBU meters that are deliberately not mapped (not priced by this version).</summary>
    public static readonly IReadOnlyList<string> IgnoredPremiumMeters =
    [
        "Premium Jobs Light Compute DBU",
        "Premium Model Training DBU",
        "Premium Serverless Realtime Inferencing DBU",
        "Premium Enhanced Security and Compliance DBU",
        "Premium Enhanced Security and Compliance - Mission Critical Add-on DBU",
        "Premium Serverless Real-Time Inference DBU",
        "Premium Serverless GPU Compute Capacity Block DBU",
        "Premium Model Serving for Anthropic DBU",
        "Premium Anthropic Serverless Inference DBU",
    ];

    public static string? SkuForMeter(string? meterName)
    {
        if (meterName is null)
        {
            return null;
        }

        var normalized = string.Join(' ', meterName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return MeterToSku.TryGetValue(normalized, out var sku) ? sku : null;
    }

    public sealed record Result(PriceSheet Sheet, IReadOnlyList<string> UnmappedPremiumMeters);

    /// <param name="vmTypes">The node types to keep VM prices for (the ones the catalog has DBU counts for).</param>
    public static Result Normalize(string region, IEnumerable<RetailPriceItem> dbuItems, IEnumerable<RetailPriceItem> vmItems, ISet<string> vmTypes, Provenance provenance)
    {
        var dbu = new Dictionary<string, CatalogValue>(StringComparer.Ordinal);
        var dbuDates = new Dictionary<string, string>(StringComparer.Ordinal);
        var unmapped = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var item in dbuItems)
        {
            if (item.Type != "Consumption" || item.CurrencyCode is not (null or "USD") || item.UnitOfMeasure != "1 Hour" ||
                !(item.ArmRegionName is null || item.ArmRegionName == region || item.ArmRegionName.Length == 0))
            {
                continue;
            }

            var sku = SkuForMeter(item.MeterName);
            if (sku is null)
            {
                if (item.MeterName?.StartsWith("Premium ", StringComparison.Ordinal) == true &&
                    !item.MeterName.StartsWith("Premium - Free Trial", StringComparison.Ordinal) &&
                    item.MeterName.EndsWith(" DBU", StringComparison.Ordinal) &&
                    !IgnoredPremiumMeters.Contains(item.MeterName, StringComparer.OrdinalIgnoreCase))
                {
                    unmapped.Add(item.MeterName);
                }

                continue;
            }

            if (item.RetailPrice <= 0)
            {
                continue;
            }

            var date = item.EffectiveStartDate ?? "";
            if (!dbuDates.TryGetValue(sku, out var seen) || string.CompareOrdinal(date, seen) > 0)
            {
                dbu[sku] = new CatalogValue(item.RetailPrice, provenance);
                dbuDates[sku] = date;
            }
        }

        var columns = new Dictionary<string, Dictionary<Commitment, (decimal Price, string Date)>>(StringComparer.Ordinal);
        foreach (var item in vmItems)
        {
            if (item.ArmSkuName is null || !vmTypes.Contains(item.ArmSkuName) || item.CurrencyCode is not (null or "USD") ||
                item.ProductName is null || item.ProductName.Contains("Windows", StringComparison.Ordinal) ||
                item.ProductName.Contains("Cloud Services", StringComparison.Ordinal) ||
                (item.MeterName?.Contains("Low Priority", StringComparison.Ordinal) ?? false))
            {
                continue;
            }

            var perType = columns.TryGetValue(item.ArmSkuName, out var existing) ? existing : columns[item.ArmSkuName] = [];
            var date = item.EffectiveStartDate ?? "";

            void Put(Commitment commitment, decimal price)
            {
                if (price > 0 && (!perType.TryGetValue(commitment, out var current) || string.CompareOrdinal(date, current.Date) > 0))
                {
                    perType[commitment] = (price, date);
                }
            }

            switch (item.Type)
            {
                case "Consumption" when item.UnitOfMeasure == "1 Hour":
                    if (item.MeterName?.EndsWith(" Spot", StringComparison.Ordinal) == true)
                    {
                        Put(Commitment.Spot, item.RetailPrice);
                    }
                    else
                    {
                        Put(Commitment.Payg, item.RetailPrice);
                        foreach (var plan in item.SavingsPlan ?? [])
                        {
                            if (plan.Term == "1 Year") Put(Commitment.SavingsPlan1y, plan.RetailPrice);
                            if (plan.Term == "3 Years") Put(Commitment.SavingsPlan3y, plan.RetailPrice);
                        }
                    }

                    break;

                // Reservation prices are the total for the term; the catalog stores an hourly rate.
                case "Reservation" when item.ReservationTerm == "1 Year":
                    Put(Commitment.Reserved1y, Math.Round(item.RetailPrice / 8760m, 6, MidpointRounding.AwayFromZero));
                    break;
                case "Reservation" when item.ReservationTerm == "3 Years":
                    Put(Commitment.Reserved3y, Math.Round(item.RetailPrice / 26280m, 6, MidpointRounding.AwayFromZero));
                    break;
            }
        }

        var vm = columns
            .Where(kv => kv.Value.ContainsKey(Commitment.Payg))
            .ToDictionary(kv => kv.Key, kv => new VmPrice(kv.Value.ToDictionary(c => c.Key, c => c.Value.Price), provenance), StringComparer.Ordinal);

        var sheet = new PriceSheet { Cloud = Cloud.Azure, Region = region, Currency = "USD", Dbu = dbu, Vm = vm };
        return new Result(sheet, unmapped.ToList());
    }

    /// <summary>Writes a price sheet in the catalog's YAML layout (see <see cref="CatalogWriter"/>).</summary>
    public static string ToYaml(PriceSheet sheet, Provenance provenance, string header) => CatalogWriter.Prices(sheet, provenance, header);

    private static string Format(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
