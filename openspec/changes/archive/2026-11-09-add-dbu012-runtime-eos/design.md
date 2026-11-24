## Context

DBU012 needs end-of-support dates and a "today" to compare them with. Rules so far saw only the reference instant (1 January of the current year), which run counting needs to be stable.

## Decisions

- **`catalog/runtimes.yaml`.** Header `as_of` and `source`, then `runtimes` keyed by `major.minor` with `end_of_support` (yyyy-MM-dd) and an optional per-entry `source`. Only LTS releases. The oldest entry doubles as the floor: anything older is past end of support, as the docs state for 10.4 LTS and earlier. The file is optional for directory catalogs, like `defaults.yaml`.
- **Version matching.** The rule reads `major.minor` from the start of `spark_version` (for example `13.3` from `13.3.x-photon-scala2.12`). Unlisted versions (non-LTS, newer, custom images) are not reported.
- **Today.** `RuleContext.Today` comes from `BundleAnalyzer.Analyze(today:)`. It defaults to the reference instant's date, which keeps the analyzer a pure function. The CLI passes the UTC date from its `TimeProvider`, and golden tests pin that provider to 2026-11-09.
- **Pricing assumption.** None: the rule reports dates, not costs.

## Risks / Trade-offs

- **Dates change when Databricks extends support.** The file is dated, sourced and reviewed like the price catalog.

AOT: no new dependency. Dates parse with `DateOnly.TryParseExact`; version keys with `System.Version`.
