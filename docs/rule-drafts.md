# Rule drafts

Ready-to-paste requirement text for the rules and pricing on the roadmap. Each block goes under `## ADDED Requirements` in the change named next to it. Re-check field names and sources when you propose a change, because drafts age. The research behind them is in `docs/research/2026-11-08-rules-and-azure.md`.

## 0.4: Idle money

Change: `add-dbu002-cluster-auto-termination` → `specs/guardrail-rules/spec.md`

### Requirement: DBU002 all-purpose cluster auto-termination
The system SHALL report a warning for a `clusters` resource with `autotermination_minutes: 0`. It SHALL report an info finding when the value is absent, or when it exceeds `rules.DBU002.max_minutes` (default 60).

#### Scenario: Auto-termination disabled
- **WHEN** a cluster sets `autotermination_minutes: 0`
- **THEN** DBU002 is reported as a warning

#### Scenario: Long idle window
- **WHEN** a cluster sets `autotermination_minutes: 240`
- **THEN** DBU002 is reported as info

#### Scenario: Value not set
- **WHEN** a cluster omits `autotermination_minutes`
- **THEN** DBU002 is reported as info, and the message says that the effective default depends on the deployment engine

Change: `add-dbu003-warehouse-auto-stop` → `specs/guardrail-rules/spec.md` (also adds `catalog/defaults.yaml`)

### Requirement: DBU003 SQL warehouse auto-stop
The system SHALL report a warning for a SQL warehouse whose `auto_stop_mins` is 0. It SHALL report an info finding when the effective value exceeds `rules.DBU003.max_minutes` (default 30). When the field is absent, the effective value is the catalog default.

#### Scenario: Auto-stop disabled
- **WHEN** a warehouse sets `auto_stop_mins: 0`
- **THEN** DBU003 is reported as a warning

#### Scenario: Default auto-stop
- **WHEN** a warehouse omits `auto_stop_mins` and the catalog default is greater than 30 minutes
- **THEN** DBU003 is reported as info, and the message states the default used

Change: `add-dbu005-continuous-job` → `specs/guardrail-rules/spec.md`

### Requirement: DBU005 continuous job
The system SHALL report a warning for a job with a `continuous` block whose `pause_status` is not `PAUSED`.

#### Scenario: Continuous job added
- **WHEN** the head adds a job with `continuous: { pause_status: UNPAUSED }`
- **THEN** DBU005 is reported

#### Scenario: Paused continuous job
- **WHEN** a job has `continuous: { pause_status: PAUSED }`
- **THEN** DBU005 is not reported

Change: `add-dbu016-short-schedule` → `specs/guardrail-rules/spec.md`

### Requirement: DBU016 classic job cluster on a short schedule
The system SHALL report an info finding for a job that meets all of these conditions:
- it runs on a classic job cluster that is not backed by an instance pool;
- it has a schedule or periodic trigger whose shortest interval between runs is under `rules.DBU016.min_interval_minutes` (default 15).

The message SHALL say that every run pays for cluster startup, and SHALL suggest serverless, a pool or a continuous job.

#### Scenario: Every five minutes on classic compute
- **WHEN** a job on a classic job cluster uses `0 0/5 * * * ?`
- **THEN** DBU016 is reported

#### Scenario: Serverless or pooled
- **WHEN** the same schedule runs on serverless compute, or on a job cluster with `instance_pool_id`
- **THEN** DBU016 is not reported

## 0.5: Hygiene

Change: `add-dbu007-job-timeout` → `specs/guardrail-rules/spec.md`

### Requirement: DBU007 job without a timeout
The system SHALL report an info finding for a job that has no `timeout_seconds` (absent or 0) on the job itself or on any of its tasks.

#### Scenario: No timeouts
- **WHEN** neither the job nor any task sets `timeout_seconds`
- **THEN** DBU007 is reported

#### Scenario: Task timeouts present
- **WHEN** every task sets `timeout_seconds`
- **THEN** DBU007 is not reported

Change: `add-dbu008-cost-tags` → `specs/guardrail-rules/spec.md`

### Requirement: DBU008 missing cost-attribution tags
When `rules.DBU008.required_tags` is non-empty, the system SHALL report a warning for each priced resource that lacks a required tag key in its tag locations:
- jobs: `tags` and job cluster `custom_tags`;
- pipelines: cluster `custom_tags`;
- clusters: `custom_tags`;
- SQL warehouses: `tags.custom_tags`.

It SHALL accept a `budget_policy_id` as attribution for serverless jobs and pipelines.

#### Scenario: Missing tag
- **WHEN** `required_tags` is `[team, cost_center]` and a job has only `team`
- **THEN** DBU008 is reported, naming `cost_center`

#### Scenario: Rule off by default
- **WHEN** `required_tags` is empty or absent
- **THEN** DBU008 is not evaluated

Change: `add-dbu012-runtime-eos` → `specs/guardrail-rules/spec.md` (also adds `catalog/runtimes.yaml`)

### Requirement: DBU012 outdated or end-of-support runtime
The system SHALL report a warning for a classic cluster whose `spark_version` has passed its end-of-support date in the catalog. It SHALL report an info finding when that date is within `rules.DBU012.warn_days` (default 90), because older runtimes miss performance improvements and security fixes. Dates SHALL be compared against an injectable reference date.

#### Scenario: Runtime past end of support
- **WHEN** the reference date is 2026-11-08 and a job cluster uses `13.3.x-scala2.12` (end of support 2026-08-22)
- **THEN** DBU012 is reported as a warning

#### Scenario: Runtime close to end of support
- **WHEN** the reference date is 2026-11-08 and a job cluster uses `14.3.x-scala2.12` (end of support 2027-02-01, 85 days away)
- **THEN** DBU012 is reported as info

#### Scenario: Supported runtime
- **WHEN** a job cluster uses `16.4.x-scala2.12` (end of support 2028-05-09)
- **THEN** DBU012 is not reported

Change: `add-dbu019-tag-collision` → `specs/guardrail-rules/spec.md`

### Requirement: DBU019 custom tag collides with a default tag
The system SHALL report an info finding for a custom tag whose key matches a Databricks default tag key. Keys SHALL be compared case-insensitively against `Vendor`, `ClusterId`, `ClusterName`, `Creator`, `RunName`, `JobId`, `SqlEndpointId`, `DatabricksInstancePoolId` and `DatabricksInstancePoolCreatorId`. The reason is that cloud billing renames the custom tag with an `x_` prefix, which breaks cost attribution.

#### Scenario: Colliding key
- **WHEN** a job cluster sets `custom_tags: { vendor: finance }`
- **THEN** DBU019 is reported, saying that the tag appears as `x_vendor` in cloud billing

#### Scenario: Normal key
- **WHEN** a job cluster sets `custom_tags: { team: finance }`
- **THEN** DBU019 is not reported

## 0.6: Compute config

Change: `add-dbu009-large-on-demand` → `specs/guardrail-rules/spec.md`

### Requirement: DBU009 large on-demand cluster
The system SHALL report an info finding for a classic job or pipeline cluster when both of these are true:
- its worker count (the maximum, for autoscale) is at least `rules.DBU009.min_workers` (default 8);
- its availability is explicitly on-demand (`ON_DEMAND`, `ON_DEMAND_AZURE` or `ON_DEMAND_GCP`).

#### Scenario: Large on-demand cluster
- **WHEN** a job cluster autoscales to 16 workers with `azure_attributes.availability: ON_DEMAND_AZURE`
- **THEN** DBU009 is reported

#### Scenario: Availability not set
- **WHEN** a large job cluster does not set availability
- **THEN** DBU009 is not reported

Change: `add-dbu017-spot-without-fallback` → `specs/guardrail-rules/spec.md`

### Requirement: DBU017 spot without fallback
The system SHALL report an info finding for a classic job or pipeline cluster whose availability is spot without fallback: `SPOT` on AWS, `SPOT_AZURE` on Azure or `PREEMPTIBLE_GCP` on GCP. The reason is that an eviction can fail the run, and the retry pays again. The message SHALL suggest the fallback variant and `first_on_demand` of at least 1.

#### Scenario: Azure spot without fallback
- **WHEN** a job cluster sets `azure_attributes.availability: SPOT_AZURE`
- **THEN** DBU017 is reported

#### Scenario: Spot with fallback
- **WHEN** a job cluster sets `azure_attributes.availability: SPOT_WITH_FALLBACK_AZURE`
- **THEN** DBU017 is not reported

Change: `add-dbu018-legacy-autoscaling` → `specs/guardrail-rules/spec.md`

### Requirement: DBU018 pipeline cluster on legacy autoscaling
The system SHALL report an info finding for a classic pipeline cluster whose `autoscale.mode` is `LEGACY`, because enhanced autoscaling adds and removes workers as the workload changes, and it is the default for new pipelines.

#### Scenario: Legacy autoscaling
- **WHEN** a pipeline cluster sets `autoscale.mode: LEGACY`
- **THEN** DBU018 is reported

#### Scenario: Enhanced or unset
- **WHEN** a pipeline cluster sets `autoscale.mode: ENHANCED` or omits `mode`
- **THEN** DBU018 is not reported

## 0.7: Serverless and Apps

Change: `add-dbu011-serverless-performance-mode` → `specs/guardrail-rules/spec.md`

### Requirement: DBU011 scheduled serverless job in performance-optimized mode
The system SHALL report an info finding for a serverless job that runs on a schedule or a periodic trigger with `performance_target` absent or set to `PERFORMANCE_OPTIMIZED`. The message SHALL suggest `STANDARD` for batch work that tolerates slower startup.

#### Scenario: Nightly serverless job
- **WHEN** a serverless job has a daily schedule and no `performance_target`
- **THEN** DBU011 is reported

#### Scenario: Already standard
- **WHEN** a scheduled serverless job sets `performance_target: STANDARD`
- **THEN** DBU011 is not reported

Change: `add-apps-pricing` → `specs/cost-estimation/spec.md` and `specs/bundle-diff/spec.md` (adds `apps` to the priced types and `catalog/serverless.yaml`)

### Requirement: Databricks Apps
The system SHALL price an app as running all month: the DBU per hour of its compute size × 730 × the Interactive Serverless price, with confidence Medium and the assumption "running all month". When no compute size is set, it SHALL use Medium. Sizes: Medium 0.5, Large 1 and XLarge 3 DBU per hour.

#### Scenario: Medium to Large
- **WHEN** an app changes from Medium (0.5 DBU per hour) to Large (1 DBU per hour), and the Interactive Serverless price is 0.80 per DBU
- **THEN** the estimate goes from 292 to 584 per month, with confidence Medium

#### Scenario: Size not set
- **WHEN** an app is added without a compute size
- **THEN** it is priced as Medium

Change: `add-dbu013-large-app` → `specs/guardrail-rules/spec.md` (after `add-apps-pricing`)

### Requirement: DBU013 large Databricks App
The system SHALL report an info finding for an app whose compute size is larger than Medium, stating its DBU per hour as a multiple of Medium, because an app is billed for its size for as long as it runs.

#### Scenario: Large app
- **WHEN** an app sets the Large compute size (1 DBU per hour)
- **THEN** DBU013 is reported, saying that it costs 2× Medium

#### Scenario: XLarge app
- **WHEN** an app sets the XLarge compute size (3 DBU per hour)
- **THEN** DBU013 is reported, saying that it costs 6× Medium

Change: `add-dbu014-serving-scale-to-zero` → `specs/guardrail-rules/spec.md` (also adds `model_serving_endpoints` as a rule-only type in `bundle-diff`)

### Requirement: DBU014 serving endpoint without scale-to-zero
The system SHALL report a finding for a served entity whose `scale_to_zero_enabled` is not `true`:
- a warning in a target whose `mode` is `development`;
- an info finding in other targets, naming the minimum provisioned concurrency that is billed while the endpoint is idle.

#### Scenario: Development endpoint always on
- **WHEN** a development target defines a served entity with `scale_to_zero_enabled: false`
- **THEN** DBU014 is reported as a warning

#### Scenario: Production endpoint with a warm floor
- **WHEN** a production target defines a served entity with `scale_to_zero_enabled: false` and `min_provisioned_concurrency: 8`
- **THEN** DBU014 is reported as info, naming a floor of 8

#### Scenario: Scale-to-zero on
- **WHEN** a served entity sets `scale_to_zero_enabled: true`
- **THEN** DBU014 is not reported

## 0.8: Lakebase

Change: `add-lakebase-pricing` → `specs/cost-estimation/spec.md` and `specs/bundle-diff/spec.md` (adds `postgres_endpoints` to the priced types)

### Requirement: Lakebase Autoscaling endpoints
For a Lakebase endpoint with scale-to-zero disabled, the system SHALL compute two figures:
- the floor: `autoscaling_limit_min_cu` × 730 × the CU-hour price × (1 − the catalog's Always-On baseline discount);
- the ceiling: the floor + (`autoscaling_limit_max_cu` − `autoscaling_limit_min_cu`) × 730 × the CU-hour price.

It SHALL show the floor as the monthly estimate, with confidence Medium, and the ceiling as "up to". With scale-to-zero enabled, the monthly estimate SHALL be Unknown without history, and the ceiling SHALL be `autoscaling_limit_max_cu` × 730 × the CU-hour price.

#### Scenario: Always-active endpoint
- **WHEN** an endpoint has min 2 CU, max 8 CU, scale-to-zero disabled, a CU-hour price of 0.111, and an Always-On discount of 0.25
- **THEN** the estimate is 122 per month (floor 121.545), shown as "up to 608" (ceiling 607.725), with confidence Medium

#### Scenario: Scale-to-zero endpoint
- **WHEN** the same endpoint has scale-to-zero enabled and calibration is off
- **THEN** the monthly estimate is Unknown, and the report shows "up to 648"

Change: `add-dbu015-lakebase-sizing` → `specs/guardrail-rules/spec.md` (after `add-lakebase-pricing`)

### Requirement: DBU015 Lakebase endpoint sizing
The system SHALL report:
- a warning for a Lakebase endpoint with scale-to-zero disabled (`no_suspension: true`) on a branch that is not a production branch;
- an info finding for any endpoint whose `autoscaling_limit_min_cu` is at least `rules.DBU015.max_min_cu` (default 8).

Production branches SHALL be the branch with ID `production` and any branch listed in `rules.DBU015.production_branches`.

#### Scenario: Always-on development branch
- **WHEN** an endpoint on branch `dev` sets `no_suspension: true`
- **THEN** DBU015 is reported as a warning

#### Scenario: Always-on production branch
- **WHEN** an endpoint on branch `production` sets `no_suspension: true` with `autoscaling_limit_min_cu: 2`
- **THEN** DBU015 is not reported

#### Scenario: High minimum
- **WHEN** any endpoint sets `autoscaling_limit_min_cu: 8`
- **THEN** DBU015 is reported as info
