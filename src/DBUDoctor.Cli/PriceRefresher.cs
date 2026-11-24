using DBUDoctor.Core;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Pricing.Azure;

namespace DBUDoctor.Cli;

/// <summary>
/// <c>--refresh-prices</c>: fetches current Azure prices for one region without credentials, caches
/// them for the UTC day, and falls back to the embedded catalog with a warning when the API is
/// unreachable.
/// </summary>
public sealed class PriceRefresher(HttpClient http, string cacheDirectory, TimeProvider time)
{
    public static string DefaultCacheDirectory(IReadOnlyDictionary<string, string?> env)
    {
        if (env.GetValueOrDefault("DBUDOCTOR_CACHE_DIR") is { Length: > 0 } explicitDir)
        {
            return explicitDir;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dbudoctor", "cache");
        }

        var xdg = env.GetValueOrDefault("XDG_CACHE_HOME");
        return string.IsNullOrEmpty(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "dbudoctor")
            : Path.Combine(xdg, "dbudoctor");
    }

    /// <summary>Returns the catalog with refreshed prices, and a warning when it had to fall back.</summary>
    public async Task<(PriceCatalog Catalog, string? Warning)> RefreshAsync(PriceCatalog catalog, Cloud? cloud, string? region, CancellationToken cancellationToken)
    {
        if (cloud != Cloud.Azure)
        {
            return (catalog, "--refresh-prices supports Azure only; using the embedded catalog.");
        }

        if (region is null)
        {
            return (catalog, "--refresh-prices needs `region`; using the embedded catalog.");
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var cacheFile = Path.Combine(cacheDirectory, $"azure-{region}-{today:yyyy-MM-dd}.yaml");
        if (File.Exists(cacheFile))
        {
            try
            {
                return (catalog.WithPrices(CatalogLoader.ParsePriceSheet($"prices/azure/{region}.yaml", await File.ReadAllTextAsync(cacheFile, cancellationToken).ConfigureAwait(false))), null);
            }
            catch (DoctorException)
            {
                File.Delete(cacheFile);
            }
        }

        try
        {
            var client = new AzureRetailPricesClient(http) { MaxAttempts = 3 };
            var dbu = await client.DatabricksMetersAsync(region, cancellationToken).ConfigureAwait(false);
            var vms = await client.VirtualMachineMetersAsync(region, cancellationToken).ConfigureAwait(false);
            var vmTypes = new HashSet<string>(catalog.NodeTypesFor(Cloud.Azure)?.NodeTypes.Keys ?? [], StringComparer.Ordinal);
            if (catalog.Warehouses.VmLayouts.TryGetValue(Cloud.Azure, out var layouts))
            {
                foreach (var layout in layouts.Values)
                {
                    vmTypes.Add(layout.Driver);
                    vmTypes.Add(layout.Worker);
                }
            }

            var provenance = new Provenance(today, AzureRetailPricesClient.Endpoint + " (live)");
            var sheet = AzurePriceNormalizer.Normalize(region, dbu, vms, vmTypes, provenance).Sheet;
            if (sheet.Dbu.Count == 0)
            {
                return (catalog, $"The Azure Retail Prices API returned no Premium DBU prices for {region}; using the embedded catalog.");
            }

            Directory.CreateDirectory(cacheDirectory);
            await File.WriteAllTextAsync(cacheFile, AzurePriceNormalizer.ToYaml(sheet, provenance, "Cached by dbudoctor --refresh-prices."), cancellationToken).ConfigureAwait(false);
            return (catalog.WithPrices(sheet), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException)
        {
            var asOf = catalog.PricesFor(Cloud.Azure, region) is not null ? " from the embedded catalog" : "";
            return (catalog, $"Could not refresh Azure prices ({ex.Message.Split('\n')[0]}); using the embedded prices{asOf}.");
        }
    }
}
