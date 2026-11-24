# Changelog

## Unreleased

- Clusters on instance pools are priced: `estimation.instance_pools` maps a pool ID to its node type, for workers (`instance_pool_id`) and drivers (`driver_instance_pool_id`). An unmapped pool stays Unknown, and the reason names the setting to add.
- `dbudoctor diff --var name=value` (repeatable) passes bundle variables to `bundle validate` at both refs and to `bundle summary`, for variables without a default. When validate fails on a required variable, the error suggests `--var` or `BUNDLE_VAR_<name>`.
- `pip install dbudoctor`: each release publishes wheels for Linux x64 and ARM64, macOS ARM64 and Windows x64 to PyPI, holding the native binary (no .NET needed); `python -m dbudoctor` works too. Wheel platform tags are read from the binary.
- Linux binaries are built on Ubuntu 22.04, so they run on glibc 2.34 and later, including Databricks Runtime 13.3 LTS and later (they needed Ubuntu 24.04's glibc before).

- AWS classic compute is priced with its EC2 instances: the AWS catalog gains On-Demand (`payg`) and Standard, No Upfront `reserved_1y` and `reserved_3y` prices per region, generated from the public AWS Price List, and AWS classic and pro SQL warehouses use the documented `i3` layout. AWS spot and savings plans are not in the catalog, so those commitments make the VM part Unknown. Reports list EBS, S3, data transfer and NAT gateways as not modeled on AWS. Price sheets can name a separate `vm_source`.

## 0.3.0

Calibration is **experimental** in this release: it has been tested against recorded query results, not yet against a live workspace. It is off unless `calibration.enabled` is set.

- DBU015 (Lakebase endpoint sizing): a warning for an always-active compute on a non-production branch, with its monthly cost; info for `autoscaling_limit_min_cu` at `rules.DBU015.max_min_cu` (8) or more.
- Lakebase Autoscaling endpoints (`postgres_endpoints`) are priced on AWS and Azure: min CU per active hour at the Database Serverless price; always-active computes run 730 hours (Medium), up to the max CU. The catalog gains `PREMIUM_DATABASE_SERVERLESS_COMPUTE` and DBU per CU. The JSON report gains `monthlyCeiling`.
- DBU013 (large Databricks App): info for an app larger than MEDIUM, as a multiple of MEDIUM, with its monthly cost.
- Databricks Apps are priced: compute size × instances × 730 × the Interactive Serverless price (confidence Medium), with sizes in the new `catalog/serverless.yaml`. Apps were listed as not priced before.
- DBU014 (serving endpoint without scale-to-zero): a warning in development targets and info elsewhere for a served entity without `scale_to_zero_enabled: true`, naming its provisioned concurrency floor. Serving endpoints are read by rules but not priced.
- DBU011 (scheduled serverless job in performance-optimized mode): info for a scheduled job with serverless tasks whose `performance_target` is absent (the default, `PERFORMANCE_OPTIMIZED`) or `PERFORMANCE_OPTIMIZED`.
- DBU018 (pipeline cluster on legacy autoscaling): info for a classic pipeline cluster with `autoscale.mode: LEGACY`.
- DBU017 (spot without fallback): info for a job or pipeline cluster on `SPOT`, `SPOT_AZURE` or `PREEMPTIBLE_GCP`, naming a driver on spot when `first_on_demand` is 0.
- DBU009 (large on-demand cluster): info for a job or pipeline cluster with at least `rules.DBU009.min_workers` (8) workers set explicitly to on-demand, with the spot saving where the catalog has spot prices (Azure).
- DBU019 (custom tag collides with a default tag): info for a custom tag whose key matches a Databricks default tag key, which cloud billing renames with an `x_` prefix.
- DBU012 (outdated or end-of-support runtime): a warning for a cluster on a Databricks Runtime past end of support, info within `rules.DBU012.warn_days` (90). Dates are in the new `catalog/runtimes.yaml` (LTS releases 11.3 to 17.3), compared with today's date.
- DBU008 (missing cost-attribution tags): with `rules.DBU008.required_tags`, a warning for a job, pipeline, cluster or warehouse that lacks a required tag key; serverless jobs and pipelines with a usage policy count as attributed. Rules can now take list settings.
- The configuration schema lists every shipped rule and its parameters under `rules` (it listed only DBU001, DBU004 and DBU010, so editors rejected settings for the 0.4 rules).
- DBU007 (job without a timeout): info for a job with no `timeout_seconds` on the job and on at least one task, with the hourly cost of a stuck run when the rate is known.
- DBU016 (classic job cluster on a short schedule): info when a job on an unpooled classic job cluster runs more often than every `rules.DBU016.min_interval_minutes` (15), with the cluster starts per month.
- DBU005 (continuous job): a warning for an unpaused `continuous` job, with its cost around the clock when the rate is known.
- DBU003 (SQL warehouse auto-stop): a warning for `auto_stop_mins: 0`, info when the effective auto-stop exceeds `rules.DBU003.max_minutes` (30), with the idle cost per hour. An unset value uses the new `catalog/defaults.yaml` (the API default, 120 minutes).
- DBU002 (all-purpose cluster auto-termination): a warning for `autotermination_minutes: 0`, info for an unset value or one above `rules.DBU002.max_minutes` (60), with the idle cost per hour.
- New-only reporting counts a finding as new when its severity rises over the base finding with the same identity.
- Calibration from system tables (`calibration.*`, opt-in, experimental): observed DBUs from `system.billing.usage` turn rates into monthly figures with confidence High for classic and serverless jobs, pipelines, SQL warehouses and all-purpose clusters. On Azure, VMs come from the observed runtime. Current list prices come from `system.billing.list_prices`. A node type whose observed DBUs per node-hour drift more than 10% from the catalog gets a warning. Queries run on a SQL warehouse you name, pass IDs as validated parameters, and are cached per UTC day; failures become warnings and never change the exit code. Auth: a token, GitHub OIDC or env OIDC token federation, OAuth M2M, or the Databricks CLI. See docs/calibration.md.
- `estimation.standard_mode_factor` (0.7): with calibration, a job's `performance_target` change to `STANDARD` scales observed DBUs, with confidence Low.
- The JSON report gains `calibration` per bundle and `history` per estimate (both null without calibration).

## 0.2.0

- Git and Databricks CLI resolution: `dbudoctor diff` checks the base ref out into a temporary worktree and runs `databricks bundle validate --output json` at both refs, for every bundle in the repository.
- Source locations from `--include-locations` when the CLI supports it.
- GitHub Action (`action/`) with one sticky pull request comment per bundle and target, falling back to the job summary on forks.
- Agent skill (`skills/dbudoctor/`), generated from rule metadata, and `dbudoctor skill export`.
- `--refresh-prices`: live Azure prices for the bundle's region, cached for the UTC day.
- AWS and GCP catalogs generated by `tools/CatalogGen` from the Databricks pricing calculator data: list $/DBU per region for the Premium and Enterprise plans, and DBU per hour per instance type with Photon counts (AWS 16 regions and 575 instance types, GCP 15 regions and 157 machine types). This replaces the unverified AWS seed.
- `pricing.tier: premium | enterprise` (and `DBUDOCTOR_TIER`). The JSON report gains a `tier` field.
- SQL warehouse 5X-Large: 1,042 DBU per hour.
- Fixes from a run against `databricks/bundle-examples`: daily and weekly crons in DST time zones are described as daily and weekly; classic → serverless changes say so, and their rate lines no longer read "→ needs history per active hour"; a schedule moved to an equivalent trigger reads "schedule changed, still daily"; changed field values are quoted; an unknown cloud no longer adds a misleading missing-node-type reason; an unresolved `${…}` reference in a typed field gets a clear error.

## 0.1.0

- Pre-resolved JSON in (`--base-json`, `--head-json`), console, Markdown and JSON out.
- Azure catalog for 50 regions from the Azure Retail Prices API: Premium DBU prices, Linux VM prices (pay-as-you-go, spot, reservations, savings plans), and DBU counts per VM from the Azure pricing page, including Photon.
- Cost model for classic and serverless jobs, pipelines, all-purpose clusters and SQL warehouses, with confidence levels.
- Rules DBU001 (task on all-purpose compute), DBU004 (continuous pipeline) and DBU010 (sharp frequency increase).
- `.dbudoctor.yml` with discounts, surcharges, overrides, VM commitments, suppressions and `fail_on` policies.
