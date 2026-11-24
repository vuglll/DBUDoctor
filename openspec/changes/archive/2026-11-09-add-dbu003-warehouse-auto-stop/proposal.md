## Why

An idle SQL warehouse keeps billing until it stops. With `auto_stop_mins: 0` it never stops, and a long auto-stop bills for every idle stretch. Bundles often omit the field, and then the API default of 120 minutes applies. That is four times the 30 minutes this rule allows, and well above the UI defaults. This is the second "idle money" rule on the roadmap (release 0.4).

## What Changes

- Add DBU003, starting from its draft in `docs/rule-drafts.md`. It reports a SQL warehouse with `auto_stop_mins: 0` as a warning. When the effective auto-stop exceeds `rules.DBU003.max_minutes` (default 30), it reports info; an absent field takes the catalog default. Messages give the idle cost from the warehouse's rate per hour.
- Add `catalog/defaults.yaml`, the roadmap's prerequisite ("Needs first: `catalog/defaults.yaml`"). It holds platform defaults that apply when a bundle omits a field. It starts with `sql_warehouse.auto_stop_mins: 120`.

Facts, checked 2026-11-09:
- https://docs.databricks.com/api/workspace/warehouses/create, `auto_stop_mins`: "Must be == 0 or >= 10 mins", "0 indicates no autostop", "Defaults to 120 mins". Bundles create warehouses through this API, so 120 is the default DBUDoctor uses.
- https://docs.databricks.com/aws/en/compute/sql-warehouse/create: the UI defaults to 45 minutes for pro and classic warehouses and 10 minutes for serverless. "Idle SQL warehouses continue to accumulate DBU and cloud instance charges until they are stopped."

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU003.
- `cost-estimation`: adds the platform-defaults catalog file.

## Impact

- Code:
  - `CatalogLoader` reads `defaults.yaml`. The file is optional for directory catalogs, so a custom catalog without it still loads; DBU003 then reports only explicit values.
  - A new rule in `DBUDoctor.Core/Rules`.
- Docs: `docs/rules/DBU003.md`, the README rules table and the regenerated agent skill.
- Out of scope:
  - warehouses created outside the bundle;
  - workspace policies that change the default;
  - the time a warehouse takes to stop after its idle threshold.
