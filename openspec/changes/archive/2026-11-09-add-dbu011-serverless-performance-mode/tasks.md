## 1. Rule

- [x] 1.1 DBU011 with metadata and its schema entry (Dbu011Tests: default mode, standard or cost-optimized, explicit performance-optimized, unscheduled or classic)
- [x] 1.2 Fixture bundle `tests/Fixtures/bundles/dbu011-performance-mode` with golden Markdown and JSON

## 2. Docs

- [x] 2.1 `docs/rules/DBU011.md`, the README rules table, CHANGELOG
- [x] 2.2 Regenerate the agent skill (SkillTests)

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu011-serverless-performance-mode --strict` passing
