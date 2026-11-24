using System.Net;
using System.Text;
using DBUDoctor.CatalogGen;
using DBUDoctor.Cli;
using DBUDoctor.Core;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Pricing.Azure;

namespace DBUDoctor.Tests;

public class CatalogTests
{
    [Fact]
    public void Embedded_catalog_loads()
    {
        var catalog = CatalogLoader.LoadEmbedded();
        Assert.NotNull(catalog.PricesFor(Cloud.Azure, "westeurope"));
        Assert.NotNull(catalog.PricesFor(Cloud.Aws, "us-east-1"));
        Assert.Equal(PhotonMethod.DbuCount, catalog.NodeTypesFor(Cloud.Azure)!.PhotonMethod);
        Assert.Equal(528m, catalog.Warehouses.Sizes["4X-Large"].Value);
    }

    [Fact]
    public void Every_catalog_entry_has_as_of_source_and_a_positive_value()
    {
        var catalog = CatalogLoader.LoadEmbedded();
        var problems = new List<string>();

        void Check(string what, decimal value, Provenance provenance)
        {
            if (value <= 0) problems.Add($"{what}: value {value}");
            if (provenance.AsOf == default) problems.Add($"{what}: no as_of");
            if (string.IsNullOrWhiteSpace(provenance.Source)) problems.Add($"{what}: no source");
        }

        foreach (var ((cloud, region), sheet) in catalog.Prices)
        {
            foreach (var (sku, value) in sheet.Dbu) Check($"{cloud}/{region} {sku}", value.Value, value.Provenance);
            foreach (var (sku, value) in sheet.DbuEnterprise) Check($"{cloud}/{region} enterprise {sku}", value.Value, value.Provenance);
            foreach (var (type, vm) in sheet.Vm)
            {
                foreach (var (column, price) in vm.Columns) Check($"{cloud}/{region} {type} {column}", price, vm.Provenance);
            }
        }

        foreach (var (cloud, table) in catalog.NodeTypes)
        {
            foreach (var (id, node) in table.NodeTypes)
            {
                Check($"{cloud} {id}", node.Dbu, node.Provenance);
                foreach (var photon in new[] { node.PhotonDbuJobs, node.PhotonDbuAllPurpose, node.DbuAllPurpose }.Where(v => v is not null))
                {
                    Check($"{cloud} {id} photon", photon!.Value, node.Provenance);
                }
            }
        }

        foreach (var (size, value) in catalog.Warehouses.Sizes) Check($"warehouse {size}", value.Value, value.Provenance);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Azure_dbu_counts_match_the_pricing_page_examples()
    {
        var nodes = CatalogLoader.LoadEmbedded().NodeTypesFor(Cloud.Azure)!.NodeTypes;
        Assert.Equal(0.75m, nodes["Standard_DS3_v2"].Dbu);
        Assert.Equal(1.5m, nodes["Standard_DS4_v2"].Dbu);
        Assert.Equal(3.0m, nodes["Standard_DS5_v2"].Dbu);
        Assert.Equal(0.75m, nodes["Standard_D4ds_v4"].Dbu);
        Assert.Equal(1.5m, nodes["Standard_D8ds_v4"].Dbu);
    }

    [Fact]
    public void Main_azure_regions_price_the_classic_skus()
    {
        // Some regions publish only part of the meters (brazilsoutheast has no plain Jobs compute meter);
        // those rates come out Unknown, with the reason. The main regions must be complete.
        var catalog = CatalogLoader.LoadEmbedded();
        Assert.True(catalog.RegionsFor(Cloud.Azure).Count() >= 40);
        foreach (var region in new[] { "eastus", "eastus2", "westus2", "westeurope", "northeurope", "uksouth", "australiaeast", "southeastasia" })
        {
            var sheet = catalog.PricesFor(Cloud.Azure, region)!;
            Assert.True(sheet.Dbu.ContainsKey(Skus.JobsCompute), $"{region}: no Jobs compute price");
            Assert.True(sheet.Dbu.ContainsKey(Skus.AllPurposeCompute), $"{region}: no all-purpose price");
            Assert.True(sheet.Vm.ContainsKey("Standard_DS3_v2") || sheet.Vm.Count > 0, $"{region}: no VM prices");
        }
    }

    [Fact]
    public void Unknown_sku_in_a_catalog_file_is_rejected_with_its_line()
    {
        var files = new Dictionary<string, string>
        {
            ["prices/azure/x.yaml"] = "cloud: azure\nregion: x\nas_of: 2026-01-01\nsource: s\ndbu:\n  NOPE: 1\n",
            ["sql-warehouses.yaml"] = "as_of: 2026-01-01\nsource: s\nsizes: {}\n",
            ["not-modeled.yaml"] = "as_of: 2026-01-01\nsource: s\nwithout_vm_prices: x\n",
        };
        var ex = Assert.Throws<DoctorException>(() => CatalogLoader.Load(files));
        Assert.Contains("catalog/prices/azure/x.yaml:6", ex.Message);
    }

    [Fact]
    public void Later_catalog_files_are_tolerated()
    {
        var files = new Dictionary<string, string>
        {
            ["lakebase.yaml"] = "anything: goes\n",
            ["sql-warehouses.yaml"] = "as_of: 2026-01-01\nsource: s\nsizes: {}\n",
            ["not-modeled.yaml"] = "as_of: 2026-01-01\nsource: s\nwithout_vm_prices: x\n",
        };
        Assert.Empty(CatalogLoader.Load(files).Prices);
    }
}

public class AzureRetailPricesContractTests
{
    private static RetailPriceItem Dbu(string meter, decimal price, string region = "westeurope") => new()
    {
        MeterName = meter, RetailPrice = price, ArmRegionName = region, Type = "Consumption", UnitOfMeasure = "1 Hour",
        CurrencyCode = "USD", ProductName = "Azure Databricks", ServiceName = "Azure Databricks",
    };

    private static RetailPriceItem Vm(string sku, string meter, string product, string type, decimal price, string? term = null, List<SavingsPlanPrice>? plans = null) => new()
    {
        ArmSkuName = sku, MeterName = meter, ProductName = product, Type = type, RetailPrice = price, ReservationTerm = term,
        UnitOfMeasure = "1 Hour", CurrencyCode = "USD", ArmRegionName = "westeurope", SavingsPlan = plans, ServiceName = "Virtual Machines",
    };

    [Theory]
    [InlineData("Premium Jobs Compute DBU", Skus.JobsCompute)]
    [InlineData("Premium Jobs Compute Photon DBU", Skus.JobsComputePhoton)]
    [InlineData("Premium All-purpose Compute DBU", Skus.AllPurposeCompute)]
    [InlineData("Premium All-Purpose Photon DBU", Skus.AllPurposeComputePhoton)]
    [InlineData("Premium Core Compute Delta Live Tables DBU", Skus.DltCore)]
    [InlineData("Premium Pro Compute Photon Delta Live Tables DBU", Skus.DltProPhoton)]
    [InlineData("Premium Advanced Compute Delta Live Tables DBU", Skus.DltAdvanced)]
    [InlineData("Premium SQL Analytics DBU", Skus.SqlClassic)]
    [InlineData("Premium SQL Compute Pro DBU", Skus.SqlPro)]
    [InlineData("Premium Serverless SQL DBU", Skus.SqlServerless)]
    [InlineData("Premium Automated Serverless Compute DBU", Skus.JobsServerless)]
    [InlineData("Premium Interactive Serverless Compute DBU", Skus.AllPurposeServerless)]
    [InlineData("Premium Jobs Serverless Compute DBU", Skus.JobsServerless)]
    [InlineData("Premium  All-Purpose   Serverless Compute DBU", Skus.AllPurposeServerless)]
    [InlineData("Premium Database Serverless Compute DBU", Skus.DatabaseServerless)]
    public void Meter_names_map_to_skus(string meter, string sku)
    {
        Assert.Equal(sku, AzurePriceNormalizer.SkuForMeter(meter));
    }

    [Theory]
    [InlineData("Standard Jobs Compute DBU")]
    [InlineData("Premium Jobs Light Compute DBU")]
    [InlineData("Premium - Free Trial Jobs Compute DBU")]
    public void Other_meters_are_not_mapped(string meter)
    {
        Assert.Null(AzurePriceNormalizer.SkuForMeter(meter));
    }

    [Fact]
    public void Normalize_keeps_premium_dbus_and_linux_vm_columns()
    {
        var dbu = new[] { Dbu("Premium Jobs Compute DBU", 0.30m), Dbu("Standard Jobs Compute DBU", 0.15m), Dbu("Premium Brand New Thing DBU", 1m) };
        var vms = new[]
        {
            Vm("Standard_DS3_v2", "DS3 v2", "Virtual Machines DSv2 Series", "Consumption", 0.272m, plans: [new() { Term = "1 Year", RetailPrice = 0.2m }, new() { Term = "3 Years", RetailPrice = 0.15m }]),
            Vm("Standard_DS3_v2", "DS3 v2 Spot", "Virtual Machines DSv2 Series", "Consumption", 0.06m),
            Vm("Standard_DS3_v2", "DS3 v2 Low Priority", "Virtual Machines DSv2 Series", "Consumption", 0.05m),
            Vm("Standard_DS3_v2", "DS3 v2", "Virtual Machines DSv2 Series Windows", "Consumption", 0.5m),
            Vm("Standard_DS3_v2", "DS3 v2", "Virtual Machines DSv2 Series", "Reservation", 876m, "1 Year"),
            Vm("Standard_DS3_v2", "DS3 v2", "Virtual Machines DSv2 Series", "Reservation", 1314m, "3 Years"),
            Vm("Standard_Other", "Other", "Virtual Machines X Series", "Consumption", 9m),
        };

        var result = AzurePriceNormalizer.Normalize("westeurope", dbu, vms, new HashSet<string> { "Standard_DS3_v2" }, new Provenance(new DateOnly(2026, 11, 8), "api"));

        Assert.Equal(0.30m, Assert.Single(result.Sheet.Dbu).Value.Value);
        Assert.Equal(["Premium Brand New Thing DBU"], result.UnmappedPremiumMeters);
        var vm = Assert.Single(result.Sheet.Vm).Value;
        Assert.Equal(0.272m, vm.For(Commitment.Payg));
        Assert.Equal(0.06m, vm.For(Commitment.Spot));
        Assert.Equal(0.1m, vm.For(Commitment.Reserved1y));
        Assert.Equal(0.05m, vm.For(Commitment.Reserved3y));
        Assert.Equal(0.2m, vm.For(Commitment.SavingsPlan1y));
        Assert.Equal(0.15m, vm.For(Commitment.SavingsPlan3y));
    }

    [Fact]
    public void Yaml_output_round_trips_through_the_loader()
    {
        var sheet = new PriceSheet
        {
            Cloud = Cloud.Azure, Region = "westeurope", Currency = "USD",
            Dbu = new Dictionary<string, CatalogValue> { [Skus.JobsCompute] = new(0.3m, new Provenance(new DateOnly(2026, 11, 8), "api")) },
            Vm = new Dictionary<string, VmPrice> { ["Standard_DS3_v2"] = new(new Dictionary<Commitment, decimal> { [Commitment.Payg] = 0.272m }, new Provenance(new DateOnly(2026, 11, 8), "api")) },
        };
        var yaml = AzurePriceNormalizer.ToYaml(sheet, new Provenance(new DateOnly(2026, 11, 8), "api"), "header");
        var loaded = CatalogLoader.ParsePriceSheet("prices/azure/westeurope.yaml", yaml);

        Assert.Equal(0.3m, loaded.Dbu[Skus.JobsCompute].Value);
        Assert.Equal(0.272m, loaded.Vm["Standard_DS3_v2"].For(Commitment.Payg));
    }

    [Fact]
    public async Task Client_follows_next_page_links_and_retries_throttling()
    {
        var handler = new FakeHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("skip=1", StringComparison.Ordinal))
            {
                return Json("""{ "Items": [ { "meterName": "b", "retailPrice": 2 } ], "NextPageLink": null }""");
            }

            return Json("""{ "Items": [ { "meterName": "a", "retailPrice": 1 } ], "NextPageLink": "https://prices.azure.com/api/retail/prices?$skip=1" }""");
        }, throttleFirst: 1);
        var client = new AzureRetailPricesClient(new HttpClient(handler)) { RetryDelayScale = 0 };

        var items = await client.QueryAsync("serviceName eq 'Azure Databricks'");

        Assert.Equal(["a", "b"], items.Select(i => i.MeterName));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Generator_writes_one_file_per_region_and_summarizes_changes()
    {
        var catalogDir = Directory.CreateTempSubdirectory("dbud-cat-").FullName;
        foreach (var file in new[] { "sql-warehouses.yaml", "not-modeled.yaml" })
        {
            File.Copy(Repo.Path("tests", "Fixtures", "catalog", file), Path.Combine(catalogDir, file));
        }

        Directory.CreateDirectory(Path.Combine(catalogDir, "node-types"));
        File.Copy(Repo.Path("tests", "Fixtures", "catalog", "node-types", "azure.yaml"), Path.Combine(catalogDir, "node-types", "azure.yaml"));

        var handler = new FakeHandler(request =>
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            return url.Contains("Virtual Machines", StringComparison.Ordinal)
                ? Json("""{ "Items": [ { "armSkuName": "Standard_DS3_v2", "meterName": "DS3 v2", "productName": "Virtual Machines DSv2 Series", "type": "Consumption", "unitOfMeasure": "1 Hour", "retailPrice": 0.272, "armRegionName": "westeurope" } ] }""")
                : Json("""{ "Items": [ { "meterName": "Premium Jobs Compute DBU", "type": "Consumption", "unitOfMeasure": "1 Hour", "retailPrice": 0.3, "armRegionName": "westeurope" }, { "meterName": "Premium Jobs Compute DBU", "type": "Consumption", "unitOfMeasure": "1 Hour", "retailPrice": 0.3, "armRegionName": "Global" } ] }""");
        });

        var summary = await DBUDoctor.CatalogGen.Program.GenerateAsync(new AzureRetailPricesClient(new HttpClient(handler)), catalogDir, null, new DateOnly(2026, 11, 8), TextWriter.Null);

        Assert.Equal(["westeurope.yaml"], Directory.GetFiles(Path.Combine(catalogDir, "prices", "azure")).Select(Path.GetFileName));
        Assert.Contains("`westeurope`: new region", summary);
        var text = File.ReadAllText(Path.Combine(catalogDir, "prices", "azure", "westeurope.yaml"));
        Assert.StartsWith("# Generated by tools/CatalogGen", text, StringComparison.Ordinal);
        Assert.Contains("Standard_DS3_v2: { payg: 0.272 }", text);
    }

    internal static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

public class PriceRefreshTests
{
    [Fact]
    public async Task Unreachable_api_falls_back_with_a_warning()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("no route to host"));
        var refresher = new PriceRefresher(new HttpClient(handler), Directory.CreateTempSubdirectory("dbud-cache-").FullName, TimeProvider.System);
        var catalog = TestCatalog.Load();

        var (result, warning) = await refresher.RefreshAsync(catalog, Cloud.Azure, "westeurope", CancellationToken.None);

        Assert.Same(catalog, result);
        Assert.Contains("Could not refresh Azure prices", warning);
    }

    [Fact]
    public async Task Refreshed_prices_are_cached_for_the_utc_day()
    {
        var handler = new FakeHandler(request =>
            Uri.UnescapeDataString(request.RequestUri!.ToString()).Contains("Virtual Machines", StringComparison.Ordinal)
                ? AzureRetailPricesContractTests.Json("""{ "Items": [] }""")
                : AzureRetailPricesContractTests.Json("""{ "Items": [ { "meterName": "Premium Jobs Compute DBU", "type": "Consumption", "unitOfMeasure": "1 Hour", "retailPrice": 0.31, "armRegionName": "westeurope" } ] }"""));
        var cache = Directory.CreateTempSubdirectory("dbud-cache-").FullName;
        var refresher = new PriceRefresher(new HttpClient(handler), cache, TimeProvider.System);

        var (first, _) = await refresher.RefreshAsync(TestCatalog.Load(), Cloud.Azure, "westeurope", CancellationToken.None);
        var calls = handler.Calls;
        var (second, _) = await refresher.RefreshAsync(TestCatalog.Load(), Cloud.Azure, "westeurope", CancellationToken.None);

        Assert.Equal(0.31m, first.PricesFor(Cloud.Azure, "westeurope")!.Dbu[Skus.JobsCompute].Value);
        Assert.Equal(0.31m, second.PricesFor(Cloud.Azure, "westeurope")!.Dbu[Skus.JobsCompute].Value);
        Assert.Equal(calls, handler.Calls);
        Assert.Single(Directory.GetFiles(cache));
    }

    [Fact]
    public async Task Other_clouds_are_not_refreshed()
    {
        var refresher = new PriceRefresher(new HttpClient(new FakeHandler(_ => throw new InvalidOperationException())), Path.GetTempPath(), TimeProvider.System);
        var (_, warning) = await refresher.RefreshAsync(TestCatalog.Load(), Cloud.Aws, "us-east-1", CancellationToken.None);
        Assert.Contains("Azure only", warning);
    }
}

internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, int throttleFirst = 0) : HttpMessageHandler
{
    public int Calls { get; private set; }

    public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Requests.Add((request.Method, request.RequestUri!.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        if (Calls <= throttleFirst)
        {
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        }

        var response = respond(request);
        response.RequestMessage = request;
        return response;
    }
}
