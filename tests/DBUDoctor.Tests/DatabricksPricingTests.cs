using DBUDoctor.Core;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Pricing.Aws;
using DBUDoctor.Core.Pricing.Databricks;

namespace DBUDoctor.Tests;

public class DatabricksPricingContractTests
{
    private static readonly Provenance Source = new(new DateOnly(2026, 11, 9), "test");

    private static PricingRow Row(string compute, string plan, string rate, string? region = null, string instance = "m5.xlarge", string dbu = "0.690") =>
        new() { Cloud = "AWS", Compute = compute, Plan = plan, BaseRate = rate, Region = region, Instance = instance, DbuRate = dbu };

    [Theory]
    [InlineData("Jobs Compute", Skus.JobsCompute)]
    [InlineData("Jobs Compute Photon", Skus.JobsComputePhoton)]
    [InlineData("All-Purpose Compute", Skus.AllPurposeCompute)]
    [InlineData("All-Purpose Compute Photon", Skus.AllPurposeComputePhoton)]
    [InlineData("DLT Core Compute", Skus.DltCore)]
    [InlineData("DLT Pro Compute Photon", Skus.DltProPhoton)]
    [InlineData("DLT Advanced Compute", Skus.DltAdvanced)]
    [InlineData("SQL Compute", Skus.SqlClassic)]
    [InlineData("SQL Pro Compute", Skus.SqlPro)]
    [InlineData("SQL Serverless Compute", Skus.SqlServerless)]
    [InlineData("Jobs Serverless Compute", Skus.JobsServerless)]
    [InlineData("Interactive Serverless Compute", Skus.AllPurposeServerless)]
    public void Workload_names_map_to_skus(string compute, string sku)
    {
        Assert.Equal(sku, DatabricksPricingNormalizer.ComputeToSku[compute]);
    }

    [Fact]
    public void Global_prices_apply_to_every_region_and_regional_rows_override_them()
    {
        var result = DatabricksPricingNormalizer.Normalize(Cloud.Aws,
        [
            Row("Jobs Compute", "Premium", "0.15"),
            Row("Jobs Compute", "Enterprise", "0.20"),
            Row("SQL Compute", "Premium", "0.22", region: "n/a", instance: "Small", dbu: "12"),
            Row("SQL Pro Compute", "Premium", "0.55", region: "US East (N. Virginia)", instance: "Small", dbu: "12"),
            Row("SQL Pro Compute", "Premium", "0.62", region: "AP (Seoul)", instance: "Small", dbu: "12"),
        ], Source);

        Assert.Equal(["ap-northeast-2", "us-east-1"], result.Regions.Keys);
        var east = result.Regions["us-east-1"];
        Assert.Equal(0.15m, east.Dbu[Skus.JobsCompute].Value);
        Assert.Equal(0.20m, east.DbuEnterprise[Skus.JobsCompute].Value);
        Assert.Equal(0.22m, east.Dbu[Skus.SqlClassic].Value);
        Assert.Equal(0.55m, east.Dbu[Skus.SqlPro].Value);
        Assert.Equal(0.62m, result.Regions["ap-northeast-2"].Dbu[Skus.SqlPro].Value);
        Assert.Empty(east.Vm);
    }

    [Fact]
    public void Lakebase_rows_price_the_database_serverless_sku()
    {
        var result = DatabricksPricingNormalizer.Normalize(Cloud.Aws,
        [
            Row("Jobs Compute", "Premium", "0.15"),
            Row("Lakebase", "Premium", "0.45", region: "AP (Mumbai)", instance: "Database Capacity Unit", dbu: "0.230"),
        ], Source);

        Assert.Equal(0.45m, result.Regions["ap-south-1"].Dbu[Skus.DatabaseServerless].Value);
        Assert.DoesNotContain("Database Capacity Unit", result.NodeTypes.Keys);
    }

    [Fact]
    public void Gov_sap_and_unpriced_workloads_are_left_out()
    {
        var result = DatabricksPricingNormalizer.Normalize(Cloud.Aws,
        [
            Row("Jobs Compute", "Premium", "0.15", region: "US East (N. Virginia)"),
            Row("Jobs Compute - Gov", "Premium", "0.25", region: "US East (N. Virginia)"),
            Row("SQL Serverless Compute - SAP", "Premium", "1.24", region: "US East (N. Virginia)"),
            Row("Jobs Light Compute", "Premium", "0.10"),
            Row("Jobs Compute", "Premium", "0.99", region: "US Gov West 1"),
            Row("Brand New Compute", "Premium", "9"),
        ], Source);

        Assert.Equal(["us-east-1"], result.Regions.Keys);
        Assert.Equal(0.15m, result.Regions["us-east-1"].Dbu[Skus.JobsCompute].Value);
        Assert.Equal(["Brand New Compute"], result.UnmappedComputes);
    }

    [Fact]
    public void Conflicting_prices_are_dropped_and_reported()
    {
        var result = DatabricksPricingNormalizer.Normalize(Cloud.Gcp,
        [
            new PricingRow { Compute = "SQL Pro Compute", Plan = "Enterprise", BaseRate = "0.69", Region = "Asia-Southeast (Singapore)", Instance = "Small", DbuRate = "12" },
            new PricingRow { Compute = "SQL Pro Compute", Plan = "Enterprise", BaseRate = "0.74", Region = "Asia (Singapore)", Instance = "Small", DbuRate = "12" },
            new PricingRow { Compute = "SQL Pro Compute", Plan = "Premium", BaseRate = "0.690", Region = "Asia (Singapore)", Instance = "Small", DbuRate = "12" },
            new PricingRow { Compute = "SQL Pro Compute", Plan = "Premium", BaseRate = "0.69", Region = "Asia-Southeast (Singapore)", Instance = "Small", DbuRate = "12" },
        ], Source);

        var sheet = result.Regions["asia-southeast1"];
        Assert.False(sheet.DbuEnterprise.ContainsKey(Skus.SqlPro));
        Assert.Equal(0.69m, sheet.Dbu[Skus.SqlPro].Value);
        Assert.Contains("gcp asia-southeast1 enterprise PREMIUM_SQL_PRO_COMPUTE: 0.69 vs 0.74", result.Conflicts);
    }

    [Fact]
    public void Node_types_come_with_photon_counts()
    {
        var result = DatabricksPricingNormalizer.Normalize(Cloud.Aws,
        [
            Row("Jobs Compute", "Premium", "0.15", instance: "m5.xlarge", dbu: "0.690"),
            Row("All-Purpose Compute", "Premium", "0.55", instance: "m5.xlarge", dbu: "0.690"),
            Row("Jobs Compute Photon", "Premium", "0.15", instance: "m5.xlarge (Photon)", dbu: "2.001"),
            Row("All-Purpose Compute Photon", "Premium", "0.55", instance: "m5.xlarge (Photon)", dbu: "1.380"),
            Row("Jobs Compute", "Premium", "0.15", instance: "x.big", dbu: "1,042.00"),
        ], Source);

        var node = result.NodeTypes["m5.xlarge"];
        Assert.Equal(0.69m, node.Dbu);
        Assert.Null(node.DbuAllPurpose);
        Assert.Equal(2.001m, node.PhotonDbuJobs);
        Assert.Equal(1.38m, node.PhotonDbuAllPurpose);
        Assert.Equal(1042m, result.NodeTypes["x.big"].Dbu);
    }

    [Fact]
    public void Unknown_region_names_are_reported()
    {
        var result = DatabricksPricingNormalizer.Normalize(Cloud.Aws, [Row("SQL Pro Compute", "Premium", "0.55", region: "Moon (Base 1)")], Source);
        Assert.Equal(["Moon (Base 1)"], result.UnmappedRegions);
    }

    [Fact]
    public void Generated_files_round_trip_through_the_loader()
    {
        var result = DatabricksPricingNormalizer.Normalize(Cloud.Aws,
        [
            Row("Jobs Compute", "Premium", "0.15", region: "US East (N. Virginia)"),
            Row("Jobs Compute", "Enterprise", "0.20", region: "US East (N. Virginia)"),
            Row("Jobs Compute Photon", "Premium", "0.15", instance: "m5.xlarge (Photon)", dbu: "2.001"),
            Row("Jobs Compute", "Premium", "0.15"),
        ], Source);

        var catalog = CatalogLoader.Load(new Dictionary<string, string>
        {
            ["prices/aws/us-east-1.yaml"] = CatalogWriter.Prices(result.Regions["us-east-1"], Source, "h"),
            ["node-types/aws.yaml"] = CatalogWriter.NodeTypes(Cloud.Aws, result.NodeTypes, Source, "h"),
            ["sql-warehouses.yaml"] = File.ReadAllText(Repo.Path("catalog", "sql-warehouses.yaml")),
            ["not-modeled.yaml"] = File.ReadAllText(Repo.Path("catalog", "not-modeled.yaml")),
        });

        Assert.Equal(0.20m, catalog.PricesFor(Cloud.Aws, "us-east-1")!.DbuEnterprise[Skus.JobsCompute].Value);
        Assert.Equal(PhotonMethod.DbuCount, catalog.NodeTypesFor(Cloud.Aws)!.PhotonMethod);
        Assert.Equal(2.001m, catalog.NodeTypesFor(Cloud.Aws)!.NodeTypes["m5.xlarge"].PhotonDbuJobs);
    }
}

public class EmbeddedAwsGcpCatalogTests
{
    private static readonly PriceCatalog Catalog = CatalogLoader.LoadEmbedded();

    [Fact]
    public void Aws_region_prices_jobs_compute_and_rates_m5_xlarge()
    {
        var engine = Run.Engine(Run.Config("region: us-east-1"), Catalog, Cloud.Aws, "us-east-1");
        var job = Bundle.Job(Bundle.ClassicJob(cluster: Bundle.Cluster("m5.xlarge", 4)), Bundle.AwsHost).Document.Resources!.Jobs!["j"];

        var estimate = engine.EstimateJob(job);

        Assert.Equal(0.15m, estimate.PricePerDbu);
        Assert.Equal(0.69m * 5, estimate.DbuPerHour);
        Assert.True(estimate.InfraIncluded);
        Assert.Equal(Catalog.PricesFor(Cloud.Aws, "us-east-1")!.Vm["m5.xlarge"].For(Commitment.Payg) * 5, estimate.VmCostPerHour);
    }

    [Fact]
    public void Photon_counts_are_published_for_aws_and_gcp()
    {
        Assert.Equal(PhotonMethod.DbuCount, Catalog.NodeTypesFor(Cloud.Aws)!.PhotonMethod);
        Assert.True(Catalog.NodeTypesFor(Cloud.Aws)!.NodeTypes.Values.Count(n => n.PhotonDbuJobs is not null) > 100);
        Assert.True(Catalog.NodeTypesFor(Cloud.Gcp)!.NodeTypes.Values.Count(n => n.PhotonDbuJobs is not null) > 50);
    }

    [Fact]
    public void Gcp_serverless_prices_are_per_region()
    {
        var tokyo = Catalog.PricesFor(Cloud.Gcp, "asia-northeast1")!.Dbu[Skus.SqlServerless].Value;
        var iowa = Catalog.PricesFor(Cloud.Gcp, "us-central1")!.Dbu[Skus.SqlServerless].Value;
        Assert.NotEqual(tokyo, iowa);
    }

    [Fact]
    public void Aws_and_gcp_have_both_tiers_and_only_aws_has_vm_prices()
    {
        foreach (var cloud in new[] { Cloud.Aws, Cloud.Gcp })
        {
            foreach (var region in Catalog.RegionsFor(cloud))
            {
                var sheet = Catalog.PricesFor(cloud, region)!;
                Assert.True(sheet.Dbu.ContainsKey(Skus.JobsCompute), $"{cloud} {region}: no Premium Jobs compute");
                Assert.True(sheet.DbuEnterprise.ContainsKey(Skus.JobsCompute), $"{cloud} {region}: no Enterprise Jobs compute");
                if (cloud == Cloud.Gcp)
                {
                    Assert.Empty(sheet.Vm);
                }
                else
                {
                    Assert.True(sheet.Vm.Count > 200, $"{cloud} {region}: only {sheet.Vm.Count} VM prices");
                    Assert.All(sheet.Vm.Values, v => Assert.Equal(AwsPriceListClient.RegionIndexUrl, v.Provenance.Source));
                    Assert.All(sheet.Vm.Values, v => Assert.NotNull(v.For(Commitment.Payg)));
                }
            }
        }
    }
}

public class TierTests
{
    private static DBUDoctor.Core.Cost.CostEstimate Price(string config, Cloud cloud, string region, string host)
    {
        var job = Bundle.Job(Bundle.ClassicJob(cluster: Bundle.Cluster(cloud == Cloud.Aws ? "m5.xlarge" : "Standard_DS3_v2", 1)), host).Document.Resources!.Jobs!["j"];
        return Run.Engine(Run.Config($"region: {region}\n{config}"), CatalogLoader.LoadEmbedded(), cloud, region).EstimateJob(job);
    }

    [Fact]
    public void Enterprise_on_aws_uses_the_enterprise_price_with_discounts()
    {
        Assert.Equal(0.20m, Price("pricing: { tier: enterprise }", Cloud.Aws, "us-east-1", Bundle.AwsHost).PricePerDbu);
        Assert.Equal(0.16m, Price("pricing: { tier: enterprise, discounts: { all: 0.20 } }", Cloud.Aws, "us-east-1", Bundle.AwsHost).PricePerDbu);
        Assert.Equal(0.15m, Price("", Cloud.Aws, "us-east-1", Bundle.AwsHost).PricePerDbu);
    }

    [Fact]
    public void Enterprise_on_azure_is_unknown_with_a_reason()
    {
        var estimate = Price("pricing: { tier: enterprise }", Cloud.Azure, "westeurope", Bundle.AzureHost);
        Assert.Null(estimate.RatePerHour);
        Assert.Contains(estimate.UnknownReasons, r => r.Contains("Azure has the Premium tier only", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_tier_lists_the_allowed_values()
    {
        var ex = Assert.Throws<DoctorException>(() => Run.Config("pricing: { tier: standard }"));
        Assert.Contains("premium, enterprise", ex.Message);
    }

    [Fact]
    public void Tier_is_in_the_report()
    {
        var report = Run.Analyze(Bundle.Of("{}", Bundle.AwsHost), Bundle.Of("{}", Bundle.AwsHost), Run.Config("region: us-east-1\npricing: { tier: enterprise }"), CatalogLoader.LoadEmbedded());
        Assert.Contains(report.GlobalAssumptions, a => a.Contains("Enterprise tier", StringComparison.Ordinal));
        Assert.Equal("enterprise", (string)Run.Json(report)["bundles"]![0]!["tier"]!);
    }
}
