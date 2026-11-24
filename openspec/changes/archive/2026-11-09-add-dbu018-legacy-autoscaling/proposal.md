## Why

Enhanced autoscaling sizes a pipeline's cluster to its workload and is the default for new pipelines. Pipelines that still set `autoscale.mode: LEGACY` keep workers longer than the work needs. This is the last "compute config" rule on the roadmap (release 0.6).

## What Changes

- Add DBU018, from its draft in `docs/rule-drafts.md`: info for a classic pipeline cluster with `autoscale.mode: LEGACY`. Two additions to the draft, both from the docs:
  - maintenance clusters are skipped, because they always use legacy autoscaling;
  - serverless pipelines are skipped, because enhanced autoscaling is always on there.

Facts, checked 2026-11-09 at https://docs.databricks.com/aws/en/ldp/auto-scaling:
- "Enhanced autoscaling is enabled by default for all new pipelines."
- The modes are `LEGACY` ("Use cluster autoscaling") and `ENHANCED`.
- "Legacy autoscaling is used for `maintenance` clusters."
- "For serverless pipelines, enhanced autoscaling is always on and cannot be disabled."

Needs first: nothing. `autoscale.mode` is already in the bundle model.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU018.

## Impact

- Code: a new rule.
- Docs: `docs/rules/DBU018.md`, the README rules table, the schema entry and the regenerated agent skill.
- Out of scope: pricing the difference, which depends on the workload's shape.
