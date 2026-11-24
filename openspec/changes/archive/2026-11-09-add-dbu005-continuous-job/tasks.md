## 1. Rule

- [x] 1.1 DBU005 with metadata (Dbu005Tests: unpaused, omitted pause status, paused, serverless, unchanged continuous job stays hidden)
- [x] 1.2 Cost in the message from the job's rate (Dbu005Tests: $2.27 per hour, about $1,657 a month)
- [x] 1.3 Fixture bundle `tests/Fixtures/bundles/dbu005-continuous-jobs` with golden Markdown and JSON

## 2. Docs

- [x] 2.1 `docs/rules/DBU005.md`, the README rules table, CHANGELOG
- [x] 2.2 Regenerate the agent skill (SkillTests)

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu005-continuous-job --strict` passing
