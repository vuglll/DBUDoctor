## Context

The foundation prices configuration: exact hourly rates, and monthly figures only where configuration implies a duration (continuous resources, or `estimation.default_run_minutes`). Unity Catalog system tables record what actually ran. `system.billing.usage` attributes DBUs to jobs and job runs, warehouses, clusters and pipelines through `usage_metadata`, and `system.billing.list_prices` holds list prices per SKU.

## Goals / Non-Goals

**Goals**

- Produce High-confidence monthly estimates for every priced resource that has history.
- Keep calibration opt-in, read-only, cheap and cached.
- Degrade gracefully: missing permissions or an unreachable warehouse never fail the build.

**Non-Goals**

- Real VM costs from Azure Cost Management. A later change could add these using the default tags (`JobId`, `ClusterId`, `SqlEndpointId`) that Azure Databricks propagates to VMs, disks and NICs.
- Forecasting growth from trends. Estimates assume the observed window repeats.

## Decisions

### C1. Billing usage is the single source of history

Calibration runs read-only queries through the SQL Statement Execution API against `calibration.warehouse_id`, over `calibration.window_days` (default 30), only for the resources the diff touches.

```sql
-- DBUs per job run
SELECT u.usage_metadata.job_id     AS job_id,
       u.usage_metadata.job_run_id AS run_id,
       u.sku_name,
       SUM(u.usage_quantity)       AS dbus
FROM system.billing.usage u
WHERE u.usage_unit = 'DBU'
  AND u.usage_date >= date_sub(current_date(), 30)
  AND u.usage_metadata.job_id IN (/* validated ids */)
GROUP BY ALL;
```

- Equivalent queries group by `usage_metadata.warehouse_id`, `usage_metadata.cluster_id` and `usage_metadata.dlt_pipeline_id`.
- `usage_metadata.job_id` and `job_run_id` are filled for job compute and serverless jobs, not for jobs on all-purpose compute; those are calibrated through their cluster, as in DBU001's note.
- Corrections arrive as `RETRACTION` rows with negative quantities followed by `RESTATEMENT` rows, so every query sums `usage_quantity` and never filters on `record_type`.
- IDs are passed as named parameters (`:job_id_0`, ...) of the Statement Execution API, which supports `:name` markers but not `?`. Validation (C3) still runs first, as defense in depth.
- Prices join `system.billing.list_prices` on `sku_name` and `cloud`, where `price_end_time IS NULL`, using `pricing.default`. `list_prices` has a row only when a price changes, so the current price is the row with `price_end_time IS NULL` for the bundle's cloud and `currency_code = 'USD'`. Configured discounts and surcharges still apply on top.

### C2. Identity through `bundle summary`

Each bundle resource key maps to its deployed ID through `databricks bundle summary --target <calibration.target> --output json`. If that fails, DBUDoctor falls back to matching by name. If that fails too, the resource is uncalibrated and the report says it was not found in the target.

### C3. Validated IDs only

Every ID is validated against its format before it is inlined: numeric job IDs, UUID pipeline IDs, and the warehouse and cluster ID patterns. No unvalidated input is concatenated into SQL. Tests include injection attempts.

### C4. Daily cache

Results are cached per workspace, query hash and UTC day in the runner cache (for example, `actions/cache`), so not every push starts the warehouse.

### C5. Auth

Auth reuses the Databricks CLI where possible (`databricks auth token`). Otherwise it uses a PAT or OAuth M2M from the standard `DATABRICKS_*` environment variables. Tokens are never logged.

### C6. Calibration costs money, and the docs say so

Queries run on the user's warehouse. The docs recommend an existing shared warehouse, or a 2X-Small serverless one with the shortest auto-stop, and least-privilege grants: SELECT on `system.billing` (and `system.compute` for C8), and CAN_USE on one warehouse.

### C7. Calibrated formulas

- **Classic jobs:**
  - DBUs: `median_dbus_per_run × runs_per_month × p(sku)`, where runs per month come from the schedule, or from observed runs for event-driven jobs.
  - Size changes: under `constant_runtime`, DBUs per run scale by `dbu_per_hour(head) / dbu_per_hour(base)`.
  - Infrastructure, when included (Azure): `run_hours = median_dbus_per_run / dbu_per_hour(base)`, and the VM part per run is `run_hours × vm_per_hour(head)`.
- **Serverless jobs and pipelines:** observed DBUs per run × runs per month × the serverless price. For jobs, a `performance_target` change from `PERFORMANCE_OPTIMIZED` (or absent) to `STANDARD` multiplies observed DBUs by `estimation.standard_mode_factor` (default 0.7) with confidence Low, and the reverse change divides by it. The default comes from a published TPC-DS comparison that measured 37 DBUs in Standard mode against 52 in Performance mode (≈ 0.71).
- **SQL warehouses:** `observed_dbus × dbu(size_head) / dbu(size_base) × p(type_head)`, which assumes constant uptime.
- **All-purpose clusters:** observed DBUs scaled by the rate ratio.
- **Confidence:** High needs at least `calibration.min_runs` runs (default 5) or 7 days of usage in the window. Less history gives Low.

### C8. Drift check

Where `system.compute.node_timeline` is readable, DBUDoctor derives the observed DBUs per node-hour for each node type it calibrated. When that differs from `node-types/<cloud>.yaml` by more than 10%, it prints a warning and suggests a catalog pull request. This keeps the community catalog honest.

## Risks / Trade-offs

- **The past may not predict the future.** The assumptions name the window, and High still means "if the next month looks like the last one".
- **`bundle summary` JSON shape may change.** A contract test runs against several CLI versions.
- **Shared all-purpose clusters can't be attributed precisely to one job.** Only the cluster as a whole is calibrated.

## Migration Plan

Additive. With `calibration.enabled: false` (the default), behavior is exactly as in 0.2.

## Open Questions

Resolved before implementation (2026-11-09):

1. **`bundle summary -o json` shape.** The CLI's resource structs declare `ID string \`json:"id,omitempty" bundle:"readonly"\`` per type, and `BaseResource` adds `url` and `modified_status` (`created` or `deleted`; empty means unchanged). Deployed IDs therefore appear as strings at `resources.<type>.<key>.id`, for `jobs`, `pipelines`, `sql_warehouses` and `clusters`. A contract test pins this shape with recorded output.
2. **`system.compute.node_timeline`.** The docs do not say whether the `compute` schema is enabled by default, and it covers all-purpose and job compute only (not serverless or SQL warehouses), at minute granularity, possibly missing nodes that ran under 10 minutes. The drift check therefore treats an unreadable table as "skip the check" with a note, never as an error.
3. **Performance mode for pipelines.** Only jobs have `performance_target` in the bundle schema (`PERFORMANCE_OPTIMIZED` or `STANDARD`). For pipelines it is a scheduler setting outside the bundle, so pipeline estimates use history as observed, and the factor applies to jobs only. Billing rows still record `product_features.performance_target` for both.
