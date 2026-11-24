## 1. Rule

- [x] 1.1 DBU015 with metadata, `max_min_cu`, `production_branches` and its schema entry (Dbu015Tests: dev always on with cost, production always on, configured production branch, high minimum, scale-to-zero dev)
- [x] 1.2 Fixture bundle `tests/Fixtures/bundles/dbu015-lakebase` with golden Markdown and JSON

## 2. Docs

- [x] 2.1 `docs/rules/DBU015.md`, the README rules table, CHANGELOG
- [x] 2.2 Regenerate the agent skill (SkillTests)

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu015-lakebase-sizing --strict` passing
