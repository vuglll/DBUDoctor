## Why

Large batch clusters pinned to on-demand VMs pay full price for workers that would run as well on spot capacity. With spot with fallback, Databricks falls back to on-demand when spot is not available, so the risk is small and the saving large. This is the first "compute config" rule on the roadmap (release 0.6).

## What Changes

- Add DBU009, from its draft in `docs/rule-drafts.md`: info for a classic job or pipeline cluster with at least `rules.DBU009.min_workers` (8) workers (the maximum, for autoscale) whose availability is explicitly on-demand (`ON_DEMAND`, `ON_DEMAND_AZURE`, `ON_DEMAND_GCP`).
- Where the catalog has both pay-as-you-go and spot prices for the worker type (Azure today), the message gives the saving per hour: workers × (on-demand − spot).
- `aws_attributes`, `azure_attributes` and `gcp_attributes` (availability and `first_on_demand`) join the bundle model. A shared helper lists the classic clusters of jobs and pipelines, for this rule and DBU017.

Facts, checked 2026-11-09 at https://docs.databricks.com/api/workspace/clusters/create:
- availability values and defaults by cloud:
  - AWS: `SPOT`, `ON_DEMAND`, `SPOT_WITH_FALLBACK` (default `SPOT_WITH_FALLBACK`);
  - Azure: `SPOT_AZURE`, `ON_DEMAND_AZURE`, `SPOT_WITH_FALLBACK_AZURE` (default `ON_DEMAND_AZURE`);
  - GCP: `PREEMPTIBLE_GCP`, `ON_DEMAND_GCP`, `PREEMPTIBLE_WITH_FALLBACK_GCP` (default `ON_DEMAND_GCP`);
- `first_on_demand`: "The first `first_on_demand` nodes of the cluster will be placed on on-demand instances."

Needs first: nothing.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU009.

## Impact

- Code: cloud attributes in the bundle model, the classic-cluster helper and the rule.
- Docs: `docs/rules/DBU009.md`, the README rules table, the schema entry and the regenerated agent skill.
- Out of scope:
  - clusters that leave availability unset (on Azure and GCP that is on-demand by default, but the draft reports explicit choices only);
  - eviction rates and the cost of fallback;
  - all-purpose clusters.
