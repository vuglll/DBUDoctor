## ADDED Requirements

### Requirement: DBU008 missing cost-attribution tags
When `rules.DBU008.required_tags` is non-empty, the system SHALL report a warning for each priced resource that lacks a required tag key in its tag locations: job `tags` and job cluster or task `custom_tags`; pipeline `tags` and cluster `custom_tags`; cluster `custom_tags`; SQL warehouse `tags.custom_tags`. It SHALL accept a `usage_policy_id` or `budget_policy_id` as attribution for serverless jobs and pipelines.

#### Scenario: Missing tag
- **WHEN** `required_tags` is `[team, cost_center]` and a job has only `team`
- **THEN** DBU008 is reported, naming `cost_center`

#### Scenario: Tag on the job cluster
- **WHEN** the job has `team` in `tags` and `cost_center` in its job cluster's `custom_tags`
- **THEN** DBU008 is not reported

#### Scenario: Serverless job with a usage policy
- **WHEN** a serverless job without tags sets `usage_policy_id`
- **THEN** DBU008 is not reported

#### Scenario: Rule off by default
- **WHEN** `required_tags` is empty or absent
- **THEN** DBU008 is not evaluated
