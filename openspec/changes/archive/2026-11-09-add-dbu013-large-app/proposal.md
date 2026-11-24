## Why

An app bills for its compute size for as long as it runs. LARGE costs twice MEDIUM and XLARGE six times, all month, whether or not anyone uses it. Sizing up "to be safe" is easy to miss in review. This rule follows `add-apps-pricing` (release 0.7).

## What Changes

- Add DBU013, from its draft in `docs/rule-drafts.md`: info for an app whose `compute_size` is larger than MEDIUM. The message states the DBU per hour per instance as a multiple of MEDIUM, and the monthly cost from app pricing.

Facts, checked 2026-11-09 at https://docs.databricks.com/aws/en/dev-tools/databricks-apps/compute-size: MEDIUM 0.5, LARGE 1 and XLARGE 3 DBU per hour. Sizes come from `catalog/serverless.yaml`.

Needs first: Apps pricing (`add-apps-pricing`), and the app field names, verified with `databricks bundle schema` (`compute_size`: MEDIUM, LARGE, XLARGE).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU013.

## Impact

- Code: a new rule.
- Docs: `docs/rules/DBU013.md`, the README rules table, the schema entry and the regenerated agent skill.
- Out of scope: the instance count (priced by app pricing, not flagged by this rule).
