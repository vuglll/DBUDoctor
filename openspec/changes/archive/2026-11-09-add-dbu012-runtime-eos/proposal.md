## Why

Clusters pinned to an old Databricks Runtime keep running long after it stops getting security fixes, and they miss engine improvements that make the same work cheaper. A pull request that adds or keeps such a pin is the moment to catch it. This is the third "hygiene" rule on the roadmap (release 0.5).

## What Changes

- Add `catalog/runtimes.yaml`, the roadmap's prerequisite: LTS end-of-support dates by `major.minor`, sourced and dated, from 11.3 to 17.3.
- Rules get an injectable "today" date, which the roadmap also lists as a prerequisite. The CLI passes the current UTC date; tests and golden files pin it. The existing reference instant (1 January, for runs per month) is unchanged.
- Add DBU012, from its draft in `docs/rule-drafts.md`. It checks job clusters, task clusters and all-purpose clusters:
  - a warning when the `spark_version` LTS release is past end of support, or older than every listed release;
  - info within `rules.DBU012.warn_days` (90) of the date.

Facts, checked 2026-11-09:
- https://docs.databricks.com/aws/en/release-notes/runtime/ gives the supported LTS releases with end-of-support dates: 17.3 → 2028-10-22, 16.4 → 2028-05-09, 15.4 → 2027-08-19, 14.3 → 2027-02-01.
- https://docs.databricks.com/aws/en/release-notes/runtime/eos gives 13.3 → 2026-08-22, 12.2 → 2026-03-01 and 11.3 → 2025-10-19, and says "Databricks Runtime 10.4 LTS and earlier have reached end-of-life".
- https://docs.databricks.com/aws/en/release-notes/runtime/databricks-runtime-ver: LTS end of support is three years after GA; non-LTS releases end six months after GA.

The draft's scenario dates match these pages.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU012.
- `cost-estimation`: adds the runtimes catalog file.

## Impact

- Code:
  - `RuntimeTable` in the catalog and its loader (optional for directory catalogs);
  - `RuleContext.Today`, set by the CLI and pinned in golden tests;
  - the rule.
- Docs: `docs/rules/DBU012.md`, the README rules table and the regenerated agent skill.
- Out of scope:
  - non-LTS releases;
  - runtimes 18 and later, whose `spark_version` format is not documented yet;
  - pipeline runtimes, which Databricks manages through `channel`.
