## 1. Rule

- [x] 1.1 DBU016 with metadata and the `min_interval_minutes` parameter (Dbu016Tests: 5-minute cron, serverless, pooled, periodic 10 minutes, at the threshold, paused, configurable threshold)
- [x] 1.2 Message with the interval, cluster starts per month and the alternatives (Dbu016Tests: about 8,760 a month)
- [x] 1.3 Fixture bundle `tests/Fixtures/bundles/dbu016-short-schedules` with golden Markdown and JSON

## 2. Docs

- [x] 2.1 `docs/rules/DBU016.md`, the README rules table, CHANGELOG
- [x] 2.2 Regenerate the agent skill (SkillTests)

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu016-short-schedule --strict` passing
