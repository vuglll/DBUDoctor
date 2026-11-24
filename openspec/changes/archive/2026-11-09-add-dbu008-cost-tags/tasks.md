## 1. Configuration

- [x] 1.1 List parameters in rule metadata, `RuleSettings`, the loader and `RuleContext.List` (Dbu008Tests: list settings are validated)
- [x] 1.2 Schema: every shipped rule and parameter under `rules` (ConfigTests: every rule and parameter is in the published schema)

## 2. Rule

- [x] 2.1 Tag and policy fields in the bundle model
- [x] 2.2 DBU008 with metadata (Dbu008Tests: missing tag, job cluster tags, off by default, serverless with a policy, serverless hint, each resource type's location)
- [x] 2.3 Fixture bundle `tests/Fixtures/bundles/dbu008-cost-tags` with golden Markdown and JSON

## 3. Docs

- [x] 3.1 `docs/rules/DBU008.md`, the README rules table, CHANGELOG
- [x] 3.2 Regenerate the agent skill, which now lists list parameters (SkillTests)

## 4. Verification

- [x] 4.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-dbu008-cost-tags --strict` passing
