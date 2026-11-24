# guardrail-rules Specification

## Purpose

The rule engine and its rules: cost guardrails that report new problems a pull request introduces, with severities, suppressions and documentation for each rule.

## Requirements

### Requirement: Rule engine
The system SHALL evaluate guardrail rules against the head configuration of each bundle. Every finding SHALL have a rule ID, a severity (`info`, `warning` or `error`), a resource, a field path, a message and a documentation link. Configuration SHALL be able to enable or disable each rule and change its severity and parameters. Adding a rule SHALL NOT require changes to the engine, the diff or the reporters.

#### Scenario: Severity override
- **WHEN** `.dbudoctor.yml` sets `rules.DBU010.severity: error`
- **THEN** DBU010 findings are reported as errors

#### Scenario: Rule disabled
- **WHEN** `.dbudoctor.yml` sets `rules.DBU001.enabled: false`
- **THEN** DBU001 is not evaluated

### Requirement: Report only what the pull request introduces
The system SHALL identify a finding by rule ID, bundle, resource type, resource key and field path. It SHALL suppress findings whose identity also exists in the base configuration at the same or a higher severity, and SHALL report them when `--report-existing` is set. A finding whose severity is higher than the base finding with the same identity SHALL count as new.

#### Scenario: Pre-existing problem
- **WHEN** a continuous pipeline exists, unchanged, in both base and head
- **THEN** DBU004 is not reported for it

#### Scenario: Problem introduced
- **WHEN** the head changes a pipeline to `continuous: true`
- **THEN** DBU004 is reported for it

#### Scenario: Report existing on request
- **WHEN** the same unchanged continuous pipeline exists and `--report-existing` is set
- **THEN** DBU004 is reported for it, marked as existing

#### Scenario: Severity rises
- **WHEN** a cluster's `autotermination_minutes` changes from 120 (DBU002 info) to 0 (DBU002 warning)
- **THEN** DBU002 is reported as a new warning

### Requirement: Suppressions require a reason
The system SHALL accept suppressions in configuration as a rule ID, a resource selector (`<type>.<key>`, with wildcards allowed) and a non-empty `reason`. It SHALL reject a suppression without a reason with exit code 2.

#### Scenario: Valid suppression
- **WHEN** configuration suppresses DBU001 for `jobs.legacy_*` with the reason "Moving off the shared cluster in Q1"
- **THEN** DBU001 findings for matching jobs are not reported, and the report counts them as suppressed

#### Scenario: Missing reason
- **WHEN** a suppression has no `reason`
- **THEN** DBUDoctor exits with code 2 and names the suppression

### Requirement: DBU001 task runs on all-purpose compute
The system SHALL report a warning for each job task that sets `existing_cluster_id`. The message SHALL state the Jobs compute DBU rate as a percentage of the all-purpose rate, computed from the catalog for the bundle's cloud.

#### Scenario: Task on an existing cluster on Azure
- **WHEN** a job task on Azure sets `existing_cluster_id`, and the catalog lists Jobs compute at 0.30 and all-purpose at 0.55 per DBU
- **THEN** DBU001 is reported for that task, saying that Jobs compute costs about 55% of the all-purpose rate

#### Scenario: Same task on AWS
- **WHEN** the catalog lists Jobs compute at 0.15 and all-purpose at 0.55 per DBU
- **THEN** the message says about 27%

#### Scenario: Task on a job cluster or serverless
- **WHEN** a task uses `job_cluster_key`, `new_cluster` or `environment_key`
- **THEN** DBU001 is not reported

### Requirement: DBU004 continuous pipeline
The system SHALL report a warning for a pipeline with `continuous: true`, stating that it runs around the clock.

#### Scenario: Pipeline made continuous
- **WHEN** a pipeline changes from `continuous: false` to `continuous: true`
- **THEN** DBU004 is reported

#### Scenario: Triggered pipeline
- **WHEN** a pipeline omits `continuous` or sets it to `false`
- **THEN** DBU004 is not reported

### Requirement: DBU010 sharp frequency increase
The system SHALL report a warning when a job's runs per month in head are at least `rules.DBU010.factor` (default 4) times its runs per month in base, and the increase is at least `rules.DBU010.min_runs` (default 100) runs per month.

#### Scenario: Hourly to every 15 minutes
- **WHEN** a job's runs per month change from 730 to 2,920
- **THEN** DBU010 is reported with both cron expressions

#### Scenario: Small absolute change
- **WHEN** a job's runs per month change from 4 to 30
- **THEN** DBU010 is not reported

### Requirement: DBU002 all-purpose cluster auto-termination
The system SHALL report DBU002 for a `clusters` resource as a warning when it sets `autotermination_minutes: 0`, and as info when it omits `autotermination_minutes` or sets it above `rules.DBU002.max_minutes` (default 60). The message SHALL give the cluster's rate per hour when it is known, and for a disabled timeout the rate over a 730-hour month.

#### Scenario: Auto-termination disabled
- **WHEN** an Azure cluster with a driver and 4 workers of `Standard_DS3_v2` (3.75 DBU per hour at 0.55 per DBU, VMs 1.145 per hour) sets `autotermination_minutes: 0`
- **THEN** DBU002 is reported as a warning, and the message says an idle cluster bills $3.21 per hour (3.75 × 0.55 + 1.145 = 3.2075), up to $2,341 a month (3.2075 × 730)

#### Scenario: Long idle window
- **WHEN** the same cluster sets `autotermination_minutes: 240`
- **THEN** DBU002 is reported as info, and the message says each idle stretch bills up to $12.83 (4 hours × 3.2075)

#### Scenario: Value not set
- **WHEN** a cluster omits `autotermination_minutes`
- **THEN** DBU002 is reported as info, and the message says that the effective default depends on the deployment engine, and that the Clusters API does not auto-terminate a cluster without the field

#### Scenario: Short idle window
- **WHEN** a cluster sets `autotermination_minutes: 30`
- **THEN** DBU002 is not reported

#### Scenario: Threshold is configurable
- **WHEN** `rules.DBU002.max_minutes` is 240 and a cluster sets `autotermination_minutes: 240`
- **THEN** DBU002 is not reported

### Requirement: DBU003 SQL warehouse auto-stop
The system SHALL report DBU003 as a warning for a SQL warehouse with `auto_stop_mins: 0`, and as info when its effective auto-stop exceeds `rules.DBU003.max_minutes` (default 30). When the field is absent, the effective value SHALL be the catalog default, and when the catalog has none the warehouse SHALL NOT be reported. The message SHALL give the warehouse's rate per hour when it is known.

#### Scenario: Auto-stop disabled
- **WHEN** an Azure serverless `Small` warehouse (12 DBU per hour at 0.70 per DBU) sets `auto_stop_mins: 0`
- **THEN** DBU003 is reported as a warning, and the message says an idle warehouse bills $8.40 per hour (12 × 0.70), up to $6,132 a month (8.40 × 730)

#### Scenario: Default auto-stop
- **WHEN** the same warehouse omits `auto_stop_mins` and the catalog default is 120 minutes
- **THEN** DBU003 is reported as info, the message states the 120-minute default it used, and says each idle stretch bills up to $16.80 (2 hours × 8.40)

#### Scenario: Explicit long auto-stop
- **WHEN** a warehouse sets `auto_stop_mins: 45`
- **THEN** DBU003 is reported as info

#### Scenario: Short auto-stop
- **WHEN** a warehouse sets `auto_stop_mins: 10`
- **THEN** DBU003 is not reported

#### Scenario: No catalog default
- **WHEN** a warehouse omits `auto_stop_mins` and the catalog has no `defaults.yaml`
- **THEN** DBU003 is not reported

### Requirement: DBU005 continuous job
The system SHALL report a warning for a job with a `continuous` block whose `pause_status` is not `PAUSED`; an omitted `pause_status` counts as unpaused. When the job's rate per hour is known, the message SHALL give it and the cost over a 730-hour month.

#### Scenario: Continuous job added
- **WHEN** the head adds an Azure job on a job cluster with a driver and 4 workers of `Standard_DS3_v2` (3.75 DBU per hour at 0.30 per DBU, VMs 1.145 per hour) and `continuous: { pause_status: UNPAUSED }`
- **THEN** DBU005 is reported as a warning, and the message says it runs around the clock at $2.27 per hour (3.75 × 0.30 + 1.145 = 2.27), about $1,657 a month (2.27 × 730)

#### Scenario: Pause status omitted
- **WHEN** a job has `continuous: {}`
- **THEN** DBU005 is reported

#### Scenario: Paused continuous job
- **WHEN** a job has `continuous: { pause_status: PAUSED }`
- **THEN** DBU005 is not reported

#### Scenario: Serverless continuous job
- **WHEN** a continuous job runs on serverless compute
- **THEN** DBU005 is reported, and the message says it runs around the clock (~730 h / month) without a price

### Requirement: DBU016 classic job cluster on a short schedule
The system SHALL report an info finding for a job that runs on a classic job cluster without `instance_pool_id` and whose schedule or periodic trigger has a shortest interval between runs under `rules.DBU016.min_interval_minutes` (default 15). The message SHALL say that every run pays for cluster start-up, give the runs per month, and suggest serverless, a pool or a continuous job.

#### Scenario: Every five minutes on classic compute
- **WHEN** a job on a classic job cluster uses `0 0/5 * * * ?` in UTC
- **THEN** DBU016 is reported as info, and the message says the job starts a cluster about 8,760 times a month (12 runs an hour × 730 hours)

#### Scenario: Serverless or pooled
- **WHEN** the same schedule runs on serverless compute, or on a job cluster with `instance_pool_id`
- **THEN** DBU016 is not reported

#### Scenario: Periodic trigger
- **WHEN** `rules.DBU016.min_interval_minutes` is 120 and a job on a classic job cluster has a periodic trigger every hour (periodic triggers count in hours, days or weeks)
- **THEN** DBU016 is reported

#### Scenario: Interval at the threshold
- **WHEN** a job on a classic job cluster runs every 15 minutes
- **THEN** DBU016 is not reported

#### Scenario: Paused schedule
- **WHEN** the short schedule is paused
- **THEN** DBU016 is not reported

### Requirement: DBU007 job without a timeout
The system SHALL report an info finding for a job that has no `timeout_seconds` (absent or 0) on the job itself and on at least one of its tasks, except for continuous jobs. When the job's rate per hour is known, the message SHALL give it as the cost of each hour a stuck run keeps going.

#### Scenario: No timeouts
- **WHEN** neither the job nor any task sets `timeout_seconds`, and the job runs on an Azure job cluster with a driver and 4 workers of `Standard_DS3_v2` (2.27 per hour)
- **THEN** DBU007 is reported as info, and the message says a stuck run keeps billing $2.27 per hour

#### Scenario: Task timeouts present
- **WHEN** every task sets `timeout_seconds`
- **THEN** DBU007 is not reported

#### Scenario: Job timeout present
- **WHEN** the job sets `timeout_seconds: 3600` and its tasks set none
- **THEN** DBU007 is not reported

#### Scenario: Zero means none
- **WHEN** the job sets `timeout_seconds: 0` and one task sets none
- **THEN** DBU007 is reported

#### Scenario: Continuous job
- **WHEN** a continuous job sets no timeout
- **THEN** DBU007 is not reported

### Requirement: DBU008 missing cost-attribution tags
When `rules.DBU008.required_tags` is non-empty, the system SHALL report a warning for each priced resource that lacks a required tag key in its tag locations: job `tags` and job cluster or task `custom_tags`; pipeline `tags` and cluster `custom_tags`; cluster `custom_tags`; SQL warehouse `tags.custom_tags`. It SHALL accept a `usage_policy_id` or `budget_policy_id` as attribution for serverless jobs and pipelines.

#### Scenario: Missing tag
- **WHEN** `required_tags` is `[team, cost_center]` and a job has only `team`
- **THEN** DBU008 is reported, naming `cost_center`

#### Scenario: Tag on the job cluster
- **WHEN** the job has `team` in `tags` and `cost_center` in its job cluster's `custom_tags`
- **THEN** DBU008 is not reported

#### Scenario: Serverless job with a usage policy
- **WHEN** a serverless job without tags sets `usage_policy_id`
- **THEN** DBU008 is not reported

#### Scenario: Rule off by default
- **WHEN** `required_tags` is empty or absent
- **THEN** DBU008 is not evaluated

### Requirement: DBU012 outdated or end-of-support runtime
The system SHALL report a warning for a job cluster, task cluster or all-purpose cluster whose `spark_version` LTS release has passed its end-of-support date in the catalog, or is older than every release in it. It SHALL report an info finding when that date is within `rules.DBU012.warn_days` (default 90). Dates SHALL be compared against an injectable reference date, which the CLI sets to the current UTC date.

#### Scenario: Runtime past end of support
- **WHEN** the reference date is 2026-11-08 and a job cluster uses `13.3.x-scala2.12` (end of support 2026-08-22)
- **THEN** DBU012 is reported as a warning

#### Scenario: Runtime close to end of support
- **WHEN** the reference date is 2026-11-08 and a job cluster uses `14.3.x-scala2.12` (end of support 2027-02-01, 85 days away)
- **THEN** DBU012 is reported as info

#### Scenario: Supported runtime
- **WHEN** a job cluster uses `16.4.x-scala2.12` (end of support 2028-05-09)
- **THEN** DBU012 is not reported

#### Scenario: Older than every listed release
- **WHEN** a job cluster uses `10.4.x-scala2.12`
- **THEN** DBU012 is reported as a warning

#### Scenario: Unlisted runtime
- **WHEN** a job cluster uses a non-LTS release such as `15.0.x-scala2.12`
- **THEN** DBU012 is not reported

### Requirement: DBU019 custom tag collides with a default tag
The system SHALL report an info finding for a custom tag whose key matches, case-insensitively, a Databricks default tag key: `Vendor`, `ClusterId`, `ClusterName`, `Creator`, `RunName`, `JobId`, `SqlEndpointId`, `DatabricksInstancePoolId` or `DatabricksInstancePoolCreatorId`. It SHALL check every tag location DBU008 checks. The message SHALL say that cloud billing renames the custom tag with an `x_` prefix.

#### Scenario: Colliding key
- **WHEN** a job cluster sets `custom_tags: { vendor: finance }`
- **THEN** DBU019 is reported, saying that the tag appears as `x_vendor` in cloud billing

#### Scenario: Normal key
- **WHEN** a job cluster sets `custom_tags: { team: finance }`
- **THEN** DBU019 is not reported

#### Scenario: Every tag location
- **WHEN** a job's `tags`, a cluster's `custom_tags`, a warehouse's `tags.custom_tags` and a pipeline's `tags` each use a default key
- **THEN** DBU019 is reported once for each of them

### Requirement: DBU009 large on-demand cluster
The system SHALL report an info finding for a classic job or pipeline cluster whose worker count (the maximum, for autoscale) is at least `rules.DBU009.min_workers` (default 8) and whose availability is explicitly `ON_DEMAND`, `ON_DEMAND_AZURE` or `ON_DEMAND_GCP`. Where the catalog has on-demand and spot prices for the worker type, the message SHALL give the hourly saving of spot workers.

#### Scenario: Large on-demand cluster
- **WHEN** an Azure job cluster of `Standard_DS3_v2` autoscales to 16 workers with `azure_attributes.availability: ON_DEMAND_AZURE`, and the catalog prices the VM at 0.229 on demand and 0.05 spot
- **THEN** DBU009 is reported, suggesting `SPOT_WITH_FALLBACK_AZURE`, with a saving of up to $2.86 per hour (16 × (0.229 − 0.05))

#### Scenario: Availability not set
- **WHEN** a large job cluster does not set availability
- **THEN** DBU009 is not reported

#### Scenario: Small cluster
- **WHEN** an on-demand job cluster has 4 workers
- **THEN** DBU009 is not reported

### Requirement: DBU017 spot without fallback
The system SHALL report an info finding for a classic job or pipeline cluster whose availability is spot without fallback: `SPOT` on AWS, `SPOT_AZURE` on Azure or `PREEMPTIBLE_GCP` on GCP. The message SHALL suggest the fallback variant and `first_on_demand` of at least 1, and SHALL say when the driver runs on spot because `first_on_demand` is 0 (explicitly, or by the AWS default).

#### Scenario: Azure spot without fallback
- **WHEN** a job cluster sets `azure_attributes.availability: SPOT_AZURE`
- **THEN** DBU017 is reported, suggesting `SPOT_WITH_FALLBACK_AZURE`

#### Scenario: Spot with fallback
- **WHEN** a job cluster sets `azure_attributes.availability: SPOT_WITH_FALLBACK_AZURE`
- **THEN** DBU017 is not reported

#### Scenario: Driver on spot
- **WHEN** a job cluster sets `aws_attributes.availability: SPOT` without `first_on_demand`
- **THEN** DBU017 is reported, and the message says the driver runs on spot too

### Requirement: DBU018 pipeline cluster on legacy autoscaling
The system SHALL report an info finding for a classic pipeline cluster whose `autoscale.mode` is `LEGACY`, because enhanced autoscaling adds and removes workers as the workload changes and is the default for new pipelines. It SHALL skip `maintenance` clusters and serverless pipelines.

#### Scenario: Legacy autoscaling
- **WHEN** a pipeline cluster sets `autoscale.mode: LEGACY`
- **THEN** DBU018 is reported

#### Scenario: Enhanced or unset
- **WHEN** a pipeline cluster sets `autoscale.mode: ENHANCED` or omits `mode`
- **THEN** DBU018 is not reported

#### Scenario: Maintenance cluster
- **WHEN** a pipeline's `maintenance` cluster sets `autoscale.mode: LEGACY`
- **THEN** DBU018 is not reported

### Requirement: DBU011 scheduled serverless job in performance-optimized mode
The system SHALL report an info finding for a job with serverless tasks that runs on an unpaused schedule or periodic trigger with `performance_target` absent (the API default is `PERFORMANCE_OPTIMIZED`) or set to `PERFORMANCE_OPTIMIZED`. The message SHALL suggest `STANDARD` for batch work that tolerates slower startup.

#### Scenario: Nightly serverless job
- **WHEN** a serverless job has a daily schedule and no `performance_target`
- **THEN** DBU011 is reported

#### Scenario: Already standard
- **WHEN** a scheduled serverless job sets `performance_target: STANDARD`
- **THEN** DBU011 is not reported

#### Scenario: Unscheduled or classic
- **WHEN** a serverless job has no schedule, or a scheduled job runs only on job clusters
- **THEN** DBU011 is not reported

### Requirement: DBU014 serving endpoint without scale-to-zero
The system SHALL report a finding for a served entity whose `scale_to_zero_enabled` is not `true`: a warning in a target whose `mode` is `development`, and an info finding in other targets. The message SHALL name the minimum provisioned concurrency kept running while the endpoint is idle: `min_provisioned_concurrency`, or the lower bound of the `workload_size` (Small 4, Medium 8, Large 16).

#### Scenario: Development endpoint always on
- **WHEN** a development target defines a served entity with `scale_to_zero_enabled: false`
- **THEN** DBU014 is reported as a warning

#### Scenario: Production endpoint with a warm floor
- **WHEN** a production target defines a served entity with `scale_to_zero_enabled: false` and `min_provisioned_concurrency: 8`
- **THEN** DBU014 is reported as info, naming a floor of 8

#### Scenario: Scale-to-zero on
- **WHEN** a served entity sets `scale_to_zero_enabled: true`
- **THEN** DBU014 is not reported

### Requirement: DBU013 large Databricks App
The system SHALL report an info finding for an app whose compute size is larger than MEDIUM, stating its DBU per hour as a multiple of MEDIUM, because an app is billed for its size for as long as it runs.

#### Scenario: Large app
- **WHEN** an app sets the LARGE compute size (1 DBU per hour)
- **THEN** DBU013 is reported, saying that it costs 2× MEDIUM

#### Scenario: XLarge app
- **WHEN** an app sets the XLARGE compute size (3 DBU per hour)
- **THEN** DBU013 is reported, saying that it costs 6× MEDIUM

#### Scenario: Medium or unset
- **WHEN** an app sets MEDIUM or no compute size
- **THEN** DBU013 is not reported

### Requirement: DBU015 Lakebase endpoint sizing
The system SHALL report a warning for a Lakebase endpoint with scale-to-zero disabled (`no_suspension: true`) on a branch that is not a production branch, and an info finding for any endpoint whose `autoscaling_limit_min_cu` is at least `rules.DBU015.max_min_cu` (default 8). Production branches SHALL be the branch with ID `production` and any branch listed in `rules.DBU015.production_branches`.

#### Scenario: Always-on development branch
- **WHEN** an endpoint on branch `dev` sets `no_suspension: true`
- **THEN** DBU015 is reported as a warning

#### Scenario: Always-on production branch
- **WHEN** an endpoint on branch `production` sets `no_suspension: true` with `autoscaling_limit_min_cu: 2`
- **THEN** DBU015 is not reported

#### Scenario: High minimum
- **WHEN** any endpoint sets `autoscaling_limit_min_cu: 8`
- **THEN** DBU015 is reported as info
