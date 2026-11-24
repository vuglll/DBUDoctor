# DBUDoctor rules

Generated for DBUDoctor 0.4.0 from rule metadata. Do not edit by hand.

Change a rule's severity or parameters, or turn it off, under `rules` in `.dbudoctor.yml`, for example `rules: { DBU010: { severity: error, factor: 4 } }`. Suppress one finding with a `suppressions` entry: a `rule`, a `resource` selector (`<type>.<key>`, wildcards allowed) and a `reason`, which is required.

## DBU001: Task runs on all-purpose compute

Default severity: warning. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU001.md

**When it fires.** A job task sets `existing_cluster_id`, so it runs on an all-purpose cluster, which is billed at the all-purpose DBU rate instead of the cheaper Jobs compute rate.

**How to fix.** Give the task a job cluster: define it under `job_clusters` and point the task at it with `job_cluster_key`, or use `new_cluster`, or remove the cluster settings to run the task on serverless compute (`environment_key` for Python tasks).

**When to suppress.** The task needs state that lives on the shared cluster (for example, cached tables or libraries installed by hand), or it runs rarely enough that a job cluster's startup time costs more than the price difference. Give the reason and when it will change.

## DBU002: All-purpose cluster auto-termination

Default severity: warning. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU002.md

**When it fires.** A cluster sets `autotermination_minutes: 0` (a warning), so it bills for every idle hour until someone stops it; or it omits the field or waits longer than `max_minutes` (default 60) before terminating (info). Without the field, the Clusters API never auto-terminates the cluster, and the effective default depends on the deployment engine.

**How to fix.** Set `autotermination_minutes` to the shortest idle time your users accept, such as 30 or 60. The allowed range is 10 to 10,000 minutes.

**When to suppress.** The cluster must stay up between infrequent requests and its idle cost has been approved, for example a cluster that serves a BI tool with a strict start-up time. Give the reason and the review date.

**Parameters.**

- `max_minutes` (default 60): Longest idle time, in minutes, before a cluster should terminate.

## DBU003: SQL warehouse auto-stop

Default severity: warning. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU003.md

**When it fires.** A SQL warehouse sets `auto_stop_mins: 0` (a warning), so it bills for every idle hour until someone stops it; or its auto-stop is longer than `max_minutes` (default 30) (info). Without the field, the SQL Warehouses API default applies: 120 minutes.

**How to fix.** Set `auto_stop_mins` to the shortest idle time your users accept: 10 minutes is the UI default for serverless warehouses, and the API accepts 0 or at least 10 (serverless from 1).

**When to suppress.** Dashboards or users need the warehouse warm between infrequent queries and the idle cost has been approved. Give the reason and the review date.

**Parameters.**

- `max_minutes` (default 30): Longest idle time, in minutes, before a warehouse should stop.

## DBU004: Continuous pipeline

Default severity: warning. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU004.md

**When it fires.** A pipeline sets `continuous: true`, so its compute runs around the clock (about 730 hours a month) instead of only while an update runs.

**How to fix.** Set `continuous: false` and trigger the pipeline on a schedule (a job with a `pipeline_task`) that matches how fresh the data must be. For low-latency needs, consider serverless or a shorter schedule before running continuously.

**When to suppress.** The data must stay fresh within minutes, and the business has accepted the always-on cost. Name the latency requirement in the reason.

## DBU005: Continuous job

Default severity: warning. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU005.md

**When it fires.** A job has a `continuous` block that is not paused (`pause_status` defaults to `UNPAUSED`). A continuous job always has one run executing, and a new run starts within a minute of the last, so its compute runs around the clock: about 730 hours a month.

**How to fix.** Replace `continuous` with a `schedule` or a file-arrival or table-update `trigger` that matches how fresh the data must be. For streaming, check whether a triggered run (`Trigger.AvailableNow`) on a schedule is enough.

**When to suppress.** The workload must process data within minutes around the clock, and the always-on cost has been approved. Name the latency requirement in the reason.

## DBU007: Job without a timeout

Default severity: info. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU007.md

**When it fires.** A job has no `timeout_seconds` (absent or 0, which means no timeout), and at least one of its tasks has none either. A stuck run keeps its compute busy until someone cancels it. Continuous jobs are skipped.

**How to fix.** Set `timeout_seconds` on the job (it applies to each run), or on every task, to a few times the normal run duration. `health` rules on `RUN_DURATION_SECONDS` only notify; they do not stop the run.

**When to suppress.** Runs have no predictable upper bound and are watched by other means, such as an on-call alert on run duration. Say what watches them.

## DBU008: Missing cost-attribution tags

Default severity: warning. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU008.md

**When it fires.** `required_tags` lists tag keys (the rule is off while it is empty), and a job, pipeline, cluster or SQL warehouse lacks one of them where it reaches billing: job `tags` and job cluster `custom_tags`; pipeline `tags` and cluster `custom_tags`; cluster `custom_tags`; warehouse `tags.custom_tags`. A serverless job or pipeline with a `usage_policy_id` (or the older `budget_policy_id`) counts as attributed.

**How to fix.** Add the missing keys with the owner's values, for example `tags: { team: data-platform, cost_center: "4711" }` on a job or `custom_tags` on its job clusters. For serverless jobs and pipelines, attach a usage policy that sets the tags.

**When to suppress.** The resource is shared infrastructure whose cost is split by another mechanism. Say which.

**Parameters.**

- `required_tags` (a list, empty by default): Tag keys every priced resource must carry, such as `[team, cost_center]`. Empty turns the rule off.

## DBU009: Large on-demand cluster

Default severity: info. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU009.md

**When it fires.** A classic job or pipeline cluster has at least `min_workers` workers (default 8; the maximum, for autoscale) and sets its availability explicitly to on-demand (`ON_DEMAND`, `ON_DEMAND_AZURE` or `ON_DEMAND_GCP`). Batch workers tolerate spot capacity well, and spot VMs cost a fraction of on-demand. Where the catalog has spot prices (Azure), the message gives the saving.

**How to fix.** Use spot with fallback (`SPOT_WITH_FALLBACK`, `SPOT_WITH_FALLBACK_AZURE` or `PREEMPTIBLE_WITH_FALLBACK_GCP`) and keep the driver on demand with `first_on_demand: 1`.

**When to suppress.** The workload cannot tolerate losing workers mid-run, for example long stateful jobs without checkpoints. Say why.

**Parameters.**

- `min_workers` (default 8): Worker count from which an on-demand cluster is reported.

## DBU010: Sharp frequency increase

Default severity: warning. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU010.md

**When it fires.** A job runs at least `factor` times (default 4) as often as in the base branch, and at least `min_runs` (default 100) more times a month. Each run pays for compute, and on classic compute also for cluster startup.

**How to fix.** Check that the new schedule matches how fresh the data must be. Keep the old cadence if it is enough, or move frequent runs to compute that starts quickly (serverless or an instance pool).

**When to suppress.** The higher frequency is a deliberate requirement and its cost has been approved. Name the requirement in the reason.

**Parameters.**

- `factor` (default 4): Minimum ratio of head runs to base runs.
- `min_runs` (default 100): Minimum increase in runs per month.

## DBU011: Scheduled serverless job in performance-optimized mode

Default severity: info. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU011.md

**When it fires.** A job with serverless tasks runs on a schedule or periodic trigger with `performance_target` absent (the default is `PERFORMANCE_OPTIMIZED`) or set to `PERFORMANCE_OPTIMIZED`. Both modes use the same SKU, but standard mode consumes fewer DBUs; it starts in 4 to 6 minutes, which scheduled batch work usually tolerates.

**How to fix.** Set `performance_target: STANDARD` on the job.

**When to suppress.** The job must start within seconds of its schedule, for example to meet a tight delivery deadline. Name the deadline.

## DBU012: Outdated or end-of-support runtime

Default severity: warning. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU012.md

**When it fires.** A job cluster, task cluster or all-purpose cluster sets a `spark_version` whose LTS release is past its end-of-support date in `catalog/runtimes.yaml` (a warning), or reaches it within `warn_days` (default 90; info). Runtimes older than every listed release are past end of support too. Older runtimes miss performance improvements and security fixes.

**How to fix.** Move `spark_version` to a current LTS release, such as `16.4.x-scala2.12`, after testing the job on it.

**When to suppress.** The workload depends on a library that does not run on newer runtimes, and a migration is planned. Give the plan and its date.

**Parameters.**

- `warn_days` (default 90): Days before end of support at which the rule starts reporting (info).

## DBU013: Large Databricks App

Default severity: info. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU013.md

**When it fires.** An app sets a compute size larger than MEDIUM (LARGE is 2×, XLARGE 6× its DBU per hour). An app bills for its size, per instance, for as long as it runs, whether or not anyone uses it.

**How to fix.** Use MEDIUM unless the app needs more CPU or memory under load; measure first. Stop apps nobody uses.

**When to suppress.** The app needs the larger size under its real load. Give the measurement.

## DBU014: Serving endpoint without scale-to-zero

Default severity: warning. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU014.md

**When it fires.** A model serving endpoint's served entity does not set `scale_to_zero_enabled: true`, so it keeps its provisioned concurrency running when no requests come in. A warning in a target with `mode: development`, where endpoints sit idle most of the time; info elsewhere, naming the minimum concurrency kept running.

**How to fix.** Set `scale_to_zero_enabled: true`. Endpoints then scale to zero after 30 minutes without traffic; the first request afterwards waits for a cold start, usually 10 to 20 seconds. Keep it off for production endpoints that need consistent latency, and lower `min_provisioned_concurrency` or the `workload_size` instead.

**When to suppress.** The endpoint serves production traffic that needs guaranteed response times; Databricks does not recommend scale to zero there. Name the latency requirement.

## DBU015: Lakebase endpoint sizing

Default severity: warning. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU015.md

**When it fires.** A Lakebase compute disables scale to zero (`no_suspension: true`) on a branch that is not a production branch (a warning): development and preview branches sit idle most of the time. Or any compute keeps `autoscaling_limit_min_cu` at `max_min_cu` (default 8) or more (info): the minimum is billed for every active hour. Production branches are `production` and any branch in `production_branches`.

**How to fix.** Let non-production computes scale to zero (remove `no_suspension`, or set `suspend_timeout_duration`), and lower `autoscaling_limit_min_cu` to what the idle load needs: autoscaling adds capacity when it is busy.

**When to suppress.** The branch serves traffic that cannot wait for a cold start, or the minimum is sized to a measured baseline. Say which.

**Parameters.**

- `max_min_cu` (default 8): Minimum CU from which a compute is reported (info).
- `production_branches` (a list, empty by default): Branch IDs that count as production besides `production`.

## DBU016: Classic job cluster on a short schedule

Default severity: info. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU016.md

**When it fires.** A job runs on a classic job cluster without an instance pool, and its schedule or periodic trigger fires more often than every `min_interval_minutes` (default 15). A job cluster is created for each run, so every run pays for cluster start-up.

**How to fix.** Run the job on serverless compute (Databricks recommends serverless over pools when the workload supports it), give the job cluster an `instance_pool_id` so it starts from idle instances, or make the job `continuous` if it really needs to run all the time.

**When to suppress.** The workload needs classic compute that pools cannot provide, and the start-up overhead has been accepted. Give the reason.

**Parameters.**

- `min_interval_minutes` (default 15): Shortest acceptable interval, in minutes, between runs on a classic job cluster.

## DBU017: Spot without fallback

Default severity: info. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU017.md

**When it fires.** A classic job or pipeline cluster sets its availability to spot without fallback: `SPOT` (AWS), `SPOT_AZURE` or `PREEMPTIBLE_GCP`. When spot capacity is evicted or unavailable, the run can fail, and the retry pays for the work again.

**How to fix.** Use the fallback variant (`SPOT_WITH_FALLBACK`, `SPOT_WITH_FALLBACK_AZURE` or `PREEMPTIBLE_WITH_FALLBACK_GCP`) and set `first_on_demand` to at least 1, so the driver stays on demand.

**When to suppress.** The job is cheap to rerun and tolerates failures, and the lowest price matters more than finishing on time. Say so.

## DBU018: Pipeline cluster on legacy autoscaling

Default severity: info. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU018.md

**When it fires.** A classic pipeline cluster sets `autoscale.mode: LEGACY`. Enhanced autoscaling adds and removes workers as the workload changes and is the default for new pipelines; legacy autoscaling uses cluster autoscaling, which is slower to shrink. Maintenance clusters, which always use legacy autoscaling, are skipped.

**How to fix.** Set `autoscale.mode: ENHANCED`, or remove `mode` to take the default.

**When to suppress.** The pipeline has been measured to run cheaper with legacy autoscaling. Give the measurement.

## DBU019: Custom tag collides with a default tag

Default severity: info. Docs: https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU019.md

**When it fires.** A job, pipeline, cluster or SQL warehouse sets a custom tag whose key matches a Databricks default tag key, ignoring case: `Vendor`, `ClusterId`, `ClusterName`, `Creator`, `RunName`, `JobId`, `SqlEndpointId`, `DatabricksInstancePoolId` or `DatabricksInstancePoolCreatorId`. Cloud billing then shows the custom tag as `x_<key>`, so cost reports grouped by that key miss it.

**How to fix.** Rename the tag to a key of your own, such as `team_vendor` instead of `vendor`, and update the cost reports that use it.

**When to suppress.** Reports already read the `x_`-prefixed key. Say which report.
