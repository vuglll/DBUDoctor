using System.Text.Json;
using System.Text.Json.Nodes;
using DBUDoctor.Core.Model;
using DBUDoctor.Core.Reporting;
using DBUDoctor.Core.Skill;

namespace DBUDoctor.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(1657.1, "$1,657")]
    [InlineData(12.604, "$12.60")]
    [InlineData(99.995, "$100")]
    [InlineData(0.125, "$0.13")]
    [InlineData(-12.6, "-$12.60")]
    [InlineData(100.5, "$101")]
    public void Money(decimal value, string expected)
    {
        Assert.Equal(expected, Format.Money(value));
    }

    [Theory]
    [InlineData(1657.1, "+$1,657")]
    [InlineData(-552.37, "-$552")]
    [InlineData(0.001, "$0")]
    public void Signed_money(decimal value, string expected)
    {
        Assert.Equal(expected, Format.SignedMoney(value));
    }

    [Fact]
    public void Totals_come_from_unrounded_values()
    {
        // Two rows of 0.004 each: rounded rows show $0.00, the total of the unrounded values is 0.008.
        Assert.Equal("$0.01", Format.Money(0.004m + 0.004m));
    }
}

public class ReportTests
{
    [Fact]
    public void No_cost_relevant_changes()
    {
        var bundle = Bundle.Job(Bundle.ClassicJob());
        var report = Run.Analyze(bundle, bundle);

        Assert.False(report.HasCostRelevantChanges);
        Assert.Contains("No cost-relevant changes.", MarkdownRenderer.Render(report));
        Assert.Contains("no cost-relevant changes", ConsoleRenderer.Render(report));
    }

    [Fact]
    public void Rate_only_change_is_not_in_the_headline()
    {
        var report = Run.Analyze(
            Bundle.Of("""{ "sql_warehouses": { "bi_wh": { "cluster_size": "Small", "enable_serverless_compute": true } } }"""),
            Bundle.Of("""{ "sql_warehouses": { "bi_wh": { "cluster_size": "Medium", "enable_serverless_compute": true } } }"""));

        Assert.Empty(report.HeadlineRows);
        Assert.Contains("- sql_warehouse `bi_wh`: cluster_size `Small` → `Medium`, $8.40 → $16.80 per active hour", MarkdownRenderer.Render(report));
    }

    [Fact]
    public void Classic_to_serverless_names_the_switch_and_keeps_units_on_amounts()
    {
        var classic = Bundle.Job("""
            {
              "job_clusters": [ { "job_cluster_key": "main", "new_cluster": { "spark_version": "15.4.x-scala2.12", "node_type_id": "Standard_DS3_v2", "num_workers": 4 } } ],
              "tasks": [ { "task_key": "t", "job_cluster_key": "main", "notebook_task": { "notebook_path": "/x" } } ]
            }
            """);
        var serverless = Bundle.Job("""{ "tasks": [ { "task_key": "t", "environment_key": "default", "notebook_task": { "notebook_path": "/x" } } ] }""");

        var markdown = MarkdownRenderer.Render(Run.Analyze(classic, serverless));

        Assert.Contains("- job `j`: classic → serverless; ", markdown);
        Assert.Matches(@"\$[0-9.]+ per active hour → needs history", markdown);
    }

    [Fact]
    public void Everything_new_shows_no_percentage()
    {
        var report = Run.Analyze(Bundle.Of("{}"), Bundle.Job(Bundle.ClassicJob()), Run.Config("region: westeurope\nestimation: { default_run_minutes: 20 }"));

        Assert.Null(report.HeadlinePercent);
        var markdown = MarkdownRenderer.Render(report);
        Assert.Contains("**+$552 / month**\n", markdown);
        Assert.DoesNotContain("on changed resources", markdown);
    }

    [Fact]
    public void Azure_footer_lists_what_is_not_modeled()
    {
        var markdown = MarkdownRenderer.Render(Run.Analyze(Bundle.Of("{}"), Bundle.Of("{}")));
        Assert.Contains("Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress", markdown);
        Assert.Contains("List-price estimate. Not an invoice.", markdown);
        Assert.DoesNotContain("cloud VMs for classic compute", markdown);
    }

    [Fact]
    public void Footer_lists_vms_without_vm_prices()
    {
        var report = Run.Analyze(Bundle.Of("{}", Bundle.AwsHost), Bundle.Of("{}", Bundle.AwsHost), Run.Config("region: us-east-1"));
        Assert.Contains("cloud VMs for classic compute", MarkdownRenderer.Render(report));
    }

    [Fact]
    public void Sticky_marker_is_the_first_line()
    {
        var markdown = MarkdownRenderer.Render(Run.Analyze(Bundle.Of("{}"), Bundle.Of("{}")));
        Assert.StartsWith("<!-- dbudoctor:bundle=b;target=prod -->\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_is_deterministic_and_ordered()
    {
        static ResolvedBundle Head() => Bundle.Of("""
            { "pipelines": { "z": { "continuous": true }, "a": { "continuous": true } },
              "jobs": { "y": { "tasks": [ { "task_key": "t", "existing_cluster_id": "c" } ] } } }
            """);

        var first = Run.Analyze(Bundle.Of("{}"), Head());
        var second = Run.Analyze(Bundle.Of("{}"), Head());
        Assert.Equal(MarkdownRenderer.Render(first), MarkdownRenderer.Render(second));
        Assert.Equal(JsonRenderer.Render(new RunReport { Bundles = [first] }, "x"), JsonRenderer.Render(new RunReport { Bundles = [second] }, "x"));
        Assert.Equal(["jobs.y", "pipelines.a", "pipelines.z"], first.Changes.Select(c => $"{c.Type}.{c.Key}"));
        Assert.Equal(["DBU001", "DBU004", "DBU004", "DBU007"], first.Findings.Select(f => f.RuleId));
    }
}

public class PolicyTests
{
    private static readonly string Hourly = Bundle.ClassicJob();
    private static readonly string Quarter = Bundle.ClassicJob(schedule: """{ "quartz_cron_expression": "0 0/15 * * * ?", "timezone_id": "UTC" }""");

    [Fact]
    public void Budget_threshold_violated()
    {
        var report = Run.Analyze(Bundle.Job(Hourly), Bundle.Job(Quarter), Run.Config("region: westeurope\nestimation: { default_run_minutes: 20 }\nfail_on: { monthly_increase: 500 }"));

        var violation = Assert.Single(report.Violations);
        Assert.Equal("fail_on.monthly_increase", violation.Policy);
        Assert.Equal(1, new RunReport { Bundles = [report] }.ExitCode);
        Assert.Contains("**Policy violated**", MarkdownRenderer.Render(report));
    }

    [Fact]
    public void Low_confidence_increase_does_not_violate()
    {
        var empty = Run.Analyze(Bundle.Of("{}"), Bundle.Of("{}"));
        var low = new Core.Cost.CostComparison
        {
            Type = "jobs", Key = "j", Kind = Core.Diff.ChangeKind.Modified, Summary = "s",
            Before = new Core.Cost.CostEstimate { RatePerHour = 1, Monthly = 100, Confidence = Core.Cost.Confidence.Medium },
            After = new Core.Cost.CostEstimate { RatePerHour = 9, Monthly = 5000, Confidence = Core.Cost.Confidence.Low },
            Confidence = Core.Cost.Confidence.Low,
        };
        var report = new BundleReport
        {
            Bundle = "b", Target = "prod", Currency = "USD", InfraIncluded = false, Commitment = Core.Config.Commitment.Payg,
            Autoscale = Core.Config.AutoscaleAssumption.Max, Scaling = Core.Config.ScalingAssumption.ConstantRuntime,
            Changes = [low], PossibleRenames = [], Unpriced = [], Findings = [], SuppressedCount = 0, ExistingHidden = 0,
            NotModeled = empty.NotModeled, Warnings = [],
        };

        PolicyEvaluator.Evaluate(report, new Core.Config.FailOnSettings { MonthlyIncrease = 500 });

        Assert.Empty(report.Violations);
        Assert.Equal(0, report.HeadlineDelta);
        Assert.Equal(1, report.ExcludedFromHeadline);
    }

    [Fact]
    public void Percentage_is_skipped_when_everything_is_new()
    {
        var report = Run.Analyze(Bundle.Of("{}"), Bundle.Job(Hourly), Run.Config("region: westeurope\nestimation: { default_run_minutes: 20 }\nfail_on: { percent_increase: 25 }"));

        Assert.Empty(report.Violations);
        Assert.Contains("fail_on.percent_increase was not evaluated", Assert.Single(report.PolicyNotes));
    }

    [Fact]
    public void Percentage_violated()
    {
        var report = Run.Analyze(Bundle.Job(Hourly), Bundle.Job(Quarter), Run.Config("region: westeurope\nestimation: { default_run_minutes: 20 }\nfail_on: { percent_increase: 25 }"));
        Assert.Equal("fail_on.percent_increase", Assert.Single(report.Violations).Policy);
    }

    [Fact]
    public void Failing_rule()
    {
        var report = Run.Analyze(
            Bundle.Of("""{ "pipelines": { "p": { "continuous": false } } }"""),
            Bundle.Of("""{ "pipelines": { "p": { "continuous": true } } }"""),
            Run.Config("region: westeurope\nfail_on: { rules: [DBU004] }"));

        Assert.Equal("fail_on.rules", Assert.Single(report.Violations).Policy);
    }

    [Fact]
    public void Severity_policy()
    {
        var head = Bundle.Of("""{ "pipelines": { "p": { "continuous": true } } }""");
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), head, Run.Config("region: westeurope\nfail_on: { severity: error }")).Violations);
        Assert.Single(Run.Analyze(Bundle.Of("{}"), head, Run.Config("region: westeurope\nfail_on: { severity: warning }")).Violations);
    }
}

public class JsonOutputTests
{
    [Fact]
    public void Json_has_the_contract_fields()
    {
        var report = Run.Analyze(
            Bundle.Job(Bundle.ClassicJob()),
            Bundle.Job(Bundle.ClassicJob(schedule: """{ "quartz_cron_expression": "0 0/15 * * * ?" }""")),
            Run.Config("region: westeurope\nestimation: { default_run_minutes: 20 }"));
        var json = Run.Json(report);

        Assert.Equal(1, (int)json["schemaVersion"]!);
        var bundle = json["bundles"]![0]!;
        Assert.NotNull(bundle["totals"]!["monthlyDelta"]);
        var change = bundle["changes"]![0]!;
        Assert.Equal("Medium", (string)change["confidence"]!);
        Assert.NotEmpty(change["after"]!["assumptions"]!.AsArray());
        Assert.NotNull(bundle["findings"]);
        Assert.NotEmpty(bundle["notModeled"]!.AsArray());
    }

    [Fact]
    public void Every_emitted_property_is_in_the_schema_and_required_ones_are_emitted()
    {
        var report = Run.Analyze(
            Bundle.Of("""{ "jobs": { "j": { "tasks": [ { "task_key": "t", "job_cluster_key": "m" } ], "job_clusters": [ { "job_cluster_key": "m", "new_cluster": { "node_type_id": "Standard_DS3_v2", "num_workers": 1 } } ] } }, "apps": { "a": {} } }"""),
            Bundle.Of("""{ "jobs": { "j2": { "tasks": [ { "task_key": "t", "job_cluster_key": "m" } ], "job_clusters": [ { "job_cluster_key": "m", "new_cluster": { "node_type_id": "Standard_DS3_v2", "num_workers": 1 } } ] } }, "pipelines": { "p": { "continuous": true } } }"""));
        var json = Run.Json(report);
        using var schema = JsonDocument.Parse(File.ReadAllText(Repo.Path("schemas", "report.schema.json")));

        Check(json, schema.RootElement, schema.RootElement, "$");

        static void Check(JsonNode? node, JsonElement schemaNode, JsonElement root, string path)
        {
            if (schemaNode.TryGetProperty("$ref", out var reference))
            {
                schemaNode = root.GetProperty("$defs").GetProperty(reference.GetString()!.Split('/')[^1]);
            }

            if (schemaNode.TryGetProperty("oneOf", out var oneOf))
            {
                var candidate = oneOf.EnumerateArray().FirstOrDefault(o => node is null ? o.TryGetProperty("type", out var t) && t.GetString() == "null" : !(o.TryGetProperty("type", out var t2) && t2.ValueKind == JsonValueKind.String && t2.GetString() == "null"));
                schemaNode = candidate;
            }

            if (node is JsonObject obj && schemaNode.ValueKind == JsonValueKind.Object && schemaNode.TryGetProperty("properties", out var properties))
            {
                foreach (var (name, value) in obj)
                {
                    Assert.True(properties.TryGetProperty(name, out var child), $"{path}.{name} is not in report.schema.json");
                    Check(value, child, root, $"{path}.{name}");
                }

                if (schemaNode.TryGetProperty("required", out var required))
                {
                    foreach (var name in required.EnumerateArray())
                    {
                        Assert.True(obj.ContainsKey(name.GetString()!), $"{path}.{name.GetString()} is required by the schema but missing");
                    }
                }
            }
            else if (node is JsonArray array && schemaNode.ValueKind == JsonValueKind.Object && schemaNode.TryGetProperty("items", out var items))
            {
                for (var i = 0; i < array.Count; i++)
                {
                    Check(array[i], items, root, $"{path}[{i}]");
                }
            }
        }
    }

    [Fact]
    public void Output_reference_documents_every_schema_property()
    {
        var documented = string.Join(" ", OutputFields.Sections.SelectMany(s => s.Fields).Select(f => f.Name));
        using var schema = JsonDocument.Parse(File.ReadAllText(Repo.Path("schemas", "report.schema.json")));
        foreach (var def in new[] { "bundle", "change", "estimate", "finding" })
        {
            foreach (var property in schema.RootElement.GetProperty("$defs").GetProperty(def).GetProperty("properties").EnumerateObject())
            {
                Assert.True(documented.Contains(property.Name, StringComparison.Ordinal), $"{def}.{property.Name} is not documented in OutputFields");
            }
        }
    }
}
