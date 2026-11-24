## ADDED Requirements

### Requirement: DBU003 SQL warehouse auto-stop
The system SHALL report DBU003 as a warning for a SQL warehouse with `auto_stop_mins: 0`, and as info when its effective auto-stop exceeds `rules.DBU003.max_minutes` (default 30). When the field is absent, the effective value SHALL be the catalog default, and when the catalog has none the warehouse SHALL NOT be reported. The message SHALL give the warehouse's rate per hour when it is known.

#### Scenario: Auto-stop disabled
- **WHEN** an Azure serverless `Small` warehouse (12 DBU per hour at 0.70 per DBU) sets `auto_stop_mins: 0`
- **THEN** DBU003 is reported as a warning, and the message says an idle warehouse bills $8.40 per hour (12 × 0.70), up to $6,132 a month (8.40 × 730)

#### Scenario: Default auto-stop
- **WHEN** the same warehouse omits `auto_stop_mins` and the catalog default is 120 minutes
- **THEN** DBU003 is reported as info, the message states the 120-minute default it used, and says each idle stretch bills up to $16.80 (2 hours × 8.40)

#### Scenario: Explicit long auto-stop
- **WHEN** a warehouse sets `auto_stop_mins: 45`
- **THEN** DBU003 is reported as info

#### Scenario: Short auto-stop
- **WHEN** a warehouse sets `auto_stop_mins: 10`
- **THEN** DBU003 is not reported

#### Scenario: No catalog default
- **WHEN** a warehouse omits `auto_stop_mins` and the catalog has no `defaults.yaml`
- **THEN** DBU003 is not reported
