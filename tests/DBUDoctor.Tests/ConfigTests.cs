using System.Text.Json;
using DBUDoctor.Core;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Rules;

namespace DBUDoctor.Tests;

public class ConfigTests
{
    private static DoctorConfig Parse(string yaml) => ConfigLoader.Parse(yaml, ".dbudoctor.yml", RuleRegistry.Parameters);

    [Fact]
    public void Instance_pools_map_ids_to_node_types()
    {
        var config = Run.Config("estimation:\n  instance_pools:\n    0101-120000-pool-abc: Standard_DS3_v2\n    pool-b: m5.xlarge\n");
        Assert.Equal("Standard_DS3_v2", config.Estimation.InstancePools["0101-120000-pool-abc"]);
        Assert.Equal("m5.xlarge", config.Estimation.InstancePools["pool-b"]);
    }

    [Theory]
    [InlineData("estimation: { instance_pools: [ a ] }")]
    [InlineData("estimation: { instance_pools: { pool-a: \"\" } }")]
    [InlineData("estimation: { instance_pools: { pool-a: [ x ] } }")]
    public void Instance_pools_with_the_wrong_shape_are_rejected(string yaml)
    {
        var ex = Assert.Throws<DoctorException>(() => Run.Config(yaml));
        Assert.Contains("estimation.instance_pools", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_key_names_the_key_and_line()
    {
        var ex = Assert.Throws<DoctorException>(() => Parse("target: prod\nregion: westeurope\npricng:\n  surcharge: 0\n"));
        Assert.Contains("unknown key `pricng`", ex.Message);
        Assert.Contains(".dbudoctor.yml:3", ex.Message);
    }

    [Fact]
    public void Invalid_commitment_lists_the_allowed_values()
    {
        var ex = Assert.Throws<DoctorException>(() => Parse("pricing:\n  infra:\n    commitment: reserved_5y\n"));
        Assert.Contains("reserved_5y", ex.Message);
        Assert.Contains("payg, spot, reserved_1y, reserved_3y, savings_plan_1y, savings_plan_3y", ex.Message);
    }

    [Fact]
    public void No_file_means_defaults()
    {
        var config = ConfigLoader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".dbudoctor.yml"), explicitPath: false, RuleRegistry.Parameters);

        Assert.Null(config.Target);
        Assert.Equal(AutoscaleAssumption.Max, config.Estimation.Autoscale);
        Assert.Equal(ScalingAssumption.ConstantRuntime, config.Estimation.Scaling);
        Assert.Null(config.Estimation.DefaultRunMinutes);
        Assert.Equal(InfraMode.Auto, config.Pricing.IncludeCloudInfra);
        Assert.Equal(Commitment.Payg, config.Pricing.Commitment);
        Assert.Equal(0, config.Pricing.Surcharge);
        Assert.Equal("USD", config.Currency);
    }

    [Fact]
    public void Explicit_missing_file_is_an_error()
    {
        Assert.Throws<DoctorException>(() => ConfigLoader.Load("/nope/.dbudoctor.yml", explicitPath: true, RuleRegistry.Parameters));
    }

    [Fact]
    public void Design_example_parses()
    {
        var config = Parse("""
            target: prod
            bundles: [etl, bi]
            cloud: azure
            region: westeurope
            currency: USD
            pricing:
              discounts:
                all: 0.20
                sql: 0.25
              surcharge: 0.0
              overrides:
                PREMIUM_JOBS_COMPUTE: 0.24
              include_cloud_infra: auto
              infra:
                commitment: payg
            estimation:
              autoscale: max
              scaling: constant_runtime
              default_run_minutes: 20
            rules:
              DBU010: { factor: 4, min_runs: 100 }
            suppressions:
              - rule: DBU001
                resource: jobs.legacy_*
                reason: Moving off the shared cluster in Q1 (JIRA-123)
            report:
              comment_on_no_change: false
            fail_on:
              monthly_increase: 500
              percent_increase: 25
              severity: error
              rules: [DBU004]
              unknown_cost: false
            """);

        Assert.Equal(["etl", "bi"], config.Bundles);
        Assert.Equal(Cloud.Azure, config.Cloud);
        Assert.Equal(0.20m, config.Pricing.Discounts["all"]);
        Assert.Equal(0.24m, config.Pricing.Overrides["PREMIUM_JOBS_COMPUTE"]);
        Assert.Equal(20m, config.Estimation.DefaultRunMinutes);
        Assert.Equal(4m, config.Rules["DBU010"].Parameters["factor"]);
        Assert.Equal("jobs.legacy_*", Assert.Single(config.Suppressions).Resource);
        Assert.Equal(Severity.Error, config.FailOn.Severity);
        Assert.Equal(["DBU004"], config.FailOn.Rules);
    }

    [Fact]
    public void Suppression_without_reason_names_the_suppression()
    {
        var ex = Assert.Throws<DoctorException>(() => Parse("suppressions:\n  - rule: DBU001\n    resource: jobs.legacy\n"));
        Assert.Contains("suppressions[0] (DBU001 for jobs.legacy)", ex.Message);
        Assert.Contains("no `reason`", ex.Message);
    }

    [Fact]
    public void Unknown_rule_parameter_is_rejected()
    {
        var ex = Assert.Throws<DoctorException>(() => Parse("rules:\n  DBU010: { factr: 4 }\n"));
        Assert.Contains("factr", ex.Message);
        Assert.Contains("factor, min_runs", ex.Message);
    }

    [Fact]
    public void Flag_overrides_environment_which_overrides_file()
    {
        var env = new Dictionary<string, string?> { ["DBUDOCTOR_TARGET"] = "env", ["DBUDOCTOR_REGION"] = "eastus" };

        var fromFlag = Parse("target: staging\nregion: westeurope\n");
        SettingsResolver.Apply(fromFlag, new CliFlags { Target = "prod" }, env);
        Assert.Equal("prod", fromFlag.Target);
        Assert.Equal("eastus", fromFlag.Region);

        var fromEnv = Parse("target: staging\n");
        SettingsResolver.Apply(fromEnv, new CliFlags(), env);
        Assert.Equal("env", fromEnv.Target);

        var fromFile = Parse("target: staging\n");
        SettingsResolver.Apply(fromFile, new CliFlags(), new Dictionary<string, string?>());
        Assert.Equal("staging", fromFile.Target);
    }

    [Fact]
    public void Run_options_follow_flags_then_environment()
    {
        var env = new Dictionary<string, string?> { ["DBUDOCTOR_FORMAT"] = "json", ["DBUDOCTOR_REPORT_EXISTING"] = "true" };
        var options = SettingsResolver.Apply(new DoctorConfig(), new CliFlags { Format = "markdown" }, env);

        Assert.Equal("markdown", options.Format);
        Assert.True(options.ReportExisting);
    }

    [Theory]
    [InlineData("adb-123.4.azuredatabricks.net", Cloud.Azure)]
    [InlineData("https://adb-123.4.azuredatabricks.net/", Cloud.Azure)]
    [InlineData("https://1234.5.gcp.databricks.com", Cloud.Gcp)]
    [InlineData("https://dbc-a1b2.cloud.databricks.com", Cloud.Aws)]
    public void Cloud_is_inferred_from_the_workspace_host(string host, Cloud expected)
    {
        Assert.Equal(expected, SettingsResolver.InferCloud(host));
    }

    [Fact]
    public void Missing_region_makes_rates_unknown_and_asks_for_region()
    {
        var head = Bundle.Of("""{ "sql_warehouses": { "w": { "cluster_size": "Small", "enable_serverless_compute": true } } }""");
        var report = Run.Analyze(Bundle.Of("{}"), head, Run.Config("estimation: { default_run_minutes: 20 }"));

        Assert.Null(Assert.Single(report.Changes).RateAfter);
        Assert.Contains(report.Warnings, w => w.Contains("`region` is not set", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_rule_and_parameter_is_in_the_published_schema()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Repo.Path("schemas", "dbudoctor.schema.json")));
        var rules = schema.RootElement.GetProperty("properties").GetProperty("rules").GetProperty("properties");
        foreach (var rule in DBUDoctor.Core.Rules.RuleRegistry.All)
        {
            Assert.True(rules.TryGetProperty(rule.Metadata.Id, out var entry), $"{rule.Metadata.Id} is missing from schemas/dbudoctor.schema.json");
            var names = rule.Metadata.Parameters.Select(p => p.Name).Concat(rule.Metadata.ListParameters.Select(p => p.Name)).ToList();
            if (names.Count == 0)
            {
                continue;
            }

            var properties = entry.GetProperty("properties");
            foreach (var name in names)
            {
                Assert.True(properties.TryGetProperty(name, out _), $"rules.{rule.Metadata.Id}.{name} is missing from the schema");
            }
        }
    }

    [Fact]
    public void Every_loader_key_is_in_the_published_schema()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Repo.Path("schemas", "dbudoctor.schema.json")));
        var paths = new HashSet<string>(StringComparer.Ordinal);
        Collect(schema.RootElement, "", paths);

        foreach (var known in ConfigLoader.KnownPaths.Where(p => !p.Contains('*', StringComparison.Ordinal)))
        {
            Assert.Contains(known.Replace("[]", "", StringComparison.Ordinal), paths);
        }

        static void Collect(JsonElement node, string prefix, HashSet<string> paths)
        {
            if (node.TryGetProperty("properties", out var properties))
            {
                foreach (var p in properties.EnumerateObject())
                {
                    var path = prefix.Length == 0 ? p.Name : $"{prefix}.{p.Name}";
                    paths.Add(path);
                    Collect(p.Value, path, paths);
                }
            }

            if (node.TryGetProperty("items", out var items))
            {
                Collect(items, prefix, paths);
            }
        }
    }
}
