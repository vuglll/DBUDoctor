## Why

After the foundation (`add-pr-cost-diff`, releases 0.1 and 0.2), several resources show only an hourly rate:
- serverless jobs and pipelines;
- SQL warehouses;
- all-purpose clusters;
- event-triggered jobs.

Monthly figures for scheduled classic jobs also rest on a run duration the user configured. The workspace already records what really ran in its system tables. Reading that history turns rates into monthly figures with confidence High, which is the step that makes the pull request comment trustworthy enough to gate merges on.

## What Changes

- Read usage history from `system.billing.usage` through the SQL Statement Execution API, on a warehouse the user names.
- Map bundle resources to deployed IDs using `databricks bundle summary --output json` for the calibration target.
- Compute calibrated monthly estimates (confidence High) for:
  - classic and serverless jobs;
  - pipelines;
  - SQL warehouses;
  - all-purpose clusters.

  On Azure, VM cost is derived from the observed runtime, because billing usage holds DBUs only. AWS and GCP stay DBU-only, as in 0.2.
- Replace catalog list prices with `system.billing.list_prices` when available.
- Estimate serverless job performance-mode changes (`performance_target`) from observed DBUs.
- Warn when observed DBUs per node-hour drift more than 10% from the catalog.
- New `calibration.*` settings in `.dbudoctor.yml`.

Release: 0.3.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `cost-estimation`: adds calibration from system tables, calibrated monthly estimates, live list prices, serverless estimates from history, and the node-type drift check.
- `configuration`: adds the calibration settings.

## Impact

- Depends on `add-pr-cost-diff`, whose specs are synced into `openspec/specs/`, so this change's requirements add to existing capabilities.
- Needs a SQL warehouse, plus SELECT on `system.billing` (and `system.compute` for the drift check). Calibration queries are small but not free: they run on the user's warehouse, and results are cached per day.
- New code in `DBUDoctor.Databricks`: the Statement Execution API client and auth.
- Out of scope: real Azure VM costs from Azure Cost Management, which is a later change.
