## Why

Serverless jobs default to performance-optimized mode, which starts within seconds and consumes more DBUs than standard mode on the same SKU. Scheduled batch work rarely needs that start-up speed. This is the first "serverless and apps" rule on the roadmap (release 0.7).

## What Changes

- Add DBU011, from its draft in `docs/rule-drafts.md`: info for a job with serverless tasks that runs on an unpaused schedule or periodic trigger, with `performance_target` absent or `PERFORMANCE_OPTIMIZED`. `STANDARD` and `COST_OPTIMIZED` are left alone.

Facts, checked 2026-11-09:
- https://docs.databricks.com/api/workspace/jobs/create:
  - `performance_target` takes `PERFORMANCE_OPTIMIZED`, `STANDARD` or `COST_OPTIMIZED`;
  - "Default: `PERFORMANCE_OPTIMIZED`".
- https://docs.databricks.com/aws/en/jobs/run-serverless-jobs:
  - "Both modes use the same SKU, but standard performance mode consumes fewer DBUs";
  - standard mode suits workloads that "tolerate slightly higher startup latency of 4 to 6 minutes";
  - the setting "only affects the serverless tasks within the job".

No source gives a DBU reduction figure, so the rule does not price the switch. With calibration, the existing `estimation.standard_mode_factor` (0.7) prices a `performance_target` change.

Needs first: nothing.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU011.

## Impact

- Code: a new rule.
- Docs: `docs/rules/DBU011.md`, the README rules table, the schema entry and the regenerated agent skill.
- Out of scope:
  - serverless pipelines, whose performance mode is a scheduler setting outside the bundle;
  - unscheduled and event-triggered jobs.
