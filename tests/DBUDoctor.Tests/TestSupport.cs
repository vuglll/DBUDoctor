using System.Text.Json.Nodes;
using DBUDoctor.Core.Analysis;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Cost;
using DBUDoctor.Core.Model;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Reporting;
using DBUDoctor.Core.Rules;

namespace DBUDoctor.Tests;

internal static class Repo
{
    public static string Root { get; } = Find();

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    private static string Find()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir, "DBUDoctor.slnx")))
        {
            dir = System.IO.Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }
}

internal static class TestCatalog
{
    private static readonly Lazy<PriceCatalog> Fixture = new(() => CatalogLoader.LoadDirectory(Repo.Path("tests", "Fixtures", "catalog")));

    /// <summary>The fixed test catalog in tests/Fixtures/catalog.</summary>
    public static PriceCatalog Load() => Fixture.Value;

    /// <summary>A minimal catalog with exactly the values a scenario names.</summary>
    public static PriceCatalog Inline(string cloud, string region, string dbuYaml, string nodesYaml, string vmYaml = "{}", string photon = "method: none")
    {
        var files = new Dictionary<string, string>
        {
            [$"prices/{cloud}/{region}.yaml"] = $"cloud: {cloud}\nregion: {region}\nas_of: 2026-11-01\nsource: test\ndbu:\n{Indent(dbuYaml)}\nvm: {(vmYaml == "{}" ? "{}" : "\n" + Indent(vmYaml))}\n",
            [$"node-types/{cloud}.yaml"] = $"cloud: {cloud}\nas_of: 2026-11-01\nsource: test\nphoton:\n  {photon.Replace("\n", "\n  ", StringComparison.Ordinal)}\nnode_types:\n{Indent(nodesYaml)}\n",
            ["sql-warehouses.yaml"] = File.ReadAllText(Repo.Path("tests", "Fixtures", "catalog", "sql-warehouses.yaml")),
            ["not-modeled.yaml"] = File.ReadAllText(Repo.Path("tests", "Fixtures", "catalog", "not-modeled.yaml")),
        };
        return CatalogLoader.Load(files);
    }

    private static string Indent(string yaml) =>
        string.Join("\n", yaml.Trim().Split('\n').Select(l => "  " + l.Trim()));
}

/// <summary>Builds bundle JSON in tests.</summary>
internal static class Bundle
{
    public const string AzureHost = "https://adb-1.2.azuredatabricks.net";
    public const string AwsHost = "https://dbc-1234.cloud.databricks.com";

    public static ResolvedBundle Of(string resourcesJson, string host = AzureHost, string name = "b", string target = "prod")
    {
        var json = $$"""{ "bundle": { "name": "{{name}}", "target": "{{target}}" }, "workspace": { "host": "{{host}}" }, "resources": {{resourcesJson}} }""";
        return ResolvedBundle.Parse(json, "test");
    }

    public static ResolvedBundle Job(string jobJson, string host = AzureHost) => Of($$"""{ "jobs": { "j": {{jobJson}} } }""", host);

    public static string Cluster(string node = "Standard_DS3_v2", int workers = 4, string extra = "") =>
        $$"""{ "spark_version": "15.4.x-scala2.12", "node_type_id": "{{node}}", "num_workers": {{workers}}{{extra}} }""";

    public static string ClassicJob(string schedule = """{ "quartz_cron_expression": "0 0 * * * ?", "timezone_id": "UTC" }""", string cluster = "") =>
        $$"""
        {
          "job_clusters": [ { "job_cluster_key": "main", "new_cluster": {{(cluster.Length > 0 ? cluster : Cluster())}} } ],
          "tasks": [ { "task_key": "t", "job_cluster_key": "main", "notebook_task": { "notebook_path": "/x" } } ],
          "schedule": {{schedule}}
        }
        """;
}

internal static class Run
{
    public static readonly DateTimeOffset Reference = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static DoctorConfig Config(string yaml = "region: westeurope") =>
        ConfigLoader.Parse(yaml, ".dbudoctor.yml", RuleRegistry.Parameters);

    public static BundleReport Analyze(ResolvedBundle baseBundle, ResolvedBundle head, DoctorConfig? config = null, PriceCatalog? catalog = null, bool reportExisting = false) =>
        BundleAnalyzer.Analyze(new BundleInput("b", "prod", baseBundle, head), config ?? Config(), catalog ?? TestCatalog.Load(), Reference, reportExisting);

    public static CostEngine Engine(DoctorConfig? config = null, PriceCatalog? catalog = null, Cloud? cloud = Cloud.Azure, string? region = "westeurope")
    {
        config ??= Config();
        return new CostEngine(new PricingContext(catalog ?? TestCatalog.Load(), cloud, region, config.Pricing, config.Estimation), Reference);
    }

    public static JsonNode Json(BundleReport report) =>
        JsonNode.Parse(JsonRenderer.Render(new RunReport { Bundles = [report] }, "0.0.0-test"))!;
}

internal static class FindingFilters
{
    /// <summary>The findings of one rule, so a rule's tests ignore what other rules report on the same bundle.</summary>
    public static List<Finding> Of(this IEnumerable<Finding> findings, string ruleId) =>
        findings.Where(f => f.RuleId == ruleId).ToList();
}
