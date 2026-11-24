## 1. Catalog

- [x] 1.1 `catalog/defaults.yaml` with `sql_warehouse.auto_stop_mins: 120`, sourced and dated (CatalogTests: embedded catalog has the default)
- [x] 1.2 `CatalogLoader` reads it into `PriceCatalog.Defaults`; optional for directory catalogs; `WithPrices` keeps it (CatalogTests: missing file loads with no defaults; bad value fails with the file name)

## 2. Rule

- [x] 2.1 DBU003 with metadata and the `max_minutes` parameter (Dbu003Tests: disabled, default, explicit long, short, no catalog default, configurable threshold)
- [x] 2.2 Idle cost from the warehouse rate (Dbu003Tests: $8.40 per hour, $6,132 a month; $16.80 per 120-minute stretch)
- [x] 2.3 Fixture bundle `tests/Fixtures/bundles/dbu003-idle-warehouses` with golden Markdown and JSON

## 3. Docs

- [x] 3.1 `docs/rules/DBU003.md`, the README rules table, CHANGELOG
- [x] 3.2 Regenerate the agent skill (SkillTests)

## 4. Verification

- [x] 4.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu003-warehouse-auto-stop --strict` passing
