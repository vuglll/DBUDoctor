## Why

A Databricks App bills for its compute size for as long as it runs, so a bundle that adds an app, or moves it to a larger size, adds a fixed monthly cost. DBUDoctor listed apps as "not priced", so that cost never reached the pull request comment. The roadmap prices apps before DBU013 (release 0.7).

## What Changes

- `apps` becomes a priced resource type. Its cost-relevant fields are:
  - `compute_size`, `compute_min_instances` and `compute_max_instances`;
  - the usage or budget policy.
- Add `catalog/serverless.yaml` with app sizes in DBU per hour per instance (MEDIUM 0.5, LARGE 1, XLARGE 3) and the default size (MEDIUM). The roadmap names this file.
- Price an app as running all month: DBU per hour × instances × 730 × the Interactive (all-purpose) Serverless price, confidence Medium. Instances follow `estimation.autoscale` between min and max (default 1).
- The diff spec's example of an unpriced type moves from `apps` to `dashboards`.

Facts, checked 2026-11-09:
- https://docs.databricks.com/aws/en/dev-tools/databricks-apps/compute-size:
  - Medium 0.5, Large 1 and XLarge 3 DBU per hour;
  - "If you don't specify an instance size, Databricks assigns the Medium size by default".
- https://docs.databricks.com/aws/en/dev-tools/databricks-apps/: "Apps are billed per hour of compute time while running, based on provisioned capacity."
- https://docs.databricks.com/aws/en/dev-tools/databricks-apps/horizontal-scaling:
  - "Compute cost scales linearly with the number of instances";
  - at most 5 instances.
- Billed against the Interactive Serverless SKU: https://learn.microsoft.com/en-us/azure/databricks/resources/pricing, from the research in `docs/research/2026-11-08-rules-and-azure.md`. The public Apps pricing page did not render a rate, so DBUDoctor uses the catalog's `PREMIUM_ALL_PURPOSE_SERVERLESS_COMPUTE` price.
- Field names come from `databricks bundle schema` (CLI 1.24.0): `compute_size` (MEDIUM, LARGE, XLARGE), `compute_min_instances` and `compute_max_instances`.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `cost-estimation`: adds app pricing and the serverless catalog file.
- `bundle-diff`: `apps` joins the priced types.

## Impact

- Code:
  - the app model, priced type and cost-relevant fields;
  - `ServerlessTable` and its loader (optional for directory catalogs);
  - `CostEngine.EstimateApp`.
- Output: apps appear as priced rows. The `azure-triggers` golden file now prices its app instead of listing it as not priced.
- Out of scope:
  - stopped apps (DBUDoctor assumes a deployed app runs);
  - calibration of apps from history;
  - the SQL warehouses or serving endpoints an app calls.
