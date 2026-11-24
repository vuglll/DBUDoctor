## ADDED Requirements

### Requirement: Calibration settings
The system SHALL support these settings, with these defaults:
- `calibration.enabled: false`
- `calibration.warehouse_id`: required when calibration is enabled
- `calibration.target`: the estimation target
- `calibration.window_days: 30`
- `calibration.min_runs: 5`
- `estimation.standard_mode_factor: 0.7`

#### Scenario: Enabled without a warehouse
- **WHEN** `calibration.enabled` is true and `calibration.warehouse_id` is not set
- **THEN** DBUDoctor exits with code 2 and asks for `calibration.warehouse_id`

#### Scenario: Disabled by default
- **WHEN** no calibration settings are given
- **THEN** DBUDoctor executes no SQL statements and behaves as before calibration existed
