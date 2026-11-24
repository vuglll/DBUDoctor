## ADDED Requirements

### Requirement: DBU018 pipeline cluster on legacy autoscaling
The system SHALL report an info finding for a classic pipeline cluster whose `autoscale.mode` is `LEGACY`, because enhanced autoscaling adds and removes workers as the workload changes and is the default for new pipelines. It SHALL skip `maintenance` clusters and serverless pipelines.

#### Scenario: Legacy autoscaling
- **WHEN** a pipeline cluster sets `autoscale.mode: LEGACY`
- **THEN** DBU018 is reported

#### Scenario: Enhanced or unset
- **WHEN** a pipeline cluster sets `autoscale.mode: ENHANCED` or omits `mode`
- **THEN** DBU018 is not reported

#### Scenario: Maintenance cluster
- **WHEN** a pipeline's `maintenance` cluster sets `autoscale.mode: LEGACY`
- **THEN** DBU018 is not reported
