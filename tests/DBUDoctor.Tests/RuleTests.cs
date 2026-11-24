using DBUDoctor.Core.Config;
using DBUDoctor.Core.Analysis;
using DBUDoctor.Core.Model;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Reporting;
using DBUDoctor.Core.Rules;

namespace DBUDoctor.Tests;

public class RuleEngineTests
{
    private static readonly ResolvedBundle Continuous = Bundle.Of("""{ "pipelines": { "events": { "continuous": true } } }""");
    private static readonly ResolvedBundle Triggered = Bundle.Of("""{ "pipelines": { "events": { "continuous": false } } }""");

    [Fact]
    public void Severity_override()
    {
        var report = Run.Analyze(Triggered, Continuous, Run.Config("region: westeurope\nrules: { DBU004: { severity: error } }"));
        Assert.Equal(Severity.Error, Assert.Single(report.Findings).Severity);
    }

    [Fact]
    public void Disabled_rule_is_not_evaluated()
    {
        var report = Run.Analyze(Triggered, Continuous, Run.Config("region: westeurope\nrules: { DBU004: { enabled: false } }"));
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Pre_existing_problem_is_not_reported()
    {
        var report = Run.Analyze(Continuous, Continuous);
        Assert.Empty(report.Findings);
        Assert.Equal(1, report.ExistingHidden);
    }

    [Fact]
    public void Existing_problem_is_reported_on_request_and_marked()
    {
        var report = Run.Analyze(Continuous, Continuous, reportExisting: true);
        Assert.True(Assert.Single(report.Findings).Existing);
        Assert.Contains("(existing)", MarkdownRenderer.Render(report));
    }

    [Fact]
    public void Valid_suppression_hides_and_counts_the_finding()
    {
        var head = Bundle.Of("""{ "jobs": { "legacy_report": { "tasks": [ { "task_key": "t", "existing_cluster_id": "c" } ] } } }""");
        var report = Run.Analyze(Bundle.Of("{}"), head, Run.Config("""
            region: westeurope
            suppressions:
              - rule: DBU001
                resource: jobs.legacy_*
                reason: Moving off the shared cluster in Q1
            """));

        Assert.Empty(report.Findings.Of("DBU001"));
        Assert.Equal(1, report.SuppressedCount);
        Assert.Contains("1 finding suppressed", MarkdownRenderer.Render(report));
    }

    [Fact]
    public void Suppression_selector_must_match_type_and_key()
    {
        var finding = new Finding
        {
            RuleId = "DBU001", Severity = Severity.Warning, Bundle = "b", ResourceType = "jobs", ResourceKey = "legacy",
            FieldPath = "x", Message = "m", DocsUrl = "u",
        };
        Assert.True(RuleEngine.Matches(new Suppression("DBU001", "jobs.leg?cy", "r", 1), finding));
        Assert.False(RuleEngine.Matches(new Suppression("DBU001", "pipelines.legacy", "r", 1), finding));
        Assert.False(RuleEngine.Matches(new Suppression("DBU004", "jobs.legacy", "r", 1), finding));
    }

    [Fact]
    public void Findings_carry_identity_and_docs_link()
    {
        var finding = Assert.Single(Run.Analyze(Triggered, Continuous).Findings);
        Assert.Equal("DBU004|b|pipelines|events|continuous", finding.Identity);
        Assert.EndsWith("/docs/rules/DBU004.md", finding.DocsUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_rule_has_complete_metadata_and_a_docs_page()
    {
        foreach (var rule in RuleRegistry.All)
        {
            var m = rule.Metadata;
            Assert.Matches("^DBU[0-9]{3}$", m.Id);
            Assert.False(string.IsNullOrWhiteSpace(m.Title));
            Assert.False(string.IsNullOrWhiteSpace(m.WhenItFires));
            Assert.False(string.IsNullOrWhiteSpace(m.HowToFix));
            Assert.False(string.IsNullOrWhiteSpace(m.WhenToSuppress));

            var page = Repo.Path("docs", "rules", m.Id + ".md");
            Assert.True(File.Exists(page), $"{page} is missing");
            var text = File.ReadAllText(page);
            Assert.StartsWith($"# {m.Id}: {m.Title}", text, StringComparison.Ordinal);
            foreach (var section in new[] { "## Why it matters", "## Example", "## How to fix", "## When to suppress" })
            {
                Assert.Contains(section, text);
            }
        }
    }
}

public class Dbu001Tests
{
    private static ResolvedBundle Task(string task, string host = Bundle.AzureHost) =>
        Bundle.Of($$"""{ "jobs": { "report": { "job_clusters": [ { "job_cluster_key": "jc", "new_cluster": {{Bundle.Cluster()}} } ], "tasks": [ {{task}} ] } } }""", host);

    [Fact]
    public void Task_on_existing_cluster_on_azure()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Task("""{ "task_key": "refresh", "existing_cluster_id": "0601-182128-dcbte59m" }""")).Findings.Of("DBU001"));

        Assert.Equal("DBU001", finding.RuleId);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("tasks[refresh].existing_cluster_id", finding.FieldPath);
        Assert.Contains("about 55% of the all-purpose DBU rate", finding.Message);
    }

    [Fact]
    public void Same_task_on_aws()
    {
        var report = Run.Analyze(
            Bundle.Of("{}", Bundle.AwsHost),
            Task("""{ "task_key": "refresh", "existing_cluster_id": "abc" }""", Bundle.AwsHost),
            Run.Config("region: us-east-1"));

        Assert.Contains("about 27%", Assert.Single(report.Findings.Of("DBU001")).Message);
    }

    [Theory]
    [InlineData("""{ "task_key": "t", "job_cluster_key": "jc" }""")]
    [InlineData("""{ "task_key": "t", "new_cluster": { "node_type_id": "Standard_DS3_v2", "num_workers": 1 } }""")]
    [InlineData("""{ "task_key": "t", "environment_key": "default" }""")]
    public void Task_on_job_cluster_or_serverless_is_fine(string task)
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Task(task)).Findings.Of("DBU001"));
    }

    [Fact]
    public void Existing_finding_is_not_new()
    {
        var bundle = Task("""{ "task_key": "refresh", "existing_cluster_id": "c" }""");
        Assert.Empty(Run.Analyze(bundle, bundle).Findings.Of("DBU001"));
    }

    [Fact]
    public void Without_prices_the_message_has_no_percentage()
    {
        var report = Run.Analyze(Bundle.Of("{}"), Task("""{ "task_key": "t", "existing_cluster_id": "c" }"""), Run.Config("estimation: {}"));
        Assert.Contains("costs less per DBU", Assert.Single(report.Findings.Of("DBU001")).Message);
    }
}

public class Dbu004Tests
{
    [Fact]
    public void Pipeline_made_continuous()
    {
        var finding = Assert.Single(Run.Analyze(
            Bundle.Of("""{ "pipelines": { "events": { "continuous": false } } }"""),
            Bundle.Of("""{ "pipelines": { "events": { "continuous": true } } }""")).Findings);

        Assert.Equal("DBU004", finding.RuleId);
        Assert.Contains("runs around the clock", finding.Message);
    }

    [Theory]
    [InlineData("""{ "pipelines": { "events": { } } }""")]
    [InlineData("""{ "pipelines": { "events": { "continuous": false } } }""")]
    public void Triggered_pipeline_is_fine(string head)
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Bundle.Of(head)).Findings);
    }

    [Fact]
    public void Unchanged_continuous_pipeline_is_not_new()
    {
        var bundle = Bundle.Of("""{ "pipelines": { "events": { "continuous": true } } }""");
        Assert.Empty(Run.Analyze(bundle, bundle).Findings);
    }
}

public class Dbu010Tests
{
    private static ResolvedBundle Cron(string cron) =>
        Bundle.Job($$"""{ "schedule": { "quartz_cron_expression": "{{cron}}", "timezone_id": "UTC" } }""");

    [Fact]
    public void Hourly_to_every_fifteen_minutes()
    {
        var finding = Assert.Single(Run.Analyze(Cron("0 0 * * * ?"), Cron("0 0/15 * * * ?")).Findings);

        Assert.Equal("DBU010", finding.RuleId);
        Assert.Contains("730 → 2,920 runs / month", finding.Message);
        Assert.Contains("`0 0 * * * ?` → `0 0/15 * * * ?`", finding.Message);
        Assert.Equal("schedule.quartz_cron_expression", finding.FieldPath);
    }

    [Fact]
    public void Small_absolute_change_is_fine()
    {
        // Weekly (~4.3 runs / month) to daily (~30.4): over 4× as often, but fewer than 100 more runs.
        Assert.Empty(Run.Analyze(Cron("0 0 6 ? * MON"), Cron("0 0 6 * * ?")).Findings);
    }

    [Fact]
    public void Factor_and_min_runs_are_configurable()
    {
        var config = Run.Config("region: westeurope\nrules: { DBU010: { factor: 2, min_runs: 10 } }");
        Assert.Single(Run.Analyze(Cron("0 0 6 ? * MON"), Cron("0 0 6 * * ?"), config).Findings);
    }

    [Fact]
    public void Below_the_factor_is_fine()
    {
        Assert.Empty(Run.Analyze(Cron("0 0 * * * ?"), Cron("0 0/30 * * * ?")).Findings);
    }

    [Fact]
    public void New_job_has_no_base_and_no_finding()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Cron("0 * * * * ?")).Findings);
    }

    [Fact]
    public void Frequency_jump_is_never_pre_existing()
    {
        var report = Run.Analyze(Cron("0 0 * * * ?"), Cron("0 0/15 * * * ?"), reportExisting: true);
        Assert.False(Assert.Single(report.Findings).Existing);
    }
}

public class Dbu002Tests
{
    private static ResolvedBundle Cluster(int? minutes) => Bundle.Of(
        $$"""{ "clusters": { "shared": { "spark_version": "15.4.x-scala2.12", "node_type_id": "Standard_DS3_v2", "num_workers": 4{{(minutes is { } m ? $", \"autotermination_minutes\": {m}" : "")}} } } }""");

    [Fact]
    public void Disabled_is_a_warning_with_the_idle_cost()
    {
        // 3.75 DBU × 0.55 + 1.145 VMs = 3.2075 per hour; × 730 = 2,341.475 a month.
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Cluster(0)).Findings);

        Assert.Equal("DBU002", finding.RuleId);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("autotermination_minutes", finding.FieldPath);
        Assert.Contains("never auto-terminates", finding.Message);
        Assert.Contains("$3.21 per hour, up to $2,341 a month", finding.Message);
    }

    [Fact]
    public void Long_idle_window_is_info()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Cluster(240)).Findings);

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("waits 240 minutes idle before it terminates (limit 60): each idle stretch bills up to $12.83 ($3.21 per hour)", finding.Message);
    }

    [Fact]
    public void Unset_value_is_info_and_names_the_engine_dependency()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Cluster(null)).Findings);

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("depends on the deployment engine", finding.Message);
        Assert.Contains("Clusters API does not auto-terminate", finding.Message);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public void Short_idle_window_is_fine(int minutes)
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Cluster(minutes)).Findings);
    }

    [Fact]
    public void Threshold_is_configurable()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Cluster(240), Run.Config("region: westeurope\nrules: { DBU002: { max_minutes: 240 } }")).Findings);
    }

    [Fact]
    public void Unknown_rate_still_reports_without_a_price()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Cluster(0), Run.Config("estimation: {}")).Findings);

        Assert.Contains("it bills for every idle hour", finding.Message);
    }

    [Fact]
    public void Configured_severity_wins_over_the_graded_one()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Cluster(240), Run.Config("region: westeurope\nrules: { DBU002: { severity: error } }")).Findings);

        Assert.Equal(Severity.Error, finding.Severity);
    }

    [Fact]
    public void Severity_rise_is_new()
    {
        // 120 minutes is info in base; 0 is a warning in head: same identity, higher severity.
        var finding = Assert.Single(Run.Analyze(Cluster(120), Cluster(0)).Findings);

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.False(finding.Existing);
    }

    [Fact]
    public void Same_severity_stays_hidden()
    {
        var report = Run.Analyze(Cluster(120), Cluster(240));

        Assert.Empty(report.Findings);
        Assert.Equal(1, report.ExistingHidden);
    }

    [Fact]
    public void Severity_drop_stays_hidden()
    {
        Assert.Empty(Run.Analyze(Cluster(0), Cluster(240)).Findings);
    }
}

public class Dbu003Tests
{
    private static ResolvedBundle Warehouse(int? minutes) => Bundle.Of(
        $$"""{ "sql_warehouses": { "bi": { "cluster_size": "Small", "enable_serverless_compute": true{{(minutes is { } m ? $", \"auto_stop_mins\": {m}" : "")}} } } }""");

    [Fact]
    public void Disabled_is_a_warning_with_the_idle_cost()
    {
        // Serverless Small: 12 DBU × 0.70 = 8.40 per hour; × 730 = 6,132 a month.
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Warehouse(0)).Findings);

        Assert.Equal("DBU003", finding.RuleId);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("auto_stop_mins", finding.FieldPath);
        Assert.Contains("never auto-stops (`auto_stop_mins: 0`): idle, it bills $8.40 per hour, up to $6,132 a month", finding.Message);
    }

    [Fact]
    public void Default_auto_stop_is_info_and_named()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Warehouse(null)).Findings);

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("does not set `auto_stop_mins`, so the API default of 120 minutes applies (limit 30): each idle stretch bills up to $16.80 ($8.40 per hour)", finding.Message);
    }

    [Fact]
    public void Explicit_long_auto_stop_is_info()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Warehouse(45)).Findings);

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("waits 45 minutes idle before it stops", finding.Message);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    public void Short_auto_stop_is_fine(int minutes)
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Warehouse(minutes)).Findings);
    }

    [Fact]
    public void No_catalog_default_skips_an_unset_warehouse()
    {
        var catalog = TestCatalog.Inline("azure", "westeurope", "PREMIUM_SERVERLESS_SQL_COMPUTE: 0.70", "n1: { dbu: 1 }");

        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Warehouse(null), catalog: catalog).Findings);
        Assert.Single(Run.Analyze(Bundle.Of("{}"), Warehouse(0), catalog: catalog).Findings);
    }

    [Fact]
    public void Threshold_is_configurable()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Warehouse(45), Run.Config("region: westeurope\nrules: { DBU003: { max_minutes: 60 } }")).Findings);
    }

    [Fact]
    public void Embedded_catalog_has_the_api_default()
    {
        var value = CatalogLoader.LoadEmbedded().Defaults.WarehouseAutoStopMinutes;

        Assert.Equal(120m, value!.Value);
        Assert.Contains("warehouses/create", value.Provenance.Source);
    }

    [Fact]
    public void Invalid_default_names_the_file()
    {
        var files = new Dictionary<string, string>
        {
            ["sql-warehouses.yaml"] = File.ReadAllText(Repo.Path("tests", "Fixtures", "catalog", "sql-warehouses.yaml")),
            ["not-modeled.yaml"] = File.ReadAllText(Repo.Path("tests", "Fixtures", "catalog", "not-modeled.yaml")),
            ["defaults.yaml"] = "as_of: 2026-11-09\nsource: x\nsql_warehouse: { auto_stop_mins: 5 }\n",
        };

        var ex = Assert.Throws<DBUDoctor.Core.DoctorException>(() => CatalogLoader.Load(files));
        Assert.Contains("catalog/defaults.yaml", ex.Message);
    }
}

public class Dbu005Tests
{
    private static ResolvedBundle Continuous(string block) => Bundle.Job($$"""
        {
          "job_clusters": [ { "job_cluster_key": "main", "new_cluster": {{Bundle.Cluster()}} } ],
          "tasks": [ { "task_key": "t", "job_cluster_key": "main", "notebook_task": { "notebook_path": "/x" } } ],
          "continuous": {{block}}
        }
        """);

    [Fact]
    public void Continuous_job_added_with_its_cost()
    {
        // 3.75 DBU × 0.30 + 1.145 VMs = 2.27 per hour; × 730 = 1,657.10.
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Continuous("""{ "pause_status": "UNPAUSED" }""")).Findings);

        Assert.Equal("DBU005", finding.RuleId);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("continuous", finding.FieldPath);
        Assert.Contains("runs around the clock at $2.27 per hour, about $1,657 a month", finding.Message);
    }

    [Fact]
    public void Omitted_pause_status_is_unpaused()
    {
        Assert.Single(Run.Analyze(Bundle.Of("{}"), Continuous("{}")).Findings);
    }

    [Fact]
    public void Paused_is_fine()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Continuous("""{ "pause_status": "PAUSED" }""")).Findings);
    }

    [Fact]
    public void Serverless_continuous_job_has_no_price()
    {
        var job = Bundle.Job("""{ "tasks": [ { "task_key": "t", "environment_key": "default", "notebook_task": { "notebook_path": "/x" } } ], "continuous": {} }""");

        Assert.Contains("(~730 h / month)", Assert.Single(Run.Analyze(Bundle.Of("{}"), job).Findings).Message);
    }

    [Fact]
    public void Unchanged_continuous_job_is_not_new()
    {
        var job = Continuous("{}");
        Assert.Empty(Run.Analyze(job, job).Findings);
    }
}

public class Dbu016Tests
{
    private static ResolvedBundle Job(string trigger, string clusterExtra = "") => Bundle.Job($$"""
        {
          "job_clusters": [ { "job_cluster_key": "main", "new_cluster": {{Bundle.Cluster(extra: clusterExtra)}} } ],
          "tasks": [ { "task_key": "t", "job_cluster_key": "main", "notebook_task": { "notebook_path": "/x" } } ],
          {{trigger}}
        }
        """);

    private static string Cron(string cron, string pause = "UNPAUSED") =>
        $$"""
        "schedule": { "quartz_cron_expression": "{{cron}}", "timezone_id": "UTC", "pause_status": "{{pause}}" }
        """;

    [Fact]
    public void Every_five_minutes_on_classic_compute()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Job(Cron("0 0/5 * * * ?"))).Findings.Of("DBU016"));

        Assert.Equal("DBU016", finding.RuleId);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("schedule.quartz_cron_expression", finding.FieldPath);
        Assert.Contains("runs every 5 min on job cluster `main`", finding.Message);
        Assert.Contains("starts a cluster about 8,760 times a month", finding.Message);
    }

    [Fact]
    public void Serverless_is_fine()
    {
        var job = Bundle.Job($$"""{ "tasks": [ { "task_key": "t", "environment_key": "default", "notebook_task": { "notebook_path": "/x" } } ], {{Cron("0 0/5 * * * ?")}} }""");
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), job).Findings.Of("DBU016"));
    }

    [Fact]
    public void Pooled_is_fine()
    {
        Assert.DoesNotContain(Run.Analyze(Bundle.Of("{}"), Job(Cron("0 0/5 * * * ?"), """, "instance_pool_id": "pool-1" """)).Findings.Of("DBU016"), f => f.RuleId == "DBU016");
    }

    [Fact]
    public void Periodic_trigger()
    {
        // Periodic triggers come in hours at the least, so only a raised threshold reports them.
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Job("""
            "trigger": { "periodic": { "interval": 1, "unit": "HOURS" } }
            """), Run.Config("region: westeurope\nrules: { DBU016: { min_interval_minutes: 120 } }")).Findings.Of("DBU016"));

        Assert.Equal("trigger.periodic", finding.FieldPath);
    }

    [Fact]
    public void At_the_threshold_is_fine()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job(Cron("0 0/15 * * * ?"))).Findings.Of("DBU016"));
    }

    [Fact]
    public void Paused_is_fine()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job(Cron("0 0/5 * * * ?", "PAUSED"))).Findings.Of("DBU016"));
    }

    [Fact]
    public void Threshold_is_configurable()
    {
        Assert.Single(Run.Analyze(Bundle.Of("{}"), Job(Cron("0 0/15 * * * ?")), Run.Config("region: westeurope\nrules: { DBU016: { min_interval_minutes: 30 } }")).Findings.Of("DBU016"));
    }
}

public class Dbu007Tests
{
    private static ResolvedBundle Job(string jobTimeout = "", string taskTimeout = "", string extra = "") => Bundle.Job($$"""
        {
          "job_clusters": [ { "job_cluster_key": "main", "new_cluster": {{Bundle.Cluster()}} } ],
          "tasks": [
            { "task_key": "a", "job_cluster_key": "main", "notebook_task": { "notebook_path": "/x" } {{taskTimeout}} },
            { "task_key": "b", "job_cluster_key": "main", "notebook_task": { "notebook_path": "/y" } {{taskTimeout}} }
          ]
          {{jobTimeout}} {{extra}}
        }
        """);

    [Fact]
    public void No_timeouts_with_the_hourly_cost()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Job()).Findings);

        Assert.Equal("DBU007", finding.RuleId);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("timeout_seconds", finding.FieldPath);
        Assert.Contains("neither the job nor its tasks set one, and a stuck run keeps billing $2.27 per hour", finding.Message);
    }

    [Fact]
    public void Task_timeouts_are_enough()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job(taskTimeout: """, "timeout_seconds": 3600""")).Findings);
    }

    [Fact]
    public void Job_timeout_is_enough()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job(jobTimeout: """, "timeout_seconds": 3600""")).Findings);
    }

    [Fact]
    public void Zero_means_none()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Job(jobTimeout: """, "timeout_seconds": 0""", taskTimeout: """, "timeout_seconds": 0""")).Findings);
        Assert.Contains("neither the job nor its tasks", finding.Message);
    }

    [Fact]
    public void Continuous_job_is_skipped()
    {
        Assert.DoesNotContain(Run.Analyze(Bundle.Of("{}"), Job(extra: """, "continuous": {}""")).Findings, f => f.RuleId == "DBU007");
    }

    [Fact]
    public void Serverless_job_has_no_price()
    {
        var job = Bundle.Job("""{ "tasks": [ { "task_key": "t", "environment_key": "default", "notebook_task": { "notebook_path": "/x" } } ] }""");
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), job).Findings);

        Assert.EndsWith("set one.", finding.Message);
    }

    [Fact]
    public void Unchanged_job_is_not_new()
    {
        Assert.Empty(Run.Analyze(Job(), Job()).Findings);
    }
}

public class Dbu008Tests
{
    private static DoctorConfig Required(string tags = "[team, cost_center]") => Run.Config($"region: westeurope\nrules: {{ DBU008: {{ required_tags: {tags} }} }}");

    private static ResolvedBundle Job(string jobTags = "", string clusterTags = "") => Bundle.Job($$"""
        {
          {{(jobTags.Length > 0 ? $"\"tags\": {jobTags}," : "")}}
          "timeout_seconds": 3600,
          "job_clusters": [ { "job_cluster_key": "main", "new_cluster": {{Bundle.Cluster(extra: clusterTags.Length > 0 ? $", \"custom_tags\": {clusterTags}" : "")}} } ],
          "tasks": [ { "task_key": "t", "job_cluster_key": "main", "notebook_task": { "notebook_path": "/x" } } ]
        }
        """);

    [Fact]
    public void Missing_tag_is_named()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Job("""{ "team": "data" }"""), Required()).Findings.Of("DBU008"));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("tags", finding.FieldPath);
        Assert.Contains("job `j` lacks required tag `cost_center`", finding.Message);
    }

    [Fact]
    public void Job_cluster_custom_tags_count()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job("""{ "team": "data" }""", """{ "cost_center": "4711" }"""), Required()).Findings.Of("DBU008"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("")]
    public void Rule_is_off_without_required_tags(string tags)
    {
        var config = tags.Length == 0 ? Run.Config() : Required(tags);
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job(), config).Findings.Of("DBU008"));
    }

    [Fact]
    public void Serverless_job_with_a_usage_policy_is_attributed()
    {
        var job = Bundle.Job("""{ "usage_policy_id": "p1", "timeout_seconds": 60, "tasks": [ { "task_key": "t", "environment_key": "default", "notebook_task": { "notebook_path": "/x" } } ] }""");
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), job, Required()).Findings.Of("DBU008"));
    }

    [Fact]
    public void Serverless_job_without_a_policy_gets_the_hint()
    {
        var job = Bundle.Job("""{ "timeout_seconds": 60, "tasks": [ { "task_key": "t", "environment_key": "default", "notebook_task": { "notebook_path": "/x" } } ] }""");
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), job, Required()).Findings.Of("DBU008"));

        Assert.Contains("lacks required tags `team`, `cost_center`", finding.Message);
        Assert.Contains("usage policy (`usage_policy_id`)", finding.Message);
    }

    [Fact]
    public void Each_resource_type_has_its_tag_location()
    {
        var head = Bundle.Of("""
            {
              "clusters": { "c": { "spark_version": "15.4.x-scala2.12", "node_type_id": "Standard_DS3_v2", "num_workers": 1, "autotermination_minutes": 30, "custom_tags": { "team": "a", "cost_center": "1" } } },
              "sql_warehouses": { "w": { "cluster_size": "Small", "auto_stop_mins": 10, "tags": { "custom_tags": [ { "key": "team", "value": "a" } ] } } },
              "pipelines": { "p": { "tags": { "team": "a" }, "clusters": [ { "label": "default", "custom_tags": { "cost_center": "1" } } ] } }
            }
            """);

        var findings = Run.Analyze(Bundle.Of("{}"), head, Required()).Findings.Of("DBU008");

        var finding = Assert.Single(findings);
        Assert.Equal("sql_warehouses", finding.ResourceType);
        Assert.Equal("tags.custom_tags", finding.FieldPath);
    }

    [Fact]
    public void List_settings_are_validated()
    {
        var ex = Assert.Throws<DBUDoctor.Core.DoctorException>(() => Run.Config("rules: { DBU008: { required_tags: team } }"));
        Assert.Contains("rules.DBU008.required_tags", ex.Message);
        var unknown = Assert.Throws<DBUDoctor.Core.DoctorException>(() => Run.Config("rules: { DBU008: { tags: [a] } }"));
        Assert.Contains("Allowed: enabled, severity, required_tags", unknown.Message);
    }
}

public class Dbu012Tests
{
    private static readonly DateOnly November8 = new(2026, 11, 8);

    private static ResolvedBundle Job(string sparkVersion) => Bundle.Job($$"""
        {
          "timeout_seconds": 3600,
          "job_clusters": [ { "job_cluster_key": "main", "new_cluster": { "spark_version": "{{sparkVersion}}", "node_type_id": "Standard_DS3_v2", "num_workers": 1 } } ],
          "tasks": [ { "task_key": "t", "job_cluster_key": "main", "notebook_task": { "notebook_path": "/x" } } ]
        }
        """);

    private static List<Finding> Findings(ResolvedBundle head, string config = "region: westeurope") =>
        BundleAnalyzer.Analyze(new BundleInput("b", "prod", Bundle.Of("{}"), head), Run.Config(config), TestCatalog.Load(), Run.Reference, false, today: November8)
            .Findings.Of("DBU012");

    [Fact]
    public void Runtime_past_end_of_support()
    {
        var finding = Assert.Single(Findings(Job("13.3.x-scala2.12")));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("job_clusters[main].new_cluster.spark_version", finding.FieldPath);
        Assert.Contains("Databricks Runtime 13.3 LTS, which reached end of support on 2026-08-22", finding.Message);
        Assert.Contains("Move to a current LTS release, such as 17.3 LTS", finding.Message);
    }

    [Fact]
    public void Runtime_close_to_end_of_support()
    {
        var finding = Assert.Single(Findings(Job("14.3.x-photon-scala2.12")));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("reaches end of support on 2027-02-01, in 85 days", finding.Message);
    }

    [Theory]
    [InlineData("16.4.x-scala2.12")]
    [InlineData("15.0.x-scala2.12")]
    [InlineData("custom-image")]
    public void Supported_or_unlisted_runtime_is_fine(string sparkVersion)
    {
        Assert.Empty(Findings(Job(sparkVersion)));
    }

    [Fact]
    public void Older_than_every_listed_release()
    {
        var finding = Assert.Single(Findings(Job("10.4.x-scala2.12")));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("10.4 LTS and earlier have reached end of life", finding.Message);
    }

    [Fact]
    public void All_purpose_cluster_and_window_setting()
    {
        var cluster = Bundle.Of("""{ "clusters": { "c": { "spark_version": "15.4.x-scala2.12", "node_type_id": "Standard_DS3_v2", "num_workers": 1, "autotermination_minutes": 30 } } }""");

        Assert.Empty(Findings(cluster));
        var finding = Assert.Single(Findings(cluster, "region: westeurope\nrules: { DBU012: { warn_days: 365 } }"));
        Assert.Equal("spark_version", finding.FieldPath);
    }

    [Fact]
    public void Embedded_catalog_has_the_lts_dates()
    {
        var table = CatalogLoader.LoadEmbedded().Runtimes;

        Assert.Equal(new DateOnly(2026, 8, 22), table.EndOfSupport[new Version(13, 3)].Date);
        Assert.Equal(new Version(11, 3), table.Oldest);
    }
}

public class Dbu019Tests
{
    private static ResolvedBundle Job(string clusterTags) => Bundle.Job($$"""
        {
          "timeout_seconds": 3600,
          "job_clusters": [ { "job_cluster_key": "main", "new_cluster": {{Bundle.Cluster(extra: $", \"custom_tags\": {clusterTags}")}} } ],
          "tasks": [ { "task_key": "t", "job_cluster_key": "main", "notebook_task": { "notebook_path": "/x" } } ]
        }
        """);

    [Fact]
    public void Colliding_key_is_named_with_its_billing_name()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Job("""{ "vendor": "finance" }""")).Findings.Of("DBU019"));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("job_clusters[main].new_cluster.custom_tags.vendor", finding.FieldPath);
        Assert.Contains("collides with the Databricks default tag `Vendor`: cloud billing shows it as `x_vendor`", finding.Message);
    }

    [Fact]
    public void Normal_key_is_fine()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job("""{ "team": "finance" }""")).Findings.Of("DBU019"));
    }

    [Fact]
    public void Every_tag_location_is_checked()
    {
        var head = Bundle.Of("""
            {
              "jobs": { "j": { "tags": { "JobId": "x" }, "timeout_seconds": 60, "tasks": [ { "task_key": "t", "environment_key": "d", "notebook_task": { "notebook_path": "/x" } } ] } },
              "clusters": { "c": { "spark_version": "15.4.x-scala2.12", "node_type_id": "Standard_DS3_v2", "num_workers": 1, "autotermination_minutes": 30, "custom_tags": { "Creator": "me" } } },
              "sql_warehouses": { "w": { "cluster_size": "Small", "auto_stop_mins": 10, "tags": { "custom_tags": [ { "key": "sqlendpointid", "value": "1" } ] } } },
              "pipelines": { "p": { "tags": { "ClusterName": "n" } } }
            }
            """);

        var findings = Run.Analyze(Bundle.Of("{}"), head).Findings.Of("DBU019");

        Assert.Equal(["clusters.c", "jobs.j", "pipelines.p", "sql_warehouses.w"], findings.Select(f => $"{f.ResourceType}.{f.ResourceKey}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Existing_collision_is_not_new()
    {
        var job = Job("""{ "Vendor": "x" }""");
        Assert.Empty(Run.Analyze(job, job).Findings.Of("DBU019"));
    }
}

public class Dbu009Tests
{
    private static ResolvedBundle Job(string clusterExtra) => Bundle.Job($$"""
        {
          "timeout_seconds": 3600,
          "job_clusters": [ { "job_cluster_key": "main", "new_cluster": { "spark_version": "16.4.x-scala2.12", "node_type_id": "Standard_DS3_v2" {{clusterExtra}} } } ],
          "tasks": [ { "task_key": "t", "job_cluster_key": "main", "notebook_task": { "notebook_path": "/x" } } ]
        }
        """);

    [Fact]
    public void Large_on_demand_cluster_with_the_spot_saving()
    {
        // 16 × (0.229 on demand − 0.05 spot) = 2.864 per hour.
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Job("""
            , "autoscale": { "min_workers": 2, "max_workers": 16 }, "azure_attributes": { "availability": "ON_DEMAND_AZURE" }
            """)).Findings.Of("DBU009"));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("job_clusters[main].new_cluster.azure_attributes.availability", finding.FieldPath);
        Assert.Contains("runs up to 16 workers on on-demand VMs (`ON_DEMAND_AZURE`)", finding.Message);
        Assert.Contains("`SPOT_WITH_FALLBACK_AZURE`", finding.Message);
        Assert.Contains("save up to $2.86 per hour", finding.Message);
    }

    [Fact]
    public void Availability_not_set_is_fine()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job(""", "num_workers": 16""")).Findings.Of("DBU009"));
    }

    [Fact]
    public void Small_cluster_is_fine()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job(""", "num_workers": 4, "azure_attributes": { "availability": "ON_DEMAND_AZURE" }""")).Findings.Of("DBU009"));
    }

    [Fact]
    public void Aws_on_demand_without_vm_prices_has_no_saving()
    {
        var job = Bundle.Job("""
            { "timeout_seconds": 60, "tasks": [ { "task_key": "t", "new_cluster": { "spark_version": "16.4.x-scala2.12", "node_type_id": "m5.xlarge", "num_workers": 8, "aws_attributes": { "availability": "ON_DEMAND" } }, "notebook_task": { "notebook_path": "/x" } } ] }
            """, Bundle.AwsHost);

        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}", Bundle.AwsHost), job, Run.Config("region: us-east-1")).Findings.Of("DBU009"));

        Assert.Equal("tasks[t].new_cluster.aws_attributes.availability", finding.FieldPath);
        Assert.Contains("`SPOT_WITH_FALLBACK`", finding.Message);
        Assert.DoesNotContain("save up to", finding.Message);
    }

    [Fact]
    public void Pipeline_cluster_and_threshold()
    {
        var head = Bundle.Of("""{ "pipelines": { "p": { "clusters": [ { "label": "default", "node_type_id": "Standard_DS3_v2", "num_workers": 4, "azure_attributes": { "availability": "ON_DEMAND_AZURE" } } ] } } }""");

        Assert.Empty(Run.Analyze(Bundle.Of("{}"), head).Findings.Of("DBU009"));
        Assert.Single(Run.Analyze(Bundle.Of("{}"), head, Run.Config("region: westeurope\nrules: { DBU009: { min_workers: 4 } }")).Findings.Of("DBU009"));
    }
}

public class Dbu017Tests
{
    private static ResolvedBundle Job(string attributes) => Bundle.Job($$"""
        {
          "timeout_seconds": 3600,
          "job_clusters": [ { "job_cluster_key": "main", "new_cluster": { "spark_version": "16.4.x-scala2.12", "node_type_id": "Standard_DS3_v2", "num_workers": 4, {{attributes}} } } ],
          "tasks": [ { "task_key": "t", "job_cluster_key": "main", "notebook_task": { "notebook_path": "/x" } } ]
        }
        """);

    [Fact]
    public void Azure_spot_without_fallback()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Job("""
            "azure_attributes": { "availability": "SPOT_AZURE" }
            """)).Findings.Of("DBU017"));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("job_clusters[main].new_cluster.azure_attributes.availability", finding.FieldPath);
        Assert.Contains("without fallback (`SPOT_AZURE`)", finding.Message);
        Assert.Contains("Use `SPOT_WITH_FALLBACK_AZURE` with `first_on_demand: 1`", finding.Message);
        Assert.DoesNotContain("driver runs on spot", finding.Message);
    }

    [Fact]
    public void Spot_with_fallback_is_fine()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job("""
            "azure_attributes": { "availability": "SPOT_WITH_FALLBACK_AZURE" }
            """)).Findings.Of("DBU017"));
    }

    [Fact]
    public void Aws_spot_driver_is_named()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Job("""
            "aws_attributes": { "availability": "SPOT" }
            """)).Findings.Of("DBU017"));

        Assert.Contains("The driver runs on spot too", finding.Message);
        Assert.Contains("`SPOT_WITH_FALLBACK`", finding.Message);
    }

    [Fact]
    public void Gcp_preemptible()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Job("""
            "gcp_attributes": { "availability": "PREEMPTIBLE_GCP", "first_on_demand": 0 }
            """)).Findings.Of("DBU017"));

        Assert.Contains("`PREEMPTIBLE_WITH_FALLBACK_GCP`", finding.Message);
        Assert.Contains("The driver runs on spot too", finding.Message);
    }
}

public class Dbu018Tests
{
    private static ResolvedBundle Pipeline(string mode, string label = "default", string extra = "") => Bundle.Of($$"""
        { "pipelines": { "p": { {{extra}} "clusters": [ { "label": "{{label}}", "node_type_id": "Standard_DS3_v2", "autoscale": { "min_workers": 1, "max_workers": 5 {{mode}} } } ] } } }
        """);

    [Fact]
    public void Legacy_autoscaling()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Pipeline(""", "mode": "LEGACY" """)).Findings.Of("DBU018"));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("clusters[default].autoscale.mode", finding.FieldPath);
        Assert.Contains("uses legacy autoscaling (`autoscale.mode: LEGACY`)", finding.Message);
    }

    [Theory]
    [InlineData(""", "mode": "ENHANCED" """)]
    [InlineData("")]
    public void Enhanced_or_unset_is_fine(string mode)
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Pipeline(mode)).Findings.Of("DBU018"));
    }

    [Fact]
    public void Maintenance_cluster_is_skipped()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Pipeline(""", "mode": "LEGACY" """, "maintenance")).Findings.Of("DBU018"));
    }
}

public class Dbu011Tests
{
    private static ResolvedBundle Job(string extra) => Bundle.Job($$"""
        {
          "timeout_seconds": 3600,
          "tasks": [ { "task_key": "t", "environment_key": "default", "notebook_task": { "notebook_path": "/x" } } ]
          {{extra}}
        }
        """);

    private const string Daily = """, "schedule": { "quartz_cron_expression": "0 0 2 * * ?", "timezone_id": "UTC" }""";

    [Fact]
    public void Nightly_serverless_job_in_the_default_mode()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Job(Daily)).Findings.Of("DBU011"));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("performance_target", finding.FieldPath);
        Assert.Contains("runs serverless tasks daily in performance-optimized mode (the default)", finding.Message);
        Assert.Contains("set `performance_target: STANDARD`", finding.Message);
    }

    [Theory]
    [InlineData("STANDARD")]
    [InlineData("COST_OPTIMIZED")]
    public void Other_modes_are_fine(string target)
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job(Daily + $", \"performance_target\": \"{target}\"")).Findings.Of("DBU011"));
    }

    [Fact]
    public void Explicit_performance_optimized_is_reported()
    {
        Assert.Single(Run.Analyze(Bundle.Of("{}"), Job(Daily + """, "performance_target": "PERFORMANCE_OPTIMIZED" """)).Findings.Of("DBU011"));
    }

    [Fact]
    public void Unscheduled_or_classic_jobs_are_fine()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Job("")).Findings.Of("DBU011"));
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Bundle.Job(Bundle.ClassicJob())).Findings.Of("DBU011"));
    }
}

public class Dbu014Tests
{
    private static ResolvedBundle Endpoint(string entity, string mode = "production") => ResolvedBundle.Parse($$"""
        {
          "bundle": { "name": "b", "target": "t", "mode": "{{mode}}" },
          "workspace": { "host": "{{Bundle.AzureHost}}" },
          "resources": { "model_serving_endpoints": { "scoring": { "name": "scoring", "config": { "served_entities": [ {{entity}} ] } } } }
        }
        """, "test");

    [Fact]
    public void Development_endpoint_always_on_is_a_warning()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Endpoint("""{ "name": "m", "entity_name": "main.ml.m", "scale_to_zero_enabled": false, "workload_size": "Small" }""", "development")).Findings.Of("DBU014"));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("config.served_entities[m].scale_to_zero_enabled", finding.FieldPath);
        Assert.Contains("in a development target: it keeps a floor of 4 provisioned concurrency running while idle", finding.Message);
    }

    [Fact]
    public void Production_endpoint_with_a_warm_floor_is_info()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Endpoint("""{ "name": "m", "scale_to_zero_enabled": false, "min_provisioned_concurrency": 8, "max_provisioned_concurrency": 32 }""")).Findings.Of("DBU014"));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("a floor of 8 provisioned concurrency", finding.Message);
    }

    [Fact]
    public void Scale_to_zero_on_is_fine()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Endpoint("""{ "name": "m", "scale_to_zero_enabled": true, "workload_size": "Small" }""")).Findings.Of("DBU014"));
    }

    [Fact]
    public void Unset_counts_as_off()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Endpoint("""{ "name": "m", "workload_size": "Large" }""")).Findings.Of("DBU014"));
        Assert.Contains("a floor of 16 provisioned concurrency", finding.Message);
    }
}

public class Dbu013Tests
{
    private static ResolvedBundle App(string size) => Bundle.Of($$"""{ "apps": { "portal": { "name": "portal" {{size}} } } }""");

    [Theory]
    [InlineData("LARGE", "1 DBU per hour per instance, 2.0× MEDIUM")]
    [InlineData("XLARGE", "3 DBU per hour per instance, 6.0× MEDIUM")]
    public void Larger_than_medium_is_reported(string size, string expected)
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), App($", \"compute_size\": \"{size}\"")).Findings.Of("DBU013"));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("compute_size", finding.FieldPath);
        Assert.Contains(expected, finding.Message);
        Assert.Contains("a month while it runs", finding.Message);
    }

    [Theory]
    [InlineData(", \"compute_size\": \"MEDIUM\"")]
    [InlineData("")]
    public void Medium_or_unset_is_fine(string size)
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), App(size)).Findings.Of("DBU013"));
    }
}

public class Dbu015Tests
{
    private static ResolvedBundle Endpoint(string branch, string fields) => Bundle.Of($$"""
        { "postgres_endpoints": { "primary": { "parent": "projects/shop/branches/{{branch}}" {{fields}} } } }
        """);

    [Fact]
    public void Always_on_development_branch_is_a_warning_with_its_cost()
    {
        // 2 CU × 0.213 DBU per CU × 0.30 × 730 = 93.29.
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Endpoint("dev", """, "autoscaling_limit_min_cu": 2, "autoscaling_limit_max_cu": 4, "no_suspension": true""")).Findings.Of("DBU015"));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("no_suspension", finding.FieldPath);
        Assert.Contains("on branch `dev` never scales to zero", finding.Message);
        Assert.Contains("about $93.29 a month at 2 CU", finding.Message);
    }

    [Fact]
    public void Always_on_production_branch_is_fine()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Endpoint("production", """, "autoscaling_limit_min_cu": 2, "autoscaling_limit_max_cu": 4, "no_suspension": true""")).Findings.Of("DBU015"));
    }

    [Fact]
    public void Configured_production_branches_count()
    {
        var config = Run.Config("region: westeurope\nrules: { DBU015: { production_branches: [main] } }");
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Endpoint("main", """, "autoscaling_limit_min_cu": 2, "no_suspension": true"""), config).Findings.Of("DBU015"));
    }

    [Fact]
    public void High_minimum_is_info()
    {
        var finding = Assert.Single(Run.Analyze(Bundle.Of("{}"), Endpoint("production", """, "autoscaling_limit_min_cu": 8, "autoscaling_limit_max_cu": 16""")).Findings.Of("DBU015"));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("autoscaling_limit_min_cu", finding.FieldPath);
        Assert.Contains("keeps at least 8 CU", finding.Message);
    }

    [Fact]
    public void Scale_to_zero_dev_branch_is_fine()
    {
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), Endpoint("dev", """, "autoscaling_limit_min_cu": 1, "autoscaling_limit_max_cu": 4""")).Findings.Of("DBU015"));
    }
}
