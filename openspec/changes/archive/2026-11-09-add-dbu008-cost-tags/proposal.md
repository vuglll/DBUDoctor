## Why

Cost reports group spend by tags. A job, pipeline, cluster or warehouse without the organization's attribution tags shows up as "unallocated", and nobody owns its bill. Catching a missing tag in the pull request is far cheaper than chasing it in next month's invoice. This is the second "hygiene" rule on the roadmap (release 0.5).

## What Changes

- Add `rules.DBU008.required_tags`, a list setting: the roadmap's prerequisite ("Needs first: `rules.DBU008.required_tags` setting"). Rules can now declare list parameters next to numeric ones, validated the same way.
- Add DBU008, from its draft in `docs/rule-drafts.md`. It warns when a priced resource lacks a required tag key in the places that reach billing:
  - jobs: `tags` and job cluster or task `custom_tags`;
  - pipelines: `tags` and cluster `custom_tags` (pipeline `tags` added to the draft, since the bundle schema has them);
  - clusters: `custom_tags`;
  - SQL warehouses: `tags.custom_tags`.

  A serverless job or pipeline with a `usage_policy_id` (or the older `budget_policy_id`) counts as attributed. The rule is off while `required_tags` is empty.
- Fix the published configuration schema: it listed only DBU001, DBU004 and DBU010 under `rules`, so editors rejected settings for the 0.4 rules. Every shipped rule and parameter is now in it, and a test keeps it that way.

Facts, checked 2026-11-09:
- The bundle schema from Databricks CLI 1.24.0 (`databricks bundle schema`) gives:
  - jobs and pipelines: `tags`, `budget_policy_id` and `usage_policy_id`;
  - clusters and pipeline clusters: `custom_tags`;
  - SQL warehouses: `tags.custom_tags` as `key`/`value` pairs.
- https://docs.databricks.com/aws/en/admin/system-tables/billing marks `budget_policy_id` deprecated in favor of `usage_policy_id`.

Tag keys are compared exactly (case-sensitive), as billing groups them.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU008.
- `configuration`: adds list-valued rule settings.

## Impact

- Code:
  - tag and policy fields in the bundle model;
  - list parameters in rule metadata, configuration and the rule context;
  - the rule itself;
  - the schema fix.
- Docs: `docs/rules/DBU008.md`, the README rules table and the regenerated agent skill.
- Out of scope:
  - tag values;
  - tags inherited from cluster policies or workspace defaults, which the bundle does not show.
