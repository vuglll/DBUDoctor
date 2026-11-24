## 1. Rule

- [x] 1.1 DBU013 with metadata and its schema entry (Dbu013Tests: LARGE 2×, XLARGE 6×, MEDIUM or unset)
- [x] 1.2 Fixture bundle `tests/Fixtures/bundles/dbu013-apps` with golden Markdown and JSON

## 2. Docs

- [x] 2.1 `docs/rules/DBU013.md`, the README rules table, CHANGELOG
- [x] 2.2 Regenerate the agent skill (SkillTests)

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu013-large-app --strict` passing
