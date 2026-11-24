# cost-estimation Specification

## Purpose

Prices bundle resources from the versioned catalog and turns configuration into hourly rates and, where the configuration implies a duration, monthly figures, each with a confidence level and its assumptions.

## Requirements

### Requirement: Versioned price catalog
The system SHALL price resources from an embedded catalog in which every price and DBU rate has an `as_of` date and a `source`. It SHALL apply discounts, surcharges and SKU overrides from configuration, and SHALL print the oldest `as_of` date it used in every report.

#### Scenario: SKU override
- **WHEN** `.dbudoctor.yml` sets `pricing.overrides.PREMIUM_JOBS_COMPUTE: 0.24`
- **THEN** that SKU is priced at 0.24 per DBU, and discounts and surcharges are not applied to it

### Requirement: Commitments and surcharges
The system SHALL compute the effective price per DBU as list × (1 − discount + surcharge). The discount is the product family's value in `pricing.discounts` when set, otherwise `pricing.discounts.all`, otherwise 0. The surcharge is `pricing.surcharge`, otherwise 0.

#### Scenario: Commit-style discount
- **WHEN** Azure Jobs compute lists at 0.30 per DBU and `pricing.discounts.all` is 0.20
- **THEN** Jobs compute is priced at 0.24 per DBU

#### Scenario: Discount and surcharge
- **WHEN** Jobs compute lists at 0.30 per DBU, `pricing.discounts.all` is 0.20, and `pricing.surcharge` is 0.30
- **THEN** Jobs compute is priced at 0.33 per DBU

#### Scenario: Family discount overrides the default
- **WHEN** `pricing.discounts.all` is 0.20 and `pricing.discounts.jobs` is 0.10
- **THEN** Jobs compute uses a 0.10 discount, and other families use 0.20

### Requirement: Azure prices from the Retail Prices API
The system SHALL ship an Azure catalog generated from the public Azure Retail Prices API. The catalog SHALL be per region and SHALL contain Premium-tier DBU prices, plus Linux VM prices for pay-as-you-go, spot, 1-year and 3-year reservations, and 1-year and 3-year savings plans. With `--refresh-prices`, the system SHALL fetch current Azure prices for the bundle's region at run time without credentials, and SHALL fall back to the embedded catalog with a warning when the API is unreachable.

#### Scenario: Region-specific prices
- **WHEN** the cloud is Azure and `region` is `westeurope`
- **THEN** DBU and VM prices come from the `westeurope` catalog entries

#### Scenario: Refresh unavailable
- **WHEN** `--refresh-prices` is set and the Retail Prices API cannot be reached
- **THEN** the embedded catalog is used, the report shows its `as_of` date, and a warning is printed

### Requirement: AWS and GCP prices from the Databricks pricing data
The system SHALL ship AWS and GCP catalogs generated from the data behind the Databricks pricing calculator. They SHALL contain, per region, list $/DBU for the Premium and Enterprise tiers, and per cloud, DBU per hour for each instance type, with Photon DBU counts where published. GCP catalogs SHALL contain no VM prices; AWS VM prices come from the AWS Price List. Conflicting values for one SKU, tier and region SHALL be left out of the catalog and reported by the generator, not resolved by guessing.

#### Scenario: AWS region
- **WHEN** the cloud is AWS and `region` is `us-east-1`
- **THEN** Jobs compute is priced from the `us-east-1` catalog entry, and an `m5.xlarge` node is rated at its published DBU per hour

#### Scenario: Region-specific serverless price
- **WHEN** the cloud is GCP and SQL serverless has a different list price in `asia-northeast1` than in `us-central1`
- **THEN** each region's catalog file holds its own price

#### Scenario: Conflicting source values
- **WHEN** the pricing data lists two different Enterprise SQL pro prices for one region
- **THEN** that price is absent from the region's catalog file, the generator's summary names it, and rates that need it are Unknown

### Requirement: Cloud infrastructure for classic compute
The system SHALL add VM cost for the driver and workers of classic compute in either case:
- `pricing.include_cloud_infra` is `true`;
- `pricing.include_cloud_infra` is `auto` (the default) and the catalog has VM prices for the cloud and region.

It SHALL use the VM price column selected by `pricing.infra.commitment`, and SHALL never add infrastructure cost to serverless resources.

#### Scenario: Azure classic job cluster
- **WHEN** an Azure job cluster has a driver and 4 workers of a VM type rated 0.75 DBU per hour with a pay-as-you-go VM price of 0.229 per hour, and Jobs compute costs 0.30 per DBU
- **THEN** the rate is (0.75 + 4 × 0.75) × 0.30 + 5 × 0.229 = 2.27 per hour, shown as 1.125 for DBUs and 1.145 for VMs

#### Scenario: Cloud without VM prices
- **WHEN** the cloud is GCP, `pricing.include_cloud_infra` is `auto`, and the catalog has no GCP VM prices
- **THEN** classic rates contain DBUs only, and the report states that cloud VMs are not included

#### Scenario: Reserved VMs
- **WHEN** `pricing.infra.commitment` is `reserved_3y`
- **THEN** the VM part of every classic rate uses the catalog's 3-year reservation price for the node type

### Requirement: Hourly rate for every priced resource
The system SHALL compute a cost per active hour for every priced resource in base and head, using only configuration and the catalog.

#### Scenario: Classic job cluster, DBUs only
- **WHEN** a job cluster has a driver and 4 workers of a node type rated 0.75 DBU per hour, Photon is off, infrastructure is off, and Jobs compute costs 0.15 per DBU
- **THEN** the rate is (0.75 + 4 × 0.75) × 0.15 = 0.5625 per hour

#### Scenario: Autoscale
- **WHEN** a cluster autoscales between 2 and 10 workers and `estimation.autoscale` is `max`
- **THEN** the rate uses 10 workers, and the report states "autoscale at max"

#### Scenario: Photon
- **WHEN** a cluster's `runtime_engine` is `PHOTON`
- **THEN** the cluster is priced with the Photon method the catalog defines for that cloud: a Photon DBU count per node type and workload (Azure publishes these on its pricing page), a DBU multiplier per VM family, or a Photon SKU rate

#### Scenario: SQL warehouse
- **WHEN** a serverless warehouse is `Small` (12 DBU per hour) with `min_num_clusters: 1`, and serverless SQL costs 0.70 per DBU
- **THEN** the rate is 8.40 per hour

#### Scenario: Unknown node type
- **WHEN** a node type is missing from the catalog
- **THEN** the resource's rate is Unknown, the report names the node type, and the exit code is unaffected unless `fail_on.unknown_cost` is true

### Requirement: Runs per month from triggers
The system SHALL compute runs per month as the number of fire times in the 365 days after a reference instant, divided by 12. It SHALL evaluate Quartz cron expressions in the job's `timezone_id`, and SHALL return Unknown for event-driven jobs and jobs without a trigger.

#### Scenario: Hourly to every 15 minutes
- **WHEN** a job's `quartz_cron_expression` changes from `0 0 * * * ?` to `0 0/15 * * * ?`, with `timezone_id: UTC`
- **THEN** runs per month change from 730 to 2,920

#### Scenario: Paused schedule
- **WHEN** `schedule.pause_status` is `PAUSED`
- **THEN** runs per month are 0

#### Scenario: File-arrival trigger
- **WHEN** a job uses `trigger.file_arrival`
- **THEN** runs per month are Unknown, and so is the monthly estimate

### Requirement: Monthly estimate with confidence
The system SHALL attach a confidence level (High, Medium, Low or Unknown) and a list of assumptions to every monthly estimate. High SHALL be reserved for estimates calibrated from observed history. The system SHALL NOT show a monthly amount when the level is Unknown. It SHALL exclude Low and Unknown estimates from headline totals, and report how many it excluded.

#### Scenario: Configured run duration
- **WHEN** a classic job has a rate of 3.00 per hour, an hourly schedule, and `estimation.default_run_minutes: 20`
- **THEN** its estimate is 730 per month, with confidence Medium and the assumption "20-minute runs (configured)"

#### Scenario: No duration
- **WHEN** `estimation.default_run_minutes` is not set
- **THEN** a scheduled classic job shows its rate and runs per month, and the monthly estimate is Unknown

### Requirement: Scaling assumption
The system SHALL apply `estimation.scaling` when a resource's compute size changes. `constant_runtime` (the default) scales DBUs with size, and `linear_speedup` keeps the cost per run unchanged. The report SHALL name the assumption used.

#### Scenario: Doubling workers
- **WHEN** a job cluster with a driver and 4 workers of one node type changes to 8 workers of the same type, and `estimation.scaling` is `constant_runtime`
- **THEN** its rate and monthly estimate scale by 9 / 5 = 1.8

#### Scenario: Linear speedup
- **WHEN** the same change happens with `estimation.scaling: linear_speedup`
- **THEN** the monthly estimate is unchanged, and the report says so

### Requirement: Serverless jobs and pipelines
For serverless jobs and pipelines, the system SHALL show the price per DBU and runs per month, and SHALL report the monthly estimate as Unknown with the note "needs history".

#### Scenario: New serverless job
- **WHEN** a serverless job is added
- **THEN** the report shows "needs history" and no monthly amount

### Requirement: Continuous resources
The system SHALL treat a pipeline with `continuous: true`, or a job with an unpaused `continuous` block, as running 730 hours per month at its rate, with confidence Medium.

#### Scenario: Pipeline switched to continuous
- **WHEN** a classic pipeline with a rate of 2.00 per hour changes from triggered to `continuous: true`
- **THEN** its head estimate is 1,460 per month, with confidence Medium

### Requirement: Classic and serverless comparisons
When a resource moves between classic and serverless compute and cloud infrastructure is not included, the system SHALL mark the delta Low and state that the classic figure excludes cloud VMs.

#### Scenario: Warehouse moves from pro to serverless without VM prices
- **WHEN** a warehouse changes from `PRO` to serverless on a cloud without VM prices in the catalog
- **THEN** the delta is marked Low with the note "classic/pro cost excludes cloud VMs; serverless includes them"

#### Scenario: Same move on Azure
- **WHEN** the same change happens on Azure with infrastructure included
- **THEN** the pro side includes VM cost, and the delta is not marked Low for this reason

### Requirement: Platform defaults catalog
The catalog SHALL hold platform defaults that apply when a bundle omits a field in `defaults.yaml`, with an `as_of` date and a `source`. It SHALL start with `sql_warehouse.auto_stop_mins`. The embedded catalog SHALL include the file; a catalog directory without it SHALL still load, with no defaults.

#### Scenario: Default from the catalog
- **WHEN** the catalog's `defaults.yaml` sets `sql_warehouse.auto_stop_mins: 120`
- **THEN** a warehouse without `auto_stop_mins` is treated as stopping after 120 idle minutes

#### Scenario: Catalog without defaults
- **WHEN** a catalog directory has no `defaults.yaml`
- **THEN** the catalog loads, and no default applies

### Requirement: Runtime support catalog
The catalog SHALL hold Databricks Runtime LTS end-of-support dates in `runtimes.yaml`, keyed by `major.minor`, with an `as_of` date and a `source`. The embedded catalog SHALL include the file; a catalog directory without it SHALL still load, with no dates.

#### Scenario: Dates from the catalog
- **WHEN** the embedded catalog loads
- **THEN** 13.3 has end of support 2026-08-22, and the oldest listed release is 11.3

#### Scenario: Catalog without runtimes
- **WHEN** a catalog directory has no `runtimes.yaml`
- **THEN** the catalog loads, and DBU012 reports nothing

### Requirement: Databricks Apps
The system SHALL price an app as running all month: its compute size's DBU per hour × instances × 730 × the Interactive Serverless price, with confidence Medium and the assumption "running all month". When no compute size is set, it SHALL use MEDIUM. Sizes come from the catalog: MEDIUM 0.5, LARGE 1 and XLARGE 3 DBU per hour. Instances SHALL follow `estimation.autoscale` between `compute_min_instances` and `compute_max_instances`, and default to 1.

#### Scenario: Medium to Large
- **WHEN** an app changes from MEDIUM (0.5 DBU per hour) to LARGE (1 DBU per hour), and the Interactive Serverless price is 0.80 per DBU
- **THEN** the estimate goes from 292 to 584 per month (0.5 × 0.80 × 730 and 1 × 0.80 × 730), with confidence Medium

#### Scenario: Size not set
- **WHEN** an app is added without a compute size
- **THEN** it is priced as MEDIUM

#### Scenario: Several instances
- **WHEN** a LARGE app sets `compute_min_instances: 2` and `compute_max_instances: 3`, priced at max instances, and the price is 0.80
- **THEN** it is estimated at 1,752 per month (3 × 1 × 0.80 × 730)

### Requirement: Lakebase Autoscaling endpoints
The system SHALL price a Lakebase endpoint per active hour as `autoscaling_limit_min_cu` × the catalog's DBU per CU × the Database Serverless price. With scale to zero disabled (`no_suspension: true`) it SHALL show that rate × 730 as the monthly estimate, confidence Medium, and the ceiling at `autoscaling_limit_max_cu` × 730. With scale to zero enabled, the monthly estimate SHALL be Unknown, with the same ceiling. A disabled endpoint SHALL cost 0.

#### Scenario: Always-active endpoint
- **WHEN** an AWS endpoint has min 2 CU, max 8 CU and `no_suspension: true`, the catalog has 0.230 DBU per CU, and Database Serverless costs 0.50 per DBU
- **THEN** the estimate is 167.90 per month (2 × 0.230 × 0.50 × 730), up to 671.60 (8 × 0.230 × 0.50 × 730), with confidence Medium

#### Scenario: Scale-to-zero endpoint
- **WHEN** the same endpoint keeps scale to zero enabled
- **THEN** the monthly estimate is Unknown, the rate is 0.23 per active hour, and the ceiling is 671.60

#### Scenario: Cloud without a CU rate
- **WHEN** an endpoint is on GCP, which has no DBU per CU in the catalog
- **THEN** its rate is Unknown, with a reason naming GCP

### Requirement: AWS VM prices from the AWS Price List
The system SHALL generate AWS VM prices from the public AWS Price List bulk files, which need no credentials: per region and per instance type in the AWS node-type table, the On-Demand price as `payg` and Standard, No Upfront 1-year and 3-year reservations as `reserved_1y` and `reserved_3y`, all per hour. Only Linux, shared-tenancy rows without pre-installed software and with capacity status `Used` SHALL count. Conflicting values SHALL be left out and reported by the generator.

#### Scenario: AWS classic job cluster
- **WHEN** an AWS job cluster has a driver and 2 workers of `m5.xlarge`, rated 0.69 DBU per hour, with an On-Demand price of 0.192 per hour, and Jobs compute costs 0.15 per DBU
- **THEN** the rate is 3 × 0.69 × 0.15 + 3 × 0.192 = 0.8865 per hour, shown as 0.3105 for DBUs and 0.576 for VMs

#### Scenario: AWS spot commitment
- **WHEN** the cloud is AWS and `pricing.infra.commitment` is `spot`, which the AWS catalog does not hold
- **THEN** the VM part of classic rates is Unknown, with the reason that there is no spot VM price for the node type in the region

### Requirement: AWS SQL warehouse VM layout
The system SHALL price the VMs of classic and pro SQL warehouses on AWS with the documented layout: an `i3.2xlarge` driver for 2X-Small and X-Small, `i3.4xlarge` for Small, `i3.8xlarge` for Medium and Large, and `i3.16xlarge` from X-Large up, with `i3.2xlarge` workers in the same counts as on Azure.

#### Scenario: AWS pro warehouse
- **WHEN** an AWS pro warehouse is Small with one cluster
- **THEN** its VM part is one `i3.4xlarge` plus 4 × `i3.2xlarge` at the selected commitment

### Requirement: Instance pool node types
For a classic cluster with `instance_pool_id` and no `node_type_id`, the system SHALL take the worker node type from `estimation.instance_pools.<pool id>`, and the driver node type from `driver_node_type_id`, else from `estimation.instance_pools.<driver_instance_pool_id>`, else the worker node type. It SHALL then price the cluster as any classic cluster and state the assumption. A pool without a mapping SHALL leave the rate Unknown, with a reason naming the setting to add.

#### Scenario: Mapped pool
- **WHEN** a job cluster has `instance_pool_id: pool-a`, 2 workers, and `estimation.instance_pools.pool-a` is `Standard_DS3_v2`
- **THEN** its rate is priced for a `Standard_DS3_v2` driver and 2 `Standard_DS3_v2` workers, with the assumption "node type `Standard_DS3_v2` from instance pool `pool-a`"

#### Scenario: Separate driver pool
- **WHEN** the cluster also has `driver_instance_pool_id: pool-b`, mapped to `Standard_DS4_v2`
- **THEN** the driver is priced as `Standard_DS4_v2` and the workers as `Standard_DS3_v2`

#### Scenario: Unmapped pool
- **WHEN** a job cluster has `instance_pool_id: pool-x` and no mapping for it
- **THEN** the rate is Unknown, and the reason says to set `estimation.instance_pools.pool-x` to the pool's node type
