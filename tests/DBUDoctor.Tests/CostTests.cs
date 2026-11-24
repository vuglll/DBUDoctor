using DBUDoctor.Core.Config;
using DBUDoctor.Core.Cost;
using DBUDoctor.Core.Model;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Scheduling;

namespace DBUDoctor.Tests;

public class PricingTests
{
    private static PricingContext Context(string yaml) =>
        new(TestCatalog.Load(), Cloud.Azure, "westeurope", Run.Config("region: westeurope\n" + yaml).Pricing, new EstimationSettings());

    [Fact]
    public void Sku_override_is_exact()
    {
        var price = Context("pricing: { overrides: { PREMIUM_JOBS_COMPUTE: 0.24 }, discounts: { all: 0.5 }, surcharge: 0.3 }")
            .EffectivePrice(Skus.JobsCompute, out _);
        Assert.Equal(0.24m, price);
    }

    [Fact]
    public void Commit_style_discount()
    {
        Assert.Equal(0.24m, Context("pricing: { discounts: { all: 0.20 } }").EffectivePrice(Skus.JobsCompute, out _));
    }

    [Fact]
    public void Discount_and_surcharge()
    {
        Assert.Equal(0.33m, Context("pricing: { discounts: { all: 0.20 }, surcharge: 0.30 }").EffectivePrice(Skus.JobsCompute, out _));
    }

    [Fact]
    public void Family_discount_overrides_the_default()
    {
        var context = Context("pricing: { discounts: { all: 0.20, jobs: 0.10 } }");
        Assert.Equal(0.27m, context.EffectivePrice(Skus.JobsCompute, out _));
        Assert.Equal(0.44m, context.EffectivePrice(Skus.AllPurposeCompute, out _));
    }

    [Fact]
    public void Missing_region_is_the_reason()
    {
        var context = new PricingContext(TestCatalog.Load(), Cloud.Azure, null, new PricingSettings(), new EstimationSettings());
        Assert.Null(context.EffectivePrice(Skus.JobsCompute, out var reason));
        Assert.Contains("region not set", reason);
    }
}

public class CostEngineTests
{
    private static Job Job(string json) => Bundle.Job(json).Document.Resources!.Jobs!["j"];

    [Fact]
    public void Azure_classic_job_cluster_includes_vms()
    {
        var estimate = Run.Engine().EstimateJob(Job(Bundle.ClassicJob()));

        Assert.Equal(1.125m, estimate.DbuCostPerHour);
        Assert.Equal(1.145m, estimate.VmCostPerHour);
        Assert.Equal(2.27m, estimate.RatePerHour);
        Assert.True(estimate.InfraIncluded);
    }

    [Fact]
    public void Classic_job_cluster_dbus_only()
    {
        var catalog = TestCatalog.Inline("aws", "us-east-1", "PREMIUM_JOBS_COMPUTE: 0.15", "n1: { dbu: 0.75 }");
        var estimate = Run.Engine(Run.Config("region: us-east-1"), catalog, Cloud.Aws, "us-east-1")
            .EstimateJob(Job(Bundle.ClassicJob(cluster: Bundle.Cluster("n1"))));

        Assert.Equal(0.5625m, estimate.RatePerHour);
        Assert.False(estimate.InfraIncluded);
    }

    [Fact]
    public void Cloud_without_vm_prices_states_vms_are_not_included()
    {
        var catalog = TestCatalog.Inline("aws", "us-east-1", "PREMIUM_JOBS_COMPUTE: 0.15", "n1: { dbu: 0.75 }");
        var head = Bundle.Job(Bundle.ClassicJob(cluster: Bundle.Cluster("n1")), Bundle.AwsHost);
        var report = Run.Analyze(Bundle.Of("{}", Bundle.AwsHost), head, Run.Config("region: us-east-1"), catalog);

        Assert.False(report.InfraIncluded);
        Assert.Contains(report.Warnings, w => w.Contains("Cloud VMs are not included", StringComparison.Ordinal));
        Assert.Contains("cloud VMs for classic compute", report.NotModeled);
    }

    private static DBUDoctor.Core.Pricing.PriceCatalog AwsWithVms() => TestCatalog.Inline(
        "aws", "us-east-1",
        "PREMIUM_JOBS_COMPUTE: 0.15\nPREMIUM_SQL_PRO_COMPUTE: 0.55",
        "m5.xlarge: { dbu: 0.69 }",
        "m5.xlarge: { payg: 0.192, reserved_1y: 0.121, reserved_3y: 0.083 }\ni3.2xlarge: { payg: 0.624 }\ni3.4xlarge: { payg: 1.248 }");

    [Fact]
    public void Aws_classic_job_cluster_includes_vms()
    {
        var estimate = Run.Engine(Run.Config("region: us-east-1"), AwsWithVms(), Cloud.Aws, "us-east-1")
            .EstimateJob(Job(Bundle.ClassicJob(cluster: Bundle.Cluster("m5.xlarge", workers: 2))));

        Assert.Equal(0.3105m, estimate.DbuCostPerHour);
        Assert.Equal(0.576m, estimate.VmCostPerHour);
        Assert.Equal(0.8865m, estimate.RatePerHour);
        Assert.True(estimate.InfraIncluded);
    }

    [Fact]
    public void Aws_spot_commitment_makes_the_vm_part_unknown()
    {
        var estimate = Run.Engine(Run.Config("region: us-east-1\npricing: { infra: { commitment: spot } }"), AwsWithVms(), Cloud.Aws, "us-east-1")
            .EstimateJob(Job(Bundle.ClassicJob(cluster: Bundle.Cluster("m5.xlarge", workers: 2))));

        Assert.Null(estimate.RatePerHour);
        Assert.Contains(estimate.UnknownReasons, u => u.Contains("no spot VM price for `m5.xlarge` in us-east-1", StringComparison.Ordinal));
    }

    [Fact]
    public void Aws_pro_warehouse_uses_the_documented_layout()
    {
        var report = Run.Analyze(
            Bundle.Of("{}", Bundle.AwsHost),
            Bundle.Of("""{ "sql_warehouses": { "w": { "cluster_size": "Small", "warehouse_type": "PRO", "enable_serverless_compute": false } } }""", Bundle.AwsHost),
            Run.Config("region: us-east-1"),
            AwsWithVms());

        var after = Assert.Single(report.RateRows).After!;
        Assert.Equal(1.248m + (4 * 0.624m), after.VmCostPerHour);
        Assert.True(report.InfraIncluded);
    }

    private const string PoolCluster = """{ "spark_version": "15.4.x-scala2.12", "instance_pool_id": "pool-a", "num_workers": 2%EXTRA% }""";

    private static CostEstimate PoolJob(string config, string extra = "") =>
        Run.Engine(Run.Config("region: westeurope\n" + config)).EstimateJob(Job(Bundle.ClassicJob(cluster: PoolCluster.Replace("%EXTRA%", extra, StringComparison.Ordinal))));

    [Fact]
    public void Pool_cluster_uses_the_mapped_node_type()
    {
        var estimate = PoolJob("estimation: { instance_pools: { pool-a: Standard_DS3_v2 } }");

        Assert.Equal(0.675m, estimate.DbuCostPerHour);
        Assert.Equal(0.687m, estimate.VmCostPerHour);
        Assert.Equal(1.362m, estimate.RatePerHour);
        Assert.Contains("node type `Standard_DS3_v2` from instance pool `pool-a` (estimation.instance_pools)", estimate.Assumptions);
        Assert.Empty(estimate.UnknownReasons);
    }

    [Fact]
    public void Driver_pool_maps_the_driver_separately()
    {
        var estimate = PoolJob(
            "estimation: { instance_pools: { pool-a: Standard_DS3_v2, pool-b: Standard_DS4_v2 } }",
            """, "driver_instance_pool_id": "pool-b" """);

        Assert.Equal(0.9m, estimate.DbuCostPerHour);
        Assert.Equal(0.916m, estimate.VmCostPerHour);
        Assert.Contains("node type `Standard_DS4_v2` from instance pool `pool-b` (estimation.instance_pools)", estimate.Assumptions);
    }

    [Fact]
    public void Unmapped_pool_is_unknown_and_names_the_setting()
    {
        var estimate = PoolJob("estimation: { instance_pools: { other: Standard_DS3_v2 } }");

        Assert.Null(estimate.RatePerHour);
        Assert.Contains(estimate.UnknownReasons, r => r.Contains("set `estimation.instance_pools.pool-a` to the pool's node type", StringComparison.Ordinal));
    }

    [Fact]
    public void Unmapped_driver_pool_is_unknown()
    {
        var estimate = PoolJob("estimation: { instance_pools: { pool-a: Standard_DS3_v2 } }", """, "driver_instance_pool_id": "pool-z" """);

        Assert.Null(estimate.RatePerHour);
        Assert.Contains(estimate.UnknownReasons, r => r.Contains("estimation.instance_pools.pool-z", StringComparison.Ordinal));
    }

    [Fact]
    public void Pool_backed_bundle_gets_a_monthly_figure()
    {
        var head = Bundle.Job(Bundle.ClassicJob(cluster: PoolCluster.Replace("%EXTRA%", "", StringComparison.Ordinal)));
        var report = Run.Analyze(Bundle.Of("{}"), head, Run.Config("region: westeurope\nestimation: { default_run_minutes: 30, instance_pools: { pool-a: Standard_DS3_v2 } }"));

        var row = Assert.Single(report.Changes);
        Assert.False(row.HasUnknownRate);
        Assert.Equal(1.362m * 0.5m * 730m, row.After!.Monthly);
    }

    [Fact]
    public void Reserved_vms_use_the_reservation_column()
    {
        var estimate = Run.Engine(Run.Config("region: westeurope\npricing: { infra: { commitment: reserved_3y } }")).EstimateJob(Job(Bundle.ClassicJob()));
        Assert.Equal(0.50m, estimate.VmCostPerHour);
    }

    [Fact]
    public void Infra_false_drops_the_vm_line()
    {
        var estimate = Run.Engine(Run.Config("region: westeurope\npricing: { include_cloud_infra: false }")).EstimateJob(Job(Bundle.ClassicJob()));
        Assert.Equal(1.125m, estimate.RatePerHour);
        Assert.Null(estimate.VmCostPerHour);
    }

    [Fact]
    public void Autoscale_at_max()
    {
        var cluster = """{ "node_type_id": "Standard_DS3_v2", "autoscale": { "min_workers": 2, "max_workers": 10 } }""";
        var estimate = Run.Engine(Run.Config("region: westeurope\npricing: { include_cloud_infra: false }")).EstimateJob(Job(Bundle.ClassicJob(cluster: cluster)));

        Assert.Equal(0.75m * 11, estimate.DbuPerHour);
        Assert.Contains(estimate.Assumptions, a => a.StartsWith("autoscale at max", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("mid", 6)]
    [InlineData("min", 2)]
    public void Autoscale_alternatives(string mode, int workers)
    {
        var cluster = """{ "node_type_id": "Standard_DS3_v2", "autoscale": { "min_workers": 2, "max_workers": 10 } }""";
        var estimate = Run.Engine(Run.Config($"region: westeurope\nestimation: {{ autoscale: {mode} }}")).EstimateJob(Job(Bundle.ClassicJob(cluster: cluster)));
        Assert.Equal(0.75m * (1 + workers), estimate.DbuPerHour);
    }

    [Fact]
    public void Photon_on_azure_uses_the_photon_dbu_count()
    {
        var cluster = Bundle.Cluster(extra: """, "runtime_engine": "PHOTON" """);
        var estimate = Run.Engine().EstimateJob(Job(Bundle.ClassicJob(cluster: cluster)));

        Assert.Equal(1.88m * 5, estimate.DbuPerHour);
        Assert.Equal(Skus.JobsComputePhoton, estimate.Sku);
    }

    [Fact]
    public void Photon_with_a_multiplier()
    {
        var catalog = TestCatalog.Inline("aws", "us-east-1", "PREMIUM_JOBS_COMPUTE: 0.15", "m5.xlarge: { dbu: 0.69, family: m5 }", photon: "method: multiplier\nmultipliers: { m5: 2.0 }");
        var cluster = Bundle.Cluster("m5.xlarge", 1, """, "runtime_engine": "PHOTON" """);
        var estimate = Run.Engine(Run.Config("region: us-east-1"), catalog, Cloud.Aws, "us-east-1").EstimateJob(Job(Bundle.ClassicJob(cluster: cluster)));

        Assert.Equal(0.69m * 2 * 2, estimate.DbuPerHour);
    }

    [Fact]
    public void Serverless_sql_warehouse_rate()
    {
        var warehouse = Bundle.Of("""{ "sql_warehouses": { "w": { "cluster_size": "Small", "min_num_clusters": 1, "enable_serverless_compute": true } } }""")
            .Document.Resources!.SqlWarehouses!["w"];
        var estimate = Run.Engine().EstimateWarehouse(warehouse);

        Assert.Equal(8.40m, estimate.RatePerHour);
        Assert.Equal(Confidence.Unknown, estimate.Confidence);
    }

    [Fact]
    public void Pro_warehouse_on_azure_adds_its_vms()
    {
        var warehouse = Bundle.Of("""{ "sql_warehouses": { "w": { "cluster_size": "Small", "warehouse_type": "PRO" } } }""")
            .Document.Resources!.SqlWarehouses!["w"];
        var estimate = Run.Engine().EstimateWarehouse(warehouse);

        // 12 DBU × 0.55, plus an E16ds_v4 driver and 4 × E8ds_v4 workers.
        Assert.Equal(6.60m, estimate.DbuCostPerHour);
        Assert.Equal(1.00m + (4 * 0.50m), estimate.VmCostPerHour);
    }

    [Fact]
    public void Unknown_node_type_is_named()
    {
        var estimate = Run.Engine().EstimateJob(Job(Bundle.ClassicJob(cluster: Bundle.Cluster("Standard_Nope"))));

        Assert.Null(estimate.RatePerHour);
        Assert.Contains(estimate.UnknownReasons, r => r.Contains("Standard_Nope", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_cloud_gives_one_reason_not_a_missing_node_type()
    {
        var estimate = Run.Engine(cloud: null, region: null).EstimateJob(Job(Bundle.ClassicJob()));

        Assert.Null(estimate.RatePerHour);
        Assert.Contains(estimate.UnknownReasons, r => r.StartsWith("cloud unknown", StringComparison.Ordinal));
        Assert.DoesNotContain(estimate.UnknownReasons, r => r.Contains("node type", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_node_type_does_not_fail_unless_configured()
    {
        var head = Bundle.Job(Bundle.ClassicJob(cluster: Bundle.Cluster("Standard_Nope")));
        Assert.Empty(Run.Analyze(Bundle.Of("{}"), head).Violations);

        var strict = Run.Analyze(Bundle.Of("{}"), head, Run.Config("region: westeurope\nfail_on: { unknown_cost: true }"));
        Assert.Equal("fail_on.unknown_cost", Assert.Single(strict.Violations).Policy);
    }

    [Fact]
    public void Configured_run_duration_gives_a_medium_monthly_figure()
    {
        var catalog = TestCatalog.Inline("aws", "us-east-1", "PREMIUM_JOBS_COMPUTE: 1.00", "n1: { dbu: 1.0 }");
        var estimate = Run.Engine(Run.Config("region: us-east-1\nestimation: { default_run_minutes: 20 }"), catalog, Cloud.Aws, "us-east-1")
            .EstimateJob(Job(Bundle.ClassicJob(cluster: Bundle.Cluster("n1", 2))));

        Assert.Equal(3.00m, estimate.RatePerHour);
        Assert.Equal(730m, estimate.Monthly);
        Assert.Equal(Confidence.Medium, estimate.Confidence);
        Assert.Contains("20-minute runs (configured)", estimate.Assumptions);
    }

    [Fact]
    public void Without_a_duration_the_monthly_figure_is_unknown()
    {
        var estimate = Run.Engine().EstimateJob(Job(Bundle.ClassicJob()));

        Assert.NotNull(estimate.RatePerHour);
        Assert.Equal(730m, estimate.Frequency!.RunsPerMonth);
        Assert.Null(estimate.Monthly);
        Assert.Equal(Confidence.Unknown, estimate.Confidence);
    }

    [Fact]
    public void Constant_runtime_scales_with_size()
    {
        var config = Run.Config("region: westeurope\nestimation: { default_run_minutes: 20 }\npricing: { include_cloud_infra: false }");
        var report = Run.Analyze(Bundle.Job(Bundle.ClassicJob()), Bundle.Job(Bundle.ClassicJob(cluster: Bundle.Cluster(workers: 8))), config);

        var row = Assert.Single(report.Changes);
        Assert.Equal(1.8m, row.RateAfter / row.RateBefore);
        Assert.Equal(1.8m, row.MonthlyAfter / row.MonthlyBefore);
        Assert.Contains(row.After!.Assumptions, a => a.StartsWith("constant runtime", StringComparison.Ordinal));
    }

    [Fact]
    public void Linear_speedup_keeps_the_monthly_figure()
    {
        var config = Run.Config("region: westeurope\nestimation: { default_run_minutes: 20, scaling: linear_speedup }");
        var report = Run.Analyze(Bundle.Job(Bundle.ClassicJob()), Bundle.Job(Bundle.ClassicJob(cluster: Bundle.Cluster(workers: 8))), config);

        var row = Assert.Single(report.Changes);
        Assert.Equal(row.MonthlyBefore, row.MonthlyAfter);
        Assert.Contains(row.After!.Assumptions, a => a.StartsWith("linear speedup", StringComparison.Ordinal));
        Assert.Contains(report.GlobalAssumptions, a => a.Contains("linear speedup", StringComparison.Ordinal));
    }

    [Fact]
    public void New_serverless_job_needs_history()
    {
        var head = Bundle.Job("""{ "tasks": [ { "task_key": "t", "environment_key": "default", "spark_python_task": { "python_file": "x.py" } } ], "schedule": { "quartz_cron_expression": "0 0 * * * ?" } }""");
        var report = Run.Analyze(Bundle.Of("{}"), head);

        var row = Assert.Single(report.HistoryRows);
        Assert.True(row.After!.Serverless);
        Assert.Null(row.After.Monthly);
        Assert.Equal("needs history", row.After.MonthlyNote);
        Assert.Contains("needs history", Core.Reporting.MarkdownRenderer.Render(report));
    }

    [Fact]
    public void Pipeline_switched_to_continuous()
    {
        var catalog = TestCatalog.Inline("aws", "us-east-1", "PREMIUM_DLT_ADVANCED_COMPUTE: 1.00", "n1: { dbu: 1.0 }");
        var pipeline = """{ "pipelines": { "p": { "edition": "ADVANCED", "continuous": %C%, "clusters": [ { "label": "default", "node_type_id": "n1", "num_workers": 1 } ] } } }""";
        var report = Run.Analyze(
            Bundle.Of(pipeline.Replace("%C%", "false", StringComparison.Ordinal), Bundle.AwsHost),
            Bundle.Of(pipeline.Replace("%C%", "true", StringComparison.Ordinal), Bundle.AwsHost),
            Run.Config("region: us-east-1"),
            catalog);

        var row = Assert.Single(report.Changes);
        Assert.Equal(2.00m, row.RateAfter);
        Assert.Equal(1460m, row.After!.Monthly);
        Assert.Equal(Confidence.Medium, row.After.Confidence);
    }

    [Fact]
    public void Continuous_job_runs_all_month()
    {
        var job = Bundle.ClassicJob(schedule: "null").Replace("\"schedule\": null", "\"continuous\": { \"pause_status\": \"UNPAUSED\" }", StringComparison.Ordinal);
        var estimate = Run.Engine().EstimateJob(Job(job));
        Assert.Equal(2.27m * 730, estimate.Monthly);
    }

    [Fact]
    public void Warehouse_moving_to_serverless_without_vm_prices_is_low()
    {
        var catalog = TestCatalog.Inline("aws", "us-east-1", "PREMIUM_SQL_PRO_COMPUTE: 0.55\nPREMIUM_SERVERLESS_SQL_COMPUTE: 0.70", "n1: { dbu: 1 }");
        var report = Run.Analyze(
            Bundle.Of("""{ "sql_warehouses": { "w": { "cluster_size": "Small", "warehouse_type": "PRO" } } }""", Bundle.AwsHost),
            Bundle.Of("""{ "sql_warehouses": { "w": { "cluster_size": "Small", "warehouse_type": "PRO", "enable_serverless_compute": true } } }""", Bundle.AwsHost),
            Run.Config("region: us-east-1"),
            catalog);

        var row = Assert.Single(report.RateRows);
        Assert.Equal(Confidence.Low, row.Confidence);
        Assert.Contains(CostComparer.CrossModeNote, row.Notes);
    }

    [Fact]
    public void Same_move_on_azure_is_not_low()
    {
        var report = Run.Analyze(
            Bundle.Of("""{ "sql_warehouses": { "w": { "cluster_size": "Small", "warehouse_type": "PRO" } } }"""),
            Bundle.Of("""{ "sql_warehouses": { "w": { "cluster_size": "Small", "warehouse_type": "PRO", "enable_serverless_compute": true } } }"""));

        var row = Assert.Single(report.RateRows);
        Assert.NotEqual(Confidence.Low, row.Confidence);
        Assert.Empty(row.Notes);
        Assert.NotNull(row.Before!.VmCostPerHour);
    }

    [Fact]
    public void Job_on_an_existing_cluster_is_priced_elsewhere()
    {
        var estimate = Run.Engine().EstimateJob(Job("""{ "tasks": [ { "task_key": "t", "existing_cluster_id": "abc" } ] }"""));

        Assert.True(estimate.PricedElsewhere);
        Assert.False(estimate.RateUnknown);
        Assert.Equal(Skus.AllPurposeCompute, estimate.Sku);
    }
}

public class RunFrequencyTests
{
    private static RunFrequency Cron(string expression, string? zone = "UTC") => RunFrequency.ForCron(expression, zone, Run.Reference);

    [Fact]
    public void Hourly_to_every_fifteen_minutes()
    {
        Assert.Equal(730m, Cron("0 0 * * * ?").RunsPerMonth);
        Assert.Equal(2920m, Cron("0 0/15 * * * ?").RunsPerMonth);
        Assert.Equal("hourly", Cron("0 0 * * * ?").Description);
        Assert.Equal("every 15 min", Cron("0 0/15 * * * ?").Description);
    }

    [Fact]
    public void Paused_schedule_runs_zero_times()
    {
        var job = new Job { Schedule = new CronSchedule { QuartzCronExpression = "0 0 * * * ?", PauseStatus = "PAUSED" } };
        Assert.Equal(0m, RunFrequency.ForJob(job, Run.Reference).RunsPerMonth);
    }

    [Fact]
    public void File_arrival_is_unknown()
    {
        var job = Bundle.Job("""{ "trigger": { "file_arrival": { "url": "s3://x/" } } }""").Document.Resources!.Jobs!["j"];
        var frequency = RunFrequency.ForJob(job, Run.Reference);
        Assert.Null(frequency.RunsPerMonth);
        Assert.Contains("file arrival", frequency.UnknownReason);
    }

    [Fact]
    public void Periodic_trigger()
    {
        var job = Bundle.Job("""{ "trigger": { "periodic": { "interval": 2, "unit": "HOURS" } } }""").Document.Resources!.Jobs!["j"];
        Assert.Equal(365m, RunFrequency.ForJob(job, Run.Reference).RunsPerMonth);
    }

    [Fact]
    public void No_trigger_is_unknown()
    {
        Assert.Null(RunFrequency.ForJob(new Job(), Run.Reference).RunsPerMonth);
    }

    [Fact]
    public void Daily_in_a_time_zone_with_dst_still_fires_365_times()
    {
        Assert.Equal(364m, Cron("0 30 2 * * ?", "Europe/Amsterdam").RunsPerMonth * 12);
        Assert.Equal(365m, Cron("0 0 6 * * ?", "Europe/Amsterdam").RunsPerMonth * 12);
    }

    [Theory]
    [InlineData("44 37 8 * * ?", "daily")]
    [InlineData("0 0 * * * ?", "hourly")]
    [InlineData("0 0 6 ? * MON", "weekly")]
    public void Dst_change_does_not_break_the_description(string expression, string description)
    {
        // Across the DST change, 08:37 to 08:37 is 23 or 25 hours, but still a day on the wall clock.
        Assert.Equal(description, Cron(expression, "Europe/Amsterdam").Description);
    }

    [Fact]
    public void Unknown_time_zone_is_unknown()
    {
        Assert.Contains("unknown time zone", Cron("0 0 * * * ?", "Mars/Olympus").UnknownReason);
    }

    [Fact]
    public void Default_reference_is_the_first_of_january()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), RunFrequency.DefaultReference(time));
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public class QuartzCronTests
{
    private static long Fires(string expression, string zone = "UTC") =>
        QuartzCron.Parse(expression).CountFires(Run.Reference, Run.Reference.AddDays(365), TimeZoneInfo.FindSystemTimeZoneById(zone));

    [Theory]
    [InlineData("0 0 * * * ?", 8760)]
    [InlineData("0 * * * * ?", 525600)]
    [InlineData("* * * * * ?", 31536000)]
    [InlineData("0 0 12 * * ?", 365)]
    [InlineData("0 0 12 ? * MON-FRI", 261)]
    [InlineData("0 0 12 ? * 2-6", 261)]
    [InlineData("0 0 12 1 * ?", 12)]
    [InlineData("0 0 12 L * ?", 12)]
    [InlineData("0 0 12 LW * ?", 12)]
    [InlineData("0 0 12 15W * ?", 12)]
    [InlineData("0 0 12 ? * 6#3", 12)]
    [InlineData("0 0 12 ? * 6L", 12)]
    [InlineData("0 0 12 ? JAN,JUL *", 62)]
    [InlineData("0 0 12 * * ? 2027", 0)]
    [InlineData("0 0 22-2 * * ?", 365 * 5)]
    [InlineData("0 0 0 ? * FRI-MON", 208)]
    public void Counts_fire_times_in_a_year(string expression, long expected)
    {
        Assert.Equal(expected, Fires(expression));
    }

    [Fact]
    public void Dst_changes_skip_missing_times_and_fire_repeated_times_once()
    {
        // 2026-03-29 02:00-02:59 does not exist in Amsterdam, so 02:30 and 02:00 do not fire that day.
        // 2026-10-25 02:00-02:59 happens twice, and fires once.
        Assert.Equal(364, Fires("0 30 2 * * ?", "Europe/Amsterdam"));
        Assert.Equal(8760 - 1, Fires("0 0 * * * ?", "Europe/Amsterdam"));
    }

    [Theory]
    [InlineData("0 0/15 * * * ?", 900)]
    [InlineData("0 0 * * * ?", 3600)]
    [InlineData("0 0 9,17 ? * MON-FRI", 8 * 3600)]
    [InlineData("0/10 * * * * ?", 10)]
    public void Shortest_interval(string expression, int seconds)
    {
        var cron = QuartzCron.Parse(expression);
        Assert.Equal(TimeSpan.FromSeconds(seconds), cron.ShortestInterval(Run.Reference, TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData("0 0 * * *")]
    [InlineData("0 0 25 * * ?")]
    [InlineData("0 0 12 1 * MON")]
    [InlineData("0 0 12 ? * 9")]
    public void Rejects_invalid_expressions(string expression)
    {
        Assert.False(QuartzCron.TryParse(expression, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Next_fire_respects_the_time_zone()
    {
        var next = QuartzCron.Parse("0 0 6 * * ?").NextFire(Run.Reference, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"));
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 5, 0, 0, TimeSpan.Zero), next);
    }
}

public class AppPricingTests
{
    private static readonly DBUDoctor.Core.Pricing.PriceCatalog Catalog = TestCatalog.Inline("aws", "us-east-1", "PREMIUM_ALL_PURPOSE_SERVERLESS_COMPUTE: 0.80", "n1: { dbu: 1 }") is var c
        ? new DBUDoctor.Core.Pricing.PriceCatalog
        {
            Prices = c.Prices, NodeTypes = c.NodeTypes, Warehouses = c.Warehouses, NotModeled = c.NotModeled,
            Serverless = DBUDoctor.Core.Pricing.CatalogLoader.LoadDirectory(Repo.Path("tests", "Fixtures", "catalog")).Serverless,
        }
        : null!;

    private static DBUDoctor.Core.Reporting.BundleReport Analyze(string baseApps, string headApps) => Run.Analyze(
        Bundle.Of($$"""{ "apps": {{baseApps}} }""", Bundle.AwsHost), Bundle.Of($$"""{ "apps": {{headApps}} }""", Bundle.AwsHost), Run.Config("region: us-east-1"), Catalog);

    [Fact]
    public void Medium_to_large()
    {
        // 0.5 × 0.80 × 730 = 292; 1 × 0.80 × 730 = 584.
        var row = Assert.Single(Analyze("""{ "a": { "compute_size": "MEDIUM" } }""", """{ "a": { "compute_size": "LARGE" } }""").Changes);

        Assert.Equal(292m, row.MonthlyBefore);
        Assert.Equal(584m, row.MonthlyAfter);
        Assert.Equal(Confidence.Medium, row.Confidence);
        Assert.Contains(row.After!.Assumptions, a => a.StartsWith("running all month", StringComparison.Ordinal));
    }

    [Fact]
    public void Size_not_set_is_medium()
    {
        var row = Assert.Single(Analyze("{}", """{ "a": { "name": "a" } }""").Changes);

        Assert.Equal(292m, row.MonthlyAfter);
        Assert.Contains(row.After!.Assumptions, a => a.Contains("priced as MEDIUM (the default)", StringComparison.Ordinal));
    }

    [Fact]
    public void Instances_multiply()
    {
        // 3 LARGE instances: 3 × 1 × 0.80 × 730 = 1,752.
        var row = Assert.Single(Analyze("{}", """{ "a": { "compute_size": "LARGE", "compute_min_instances": 2, "compute_max_instances": 3 } }""").Changes);

        Assert.Equal(1752m, row.MonthlyAfter);
    }
}

public class LakebasePricingTests
{
    private static readonly DBUDoctor.Core.Pricing.PriceCatalog Catalog = TestCatalog.Inline("aws", "us-east-1", "PREMIUM_DATABASE_SERVERLESS_COMPUTE: 0.50", "n1: { dbu: 1 }") is var c
        ? new DBUDoctor.Core.Pricing.PriceCatalog
        {
            Prices = c.Prices, NodeTypes = c.NodeTypes, Warehouses = c.Warehouses, NotModeled = c.NotModeled,
            Serverless = DBUDoctor.Core.Pricing.CatalogLoader.LoadDirectory(Repo.Path("tests", "Fixtures", "catalog")).Serverless,
        }
        : null!;

    private static CostComparison Row(string endpoint) => Assert.Single(Run.Analyze(
        Bundle.Of("{}", Bundle.AwsHost),
        Bundle.Of($$"""{ "postgres_endpoints": { "e": {{endpoint}} } }""", Bundle.AwsHost),
        Run.Config("region: us-east-1"), Catalog).Changes);

    [Fact]
    public void Always_active_endpoint()
    {
        // 0.230 DBU per CU × 0.50 = 0.115 per CU-hour; 2 CU × 730 = 167.90; up to 8 CU × 730 = 671.60.
        var row = Row("""{ "autoscaling_limit_min_cu": 2, "autoscaling_limit_max_cu": 8, "no_suspension": true }""");

        Assert.Equal(167.90m, row.MonthlyAfter);
        Assert.Equal(671.60m, row.After!.MonthlyCeiling);
        Assert.Equal(Confidence.Medium, row.Confidence);
        Assert.Contains(row.After.Assumptions, a => a.Contains("up to $672 a month at 8 CU", StringComparison.Ordinal));
    }

    [Fact]
    public void Scale_to_zero_endpoint_shows_a_rate_and_a_ceiling()
    {
        var row = Row("""{ "autoscaling_limit_min_cu": 2, "autoscaling_limit_max_cu": 8 }""");

        Assert.Null(row.MonthlyAfter);
        Assert.Equal(0.23m, row.RateAfter);
        Assert.Equal(671.60m, row.After!.MonthlyCeiling);
        Assert.Equal("scales to zero: monthly cost depends on active time", row.After.MonthlyNote);
    }

    [Fact]
    public void Gcp_has_no_cu_rate()
    {
        var prices = new PricingContext(Catalog, Cloud.Gcp, "us-central1", new PricingSettings(), new EstimationSettings());
        var estimate = new CostEngine(prices, Run.Reference).EstimatePostgresEndpoint(new DBUDoctor.Core.Model.PostgresEndpoint { MinCu = 1, NoSuspension = true });

        Assert.Contains(estimate.UnknownReasons, r => r.Contains("no Lakebase DBU per CU for GCP", StringComparison.Ordinal));
    }
}
