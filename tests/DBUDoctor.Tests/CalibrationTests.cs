using System.Net;
using System.Text;
using DBUDoctor.Core;
using DBUDoctor.Core.Analysis;
using DBUDoctor.Core.Calibration;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Cost;
using DBUDoctor.Core.Diff;
using DBUDoctor.Core.Model;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Reporting;
using DBUDoctor.Core.Rules;
using DBUDoctor.Databricks;

namespace DBUDoctor.Tests;

/// <summary>Builds usage history the way <see cref="HistoryCollector"/> would.</summary>
internal static class Usage
{
    public static UsageHistory History(params ResourceHistory[] resources)
    {
        var history = new UsageHistory { Target = "prod", WindowDays = 30, MinRuns = 5 };
        foreach (var r in resources)
        {
            history.Resources[(r.Type, r.Key)] = r;
        }

        return history;
    }

    /// <summary>A job with <paramref name="runs"/> runs of <paramref name="dbusPerRun"/> DBUs, one per day up to 30 days.</summary>
    public static ResourceHistory Job(string key, int runs, decimal dbusPerRun, bool serverless = false)
    {
        var r = new ResourceHistory { Type = ResourceTypes.Jobs, Key = key, Id = "123", WindowDays = 30, Serverless = serverless };
        for (var i = 0; i < runs; i++)
        {
            r.Runs[$"run{i}"] = dbusPerRun;
            r.TotalDbus += dbusPerRun;
            r.Days.Add(new DateOnly(2026, 10, 1).AddDays(i % 30));
        }

        r.Skus[serverless ? "PREMIUM_JOBS_SERVERLESS_COMPUTE_US_EAST_N_VIRGINIA" : "PREMIUM_JOBS_COMPUTE"] = r.TotalDbus;
        return r;
    }

    /// <summary>A warehouse or cluster with <paramref name="dbus"/> DBUs over <paramref name="days"/> days.</summary>
    public static ResourceHistory Continuous(string type, string key, string id, decimal dbus, int days)
    {
        var r = new ResourceHistory { Type = type, Key = key, Id = id, WindowDays = 30, TotalDbus = dbus };
        for (var i = 0; i < days; i++)
        {
            r.Days.Add(new DateOnly(2026, 10, 1).AddDays(i));
        }

        return r;
    }

    public static BundleReport Analyze(ResolvedBundle baseBundle, ResolvedBundle head, UsageHistory history, string config = "region: westeurope", PriceCatalog? catalog = null) =>
        BundleAnalyzer.Analyze(new BundleInput("b", "prod", baseBundle, head), Run.Config(config), catalog ?? TestCatalog.Load(), Run.Reference, false, history: history);
}

public class CalibrationSettingsTests
{
    [Fact]
    public void Disabled_by_default()
    {
        var config = Run.Config("region: westeurope");
        Assert.False(config.Calibration.Enabled);
        Assert.Equal(30, config.Calibration.WindowDays);
        Assert.Equal(5, config.Calibration.MinRuns);
        Assert.Equal(0.7m, config.Estimation.StandardModeFactor);
    }

    [Fact]
    public void Enabled_without_a_warehouse_is_an_input_error()
    {
        var config = Run.Config("calibration: { enabled: true }");
        var ex = Assert.Throws<DoctorException>(() => SettingsResolver.Apply(config, new CliFlags(), new Dictionary<string, string?>()));
        Assert.Contains("calibration.warehouse_id", ex.Message);
    }

    [Fact]
    public void Warehouse_from_the_environment()
    {
        var config = Run.Config("calibration: { enabled: true }");
        SettingsResolver.Apply(config, new CliFlags(), new Dictionary<string, string?> { ["DBUDOCTOR_WAREHOUSE_ID"] = "abcdef0123456789" });
        Assert.Equal("abcdef0123456789", config.Calibration.WarehouseId);
    }

    [Theory]
    [InlineData("calibration: { warehouse_id: \"abc' OR 1=1\" }", "warehouse_id")]
    [InlineData("calibration: { window_days: 0 }", "window_days")]
    [InlineData("calibration: { min_runs: 2.5 }", "min_runs")]
    [InlineData("calibration: { unknown: 1 }", "unknown key")]
    [InlineData("estimation: { standard_mode_factor: 1.5 }", "standard_mode_factor")]
    public void Invalid_settings_are_rejected_with_a_line(string yaml, string expected)
    {
        var ex = Assert.Throws<DoctorException>(() => Run.Config(yaml));
        Assert.Contains(expected, ex.Message);
        Assert.Contains(".dbudoctor.yml:1", ex.Message);
    }
}

public class ResourceIdTests
{
    [Theory]
    [InlineData("jobs", "206000809187888", true)]
    [InlineData("jobs", "0", false)]
    [InlineData("jobs", "1 OR 1=1", false)]
    [InlineData("jobs", "1); DROP TABLE x; --", false)]
    [InlineData("jobs", "12345678901234567890", false)]
    [InlineData("jobs", "١٢٣", false)]
    [InlineData("pipelines", "8f2a1c3e-4b5d-4e6f-8a9b-0c1d2e3f4a5b", true)]
    [InlineData("pipelines", "8f2a1c3e-4b5d-4e6f-8a9b-0c1d2e3f4a5b' --", false)]
    [InlineData("sql_warehouses", "abcdef0123456789", true)]
    [InlineData("sql_warehouses", "abcdef012345678'", false)]
    [InlineData("clusters", "0601-182128-dcbte59m", true)]
    [InlineData("clusters", "0601-182128-dcbte59m\n", false)]
    [InlineData("clusters", "x' UNION SELECT *", false)]
    public void Ids_are_validated(string type, string id, bool valid)
    {
        Assert.Equal(valid, ResourceIds.IsValid(type, id));
    }

    [Fact]
    public void Queries_pass_ids_as_parameters_and_refuse_invalid_ones()
    {
        var statement = UsageQueries.Usage(ResourceTypes.Jobs, ["123", "456"], 30);

        Assert.Contains("usage_metadata.job_id IN (:id_0, :id_1)", statement.Text);
        Assert.DoesNotContain("123", statement.Text);
        Assert.Contains(statement.Parameters, p => p is { Name: "id_0", Value: "123" });
        Assert.Contains(statement.Parameters, p => p is { Name: "window_days", Value: "30", Type: "INT" });
        Assert.Throws<ArgumentException>(() => UsageQueries.Usage(ResourceTypes.Jobs, ["1 OR 1=1"], 30));
    }
}

public class CalibratorTests
{
    private static readonly PriceCatalog Aws = TestCatalog.Inline(
        "aws", "us-east-1",
        "PREMIUM_JOBS_COMPUTE: 0.15\nPREMIUM_JOBS_SERVERLESS_COMPUTE: 0.35\nPREMIUM_SERVERLESS_SQL_COMPUTE: 0.70",
        "n1: { dbu: 0.75 }");

    private static ResolvedBundle AwsJob(string cron, int workers = 4) => Bundle.Job(
        Bundle.ClassicJob(
            schedule: $$"""{ "quartz_cron_expression": "{{cron}}", "timezone_id": "UTC" }""",
            cluster: Bundle.Cluster("n1", workers)),
        Bundle.AwsHost);

    private static ResolvedBundle Serverless(string cron, string? target = null) => Bundle.Job($$"""
        {
          "tasks": [ { "task_key": "t", "environment_key": "default", "notebook_task": { "notebook_path": "/x" } } ],
          "schedule": { "quartz_cron_expression": "{{cron}}", "timezone_id": "UTC" }
          {{(target is null ? "" : $", \"performance_target\": \"{target}\"")}}
        }
        """, Bundle.AwsHost);

    [Fact]
    public void Calibrated_job()
    {
        // 720 runs, median 6 DBUs at 0.15: hourly 6 × 730 × 0.15 = 657; every 15 min 6 × 2,920 × 0.15 = 2,628.
        var report = Usage.Analyze(AwsJob("0 0 * * * ?"), AwsJob("0 0/15 * * * ?"), Usage.History(Usage.Job("j", 720, 6)), "region: us-east-1", Aws);

        var row = Assert.Single(report.Changes);
        Assert.Equal(657m, row.MonthlyBefore);
        Assert.Equal(2628m, row.MonthlyAfter);
        Assert.Equal(Confidence.High, row.Confidence);
        Assert.Contains(row.Before!.Assumptions, a => a.Contains("720 runs, median 6 DBUs per run", StringComparison.Ordinal));
        Assert.Equal(720, row.After!.History!.Runs);
    }

    [Fact]
    public void Calibrated_warehouse_resize()
    {
        // 2,520 DBUs in 30 days is 2,555 DBUs per 730-hour month: 2,555 × 0.70 = 1,788.50; Medium doubles it.
        static ResolvedBundle Warehouse(string size) => Bundle.Of(
            $$"""{ "sql_warehouses": { "bi": { "cluster_size": "{{size}}", "enable_serverless_compute": true } } }""", Bundle.AwsHost);
        var history = Usage.History(Usage.Continuous(ResourceTypes.SqlWarehouses, "bi", "abcdef0123456789", 2520, 30));

        var row = Assert.Single(Usage.Analyze(Warehouse("Small"), Warehouse("Medium"), history, "region: us-east-1", Aws).Changes);

        Assert.Equal(1788.50m, decimal.Round(row.MonthlyBefore!.Value, 2));
        Assert.Equal(3577.00m, decimal.Round(row.MonthlyAfter!.Value, 2));
        Assert.Equal(Confidence.High, row.Confidence);
    }

    [Fact]
    public void Calibrated_scale_up_follows_the_node_count()
    {
        // A driver and 4 workers is 5 nodes; 8 workers is 9 nodes: DBUs per run × 9 / 5 = 1.8.
        var row = Assert.Single(Usage.Analyze(AwsJob("0 0 * * * ?", 4), AwsJob("0 0 * * * ?", 8), Usage.History(Usage.Job("j", 720, 6)), "region: us-east-1", Aws).Changes);

        Assert.Equal(row.MonthlyBefore * 1.8m, row.MonthlyAfter);
        Assert.Contains(row.After!.Assumptions, a => a.Contains("6.75 / 3.75", StringComparison.Ordinal));
    }

    [Fact]
    public void Azure_infrastructure_from_observed_runtime()
    {
        // 6 DBUs at 3.75 DBU per hour is 1.6 hours; VMs 1.6 × 1.145 = 1.832 plus DBUs 6 × 0.30 = 1.80 per run.
        var job = Bundle.Job(Bundle.ClassicJob(schedule: """{ "quartz_cron_expression": "0 0 6 1 * ?", "timezone_id": "UTC" }"""));
        var job2 = Bundle.Job(Bundle.ClassicJob(schedule: """{ "quartz_cron_expression": "0 0 7 1 * ?", "timezone_id": "UTC" }"""));

        var row = Assert.Single(Usage.Analyze(job, job2, Usage.History(Usage.Job("j", 20, 6))).Changes);

        // Monthly on the first: 12 runs a year, 1 run per month.
        Assert.Equal(1.80m + 1.832m, row.MonthlyBefore);
        Assert.Equal(1.80m + 1.832m, row.MonthlyAfter);
    }

    [Fact]
    public void Thin_history_is_low()
    {
        var row = Assert.Single(Usage.Analyze(AwsJob("0 0 * * * ?"), AwsJob("0 0/15 * * * ?"), Usage.History(Usage.Job("j", 3, 6)), "region: us-east-1", Aws).Changes);

        Assert.Equal(Confidence.Low, row.Confidence);
        Assert.Equal(657m, row.MonthlyBefore);
    }

    [Fact]
    public void Switch_to_standard_mode()
    {
        // One run a month of 120 DBUs at 0.35 = 42.00; standard mode × 0.7 = 29.40.
        var row = Assert.Single(Usage.Analyze(
            Serverless("0 0 6 1 * ?"), Serverless("0 0 6 1 * ?", "STANDARD"),
            Usage.History(Usage.Job("j", 1, 120, serverless: true)), "region: us-east-1", Aws).Changes);

        Assert.Equal(42.00m, row.MonthlyBefore);
        Assert.Equal(29.40m, row.MonthlyAfter);
        Assert.Equal(Confidence.Low, row.Confidence);
    }

    [Fact]
    public void Serverless_job_with_history_follows_the_schedule()
    {
        var row = Assert.Single(Usage.Analyze(
            Serverless("0 0 6 * * ?"), Serverless("0 0 6,18 * * ?"),
            Usage.History(Usage.Job("j", 30, 4, serverless: true)), "region: us-east-1", Aws).Changes);

        Assert.Equal(row.MonthlyBefore * 2, row.MonthlyAfter);
        Assert.Equal(Confidence.High, row.Confidence);
    }

    [Fact]
    public void Unmapped_resource_is_estimated_without_history()
    {
        var history = Usage.History();
        history.NotFound.Add((ResourceTypes.Jobs, "j"));

        var report = Usage.Analyze(AwsJob("0 0 * * * ?"), AwsJob("0 0/15 * * * ?"), history, "region: us-east-1", Aws);

        var row = Assert.Single(report.Changes);
        Assert.Null(row.Before!.History);
        Assert.Contains(row.Before.Assumptions, a => a.Contains("not found in calibration target `prod`", StringComparison.Ordinal));
        Assert.Contains(report.GlobalAssumptions, a => a.Contains("1 not found in the target", StringComparison.Ordinal));
    }

    [Fact]
    public void Live_list_price_wins_over_the_catalog()
    {
        var history = Usage.History(Usage.Job("j", 720, 6));
        history.ListPrices["PREMIUM_JOBS_COMPUTE"] = 0.20m;
        history.ListPrices["ENTERPRISE_JOBS_COMPUTE"] = 0.25m;

        var report = Usage.Analyze(AwsJob("0 0 * * * ?"), AwsJob("0 0/15 * * * ?"), history, "region: us-east-1", Aws);

        Assert.Equal(6m * 730 * 0.20m, Assert.Single(report.Changes).MonthlyBefore);
        Assert.Contains(report.GlobalAssumptions, a => a.Contains("list prices from system.billing.list_prices", StringComparison.Ordinal));
    }

    [Fact]
    public void Region_suffixed_list_prices_use_the_sku_the_workspace_used()
    {
        var prices = Run.Engine(Run.Config("region: us-east-1"), Aws, Cloud.Aws, "us-east-1").Prices;
        prices.UseListPrices(
            new Dictionary<string, decimal>
            {
                ["PREMIUM_JOBS_SERVERLESS_COMPUTE_US_EAST_N_VIRGINIA"] = 0.39m,
                ["PREMIUM_JOBS_SERVERLESS_COMPUTE_EU_WEST_IRELAND"] = 0.45m,
                ["PREMIUM_JOBS_COMPUTE_PHOTON"] = 0.16m,
            },
            ["PREMIUM_JOBS_SERVERLESS_COMPUTE_US_EAST_N_VIRGINIA"]);

        Assert.Equal(0.39m, prices.LiveListPrices[Skus.JobsServerless]);
        Assert.Equal(0.16m, prices.LiveListPrices[Skus.JobsComputePhoton]);
        Assert.False(prices.LiveListPrices.ContainsKey(Skus.JobsCompute));
    }

    [Fact]
    public void No_usage_in_the_window_keeps_the_configured_estimate()
    {
        var empty = new ResourceHistory { Type = ResourceTypes.Jobs, Key = "j", Id = "123", WindowDays = 30 };
        var row = Assert.Single(Usage.Analyze(AwsJob("0 0 * * * ?"), AwsJob("0 0/15 * * * ?"), Usage.History(empty), "region: us-east-1", Aws).Changes);

        Assert.Null(row.Before!.History);
        Assert.Contains(row.Before.Assumptions, a => a.Contains("no usage in the last 30 days", StringComparison.Ordinal));
    }

    [Fact]
    public void Calibrated_row_reads_runs_observed()
    {
        var report = Usage.Analyze(AwsJob("0 0 * * * ?"), AwsJob("0 0/15 * * * ?"), Usage.History(Usage.Job("j", 720, 6)), "region: us-east-1", Aws);

        var markdown = MarkdownRenderer.Render(report);
        Assert.Contains("High · 720 runs observed", markdown);
        Assert.Contains("Calibration: history from system.billing.usage over the last 30 days in target `prod`, 1 calibrated.", markdown);
    }

    [Theory]
    [InlineData(0.90, true)]
    [InlineData(0.78, false)]
    public void Node_type_drift(double observed, bool warns)
    {
        // n1 is rated 0.75 DBU per hour: 0.90 is 20% off, 0.78 is 4%.
        var history = Usage.History(Usage.Job("j", 720, 6));
        history.Nodes.Add(new NodeObservation("n1", (decimal)observed * 100, 100));

        var report = Usage.Analyze(AwsJob("0 0 * * * ?"), AwsJob("0 0/15 * * * ?"), history, "region: us-east-1", Aws);

        Assert.Equal(warns, report.Warnings.Any(w => w.Contains("Catalog drift: `n1` is rated 0.75 DBU per hour", StringComparison.Ordinal) && w.Contains("0.9", StringComparison.Ordinal)));
    }
}

/// <summary>Replays recorded rows by statement purpose and records what ran.</summary>
internal sealed class RecordedSql(Dictionary<string, IReadOnlyList<IReadOnlyList<string?>>> rows, string? failPurpose = null, string failCode = "PERMISSION_DENIED") : ISqlRunner
{
    public List<SqlStatement> Statements { get; } = [];

    public Task<IReadOnlyList<IReadOnlyList<string?>>> QueryAsync(SqlStatement statement, CancellationToken cancellationToken)
    {
        Statements.Add(statement);
        if (failPurpose is not null && statement.Purpose.StartsWith(failPurpose, StringComparison.Ordinal))
        {
            throw new SqlQueryException(failCode, $"[{failCode}] User does not have SELECT on table");
        }

        return Task.FromResult(rows.GetValueOrDefault(statement.Purpose) ?? []);
    }
}

public class HistoryCollectorTests
{
    private static readonly CalibrationSettings Settings = new() { Enabled = true, WarehouseId = "abcdef0123456789" };

    private static readonly Dictionary<(string, string), string> Ids = new() { [(ResourceTypes.Jobs, "j")] = "123" };

    private static RecordedSql Recorded(string? fail = null) => new(new()
    {
        ["usage of jobs"] =
        [
            ["123", "r1", "PREMIUM_JOBS_COMPUTE", "false", null, "2026-10-01", "4"],
            ["123", "r1", "PREMIUM_JOBS_COMPUTE", "false", null, "2026-10-02", "2"],
            ["123", "r2", "PREMIUM_JOBS_COMPUTE", "false", null, "2026-10-03", "8"],
            ["123", "r3", "PREMIUM_JOBS_COMPUTE", "false", null, "2026-10-03", "-1"],
            ["999", "x", "PREMIUM_JOBS_COMPUTE", "false", null, "2026-10-03", "100"],
        ],
        ["list prices"] = [["PREMIUM_JOBS_COMPUTE", "0.15", "2025-01-01T00:00:00Z"], ["PREMIUM_JOBS_COMPUTE", "0.16", "2026-01-01T00:00:00Z"]],
        ["node timeline"] = [["0601-182128-dcbte59m", "n1", "false", "75", "100"], ["0601-182128-aaaaaaaa", "n1", "true", "50", "10"]],
    }, fail);

    [Fact]
    public async Task Rows_become_runs_days_and_prices()
    {
        var history = await HistoryCollector.CollectAsync(Recorded(), Ids, [], Settings, Cloud.Aws, "prod", CancellationToken.None);

        var job = history.Resources[(ResourceTypes.Jobs, "j")];
        Assert.Equal(13m, job.TotalDbus);
        Assert.Equal(2, job.RunCount); // r3 nets to a negative correction and does not count
        Assert.Equal(7m, job.MedianDbusPerRun); // runs of 6 and 8
        Assert.Equal(3, job.Days.Count);
        Assert.Equal(0.16m, history.ListPrices["PREMIUM_JOBS_COMPUTE"]);
        var node = Assert.Single(history.Nodes); // the Photon cluster is left out
        Assert.Equal(0.75m, node.DbuPerNodeHour);
    }

    [Fact]
    public async Task Missing_permission_is_a_warning_not_a_failure()
    {
        var history = await HistoryCollector.CollectAsync(Recorded("usage"), Ids, [], Settings, Cloud.Aws, "prod", CancellationToken.None);

        Assert.Empty(history.Resources);
        Assert.Contains("PERMISSION_DENIED", Assert.Single(history.Warnings));
        Assert.Contains("SELECT on `system.billing`", history.Warnings[0]);
    }

    [Fact]
    public async Task Unreadable_compute_tables_skip_the_drift_check()
    {
        var history = await HistoryCollector.CollectAsync(Recorded("node timeline"), Ids, [], Settings, Cloud.Aws, "prod", CancellationToken.None);

        Assert.Empty(history.Nodes);
        Assert.Contains(history.Notes, n => n.Contains("drift check skipped", StringComparison.Ordinal));
        Assert.Empty(history.Warnings);
    }
}

public class DeployedIdsTests
{
    // Recorded from the CLI's resource structs: IDs are strings at resources.<type>.<key>.id.
    private const string Summary = """
        {
          "bundle": { "name": "b", "target": "prod" },
          "resources": {
            "jobs": {
              "orders": { "id": "206000809187888", "url": "https://x/jobs/206000809187888", "name": "orders" },
              "fresh": { "name": "fresh", "modified_status": "created" },
              "bad": { "id": "1 OR 1=1" }
            },
            "pipelines": { "events": { "id": "8f2a1c3e-4b5d-4e6f-8a9b-0c1d2e3f4a5b" } },
            "sql_warehouses": { "bi": { "id": "abcdef0123456789" } },
            "clusters": { "shared": { "id": "0601-182128-dcbte59m" } },
            "schemas": { "s": { "id": "main.s" } }
          }
        }
        """;

    [Fact]
    public void Summary_ids_by_type_and_key()
    {
        var ids = DeployedIds.FromSummary(Summary);

        Assert.Equal("206000809187888", ids[(ResourceTypes.Jobs, "orders")]);
        Assert.Equal("8f2a1c3e-4b5d-4e6f-8a9b-0c1d2e3f4a5b", ids[(ResourceTypes.Pipelines, "events")]);
        Assert.Equal("abcdef0123456789", ids[(ResourceTypes.SqlWarehouses, "bi")]);
        Assert.Equal("0601-182128-dcbte59m", ids[(ResourceTypes.Clusters, "shared")]);
        Assert.Equal(4, ids.Count); // not deployed, invalid and unpriced entries are left out
    }

    [Fact]
    public void Name_fallback_needs_exactly_one_id_per_name()
    {
        var resolved = DeployedIds.ReadJobsByName([["orders", "1"], ["dup", "2"], ["dup", "3"], ["bad", "x"]]);

        Assert.Equal("1", resolved["orders"]);
        Assert.False(resolved.ContainsKey("dup"));
        Assert.False(resolved.ContainsKey("bad"));
        Assert.Contains(":name_0", DeployedIds.JobsByName(["orders"], 30).Text);
    }
}

public class StatementClientTests
{
    private static readonly DatabricksCredentials Credentials = new("https://adb-1.2.azuredatabricks.net", "dapi-secret");

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Polls_then_follows_chunks()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/2.0/sql/statements" => Json("""{ "statement_id": "s1", "status": { "state": "PENDING" } }"""),
            "/api/2.0/sql/statements/s1" => Json("""{ "statement_id": "s1", "status": { "state": "SUCCEEDED" }, "result": { "data_array": [["1", null]], "next_chunk_internal_link": "/api/2.0/sql/statements/s1/result/chunks/1" } }"""),
            "/api/2.0/sql/statements/s1/result/chunks/1" => Json("""{ "data_array": [["2", "b"]] }"""),
            _ => Json("{}", HttpStatusCode.NotFound),
        });
        var client = new StatementExecutionClient(new HttpClient(handler), Credentials, "abcdef0123456789") { PollInterval = TimeSpan.Zero };

        var rows = await client.QueryAsync(UsageQueries.Usage(ResourceTypes.Jobs, ["123"], 30), CancellationToken.None);

        Assert.Equal([["1", null], ["2", "b"]], rows.Select(r => r.ToArray()).ToArray());
        var body = handler.Requests[0].Body!;
        Assert.Contains("\"warehouse_id\":\"abcdef0123456789\"", body);
        Assert.Contains("{\"name\":\"id_0\",\"value\":\"123\",\"type\":\"STRING\"}", body);
        Assert.Contains("\"format\":\"JSON_ARRAY\"", body);
    }

    [Fact]
    public async Task Failed_statement_carries_its_error_code_and_not_the_token()
    {
        var handler = new FakeHandler(_ => Json("""{ "statement_id": "s1", "status": { "state": "FAILED", "error": { "error_code": "PERMISSION_DENIED", "message": "no SELECT" } } }"""));
        var client = new StatementExecutionClient(new HttpClient(handler), Credentials, "abcdef0123456789");

        var ex = await Assert.ThrowsAsync<SqlQueryException>(() => client.QueryAsync(UsageQueries.ListPrices("AWS"), CancellationToken.None));

        Assert.True(ex.IsPermission);
        Assert.DoesNotContain("dapi-secret", ex.Message);
        Assert.DoesNotContain("dapi-secret", Credentials.ToString());
    }

    [Fact]
    public async Task Forbidden_warehouse_is_a_permission_problem()
    {
        var handler = new FakeHandler(_ => Json("""{ "message": "no CAN USE on warehouse" }""", HttpStatusCode.Forbidden));
        var client = new StatementExecutionClient(new HttpClient(handler), Credentials, "abcdef0123456789");

        var ex = await Assert.ThrowsAsync<SqlQueryException>(() => client.QueryAsync(UsageQueries.ListPrices("AWS"), CancellationToken.None));

        Assert.True(ex.IsPermission);
    }

    [Fact]
    public async Task Throttling_is_retried()
    {
        var handler = new FakeHandler(_ => Json("""{ "statement_id": "s1", "status": { "state": "SUCCEEDED" }, "result": { "data_array": [] } }"""), throttleFirst: 2);
        var client = new StatementExecutionClient(new HttpClient(handler), Credentials, "abcdef0123456789") { RetryDelayScale = 0 };

        Assert.Empty(await client.QueryAsync(UsageQueries.ListPrices("AWS"), CancellationToken.None));
        Assert.Equal(3, handler.Calls);
    }
}

public class CalibrationCacheTests
{
    [Fact]
    public async Task Second_run_on_the_same_day_runs_no_statements()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dbudoctor-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var recorded = new RecordedSql(new() { ["list prices"] = [["PREMIUM_JOBS_COMPUTE", "0.15", null]] });
            var time = new FixedTime(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero));
            var statement = UsageQueries.ListPrices("AWS");

            var first = new CachingSqlRunner(() => recorded, dir, "https://h", time);
            await first.QueryAsync(statement, CancellationToken.None);
            var second = new CachingSqlRunner(() => recorded, dir, "https://h", time);
            var rows = await second.QueryAsync(statement, CancellationToken.None);

            Assert.Equal(1, first.Executed);
            Assert.Equal(0, second.Executed);
            Assert.Equal("0.15", rows[0][1]);
            Assert.Null(rows[0][2]);

            var nextDay = new CachingSqlRunner(() => recorded, dir, "https://h", new FixedTime(time.GetUtcNow().AddDays(1)));
            await nextDay.QueryAsync(statement, CancellationToken.None);
            Assert.Equal(1, nextDay.Executed);
            Assert.Single(Directory.GetDirectories(Path.Combine(dir, "calibration"))); // yesterday was pruned
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public class DatabricksAuthTests
{
    [Fact]
    public async Task Personal_access_token_first()
    {
        var credentials = await DatabricksAuth.ResolveAsync("adb-1.2.azuredatabricks.net", new Dictionary<string, string?> { ["DATABRICKS_TOKEN"] = "t" }, new HttpClient(), new FakeDatabricks(), null, CancellationToken.None);

        Assert.Equal("https://adb-1.2.azuredatabricks.net", credentials.Host);
        Assert.Equal("t", credentials.Token);
    }

    [Fact]
    public async Task OAuth_machine_to_machine()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "access_token": "m2m" }""") });
        var env = new Dictionary<string, string?> { ["DATABRICKS_HOST"] = "https://h.cloud.databricks.com/", ["DATABRICKS_CLIENT_ID"] = "id", ["DATABRICKS_CLIENT_SECRET"] = "s" };

        var credentials = await DatabricksAuth.ResolveAsync(null, env, new HttpClient(handler), new FakeDatabricks(), null, CancellationToken.None);

        Assert.Equal("m2m", credentials.Token);
        Assert.Equal("https://h.cloud.databricks.com/oidc/v1/token", handler.Requests[0].Url);
        Assert.Contains("grant_type=client_credentials", handler.Requests[0].Body);
    }

    [Fact]
    public async Task GitHub_oidc_token_is_exchanged()
    {
        var handler = new FakeHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.Host == "gh.example" ? """{ "value": "gh-jwt" }""" : """{ "access_token": "federated" }"""),
        });
        var env = new Dictionary<string, string?>
        {
            ["DATABRICKS_AUTH_TYPE"] = "github-oidc",
            ["DATABRICKS_CLIENT_ID"] = "sp-uuid",
            ["DATABRICKS_ACCOUNT_ID"] = "acct-1",
            ["ACTIONS_ID_TOKEN_REQUEST_URL"] = "https://gh.example/token?api-version=2.0",
            ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = "req",
        };

        var credentials = await DatabricksAuth.ResolveAsync("https://h.cloud.databricks.com", env, new HttpClient(handler), new FakeDatabricks(), null, CancellationToken.None);

        Assert.Equal("federated", credentials.Token);
        Assert.Equal("https://gh.example/token?api-version=2.0&audience=acct-1", handler.Requests[0].Url);
        Assert.Equal("https://h.cloud.databricks.com/oidc/v1/token", handler.Requests[1].Url);
        var body = handler.Requests[1].Body!;
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Atoken-exchange", body);
        Assert.Contains("subject_token=gh-jwt", body);
        Assert.Contains("client_id=sp-uuid", body);
    }

    [Fact]
    public async Task GitHub_oidc_without_id_token_permission_is_explained()
    {
        var ex = await Assert.ThrowsAsync<CalibrationUnavailableException>(() => DatabricksAuth.ResolveAsync(
            "h", new Dictionary<string, string?> { ["DATABRICKS_AUTH_TYPE"] = "github-oidc" }, new HttpClient(), new FakeDatabricks(), null, CancellationToken.None));

        Assert.Contains("id-token: write", ex.Message);
    }

    [Fact]
    public async Task No_credentials_is_unavailable_not_fatal()
    {
        var ex = await Assert.ThrowsAsync<CalibrationUnavailableException>(() =>
            DatabricksAuth.ResolveAsync("h", new Dictionary<string, string?>(), new HttpClient(), new FakeDatabricks(), null, CancellationToken.None));

        Assert.Contains("DATABRICKS_TOKEN", ex.Message);
    }
}

public class CalibrationCliTests
{
    private static readonly string Fixture = Repo.Path("tests", "Fixtures", "bundles", "azure-calibrated");

    private static async Task<(int Exit, string Out, string Error)> RunAsync(RecordedSql sql, string cache, Dictionary<string, string?>? extra = null, string? config = null)
    {
        var variables = new Dictionary<string, string?> { ["DATABRICKS_TOKEN"] = "t", ["DBUDOCTOR_CACHE_DIR"] = cache };
        foreach (var (k, v) in extra ?? [])
        {
            variables[k] = v;
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var env = new DBUDoctor.Cli.DiffEnvironment
        {
            Variables = variables,
            WorkingDirectory = Fixture,
            Out = output,
            Error = error,
            Catalog = TestCatalog.Load,
            Reference = Run.Reference,
            ToolVersion = "0.0.0-test",
            Sql = _ => sql,
        };
        var exit = await DBUDoctor.Cli.DiffCommand.RunAsync(
            new DBUDoctor.Cli.DiffArguments { BaseJson = "base.json", HeadJson = "head.json", Flags = new CliFlags { Format = "json", Config = config } }, env);
        return (exit, output.ToString(), error.ToString());
    }

    private static RecordedSql Recorded(string? fail = null) => new(new()
    {
        ["jobs by name"] = [["orders_sync", "111"]],
        ["usage of jobs"] = [["111", "r1", "PREMIUM_JOBS_COMPUTE", "false", null, "2026-10-01", "6"]],
    }, fail);

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "dbudoctor-cal-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Missing_permission_warns_and_keeps_the_exit_code()
    {
        var cache = TempDir();
        try
        {
            var (exit, output, error) = await RunAsync(Recorded("jobs by name"), cache);

            Assert.Equal(0, exit);
            Assert.Contains("warning: Calibration skipped: cannot read `system.billing.usage` (PERMISSION_DENIED)", error);
            Assert.Contains("SELECT on `system.billing`", output);
            Assert.DoesNotContain("\"t\"", output);
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task Same_day_rerun_executes_no_statements()
    {
        var cache = TempDir();
        try
        {
            var first = Recorded();
            await RunAsync(first, cache);
            var second = Recorded();
            var (_, output, _) = await RunAsync(second, cache);

            Assert.NotEmpty(first.Statements);
            Assert.Empty(second.Statements);
            Assert.Contains("\"calibratedChanges\": 1", output);
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task Disabled_calibration_executes_no_statements()
    {
        var cache = TempDir();
        var config = Path.Combine(Path.GetTempPath(), "dbudoctor-off-" + Guid.NewGuid().ToString("N") + ".yml");
        await File.WriteAllTextAsync(config, "region: westeurope\n");
        try
        {
            var sql = Recorded();
            var (exit, output, _) = await RunAsync(sql, cache, config: config);

            Assert.Equal(0, exit);
            Assert.Empty(sql.Statements);
            Assert.Contains("\"calibration\": null", output);
        }
        finally
        {
            File.Delete(config);
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task No_credentials_is_a_warning()
    {
        var cache = TempDir();
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var env = new DBUDoctor.Cli.DiffEnvironment
            {
                Variables = new Dictionary<string, string?>(),
                WorkingDirectory = Fixture,
                Out = output,
                Error = error,
                Catalog = TestCatalog.Load,
                Reference = Run.Reference,
            };

            var exit = await DBUDoctor.Cli.DiffCommand.RunAsync(new DBUDoctor.Cli.DiffArguments { BaseJson = "base.json", HeadJson = "head.json" }, env);

            Assert.Equal(0, exit);
            Assert.Contains("Calibration skipped: no Databricks credentials", error.ToString());
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }
}
