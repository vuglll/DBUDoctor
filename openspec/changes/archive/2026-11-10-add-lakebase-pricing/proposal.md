## Why

Lakebase Autoscaling computes bill per compute unit (CU) for every hour they are active. A bundle that adds an always-active compute, or raises its size, adds a monthly cost that DBUDoctor listed as "not priced". The roadmap prices Lakebase before DBU015 (release 0.8).

## What Changes

- `postgres_endpoints` becomes a priced resource type. Its cost-relevant fields are:
  - `autoscaling_limit_min_cu` and `autoscaling_limit_max_cu`;
  - `no_suspension`, `suspend_timeout_duration` and `disabled`;
  - `endpoint_type` and `group`.
- A new SKU, `PREMIUM_DATABASE_SERVERLESS_COMPUTE` (product family `database`). The catalog generator now maps:
  - Azure's "Premium Database Serverless Compute DBU" meter;
  - the pricing calculator's "Lakebase" rows for AWS and GCP.

  Both were ignored before.
- `catalog/serverless.yaml` gains the DBU per CU-hour by cloud: AWS 0.230, Azure 0.213. GCP is Unknown, because its calculator rows are per DBU without a CU rate.
- Pricing:
  - rate per active hour = min CU × DBU per CU × the Database Serverless price;
  - always active (`no_suspension: true`): the rate × 730, confidence Medium, with a ceiling at max CU;
  - with scale to zero, the active time is unknown, so the rate and the ceiling are shown without a monthly figure;
  - disabled computes cost 0.
- The JSON report gains `monthlyCeiling` per estimate (null when not applicable); additive, `schemaVersion` stays 1.

**Changed from the draft.** The draft discounted always-active baseline capacity by 25% ("Always-On" pricing). The only source for that figure is a secondary article; the Lakebase pricing and compute docs checked 2026-11-09 do not state it. DBUDoctor prices list rates without that discount until an official source exists.

Facts, checked 2026-11-09:
- https://docs.databricks.com/aws/en/oltp/projects/manage-computes:
  - a CU is "approximately 2 GB of RAM";
  - autoscaling runs from 0.5 to 64 CU, with "max - min ≤ 16 CU";
  - `no_suspension: true` disables scale to zero.
- https://docs.databricks.com/aws/en/oltp/projects/scale-to-zero: "You pay only for active compute time, not for idle periods".
- https://www.databricks.com/en-pricing-assets/data/pricing/AWS.json, Lakebase rows: a "Database Capacity Unit" at 0.230 DBU, with $/DBU by region (for example 0.45 in ap-south-1, $0.104 per CU-hour).
- https://learn.microsoft.com/en-us/azure/databricks/resources/pricing (from the roadmap research): 0.213 DBU per CU-hour on Azure.
- Field names from `databricks bundle schema` (CLI 1.24.0), `resources.PostgresEndpoint`: `autoscaling_limit_min_cu`, `autoscaling_limit_max_cu`, `no_suspension`, `parent`, `disabled`.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `cost-estimation`: adds Lakebase endpoint pricing and the ceiling.
- `bundle-diff`: `postgres_endpoints` joins the priced types.

## Impact

- Code:
  - the SKU and family;
  - both generator mappings;
  - the endpoint model and priced type;
  - `CostEngine.EstimatePostgresEndpoint`;
  - `MonthlyCeiling` and its JSON field.
- Catalog: regenerated with `tools/CatalogGen`, so every price sheet gains the Database Serverless price and today's prices.
- Out of scope:
  - Lakebase storage and point-in-time-restore storage;
  - read replicas beyond the primary compute;
  - calibration from history;
  - GCP.
