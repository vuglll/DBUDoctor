## Why

Lakebase computes bill only while active, so scale to zero is what keeps development and preview branches cheap. A compute that never scales to zero off production, or that keeps a large minimum, bills every hour for capacity it rarely uses. This is the last rule on the roadmap (release 0.8).

## What Changes

- Add DBU015, from its draft in `docs/rule-drafts.md`:
  - a warning for a compute with `no_suspension: true` on a branch that is not a production branch, with its monthly cost from Lakebase pricing;
  - info for any compute with `autoscaling_limit_min_cu` at `rules.DBU015.max_min_cu` (8) or more.
- Production branches are `production` plus `rules.DBU015.production_branches` (a list setting). The branch is read from `parent` (`projects/<p>/branches/<b>`).

Facts, checked 2026-11-10:
- https://docs.databricks.com/aws/en/oltp/projects/manage-projects: "Each Lakebase project is created with a root branch called `production`".
- https://docs.databricks.com/aws/en/oltp/projects/scale-to-zero: "You pay only for active compute time, not for idle periods".
- https://docs.databricks.com/aws/en/oltp/projects/manage-computes: `no_suspension: true` disables scale to zero; autoscaling runs from 0.5 to 64 CU.
- Field names from `databricks bundle schema` (CLI 1.24.0): `parent`, `no_suspension`, `autoscaling_limit_min_cu`.

Needs first: Lakebase pricing (`add-lakebase-pricing`), and the endpoint field names, verified as above.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU015.

## Impact

- Code: a new rule. The fixture catalog gains a Database Serverless price for Azure westeurope.
- Docs: `docs/rules/DBU015.md`, the README rules table, the schema entry and the regenerated agent skill.
- Out of scope: read replicas and high availability settings (`group`).
