## ADDED Requirements

### Requirement: Rule engine
The system SHALL evaluate guardrail rules against the head configuration of each bundle. Every finding SHALL have a rule ID, a severity (`info`, `warning` or `error`), a resource, a field path, a message and a documentation link. Configuration SHALL be able to enable or disable each rule and change its severity and parameters. Adding a rule SHALL NOT require changes to the engine, the diff or the reporters.

#### Scenario: Severity override
- **WHEN** `.dbudoctor.yml` sets `rules.DBU010.severity: error`
- **THEN** DBU010 findings are reported as errors

#### Scenario: Rule disabled
- **WHEN** `.dbudoctor.yml` sets `rules.DBU001.enabled: false`
- **THEN** DBU001 is not evaluated

### Requirement: Report only what the pull request introduces
The system SHALL identify a finding by rule ID, bundle, resource type, resource key and field path. It SHALL suppress findings whose identity also exists in the base configuration, and SHALL report them when `--report-existing` is set.

#### Scenario: Pre-existing problem
- **WHEN** a continuous pipeline exists, unchanged, in both base and head
- **THEN** DBU004 is not reported for it

#### Scenario: Problem introduced
- **WHEN** the head changes a pipeline to `continuous: true`
- **THEN** DBU004 is reported for it

#### Scenario: Report existing on request
- **WHEN** the same unchanged continuous pipeline exists and `--report-existing` is set
- **THEN** DBU004 is reported for it, marked as existing

### Requirement: Suppressions require a reason
The system SHALL accept suppressions in configuration as a rule ID, a resource selector (`<type>.<key>`, with wildcards allowed) and a non-empty `reason`. It SHALL reject a suppression without a reason with exit code 2.

#### Scenario: Valid suppression
- **WHEN** configuration suppresses DBU001 for `jobs.legacy_*` with the reason "Moving off the shared cluster in Q1"
- **THEN** DBU001 findings for matching jobs are not reported, and the report counts them as suppressed

#### Scenario: Missing reason
- **WHEN** a suppression has no `reason`
- **THEN** DBUDoctor exits with code 2 and names the suppression

### Requirement: DBU001 task runs on all-purpose compute
The system SHALL report a warning for each job task that sets `existing_cluster_id`. The message SHALL state the Jobs compute DBU rate as a percentage of the all-purpose rate, computed from the catalog for the bundle's cloud.

#### Scenario: Task on an existing cluster on Azure
- **WHEN** a job task on Azure sets `existing_cluster_id`, and the catalog lists Jobs compute at 0.30 and all-purpose at 0.55 per DBU
- **THEN** DBU001 is reported for that task, saying that Jobs compute costs about 55% of the all-purpose rate

#### Scenario: Same task on AWS
- **WHEN** the catalog lists Jobs compute at 0.15 and all-purpose at 0.55 per DBU
- **THEN** the message says about 27%

#### Scenario: Task on a job cluster or serverless
- **WHEN** a task uses `job_cluster_key`, `new_cluster` or `environment_key`
- **THEN** DBU001 is not reported

### Requirement: DBU004 continuous pipeline
The system SHALL report a warning for a pipeline with `continuous: true`, stating that it runs around the clock.

#### Scenario: Pipeline made continuous
- **WHEN** a pipeline changes from `continuous: false` to `continuous: true`
- **THEN** DBU004 is reported

#### Scenario: Triggered pipeline
- **WHEN** a pipeline omits `continuous` or sets it to `false`
- **THEN** DBU004 is not reported

### Requirement: DBU010 sharp frequency increase
The system SHALL report a warning when a job's runs per month in head are at least `rules.DBU010.factor` (default 4) times its runs per month in base, and the increase is at least `rules.DBU010.min_runs` (default 100) runs per month.

#### Scenario: Hourly to every 15 minutes
- **WHEN** a job's runs per month change from 730 to 2,920
- **THEN** DBU010 is reported with both cron expressions

#### Scenario: Small absolute change
- **WHEN** a job's runs per month change from 4 to 30
- **THEN** DBU010 is not reported
