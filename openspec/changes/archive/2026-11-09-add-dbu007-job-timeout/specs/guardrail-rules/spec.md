## ADDED Requirements

### Requirement: DBU007 job without a timeout
The system SHALL report an info finding for a job that has no `timeout_seconds` (absent or 0) on the job itself and on at least one of its tasks, except for continuous jobs. When the job's rate per hour is known, the message SHALL give it as the cost of each hour a stuck run keeps going.

#### Scenario: No timeouts
- **WHEN** neither the job nor any task sets `timeout_seconds`, and the job runs on an Azure job cluster with a driver and 4 workers of `Standard_DS3_v2` (2.27 per hour)
- **THEN** DBU007 is reported as info, and the message says a stuck run keeps billing $2.27 per hour

#### Scenario: Task timeouts present
- **WHEN** every task sets `timeout_seconds`
- **THEN** DBU007 is not reported

#### Scenario: Job timeout present
- **WHEN** the job sets `timeout_seconds: 3600` and its tasks set none
- **THEN** DBU007 is not reported

#### Scenario: Zero means none
- **WHEN** the job sets `timeout_seconds: 0` and one task sets none
- **THEN** DBU007 is reported

#### Scenario: Continuous job
- **WHEN** a continuous job sets no timeout
- **THEN** DBU007 is not reported
