## 1. Rule

- [x] 1.1 Cloud attributes in the bundle model and the classic-cluster helper
- [x] 1.2 DBU009 with metadata, `min_workers` and its schema entry (Dbu009Tests: large on-demand with saving, availability not set, small cluster, AWS without VM prices, pipeline cluster and threshold)
- [x] 1.3 Fixture bundle `tests/Fixtures/bundles/dbu009-on-demand` with golden Markdown and JSON

## 2. Docs

- [x] 2.1 `docs/rules/DBU009.md`, the README rules table, CHANGELOG
- [x] 2.2 Regenerate the agent skill (SkillTests)

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu009-large-on-demand --strict` passing
