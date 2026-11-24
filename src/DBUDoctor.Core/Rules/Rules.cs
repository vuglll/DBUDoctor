using DBUDoctor.Core.Config;
using DBUDoctor.Core.Diff;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Reporting;
using DBUDoctor.Core.Scheduling;

namespace DBUDoctor.Core.Rules;

public static class RuleRegistry
{
    /// <summary>Every shipped rule, in ID order.</summary>
    public static IReadOnlyList<IRule> All { get; } = new IRule[]
    {
        new Dbu001AllPurposeCompute(),
        new Dbu002ClusterAutoTermination(),
        new Dbu003WarehouseAutoStop(),
        new Dbu004ContinuousPipeline(),
        new Dbu005ContinuousJob(),
        new Dbu007JobTimeout(),
        new Dbu008CostTags(),
        new Dbu009LargeOnDemand(),
        new Dbu010FrequencyIncrease(),
        new Dbu011ServerlessPerformanceMode(),
        new Dbu012RuntimeEndOfSupport(),
        new Dbu013LargeApp(),
        new Dbu014ServingScaleToZero(),
        new Dbu015LakebaseSizing(),
        new Dbu016ShortSchedule(),
        new Dbu017SpotWithoutFallback(),
        new Dbu018LegacyAutoscaling(),
        new Dbu019DefaultTagCollision(),
    }.OrderBy(r => r.Metadata.Id, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Rule IDs and the parameter names each accepts, for configuration validation. List parameters
    /// carry a <c>[]</c> suffix, such as <c>required_tags[]</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Parameters { get; } =
        All.ToDictionary(
            r => r.Metadata.Id,
            r => (IReadOnlyCollection<string>)r.Metadata.Parameters.Select(p => p.Name).Concat(r.Metadata.ListParameters.Select(p => p.Name + "[]")).ToArray(),
            StringComparer.Ordinal);
}

/// <summary>DBU001: a job task runs on an existing all-purpose cluster instead of Jobs compute.</summary>
public sealed class Dbu001AllPurposeCompute : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU001",
        Title = "Task runs on all-purpose compute",
        DefaultSeverity = Severity.Warning,
        WhenItFires = "A job task sets `existing_cluster_id`, so it runs on an all-purpose cluster, which is billed at the all-purpose DBU rate instead of the cheaper Jobs compute rate.",
        HowToFix = "Give the task a job cluster: define it under `job_clusters` and point the task at it with `job_cluster_key`, or use `new_cluster`, or remove the cluster settings to run the task on serverless compute (`environment_key` for Python tasks).",
        WhenToSuppress = "The task needs state that lives on the shared cluster (for example, cached tables or libraries installed by hand), or it runs rarely enough that a job cluster's startup time costs more than the price difference. Give the reason and when it will change.",
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var jobs = context.Current.Document.Resources?.Jobs;
        if (jobs is null)
        {
            yield break;
        }

        var ratio = Ratio(context);
        foreach (var (key, job) in jobs.OrderBy(j => j.Key, StringComparer.Ordinal))
        {
            foreach (var task in job.Tasks ?? [])
            {
                if (task.ExistingClusterId is not { } clusterId)
                {
                    continue;
                }

                var cluster = clusterId.Length > 16 ? clusterId[..12] + "…" : clusterId;
                var tail = ratio is { } r
                    ? $"Jobs compute costs about {Format.Percent(r)} of the all-purpose DBU rate here."
                    : "Jobs compute costs less per DBU than all-purpose compute.";
                yield return context.Finding(
                    ResourceTypes.Jobs,
                    key,
                    $"tasks[{task.TaskKey}].existing_cluster_id",
                    $"job `{key}`, task `{task.TaskKey}` runs on all-purpose cluster `{cluster}`. {tail}");
            }
        }
    }

    private static decimal? Ratio(RuleContext context)
    {
        var jobs = context.Prices.ListPrice(Skus.JobsCompute);
        var allPurpose = context.Prices.ListPrice(Skus.AllPurposeCompute);
        return jobs is { } j && allPurpose is > 0 ? j / allPurpose.Value : null;
    }
}

/// <summary>DBU002: an all-purpose cluster never auto-terminates, or waits long before it does.</summary>
public sealed class Dbu002ClusterAutoTermination : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU002",
        Title = "All-purpose cluster auto-termination",
        DefaultSeverity = Severity.Warning,
        WhenItFires = "A cluster sets `autotermination_minutes: 0` (a warning), so it bills for every idle hour until someone stops it; or it omits the field or waits longer than `max_minutes` (default 60) before terminating (info). Without the field, the Clusters API never auto-terminates the cluster, and the effective default depends on the deployment engine.",
        HowToFix = "Set `autotermination_minutes` to the shortest idle time your users accept, such as 30 or 60. The allowed range is 10 to 10,000 minutes.",
        WhenToSuppress = "The cluster must stay up between infrequent requests and its idle cost has been approved, for example a cluster that serves a BI tool with a strict start-up time. Give the reason and the review date.",
        Parameters = [new RuleParameter("max_minutes", 60, "Longest idle time, in minutes, before a cluster should terminate.")],
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var clusters = context.Current.Document.Resources?.Clusters;
        if (clusters is null)
        {
            yield break;
        }

        var maxMinutes = context.Parameter("max_minutes");
        var engine = new Cost.CostEngine(context.Prices, context.Reference);
        foreach (var (key, cluster) in clusters.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            var minutes = cluster.AutoterminationMinutes;
            if (minutes is { } m && m > 0 && m <= maxMinutes)
            {
                continue;
            }

            var rate = engine.EstimateCluster(cluster).RatePerHour;
            var perHour = rate is { } r ? Format.Money(r) : null;
            if (minutes == 0)
            {
                var cost = rate is { } r0
                    ? $"idle, it bills {perHour} per hour, up to {Format.Money(r0 * RunFrequency.HoursPerMonth)} a month"
                    : "it bills for every idle hour";
                yield return context.Finding(ResourceTypes.Clusters, key, "autotermination_minutes",
                    $"cluster `{key}` never auto-terminates (`autotermination_minutes: 0`): {cost}.", Severity.Warning);
            }
            else if (minutes is { } idle)
            {
                var hours = idle / 60m;
                var cost = rate is { } r1
                    ? $"each idle stretch bills up to {Format.Money(r1 * hours)} ({perHour} per hour)"
                    : $"each idle stretch bills up to {Format.Number(hours)} hours of the cluster";
                yield return context.Finding(ResourceTypes.Clusters, key, "autotermination_minutes",
                    $"cluster `{key}` waits {Format.Number(idle)} minutes idle before it terminates (limit {Format.Number(maxMinutes)}): {cost}.", Severity.Info);
            }
            else
            {
                var cost = perHour is null ? "" : $" Idle, it bills {perHour} per hour.";
                yield return context.Finding(ResourceTypes.Clusters, key, "autotermination_minutes",
                    $"cluster `{key}` does not set `autotermination_minutes`. The effective default depends on the deployment engine; the Clusters API does not auto-terminate a cluster without it.{cost}", Severity.Info);
            }
        }
    }
}

/// <summary>DBU003: a SQL warehouse never auto-stops, or waits long before it does.</summary>
public sealed class Dbu003WarehouseAutoStop : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU003",
        Title = "SQL warehouse auto-stop",
        DefaultSeverity = Severity.Warning,
        WhenItFires = "A SQL warehouse sets `auto_stop_mins: 0` (a warning), so it bills for every idle hour until someone stops it; or its auto-stop is longer than `max_minutes` (default 30) (info). Without the field, the SQL Warehouses API default applies: 120 minutes.",
        HowToFix = "Set `auto_stop_mins` to the shortest idle time your users accept: 10 minutes is the UI default for serverless warehouses, and the API accepts 0 or at least 10 (serverless from 1).",
        WhenToSuppress = "Dashboards or users need the warehouse warm between infrequent queries and the idle cost has been approved. Give the reason and the review date.",
        Parameters = [new RuleParameter("max_minutes", 30, "Longest idle time, in minutes, before a warehouse should stop.")],
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var warehouses = context.Current.Document.Resources?.SqlWarehouses;
        if (warehouses is null)
        {
            yield break;
        }

        var maxMinutes = context.Parameter("max_minutes");
        var engine = new Cost.CostEngine(context.Prices, context.Reference);
        var catalogDefault = context.Prices.Catalog.Defaults.WarehouseAutoStopMinutes;
        foreach (var (key, warehouse) in warehouses.OrderBy(w => w.Key, StringComparer.Ordinal))
        {
            var minutes = (decimal?)warehouse.AutoStopMins ?? catalogDefault?.Value;
            if (minutes is not { } effective || (effective > 0 && effective <= maxMinutes))
            {
                continue;
            }

            var rate = engine.EstimateWarehouse(warehouse).RatePerHour;
            var perHour = rate is { } r ? Format.Money(r) : null;
            if (effective == 0)
            {
                var cost = rate is { } r0
                    ? $"idle, it bills {perHour} per hour, up to {Format.Money(r0 * RunFrequency.HoursPerMonth)} a month"
                    : "it bills for every idle hour";
                var origin = warehouse.AutoStopMins is null ? " by default" : "";
                yield return context.Finding(ResourceTypes.SqlWarehouses, key, "auto_stop_mins",
                    $"sql_warehouse `{key}` never auto-stops{origin} (`auto_stop_mins: 0`): {cost}.", Severity.Warning);
                continue;
            }

            var hours = effective / 60m;
            var stretch = rate is { } r1
                ? $"each idle stretch bills up to {Format.Money(r1 * hours)} ({perHour} per hour)"
                : $"each idle stretch bills up to {Format.Number(hours)} hours of the warehouse";
            var setting = warehouse.AutoStopMins is null
                ? $"does not set `auto_stop_mins`, so the API default of {Format.Number(effective)} minutes applies"
                : $"waits {Format.Number(effective)} minutes idle before it stops";
            if (warehouse.AutoStopMins is null && catalogDefault is not null)
            {
                context.Prices.Used(catalogDefault.Provenance);
            }

            yield return context.Finding(ResourceTypes.SqlWarehouses, key, "auto_stop_mins",
                $"sql_warehouse `{key}` {setting} (limit {Format.Number(maxMinutes)}): {stretch}.", Severity.Info);
        }
    }
}

/// <summary>DBU004: a pipeline runs continuously.</summary>
public sealed class Dbu004ContinuousPipeline : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU004",
        Title = "Continuous pipeline",
        DefaultSeverity = Severity.Warning,
        WhenItFires = "A pipeline sets `continuous: true`, so its compute runs around the clock (about 730 hours a month) instead of only while an update runs.",
        HowToFix = "Set `continuous: false` and trigger the pipeline on a schedule (a job with a `pipeline_task`) that matches how fresh the data must be. For low-latency needs, consider serverless or a shorter schedule before running continuously.",
        WhenToSuppress = "The data must stay fresh within minutes, and the business has accepted the always-on cost. Name the latency requirement in the reason.",
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var pipelines = context.Current.Document.Resources?.Pipelines;
        if (pipelines is null)
        {
            yield break;
        }

        foreach (var (key, pipeline) in pipelines.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (pipeline.Continuous == true)
            {
                yield return context.Finding(
                    ResourceTypes.Pipelines,
                    key,
                    "continuous",
                    $"pipeline `{key}` is continuous: it runs around the clock (~730 h / month).");
            }
        }
    }
}

/// <summary>DBU005: a job runs in continuous mode.</summary>
public sealed class Dbu005ContinuousJob : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU005",
        Title = "Continuous job",
        DefaultSeverity = Severity.Warning,
        WhenItFires = "A job has a `continuous` block that is not paused (`pause_status` defaults to `UNPAUSED`). A continuous job always has one run executing, and a new run starts within a minute of the last, so its compute runs around the clock: about 730 hours a month.",
        HowToFix = "Replace `continuous` with a `schedule` or a file-arrival or table-update `trigger` that matches how fresh the data must be. For streaming, check whether a triggered run (`Trigger.AvailableNow`) on a schedule is enough.",
        WhenToSuppress = "The workload must process data within minutes around the clock, and the always-on cost has been approved. Name the latency requirement in the reason.",
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var jobs = context.Current.Document.Resources?.Jobs;
        if (jobs is null)
        {
            yield break;
        }

        var engine = new Cost.CostEngine(context.Prices, context.Reference);
        foreach (var (key, job) in jobs.OrderBy(j => j.Key, StringComparer.Ordinal))
        {
            if (job.Continuous is null || RunFrequency.ForJob(job, context.Reference).AlwaysOn != true)
            {
                continue;
            }

            var rate = engine.EstimateJob(job).RatePerHour;
            var cost = rate is { } r
                ? $"at {Format.Money(r)} per hour, about {Format.Money(r * RunFrequency.HoursPerMonth)} a month"
                : "(~730 h / month)";
            yield return context.Finding(ResourceTypes.Jobs, key, "continuous",
                $"job `{key}` is continuous: a new run starts within a minute of the last, so it runs around the clock {cost}.");
        }
    }
}

/// <summary>DBU007: a job run can go on forever: no timeout on the job, and none on a task.</summary>
public sealed class Dbu007JobTimeout : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU007",
        Title = "Job without a timeout",
        DefaultSeverity = Severity.Info,
        WhenItFires = "A job has no `timeout_seconds` (absent or 0, which means no timeout), and at least one of its tasks has none either. A stuck run keeps its compute busy until someone cancels it. Continuous jobs are skipped.",
        HowToFix = "Set `timeout_seconds` on the job (it applies to each run), or on every task, to a few times the normal run duration. `health` rules on `RUN_DURATION_SECONDS` only notify; they do not stop the run.",
        WhenToSuppress = "Runs have no predictable upper bound and are watched by other means, such as an on-call alert on run duration. Say what watches them.",
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var jobs = context.Current.Document.Resources?.Jobs;
        if (jobs is null)
        {
            yield break;
        }

        var engine = new Cost.CostEngine(context.Prices, context.Reference);
        foreach (var (key, job) in jobs.OrderBy(j => j.Key, StringComparer.Ordinal))
        {
            if (job.TimeoutSeconds is > 0 || job.Continuous is not null)
            {
                continue;
            }

            var open = (job.Tasks ?? []).Where(t => t.TimeoutSeconds is not > 0).Select(t => t.TaskKey ?? "?").ToList();
            if (open.Count == 0)
            {
                continue;
            }

            var rate = engine.EstimateJob(job).RatePerHour;
            var tasks = open.Count == (job.Tasks?.Count ?? 0) ? "neither the job nor its tasks set one" : $"the job sets none, nor {(open.Count == 1 ? "does task" : "do tasks")} {string.Join(", ", open.Select(t => $"`{t}`"))}";
            var cost = rate is { } r ? $", and a stuck run keeps billing {Format.Money(r)} per hour until someone cancels it" : "";
            yield return context.Finding(ResourceTypes.Jobs, key, "timeout_seconds",
                $"job `{key}` has no timeout: {tasks}{cost}.");
        }
    }
}

/// <summary>DBU008: a priced resource lacks a tag key the organization requires for cost attribution.</summary>
public sealed class Dbu008CostTags : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU008",
        Title = "Missing cost-attribution tags",
        DefaultSeverity = Severity.Warning,
        WhenItFires = "`required_tags` lists tag keys (the rule is off while it is empty), and a job, pipeline, cluster or SQL warehouse lacks one of them where it reaches billing: job `tags` and job cluster `custom_tags`; pipeline `tags` and cluster `custom_tags`; cluster `custom_tags`; warehouse `tags.custom_tags`. A serverless job or pipeline with a `usage_policy_id` (or the older `budget_policy_id`) counts as attributed.",
        HowToFix = "Add the missing keys with the owner's values, for example `tags: { team: data-platform, cost_center: \"4711\" }` on a job or `custom_tags` on its job clusters. For serverless jobs and pipelines, attach a usage policy that sets the tags.",
        WhenToSuppress = "The resource is shared infrastructure whose cost is split by another mechanism. Say which.",
        ListParameters = [new RuleListParameter("required_tags", "Tag keys every priced resource must carry, such as `[team, cost_center]`. Empty turns the rule off.")],
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var required = context.List("required_tags");
        var resources = context.Current.Document.Resources;
        if (required.Count == 0 || resources is null)
        {
            yield break;
        }

        var findings = new List<Finding>();
        foreach (var (key, job) in (resources.Jobs ?? []).OrderBy(j => j.Key, StringComparer.Ordinal))
        {
            var serverless = !Cost.CostEngine.Flatten(job.Tasks ?? []).Any(t => t.JobClusterKey is not null || t.NewCluster is not null || t.ExistingClusterId is not null);
            if (serverless && (job.UsagePolicyId ?? job.BudgetPolicyId) is not null)
            {
                continue;
            }

            var keys = Keys(job.Tags)
                .Concat((job.JobClusters ?? []).SelectMany(c => Keys(c.NewCluster?.CustomTags)))
                .Concat(Cost.CostEngine.Flatten(job.Tasks ?? []).SelectMany(t => Keys(t.NewCluster?.CustomTags)));
            Report(ResourceTypes.Jobs, key, "tags", keys, serverless);
        }

        foreach (var (key, pipeline) in (resources.Pipelines ?? []).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (pipeline.Serverless == true && (pipeline.UsagePolicyId ?? pipeline.BudgetPolicyId) is not null)
            {
                continue;
            }

            Report(ResourceTypes.Pipelines, key, "tags", Keys(pipeline.Tags).Concat((pipeline.Clusters ?? []).SelectMany(c => Keys(c.CustomTags))), pipeline.Serverless == true);
        }

        foreach (var (key, cluster) in (resources.Clusters ?? []).OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            Report(ResourceTypes.Clusters, key, "custom_tags", Keys(cluster.CustomTags), false);
        }

        foreach (var (key, warehouse) in (resources.SqlWarehouses ?? []).OrderBy(w => w.Key, StringComparer.Ordinal))
        {
            Report(ResourceTypes.SqlWarehouses, key, "tags.custom_tags", (warehouse.Tags?.CustomTags ?? []).Select(t => t.Key).OfType<string>(), false);
        }

        foreach (var finding in findings)
        {
            yield return finding;
        }

        void Report(string type, string key, string field, IEnumerable<string> present, bool serverless)
        {
            var have = present.ToHashSet(StringComparer.Ordinal);
            var missing = required.Where(r => !have.Contains(r)).ToList();
            if (missing.Count == 0)
            {
                return;
            }

            var names = string.Join(", ", missing.Select(m => $"`{m}`"));
            var hint = serverless ? " For serverless compute, attach a usage policy (`usage_policy_id`) that sets them." : "";
            findings.Add(context.Finding(type, key, field,
                $"{ResourceTypes.Singular(type)} `{key}` lacks required tag{(missing.Count == 1 ? "" : "s")} {names} (rules.DBU008.required_tags), so billing cannot attribute its cost.{hint}"));
        }
    }

    private static IEnumerable<string> Keys<T>(Dictionary<string, T>? tags) => tags?.Keys ?? Enumerable.Empty<string>();
}

/// <summary>The classic clusters of jobs and pipelines, with where they sit in the resource.</summary>
internal static class ClassicClusters
{
    public static IEnumerable<(string Type, string Key, string Path, string What, Model.ClusterSpec Spec)> Of(Model.BundleResources? resources)
    {
        foreach (var (key, job) in (resources?.Jobs ?? []).OrderBy(j => j.Key, StringComparer.Ordinal))
        {
            foreach (var cluster in job.JobClusters ?? [])
            {
                if (cluster.NewCluster is { } spec)
                {
                    yield return (ResourceTypes.Jobs, key, $"job_clusters[{cluster.JobClusterKey}].new_cluster", $"job `{key}`, job cluster `{cluster.JobClusterKey}`", spec);
                }
            }

            foreach (var task in Cost.CostEngine.Flatten(job.Tasks ?? []))
            {
                if (task.NewCluster is { } spec)
                {
                    yield return (ResourceTypes.Jobs, key, $"tasks[{task.TaskKey}].new_cluster", $"job `{key}`, task `{task.TaskKey}`", spec);
                }
            }
        }

        foreach (var (key, pipeline) in (resources?.Pipelines ?? []).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (pipeline.Serverless == true)
            {
                continue;
            }

            foreach (var cluster in pipeline.Clusters ?? [])
            {
                var label = cluster.Label ?? "default";
                yield return (ResourceTypes.Pipelines, key, $"clusters[{label}]", $"pipeline `{key}`, cluster `{label}`", cluster);
            }
        }
    }

    /// <summary>The fallback variant of an availability value, per cloud.</summary>
    public static string? Fallback(string availability) => availability.ToUpperInvariant() switch
    {
        "ON_DEMAND" or "SPOT" => "SPOT_WITH_FALLBACK",
        "ON_DEMAND_AZURE" or "SPOT_AZURE" => "SPOT_WITH_FALLBACK_AZURE",
        "ON_DEMAND_GCP" or "PREEMPTIBLE_GCP" => "PREEMPTIBLE_WITH_FALLBACK_GCP",
        _ => null,
    };
}

/// <summary>DBU009: a large job or pipeline cluster explicitly runs every worker on on-demand VMs.</summary>
public sealed class Dbu009LargeOnDemand : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU009",
        Title = "Large on-demand cluster",
        DefaultSeverity = Severity.Info,
        WhenItFires = "A classic job or pipeline cluster has at least `min_workers` workers (default 8; the maximum, for autoscale) and sets its availability explicitly to on-demand (`ON_DEMAND`, `ON_DEMAND_AZURE` or `ON_DEMAND_GCP`). Batch workers tolerate spot capacity well, and spot VMs cost a fraction of on-demand. Where the catalog has spot prices (Azure), the message gives the saving.",
        HowToFix = "Use spot with fallback (`SPOT_WITH_FALLBACK`, `SPOT_WITH_FALLBACK_AZURE` or `PREEMPTIBLE_WITH_FALLBACK_GCP`) and keep the driver on demand with `first_on_demand: 1`.",
        WhenToSuppress = "The workload cannot tolerate losing workers mid-run, for example long stateful jobs without checkpoints. Say why.",
        Parameters = [new RuleParameter("min_workers", 8, "Worker count from which an on-demand cluster is reported.")],
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var minWorkers = context.Parameter("min_workers");
        foreach (var (type, key, path, what, spec) in ClassicClusters.Of(context.Current.Document.Resources))
        {
            var workers = spec.Autoscale?.MaxWorkers ?? spec.NumWorkers ?? 0;
            if (workers < minWorkers || spec.Availability is not { } availability ||
                !availability.StartsWith("ON_DEMAND", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var saving = "";
            if (spec.NodeTypeId is { } node && context.Prices.Sheet?.Vm.TryGetValue(node, out var vm) == true &&
                vm.For(Commitment.Payg) is { } payg && vm.For(Commitment.Spot) is { } spot && payg > spot)
            {
                context.Prices.Used(vm.Provenance);
                saving = $" Spot workers would save up to {Format.Money(workers * (payg - spot))} per hour on VMs ({Format.Number(workers)} × `{node}` at {Format.Money(payg)} on demand against {Format.Money(spot)} spot, catalog prices).";
            }

            var attributes = spec.AwsAttributes?.Availability is not null ? "aws_attributes" : spec.AzureAttributes?.Availability is not null ? "azure_attributes" : "gcp_attributes";
            yield return context.Finding(type, key, $"{path}.{attributes}.availability",
                $"{what} runs up to {Format.Number(workers)} workers on on-demand VMs (`{availability}`). Spot with fallback (`{ClassicClusters.Fallback(availability)}`) with `first_on_demand: 1` keeps the driver on demand.{saving}");
        }
    }
}

/// <summary>DBU010: a job's schedule runs sharply more often than in base.</summary>
public sealed class Dbu010FrequencyIncrease : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU010",
        Title = "Sharp frequency increase",
        DefaultSeverity = Severity.Warning,
        WhenItFires = "A job runs at least `factor` times (default 4) as often as in the base branch, and at least `min_runs` (default 100) more times a month. Each run pays for compute, and on classic compute also for cluster startup.",
        HowToFix = "Check that the new schedule matches how fresh the data must be. Keep the old cadence if it is enough, or move frequent runs to compute that starts quickly (serverless or an instance pool).",
        WhenToSuppress = "The higher frequency is a deliberate requirement and its cost has been approved. Name the requirement in the reason.",
        Parameters =
        [
            new RuleParameter("factor", 4, "Minimum ratio of head runs to base runs."),
            new RuleParameter("min_runs", 100, "Minimum increase in runs per month."),
        ],
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var headJobs = context.Current.Document.Resources?.Jobs;
        var baseJobs = context.Previous?.Document.Resources?.Jobs;
        if (headJobs is null || baseJobs is null)
        {
            yield break;
        }

        var factor = context.Parameter("factor");
        var minRuns = context.Parameter("min_runs");
        foreach (var (key, job) in headJobs.OrderBy(j => j.Key, StringComparer.Ordinal))
        {
            if (!baseJobs.TryGetValue(key, out var baseJob))
            {
                continue;
            }

            var before = RunFrequency.ForJob(baseJob, context.Reference);
            var after = RunFrequency.ForJob(job, context.Reference);
            if (before.RunsPerMonth is not { } b || after.RunsPerMonth is not { } a)
            {
                continue;
            }

            if (a - b < minRuns || a < factor * b)
            {
                continue;
            }

            var crons = before.CronExpression is { } oldCron && after.CronExpression is { } newCron && oldCron != newCron
                ? $" Cron `{oldCron}` → `{newCron}`."
                : "";
            var message = b > 0
                ? $"job `{key}` runs {Format.Multiplier(a / b)} as often ({Format.Number(b)} → {Format.Number(a)} runs / month).{crons}"
                : $"job `{key}` now runs {Format.Number(a)} times / month (was {Format.Number(b)}).{crons}";
            var field = job.Schedule is not null
                ? (before.CronExpression != after.CronExpression ? "schedule.quartz_cron_expression" : "schedule")
                : "trigger";
            yield return context.Finding(ResourceTypes.Jobs, key, field, message);
        }
    }
}

/// <summary>DBU011: a scheduled serverless job runs in performance-optimized mode.</summary>
public sealed class Dbu011ServerlessPerformanceMode : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU011",
        Title = "Scheduled serverless job in performance-optimized mode",
        DefaultSeverity = Severity.Info,
        WhenItFires = "A job with serverless tasks runs on a schedule or periodic trigger with `performance_target` absent (the default is `PERFORMANCE_OPTIMIZED`) or set to `PERFORMANCE_OPTIMIZED`. Both modes use the same SKU, but standard mode consumes fewer DBUs; it starts in 4 to 6 minutes, which scheduled batch work usually tolerates.",
        HowToFix = "Set `performance_target: STANDARD` on the job.",
        WhenToSuppress = "The job must start within seconds of its schedule, for example to meet a tight delivery deadline. Name the deadline.",
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        foreach (var (key, job) in (context.Current.Document.Resources?.Jobs ?? []).OrderBy(j => j.Key, StringComparer.Ordinal))
        {
            var target = job.PerformanceTarget?.ToUpperInvariant() ?? "PERFORMANCE_OPTIMIZED";
            if (target != "PERFORMANCE_OPTIMIZED" || (job.Schedule is null && job.Trigger?.Periodic is null))
            {
                continue;
            }

            var frequency = RunFrequency.ForJob(job, context.Reference);
            var serverless = Cost.CostEngine.Flatten(job.Tasks ?? []).Any(t =>
                t.JobClusterKey is null && t.NewCluster is null && t.ExistingClusterId is null && !t.UsesOtherCompute);
            if (!serverless || frequency.RunsPerMonth is not > 0)
            {
                continue;
            }

            var mode = job.PerformanceTarget is null ? "performance-optimized mode (the default)" : "`performance_target: PERFORMANCE_OPTIMIZED`";
            yield return context.Finding(ResourceTypes.Jobs, key, "performance_target",
                $"job `{key}` runs serverless tasks {frequency.Description} in {mode}. Standard mode uses the same SKU but consumes fewer DBUs, and starts in 4 to 6 minutes; for scheduled batch work, set `performance_target: STANDARD`.");
        }
    }
}

/// <summary>DBU012: a classic cluster runs a Databricks Runtime past or close to its end of support.</summary>
public sealed partial class Dbu012RuntimeEndOfSupport : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU012",
        Title = "Outdated or end-of-support runtime",
        DefaultSeverity = Severity.Warning,
        WhenItFires = "A job cluster, task cluster or all-purpose cluster sets a `spark_version` whose LTS release is past its end-of-support date in `catalog/runtimes.yaml` (a warning), or reaches it within `warn_days` (default 90; info). Runtimes older than every listed release are past end of support too. Older runtimes miss performance improvements and security fixes.",
        HowToFix = "Move `spark_version` to a current LTS release, such as `16.4.x-scala2.12`, after testing the job on it.",
        WhenToSuppress = "The workload depends on a library that does not run on newer runtimes, and a migration is planned. Give the plan and its date.",
        Parameters = [new RuleParameter("warn_days", 90, "Days before end of support at which the rule starts reporting (info).")],
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var table = context.Prices.Catalog.Runtimes;
        var resources = context.Current.Document.Resources;
        if (table.Oldest is null || resources is null)
        {
            yield break;
        }

        var warnDays = (int)context.Parameter("warn_days");
        var latest = table.EndOfSupport.Keys.Max()!;
        var clusters = new List<(string Type, string Key, string Field, string What, string? SparkVersion)>();
        foreach (var (key, job) in (resources.Jobs ?? []).OrderBy(j => j.Key, StringComparer.Ordinal))
        {
            foreach (var cluster in job.JobClusters ?? [])
            {
                clusters.Add((ResourceTypes.Jobs, key, $"job_clusters[{cluster.JobClusterKey}].new_cluster.spark_version", $"job `{key}`, job cluster `{cluster.JobClusterKey}`", cluster.NewCluster?.SparkVersion));
            }

            foreach (var task in Cost.CostEngine.Flatten(job.Tasks ?? []))
            {
                clusters.Add((ResourceTypes.Jobs, key, $"tasks[{task.TaskKey}].new_cluster.spark_version", $"job `{key}`, task `{task.TaskKey}`", task.NewCluster?.SparkVersion));
            }
        }

        foreach (var (key, cluster) in (resources.Clusters ?? []).OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            clusters.Add((ResourceTypes.Clusters, key, "spark_version", $"cluster `{key}`", cluster.SparkVersion));
        }

        foreach (var (type, key, field, what, sparkVersion) in clusters)
        {
            if (sparkVersion is null || MajorMinor().Match(sparkVersion) is not { Success: true } match)
            {
                continue;
            }

            var version = new Version(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            var upgrade = $"Move to a current LTS release, such as {latest} LTS, after testing.";
            if (version < table.Oldest)
            {
                yield return context.Finding(type, key, field,
                    $"{what} uses Databricks Runtime {version}, older than every supported LTS release: Databricks Runtime 10.4 LTS and earlier have reached end of life. {upgrade}",
                    Severity.Warning);
                continue;
            }

            if (!table.EndOfSupport.TryGetValue(version, out var entry))
            {
                continue;
            }

            context.Prices.Used(entry.Provenance);
            var days = entry.Date.DayNumber - context.Today.DayNumber;
            var date = entry.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (days < 0)
            {
                yield return context.Finding(type, key, field,
                    $"{what} uses Databricks Runtime {version} LTS, which reached end of support on {date}: it gets no more security fixes, and newer runtimes run the same work faster. {upgrade}",
                    Severity.Warning);
            }
            else if (days <= warnDays)
            {
                yield return context.Finding(type, key, field,
                    $"{what} uses Databricks Runtime {version} LTS, which reaches end of support on {date}, in {days} day{(days == 1 ? "" : "s")}. {upgrade}",
                    Severity.Info);
            }
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\A(\d+)\.(\d+)\.")]
    private static partial System.Text.RegularExpressions.Regex MajorMinor();
}

/// <summary>DBU013: an app runs on a compute size larger than Medium.</summary>
public sealed class Dbu013LargeApp : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU013",
        Title = "Large Databricks App",
        DefaultSeverity = Severity.Info,
        WhenItFires = "An app sets a compute size larger than MEDIUM (LARGE is 2×, XLARGE 6× its DBU per hour). An app bills for its size, per instance, for as long as it runs, whether or not anyone uses it.",
        HowToFix = "Use MEDIUM unless the app needs more CPU or memory under load; measure first. Stop apps nobody uses.",
        WhenToSuppress = "The app needs the larger size under its real load. Give the measurement.",
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var apps = context.Current.Document.Resources?.Apps;
        var sizes = context.Prices.Catalog.Serverless.AppSizes;
        if (apps is null || !sizes.TryGetValue("MEDIUM", out var medium))
        {
            yield break;
        }

        var engine = new Cost.CostEngine(context.Prices, context.Reference);
        foreach (var (key, app) in apps.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            var size = app.ComputeSize?.ToUpperInvariant();
            if (size is null || !sizes.TryGetValue(size, out var value) || value.Value <= medium.Value)
            {
                continue;
            }

            var monthly = engine.EstimateApp(app).Monthly;
            var cost = monthly is { } m ? $", about {Format.Money(m)} a month while it runs" : "";
            yield return context.Finding(ResourceTypes.Apps, key, "compute_size",
                $"app `{key}` runs on {size} compute: {Format.Number(value.Value)} DBU per hour per instance, {Format.Multiplier(value.Value / medium.Value)} MEDIUM{cost}.");
        }
    }
}

/// <summary>DBU014: a served entity keeps provisioned concurrency up while idle.</summary>
public sealed class Dbu014ServingScaleToZero : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU014",
        Title = "Serving endpoint without scale-to-zero",
        DefaultSeverity = Severity.Warning,
        WhenItFires = "A model serving endpoint's served entity does not set `scale_to_zero_enabled: true`, so it keeps its provisioned concurrency running when no requests come in. A warning in a target with `mode: development`, where endpoints sit idle most of the time; info elsewhere, naming the minimum concurrency kept running.",
        HowToFix = "Set `scale_to_zero_enabled: true`. Endpoints then scale to zero after 30 minutes without traffic; the first request afterwards waits for a cold start, usually 10 to 20 seconds. Keep it off for production endpoints that need consistent latency, and lower `min_provisioned_concurrency` or the `workload_size` instead.",
        WhenToSuppress = "The endpoint serves production traffic that needs guaranteed response times; Databricks does not recommend scale to zero there. Name the latency requirement.",
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var endpoints = context.Current.Document.Resources?.ModelServingEndpoints;
        if (endpoints is null)
        {
            yield break;
        }

        var development = string.Equals(context.Current.Document.Bundle?.Mode, "development", StringComparison.OrdinalIgnoreCase);
        foreach (var (key, endpoint) in endpoints.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var entities = (endpoint.Config?.ServedEntities ?? []).Select(e => ("served_entities", e))
                .Concat((endpoint.Config?.ServedModels ?? []).Select(e => ("served_models", e)));
            foreach (var (list, entity) in entities)
            {
                if (entity.ScaleToZeroEnabled == true)
                {
                    continue;
                }

                var name = entity.Name ?? entity.EntityName ?? entity.ModelName ?? "?";
                var floor = entity.MinProvisionedConcurrency ?? (entity.WorkloadSize?.ToUpperInvariant() switch
                {
                    "SMALL" => 4,
                    "MEDIUM" => 8,
                    "LARGE" => 16,
                    _ => (int?)null,
                });
                var floorText = floor is { } f ? $"a floor of {f} provisioned concurrency" : "its provisioned concurrency";
                var message = development
                    ? $"serving endpoint `{key}`, served entity `{name}` does not scale to zero in a development target: it keeps {floorText} running while idle. Set `scale_to_zero_enabled: true`."
                    : $"serving endpoint `{key}`, served entity `{name}` does not scale to zero: it keeps {floorText} running while idle. Fine for production traffic that needs consistent latency; otherwise set `scale_to_zero_enabled: true`.";
                yield return context.Finding("model_serving_endpoints", key, $"config.{list}[{name}].scale_to_zero_enabled", message,
                    development ? Severity.Warning : Severity.Info);
            }
        }
    }
}

/// <summary>DBU015: a Lakebase compute stays always active off production, or keeps a large minimum.</summary>
public sealed class Dbu015LakebaseSizing : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU015",
        Title = "Lakebase endpoint sizing",
        DefaultSeverity = Severity.Warning,
        WhenItFires = "A Lakebase compute disables scale to zero (`no_suspension: true`) on a branch that is not a production branch (a warning): development and preview branches sit idle most of the time. Or any compute keeps `autoscaling_limit_min_cu` at `max_min_cu` (default 8) or more (info): the minimum is billed for every active hour. Production branches are `production` and any branch in `production_branches`.",
        HowToFix = "Let non-production computes scale to zero (remove `no_suspension`, or set `suspend_timeout_duration`), and lower `autoscaling_limit_min_cu` to what the idle load needs: autoscaling adds capacity when it is busy.",
        WhenToSuppress = "The branch serves traffic that cannot wait for a cold start, or the minimum is sized to a measured baseline. Say which.",
        Parameters = [new RuleParameter("max_min_cu", 8, "Minimum CU from which a compute is reported (info).")],
        ListParameters = [new RuleListParameter("production_branches", "Branch IDs that count as production besides `production`.")],
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var endpoints = context.Current.Document.Resources?.PostgresEndpoints;
        if (endpoints is null)
        {
            yield break;
        }

        var production = new HashSet<string>(context.List("production_branches"), StringComparer.Ordinal) { "production" };
        var maxMinCu = context.Parameter("max_min_cu");
        var engine = new Cost.CostEngine(context.Prices, context.Reference);
        foreach (var (key, endpoint) in endpoints.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (endpoint.Disabled == true)
            {
                continue;
            }

            var branch = Branch(endpoint.Parent);
            var estimate = engine.EstimatePostgresEndpoint(endpoint);
            if (endpoint.NoSuspension == true && branch is not null && !production.Contains(branch))
            {
                var cost = estimate.Monthly is { } m ? $": about {Format.Money(m)} a month at {Format.Number(endpoint.MinCu ?? 0)} CU even when idle" : "";
                yield return context.Finding(ResourceTypes.PostgresEndpoints, key, "no_suspension",
                    $"postgres_endpoint `{key}` on branch `{branch}` never scales to zero (`no_suspension: true`){cost}. Let non-production branches scale to zero.",
                    Severity.Warning);
            }

            if (endpoint.MinCu is { } min && min >= maxMinCu)
            {
                var rate = estimate.RatePerHour is { } r ? $" at {Format.Money(r)} per active hour" : "";
                yield return context.Finding(ResourceTypes.PostgresEndpoints, key, "autoscaling_limit_min_cu",
                    $"postgres_endpoint `{key}` keeps at least {Format.Number(min)} CU (limit {Format.Number(maxMinCu)}){rate}: the minimum is billed for every active hour, and autoscaling adds capacity when it is busy.",
                    Severity.Info);
            }
        }
    }

    /// <summary>The branch ID from a parent such as <c>projects/shop/branches/dev</c>.</summary>
    private static string? Branch(string? parent)
    {
        if (parent is null)
        {
            return null;
        }

        var marker = parent.LastIndexOf("/branches/", StringComparison.Ordinal);
        return marker >= 0 ? parent[(marker + "/branches/".Length)..].Split('/')[0] : null;
    }
}

/// <summary>DBU016: a job on a classic job cluster runs on a short schedule, paying for a cluster start each run.</summary>
public sealed class Dbu016ShortSchedule : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU016",
        Title = "Classic job cluster on a short schedule",
        DefaultSeverity = Severity.Info,
        WhenItFires = "A job runs on a classic job cluster without an instance pool, and its schedule or periodic trigger fires more often than every `min_interval_minutes` (default 15). A job cluster is created for each run, so every run pays for cluster start-up.",
        HowToFix = "Run the job on serverless compute (Databricks recommends serverless over pools when the workload supports it), give the job cluster an `instance_pool_id` so it starts from idle instances, or make the job `continuous` if it really needs to run all the time.",
        WhenToSuppress = "The workload needs classic compute that pools cannot provide, and the start-up overhead has been accepted. Give the reason.",
        Parameters = [new RuleParameter("min_interval_minutes", 15, "Shortest acceptable interval, in minutes, between runs on a classic job cluster.")],
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var jobs = context.Current.Document.Resources?.Jobs;
        if (jobs is null)
        {
            yield break;
        }

        var minMinutes = context.Parameter("min_interval_minutes");
        foreach (var (key, job) in jobs.OrderBy(j => j.Key, StringComparer.Ordinal))
        {
            var frequency = RunFrequency.ForJob(job, context.Reference);
            if (frequency.ShortestInterval is not { } interval || (decimal)interval.TotalMinutes >= minMinutes || frequency.RunsPerMonth is not > 0)
            {
                continue;
            }

            if (UnpooledClassicCluster(job) is not { } cluster)
            {
                continue;
            }

            var field = job.Schedule is not null ? "schedule.quartz_cron_expression" : "trigger.periodic";
            yield return context.Finding(ResourceTypes.Jobs, key, field,
                $"job `{key}` runs {frequency.Description} on {cluster}, which has no instance pool: it starts a cluster about {Format.Number(frequency.RunsPerMonth.Value)} times a month, and every run pays for start-up. Consider serverless, an `instance_pool_id`, or a continuous job.");
        }
    }

    /// <summary>The first classic job cluster a task uses that is not backed by an instance pool.</summary>
    private static string? UnpooledClassicCluster(Model.Job job)
    {
        var jobClusters = (job.JobClusters ?? []).Where(c => c.JobClusterKey is not null)
            .GroupBy(c => c.JobClusterKey!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().NewCluster, StringComparer.Ordinal);
        foreach (var task in Cost.CostEngine.Flatten(job.Tasks ?? []))
        {
            if (task.JobClusterKey is { } clusterKey && jobClusters.GetValueOrDefault(clusterKey) is { InstancePoolId: null })
            {
                return $"job cluster `{clusterKey}`";
            }

            if (task.NewCluster is { InstancePoolId: null })
            {
                return $"the new cluster of task `{task.TaskKey}`";
            }
        }

        return null;
    }
}

/// <summary>DBU017: a job or pipeline cluster runs on spot capacity with no on-demand fallback.</summary>
public sealed class Dbu017SpotWithoutFallback : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU017",
        Title = "Spot without fallback",
        DefaultSeverity = Severity.Info,
        WhenItFires = "A classic job or pipeline cluster sets its availability to spot without fallback: `SPOT` (AWS), `SPOT_AZURE` or `PREEMPTIBLE_GCP`. When spot capacity is evicted or unavailable, the run can fail, and the retry pays for the work again.",
        HowToFix = "Use the fallback variant (`SPOT_WITH_FALLBACK`, `SPOT_WITH_FALLBACK_AZURE` or `PREEMPTIBLE_WITH_FALLBACK_GCP`) and set `first_on_demand` to at least 1, so the driver stays on demand.",
        WhenToSuppress = "The job is cheap to rerun and tolerates failures, and the lowest price matters more than finishing on time. Say so.",
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        foreach (var (type, key, path, what, spec) in ClassicClusters.Of(context.Current.Document.Resources))
        {
            var (attributes, block) = spec.AwsAttributes?.Availability is not null ? ("aws_attributes", spec.AwsAttributes)
                : spec.AzureAttributes?.Availability is not null ? ("azure_attributes", spec.AzureAttributes)
                : ("gcp_attributes", spec.GcpAttributes);
            if (block?.Availability?.ToUpperInvariant() is not ("SPOT" or "SPOT_AZURE" or "PREEMPTIBLE_GCP") || block.Availability is not { } availability)
            {
                continue;
            }

            // AWS defaults first_on_demand to 0, Azure and GCP to 1.
            var firstOnDemand = block.FirstOnDemand ?? (attributes == "aws_attributes" ? 0 : 1);
            var driver = firstOnDemand < 1 ? " The driver runs on spot too (`first_on_demand` is 0), so one eviction ends the run." : "";
            yield return context.Finding(type, key, $"{path}.{attributes}.availability",
                $"{what} runs on spot capacity without fallback (`{availability}`): an eviction or a shortage can fail the run, and the retry pays again.{driver} Use `{ClassicClusters.Fallback(availability)}` with `first_on_demand: 1`.");
        }
    }
}

/// <summary>DBU018: a classic pipeline cluster uses legacy autoscaling.</summary>
public sealed class Dbu018LegacyAutoscaling : IRule
{
    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU018",
        Title = "Pipeline cluster on legacy autoscaling",
        DefaultSeverity = Severity.Info,
        WhenItFires = "A classic pipeline cluster sets `autoscale.mode: LEGACY`. Enhanced autoscaling adds and removes workers as the workload changes and is the default for new pipelines; legacy autoscaling uses cluster autoscaling, which is slower to shrink. Maintenance clusters, which always use legacy autoscaling, are skipped.",
        HowToFix = "Set `autoscale.mode: ENHANCED`, or remove `mode` to take the default.",
        WhenToSuppress = "The pipeline has been measured to run cheaper with legacy autoscaling. Give the measurement.",
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        foreach (var (key, pipeline) in (context.Current.Document.Resources?.Pipelines ?? []).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (pipeline.Serverless == true)
            {
                continue;
            }

            foreach (var cluster in pipeline.Clusters ?? [])
            {
                var label = cluster.Label ?? "default";
                if (label == "maintenance" || !string.Equals(cluster.Autoscale?.Mode, "LEGACY", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return context.Finding(ResourceTypes.Pipelines, key, $"clusters[{label}].autoscale.mode",
                    $"pipeline `{key}`, cluster `{label}` uses legacy autoscaling (`autoscale.mode: LEGACY`): enhanced autoscaling adds and removes workers as the workload changes, and it is the default for new pipelines. Set `mode: ENHANCED`.");
            }
        }
    }
}

/// <summary>DBU019: a custom tag uses the key of a Databricks default tag, so billing renames it.</summary>
public sealed class Dbu019DefaultTagCollision : IRule
{
    /// <summary>Default tag keys on clusters, jobs compute, SQL warehouses and pools.</summary>
    public static readonly IReadOnlyList<string> DefaultKeys =
    [
        "Vendor", "ClusterId", "ClusterName", "Creator", "RunName", "JobId", "SqlEndpointId",
        "DatabricksInstancePoolId", "DatabricksInstancePoolCreatorId",
    ];

    public RuleMetadata Metadata { get; } = new()
    {
        Id = "DBU019",
        Title = "Custom tag collides with a default tag",
        DefaultSeverity = Severity.Info,
        WhenItFires = "A job, pipeline, cluster or SQL warehouse sets a custom tag whose key matches a Databricks default tag key, ignoring case: `Vendor`, `ClusterId`, `ClusterName`, `Creator`, `RunName`, `JobId`, `SqlEndpointId`, `DatabricksInstancePoolId` or `DatabricksInstancePoolCreatorId`. Cloud billing then shows the custom tag as `x_<key>`, so cost reports grouped by that key miss it.",
        HowToFix = "Rename the tag to a key of your own, such as `team_vendor` instead of `vendor`, and update the cost reports that use it.",
        WhenToSuppress = "Reports already read the `x_`-prefixed key. Say which report.",
    };

    public IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var resources = context.Current.Document.Resources;
        if (resources is null)
        {
            yield break;
        }

        var tags = new List<(string Type, string Key, string Field, string Tag)>();
        foreach (var (key, job) in (resources.Jobs ?? []).OrderBy(j => j.Key, StringComparer.Ordinal))
        {
            tags.AddRange(Keys(job.Tags).Select(t => (ResourceTypes.Jobs, key, $"tags.{t}", t)));
            foreach (var cluster in job.JobClusters ?? [])
            {
                tags.AddRange(Keys(cluster.NewCluster?.CustomTags).Select(t => (ResourceTypes.Jobs, key, $"job_clusters[{cluster.JobClusterKey}].new_cluster.custom_tags.{t}", t)));
            }

            foreach (var task in Cost.CostEngine.Flatten(job.Tasks ?? []))
            {
                tags.AddRange(Keys(task.NewCluster?.CustomTags).Select(t => (ResourceTypes.Jobs, key, $"tasks[{task.TaskKey}].new_cluster.custom_tags.{t}", t)));
            }
        }

        foreach (var (key, pipeline) in (resources.Pipelines ?? []).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            tags.AddRange(Keys(pipeline.Tags).Select(t => (ResourceTypes.Pipelines, key, $"tags.{t}", t)));
            foreach (var cluster in pipeline.Clusters ?? [])
            {
                tags.AddRange(Keys(cluster.CustomTags).Select(t => (ResourceTypes.Pipelines, key, $"clusters[{cluster.Label ?? "default"}].custom_tags.{t}", t)));
            }
        }

        foreach (var (key, cluster) in (resources.Clusters ?? []).OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            tags.AddRange(Keys(cluster.CustomTags).Select(t => (ResourceTypes.Clusters, key, $"custom_tags.{t}", t)));
        }

        foreach (var (key, warehouse) in (resources.SqlWarehouses ?? []).OrderBy(w => w.Key, StringComparer.Ordinal))
        {
            tags.AddRange((warehouse.Tags?.CustomTags ?? []).Select(t => t.Key).OfType<string>().Select(t => (ResourceTypes.SqlWarehouses, key, $"tags.custom_tags.{t}", t)));
        }

        foreach (var (type, key, field, tag) in tags)
        {
            if (DefaultKeys.FirstOrDefault(d => string.Equals(d, tag, StringComparison.OrdinalIgnoreCase)) is not { } builtIn)
            {
                continue;
            }

            yield return context.Finding(type, key, field,
                $"{ResourceTypes.Singular(type)} `{key}` sets custom tag `{tag}`, which collides with the Databricks default tag `{builtIn}`: cloud billing shows it as `x_{tag}`, so cost reports grouped by `{tag}` miss it.");
        }
    }

    private static IEnumerable<string> Keys<T>(Dictionary<string, T>? tags) => tags?.Keys.Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>();
}
