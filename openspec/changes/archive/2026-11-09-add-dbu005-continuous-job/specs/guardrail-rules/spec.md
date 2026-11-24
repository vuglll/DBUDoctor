## ADDED Requirements

### Requirement: DBU005 continuous job
The system SHALL report a warning for a job with a `continuous` block whose `pause_status` is not `PAUSED`; an omitted `pause_status` counts as unpaused. When the job's rate per hour is known, the message SHALL give it and the cost over a 730-hour month.

#### Scenario: Continuous job added
- **WHEN** the head adds an Azure job on a job cluster with a driver and 4 workers of `Standard_DS3_v2` (3.75 DBU per hour at 0.30 per DBU, VMs 1.145 per hour) and `continuous: { pause_status: UNPAUSED }`
- **THEN** DBU005 is reported as a warning, and the message says it runs around the clock at $2.27 per hour (3.75 × 0.30 + 1.145 = 2.27), about $1,657 a month (2.27 × 730)

#### Scenario: Pause status omitted
- **WHEN** a job has `continuous: {}`
- **THEN** DBU005 is reported

#### Scenario: Paused continuous job
- **WHEN** a job has `continuous: { pause_status: PAUSED }`
- **THEN** DBU005 is not reported

#### Scenario: Serverless continuous job
- **WHEN** a continuous job runs on serverless compute
- **THEN** DBU005 is reported, and the message says it runs around the clock (~730 h / month) without a price
