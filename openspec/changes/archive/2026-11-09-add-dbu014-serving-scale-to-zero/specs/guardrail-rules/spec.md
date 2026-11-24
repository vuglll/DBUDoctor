## ADDED Requirements

### Requirement: DBU014 serving endpoint without scale-to-zero
The system SHALL report a finding for a served entity whose `scale_to_zero_enabled` is not `true`: a warning in a target whose `mode` is `development`, and an info finding in other targets. The message SHALL name the minimum provisioned concurrency kept running while the endpoint is idle: `min_provisioned_concurrency`, or the lower bound of the `workload_size` (Small 4, Medium 8, Large 16).

#### Scenario: Development endpoint always on
- **WHEN** a development target defines a served entity with `scale_to_zero_enabled: false`
- **THEN** DBU014 is reported as a warning

#### Scenario: Production endpoint with a warm floor
- **WHEN** a production target defines a served entity with `scale_to_zero_enabled: false` and `min_provisioned_concurrency: 8`
- **THEN** DBU014 is reported as info, naming a floor of 8

#### Scenario: Scale-to-zero on
- **WHEN** a served entity sets `scale_to_zero_enabled: true`
- **THEN** DBU014 is not reported
