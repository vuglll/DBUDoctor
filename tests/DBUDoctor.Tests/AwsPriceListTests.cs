using System.Net;
using DBUDoctor.Core;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Pricing.Aws;

namespace DBUDoctor.Tests;

public class AwsPriceListTests
{
    private static readonly HashSet<string> Types = new(["m5.xlarge", "r5.large"], StringComparer.Ordinal);

    private static AwsPriceListNormalizer.Result Sample() =>
        AwsPriceListNormalizer.Normalize(Csv.ReadAll(File.ReadAllText(Repo.Path("tests", "Fixtures", "aws-price-list", "eu-north-1-sample.csv"))), Types);

    [Fact]
    public void On_demand_and_standard_no_upfront_reservations_are_read()
    {
        // Real rows from the eu-north-1 EC2 price list (2026-11-10), including Windows, dedicated
        // tenancy, capacity reservations, convertible and upfront offers, which must not count.
        var m5 = Sample().Prices["m5.xlarge"];

        Assert.Equal(0.204m, m5[Commitment.Payg]);
        Assert.Equal(0.129m, m5[Commitment.Reserved1y]);
        Assert.Equal(0.088m, m5[Commitment.Reserved3y]);
        Assert.Equal(3, m5.Count);
    }

    [Fact]
    public void Every_requested_type_in_the_sample_is_priced_without_conflicts()
    {
        var result = Sample();

        Assert.Equal(Types.Order(), result.Prices.Keys.Order());
        Assert.Empty(result.Conflicts);
        Assert.All(result.Prices.Values, p => Assert.True(p[Commitment.Payg] > p[Commitment.Reserved3y]));
    }

    [Fact]
    public void Types_outside_the_node_table_are_skipped()
    {
        var result = AwsPriceListNormalizer.Normalize(Csv.ReadAll(File.ReadAllText(Repo.Path("tests", "Fixtures", "aws-price-list", "eu-north-1-sample.csv"))), new HashSet<string>(["r5.large"]));
        Assert.Equal(["r5.large"], result.Prices.Keys);
    }

    private const string Header = "\"SKU\",\"TermType\",\"Unit\",\"PricePerUnit\",\"Currency\",\"LeaseContractLength\",\"PurchaseOption\",\"OfferingClass\",\"Product Family\",\"Instance Type\",\"Tenancy\",\"Operating System\",\"License Model\",\"operation\",\"CapacityStatus\",\"Pre Installed S/W\",\"MarketOption\"";

    private static string Row(string price, string term = "OnDemand", string type = "m5.xlarge") =>
        $"\"A\",\"{term}\",\"Hrs\",\"{price}\",\"USD\",\"\",\"\",\"\",\"Compute Instance\",\"{type}\",\"Shared\",\"Linux\",\"No License required\",\"RunInstances\",\"Used\",\"NA\",\"OnDemand\"";

    [Fact]
    public void Conflicting_values_are_left_out_and_reported()
    {
        var csv = string.Join("\n", "\"FormatVersion\",\"v1.0\"", Header, Row("0.192"), Row("0.200"), Row("0.192"));
        var result = AwsPriceListNormalizer.Normalize(Csv.ReadAll(csv), Types);

        Assert.Empty(result.Prices);
        Assert.Equal(["m5.xlarge payg: 0.192 and 0.2"], result.Conflicts);
    }

    [Fact]
    public void A_changed_layout_fails_instead_of_writing_an_empty_catalog()
    {
        var missingColumn = AwsPriceListNormalizer.Normalize;
        var ex = Assert.Throws<DoctorException>(() => missingColumn(Csv.ReadAll("\"SKU\",\"TermType\"\n\"A\",\"OnDemand\""), Types));
        Assert.Contains("`Unit`", ex.Message, StringComparison.Ordinal);

        Assert.Throws<DoctorException>(() => AwsPriceListNormalizer.Normalize(Csv.ReadAll("\"FormatVersion\",\"v1.0\""), Types));
    }

    [Fact]
    public void Csv_reader_handles_quotes_commas_and_line_breaks()
    {
        var records = Csv.ReadAll("\"a, b\",\"say \"\"hi\"\"\",plain\n\"two\nlines\",x").ToList();

        Assert.Equal(["a, b", "say \"hi\"", "plain"], records[0]);
        Assert.Equal(["two\nlines", "x"], records[1]);
    }

    [Fact]
    public async Task Region_index_gives_csv_urls()
    {
        const string index = """{ "regions": { "eu-north-1": { "regionCode": "eu-north-1", "currentVersionUrl": "/offers/v1.0/aws/AmazonEC2/20261110173852/eu-north-1/index.json" } } }""";
        var client = new AwsPriceListClient(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(index) })));

        var urls = await client.RegionCsvUrlsAsync();

        Assert.Equal("https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AmazonEC2/20261110173852/eu-north-1/index.csv", urls["eu-north-1"]);
    }

    [Fact]
    public void Vm_source_is_kept_apart_from_the_dbu_source()
    {
        var vmSource = new Provenance(new DateOnly(2026, 11, 10), AwsPriceListClient.RegionIndexUrl);
        var sheet = new PriceSheet
        {
            Cloud = Cloud.Aws,
            Region = "eu-north-1",
            Currency = "USD",
            Dbu = new Dictionary<string, CatalogValue> { [Skus.JobsCompute] = new(0.15m, vmSource) },
            Vm = new Dictionary<string, VmPrice> { ["m5.xlarge"] = new(new Dictionary<Commitment, decimal> { [Commitment.Payg] = 0.204m }, vmSource) },
            VmProvenance = vmSource,
        };

        var yaml = CatalogWriter.Prices(sheet, new Provenance(new DateOnly(2026, 11, 10), "https://dbu.example/AWS.json"), "test");
        var loaded = CatalogLoader.ParsePriceSheet("prices/aws/eu-north-1.yaml", yaml);

        Assert.Contains("vm_source: " + AwsPriceListClient.RegionIndexUrl, yaml, StringComparison.Ordinal);
        Assert.Equal(AwsPriceListClient.RegionIndexUrl, loaded.Vm["m5.xlarge"].Provenance.Source);
        Assert.Equal("https://dbu.example/AWS.json", loaded.Dbu[Skus.JobsCompute].Provenance.Source);
    }
}
