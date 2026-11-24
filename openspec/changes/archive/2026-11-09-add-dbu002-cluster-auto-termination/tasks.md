## 1. Engine

- [x] 1.1 Per-finding severity in `RuleContext.Finding`, overridden by a configured `rules.<id>.severity` (RuleEngineTests: override still wins)
- [x] 1.2 New-only check compares severity: a finding above the base severity is new (RuleEngineTests: severity rises; same severity stays hidden)

## 2. Rule

- [x] 2.1 DBU002 with metadata (title, when it fires, fix, suppression) and the `max_minutes` parameter (Dbu002Tests: disabled, long, unset, short, configurable threshold, unknown rate)
- [x] 2.2 Idle cost in the message from the cluster's rate (Dbu002Tests: $3.21 per hour and $2,341 a month; $12.83 per 240-minute stretch)
- [x] 2.3 Fixture bundle `tests/Fixtures/bundles/dbu002-idle-clusters` with golden Markdown and JSON

## 3. Docs

- [x] 3.1 `docs/rules/DBU002.md` (why it matters, example, fix, when to suppress), the README rules table, CHANGELOG
- [x] 3.2 Regenerate the agent skill (SkillTests)

## 4. Verification

- [x] 4.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu002-cluster-auto-termination --strict` passing
