## Why

A serving endpoint without scale to zero keeps its provisioned concurrency running when no requests come in. In development targets, where endpoints sit idle most of the day, that is waste; in production it is often a deliberate latency choice. This rule makes the choice visible in the pull request (release 0.7).

## What Changes

- Model serving endpoints (`model_serving_endpoints`) join the bundle model as a rule-only type. They were already listed as not priced, and stay unpriced.
- Add DBU014, from its draft in `docs/rule-drafts.md`: a finding for a served entity (`served_entities`, or the older `served_models`) whose `scale_to_zero_enabled` is not `true`. It is a warning in a target with `mode: development` and info elsewhere. The message names the floor: `min_provisioned_concurrency`, or the `workload_size` lower bound.

Facts, checked 2026-11-09:
- https://docs.databricks.com/api/workspace/servingendpoints/create:
  - workload sizes "Small" (4 - 4 provisioned concurrency), "Medium" (8 - 16) and "Large" (16 - 64);
  - "If scale-to-zero is enabled, the lower bound of the provisioned concurrency for each workload size is 0";
  - `min_provisioned_concurrency` is "The minimum provisioned concurrency that the endpoint can scale down to".
- https://docs.databricks.com/aws/en/machine-learning/model-serving/custom-models:
  - scale to zero scales down "after 30 minutes of inactivity";
  - cold starts usually take 10 to 20 seconds;
  - it "should not be used for production workloads that require consistent uptime or guaranteed response times".

The docs do not state how idle concurrency is billed, so the message says "kept running" and gives no price.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU014.

## Impact

- Code: serving endpoints in the bundle model, and the rule.
- Docs: `docs/rules/DBU014.md`, the README rules table, the schema entry and the regenerated agent skill.
- Out of scope: pricing serving endpoints, and GPU workload types beyond naming the floor.
