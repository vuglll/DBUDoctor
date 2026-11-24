## Why

An all-purpose cluster that never auto-terminates keeps billing DBUs, and on Azure its VMs, while nobody uses it. A long idle window does the same for hours after the last command. This is the first "idle money" rule on the roadmap (release 0.4).

## What Changes

- Add DBU002, starting from its draft in `docs/rule-drafts.md`. It reports a `clusters` resource with:
  - `autotermination_minutes: 0` as a warning;
  - no `autotermination_minutes`, or one above `rules.DBU002.max_minutes` (default 60), as info.

  The message gives the idle cost: the cluster's rate per hour, and for a disabled timeout the rate × 730 hours.
- New-only reporting treats a finding whose severity rises over the base finding with the same identity as new. Without this, a cluster going from a 120-minute timeout (info) to `0` (warning) would be hidden as pre-existing, which is the change DBU002 exists to catch.

Field facts, checked 2026-11-09 at https://docs.databricks.com/api/workspace/clusters/create:
- Not set: "If not set, this cluster will not be automatically terminated."
- `0` explicitly disables automatic termination.
- Valid values run from 10 to 10,000 minutes.

The effective value of an unset field still depends on the bundle's deployment engine, so DBU002 reports it as info and asks for an explicit value.

Needs first: nothing (roadmap "Needs first" is "—"). The cluster rate comes from the existing cost engine.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guardrail-rules`: adds DBU002, and a scenario to "Report only what the pull request introduces" for findings whose severity rises.

## Impact

- Code: a new rule in `DBUDoctor.Core/Rules`, a per-finding severity in `RuleContext` (the configured severity still overrides it), and a severity comparison in the new-only check.
- Docs: `docs/rules/DBU002.md`, the README rules table and the regenerated agent skill.
- Out of scope:
  - job clusters, which terminate with their run;
  - SQL warehouses, which DBU003 covers;
  - cluster policies that fix the value outside the bundle.
