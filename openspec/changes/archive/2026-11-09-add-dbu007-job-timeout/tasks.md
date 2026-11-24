## 1. Rule

- [x] 1.1 `timeout_seconds` on jobs and tasks in the bundle model
- [x] 1.2 DBU007 with metadata (Dbu007Tests: no timeouts, task timeouts, job timeout, zero, continuous, serverless without a price, unchanged job stays hidden)
- [x] 1.3 Fixture bundle `tests/Fixtures/bundles/dbu007-job-timeouts` with golden Markdown and JSON

## 2. Docs

- [x] 2.1 `docs/rules/DBU007.md`, the README rules table, CHANGELOG
- [x] 2.2 Regenerate the agent skill (SkillTests)

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu007-job-timeout --strict` passing
