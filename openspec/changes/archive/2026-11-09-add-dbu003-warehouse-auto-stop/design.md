## Context

DBU003 needs the auto-stop that applies when a bundle omits `auto_stop_mins`. It is a platform default, not a price, so it does not belong in the price sheets. The roadmap already names `catalog/defaults.yaml` for this, and DBU002-style rules will need more defaults later.

## Goals / Non-Goals

**Goals:** one versioned place for platform defaults, with a source and a date like every catalog file.

**Non-Goals:** per-workspace or policy defaults, and defaults for fields no rule reads.

## Decisions

- **File shape.** Header `as_of` and `source`, then a mapping per resource kind:

  ```yaml
  as_of: 2026-11-09
  source: https://docs.databricks.com/api/workspace/warehouses/create
  sql_warehouse:
    auto_stop_mins: 120
  ```

  Entries may carry their own `source` and `as_of`, like other catalog entries.
- **API default, not UI default.** Bundles deploy through the SQL Warehouses API, whose documented default is 120 minutes. The UI defaults (45 for pro and classic, 10 for serverless) do not apply to bundle deployments.
- **Optional file.** The embedded catalog always ships it. A directory catalog without it loads with no defaults, and DBU003 skips warehouses that omit the field instead of guessing.
- **Pricing assumption.** The idle cost in DBU003's messages is the warehouse's existing rate per hour (confidence as in the cost engine). It adds no formula.

## Risks / Trade-offs

- **The documented default may change.** The file is dated and sourced, and the rule's message names the default it used.

AOT: no new dependency. The loader uses the existing YamlDotNet RepresentationModel path.
