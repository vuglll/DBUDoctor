## ADDED Requirements

### Requirement: DBU013 large Databricks App
The system SHALL report an info finding for an app whose compute size is larger than MEDIUM, stating its DBU per hour as a multiple of MEDIUM, because an app is billed for its size for as long as it runs.

#### Scenario: Large app
- **WHEN** an app sets the LARGE compute size (1 DBU per hour)
- **THEN** DBU013 is reported, saying that it costs 2× MEDIUM

#### Scenario: XLarge app
- **WHEN** an app sets the XLARGE compute size (3 DBU per hour)
- **THEN** DBU013 is reported, saying that it costs 6× MEDIUM

#### Scenario: Medium or unset
- **WHEN** an app sets MEDIUM or no compute size
- **THEN** DBU013 is not reported
