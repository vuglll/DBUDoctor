## Why

Spot capacity is cheap until it is evicted. A cluster on spot without fallback can fail its run on an eviction or a capacity shortage, and the retry pays for the work again. The fallback variants keep most of the saving and finish the run. This is the second "compute config" rule on the roadmap (release 0.6).

## What Changes

- Add DBU017, from its draft in `docs/rule-drafts.md`: info for a classic job or pipeline cluster whose availability is `SPOT` (AWS), `SPOT_AZURE` or `PREEMPTIBLE_GCP`. The message suggests the fallback variant and `first_on_demand` of at least 1.
- When `first_on_demand` is 0, the message also says the driver runs on spot. That covers an explicit 0, or the AWS default.

Facts, checked 2026-11-09 at https://docs.databricks.com/api/workspace/clusters/create:
- the availability values per cloud;
- `first_on_demand`: "The first `first_on_demand` nodes of the cluster will be placed on on-demand instances". It defaults to 0 on AWS and 1 on Azure and GCP.

Needs first: nothing. It reuses DBU009's cloud attributes and classic-cluster helper.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU017.

## Impact

- Code: a new rule.
- Docs: `docs/rules/DBU017.md`, the README rules table, the schema entry and the regenerated agent skill.
- Out of scope:
  - eviction rates;
  - spot bid prices (`spot_bid_max_price`);
  - all-purpose clusters.
