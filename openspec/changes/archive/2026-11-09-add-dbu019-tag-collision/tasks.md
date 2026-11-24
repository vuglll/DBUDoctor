## 1. Rule

- [x] 1.1 DBU019 with metadata and its schema entry (Dbu019Tests: colliding key, normal key, every tag location, existing collision stays hidden)
- [x] 1.2 Fixture bundle `tests/Fixtures/bundles/dbu019-tag-collisions` with golden Markdown and JSON

## 2. Docs

- [x] 2.1 `docs/rules/DBU019.md`, the README rules table, CHANGELOG
- [x] 2.2 Regenerate the agent skill (SkillTests)

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu019-tag-collision --strict` passing
