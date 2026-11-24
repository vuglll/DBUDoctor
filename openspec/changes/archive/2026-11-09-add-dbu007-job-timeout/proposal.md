## Why

A job run without a timeout can hang for days: a stuck stream, a deadlocked driver, a query waiting on a lock. On classic compute it keeps billing its cluster until someone notices. A timeout is the cheapest guard against that. This is the first "hygiene" rule on the roadmap (release 0.5).

## What Changes

- Add DBU007, from its draft in `docs/rule-drafts.md`: info for a job with no `timeout_seconds` (absent or 0) on the job itself and on at least one of its tasks. A job-level timeout covers every task. Without one, every task needs its own.
- When the job's rate per hour is known (classic compute), the message gives it as the cost of every hour a stuck run keeps going.
- Continuous jobs are skipped, because their runs are meant to be long (DBU005 covers them).

Facts, checked 2026-11-09 at https://docs.databricks.com/api/workspace/jobs/create:
- job `timeout_seconds`: "An optional timeout applied to each run of this job. A value of `0` means no timeout." Default `0`.
- task `timeout_seconds`: the same for each run of the task. Default `0`.
- `health` rules on `RUN_DURATION_SECONDS` only notify; they do not stop a run, so they do not count as a timeout.

Needs first: nothing.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU007.

## Impact

- Code:
  - `timeout_seconds` on jobs and tasks in the bundle model (both already counted as cost-relevant fields);
  - a new rule.
- Docs: `docs/rules/DBU007.md`, the README rules table and the regenerated agent skill.
- Out of scope:
  - choosing a timeout value;
  - pipelines (`pipeline_task` runs inherit the job's timeout);
  - SQL statement timeouts.
