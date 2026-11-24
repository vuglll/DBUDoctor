## 1. Rule

- [x] 1.1 DBU018 with metadata and its schema entry (Dbu018Tests: legacy, enhanced or unset, maintenance cluster)
- [x] 1.2 Fixture bundle `tests/Fixtures/bundles/dbu018-autoscaling` with golden Markdown and JSON

## 2. Docs

- [x] 2.1 `docs/rules/DBU018.md`, the README rules table, CHANGELOG
- [x] 2.2 Regenerate the agent skill (SkillTests)

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu018-legacy-autoscaling --strict` passing
