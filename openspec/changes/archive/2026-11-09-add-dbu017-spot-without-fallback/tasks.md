## 1. Rule

- [x] 1.1 DBU017 with metadata and its schema entry (Dbu017Tests: Azure spot, spot with fallback, AWS driver on spot, GCP preemptible)
- [x] 1.2 Fixture bundle `tests/Fixtures/bundles/dbu017-spot` with golden Markdown and JSON

## 2. Docs

- [x] 2.1 `docs/rules/DBU017.md`, the README rules table, CHANGELOG
- [x] 2.2 Regenerate the agent skill (SkillTests)

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu017-spot-without-fallback --strict` passing
