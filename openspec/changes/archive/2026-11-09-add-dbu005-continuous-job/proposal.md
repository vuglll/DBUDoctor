## Why

A continuous job always has one run executing: a new run starts within a minute of the last one ending. Its compute therefore runs around the clock, about 730 hours a month, even when there is little to process. Switching a scheduled job to continuous is one of the largest cost jumps a one-line change can make. This is the third "idle money" rule on the roadmap (release 0.4).

## What Changes

- Add DBU005, from its draft in `docs/rule-drafts.md`: a warning for a job with a `continuous` block whose `pause_status` is not `PAUSED`. An omitted `pause_status` counts as unpaused.
- The message gives the job's rate per hour and its cost over 730 hours when the rate is known (classic compute). For serverless, the DBUs per hour need history.

Facts, checked 2026-11-09:
- https://docs.databricks.com/api/workspace/jobs/create:
  - `continuous` "will ensure that there is always one run executing";
  - `pause_status` "Defaults to UNPAUSED";
  - "Only one of `schedule` and `continuous` can be used."
- https://docs.databricks.com/aws/en/jobs/continuous: "A delay exists between a run finishing and a new run starting. This delay should be less than 60 seconds."

Needs first: nothing. The cost engine already prices unpaused continuous jobs at their rate × 730 (confidence Medium).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU005.

## Impact

- Code: a new rule in `DBUDoctor.Core/Rules`.
- Docs: `docs/rules/DBU005.md`, the README rules table and the regenerated agent skill.
- Out of scope:
  - how much of each continuous run is idle waiting for data;
  - the exponential backoff after failures, which lowers the run count but not the always-on intent.
