## 1. Catalog and clock

- [x] 1.1 `catalog/runtimes.yaml` with LTS dates 11.3 to 17.3, sourced and dated (Dbu012Tests: embedded catalog has the dates)
- [x] 1.2 `RuntimeTable` and its loader, optional for directory catalogs (CatalogTests: later files tolerated)
- [x] 1.3 `RuleContext.Today` through `BundleAnalyzer.Analyze(today:)`; the CLI passes the UTC date; golden tests pin the clock

## 2. Rule

- [x] 2.1 DBU012 with metadata and `warn_days` (Dbu012Tests: past, close, supported or unlisted, older than every release, all-purpose cluster, window setting)
- [x] 2.2 Schema entry for `rules.DBU012.warn_days` (ConfigTests: every rule and parameter is in the schema)
- [x] 2.3 Fixture bundle `tests/Fixtures/bundles/dbu012-runtimes` with golden Markdown and JSON

## 3. Docs

- [x] 3.1 `docs/rules/DBU012.md`, the README rules table, CHANGELOG
- [x] 3.2 Regenerate the agent skill (SkillTests)

## 4. Verification

- [x] 4.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu012-runtime-eos --strict` passing
