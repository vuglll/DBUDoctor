## Why

A classic job cluster is created for each run and released when the run ends. A job that runs every few minutes on one therefore starts a cluster every few minutes, and the start-up is billed or simply wasted waiting. Serverless compute, an instance pool or a continuous job avoid most of that. This is the last "idle money" rule on the roadmap (release 0.4).

## What Changes

- Add DBU016, from its draft in `docs/rule-drafts.md`. It reports info for a job when both hold:
  - the job runs on a classic job cluster without `instance_pool_id`;
  - its schedule or periodic trigger fires more often than every `rules.DBU016.min_interval_minutes` (default 15), using the shortest interval between runs.
- The message names the interval, the runs per month (each starts a cluster), and suggests serverless, a pool or a continuous job.

Needs first: the shortest cron interval, built in 0.1 (`QuartzCron.ShortestInterval`, wall-clock based since the bundle-examples fixes). Periodic triggers use their configured interval.

Facts, checked 2026-11-09:
- https://docs.databricks.com/aws/en/compute/pool-index: "When cluster nodes are created using the idle instances, cluster start and auto-scaling times are reduced."
- The same page: "If your workload supports serverless compute, Databricks recommends using serverless compute instead of pools."

No Databricks page gives a start-up time for classic job clusters, so the message counts cluster starts and does not price them.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU016.

## Impact

- Code: a new rule in `DBUDoctor.Core/Rules`.
- Docs: `docs/rules/DBU016.md`, the README rules table and the regenerated agent skill.
- Out of scope:
  - the cost of start-up time, which no source quantifies;
  - jobs on all-purpose clusters, which DBU001 covers;
  - event-driven triggers, whose frequency is unknown.
