## 1. Rule

- [x] 1.1 Serving endpoints in the bundle model (served entities and served models)
- [x] 1.2 DBU014 with metadata and its schema entry (Dbu014Tests: development warning, production floor, scale to zero on, unset counts as off)
- [x] 1.3 Fixture bundle `tests/Fixtures/bundles/dbu014-serving` with golden Markdown and JSON

## 2. Docs

- [x] 2.1 `docs/rules/DBU014.md`, the README rules table, CHANGELOG
- [x] 2.2 Regenerate the agent skill (SkillTests)

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu014-serving-scale-to-zero --strict` passing
