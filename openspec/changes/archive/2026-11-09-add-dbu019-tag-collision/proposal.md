## Why

Teams often tag compute with keys like `vendor` or `creator` for their own reports. Databricks already sets those keys as default tags, and billing renames the custom one with an `x_` prefix, so the report that groups by it quietly misses the cost. This is the last "hygiene" rule on the roadmap (release 0.5).

## What Changes

- Add DBU019, from its draft in `docs/rule-drafts.md`: info for each custom tag whose key matches a Databricks default tag key, ignoring case. The keys are `Vendor`, `ClusterId`, `ClusterName`, `Creator`, `RunName`, `JobId`, `SqlEndpointId`, `DatabricksInstancePoolId` and `DatabricksInstancePoolCreatorId`.
- It checks the same tag locations as DBU008: job `tags` and job and task cluster `custom_tags`; pipeline `tags` and cluster `custom_tags`; cluster `custom_tags`; warehouse `tags.custom_tags`.

Facts, checked 2026-11-09 at https://docs.databricks.com/aws/en/admin/account-settings/usage-detail-tags (and the Azure page, which matches):
- The default tag keys for compute, SQL warehouses and pools are the nine above.
- "the custom tag is automatically prefixed with `x_` during propagation", while "The default Databricks tag retains its original key name".

Needs first: nothing. It reuses the tag fields DBU008 added to the bundle model.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU019.

## Impact

- Code: a new rule in `DBUDoctor.Core/Rules`, and its schema entry.
- Docs: `docs/rules/DBU019.md`, the README rules table and the regenerated agent skill.
- Out of scope:
  - tags set by compute policies, which the bundle does not show (the docs say those collisions fail cluster start instead);
  - the `Name` tag, which is not in the default tag tables.
