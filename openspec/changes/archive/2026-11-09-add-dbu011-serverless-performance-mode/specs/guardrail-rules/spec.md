## ADDED Requirements

### Requirement: DBU011 scheduled serverless job in performance-optimized mode
The system SHALL report an info finding for a job with serverless tasks that runs on an unpaused schedule or periodic trigger with `performance_target` absent (the API default is `PERFORMANCE_OPTIMIZED`) or set to `PERFORMANCE_OPTIMIZED`. The message SHALL suggest `STANDARD` for batch work that tolerates slower startup.

#### Scenario: Nightly serverless job
- **WHEN** a serverless job has a daily schedule and no `performance_target`
- **THEN** DBU011 is reported

#### Scenario: Already standard
- **WHEN** a scheduled serverless job sets `performance_target: STANDARD`
- **THEN** DBU011 is not reported

#### Scenario: Unscheduled or classic
- **WHEN** a serverless job has no schedule, or a scheduled job runs only on job clusters
- **THEN** DBU011 is not reported
