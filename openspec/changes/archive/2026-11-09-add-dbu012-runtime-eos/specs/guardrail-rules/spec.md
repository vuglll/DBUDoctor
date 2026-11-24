## ADDED Requirements

### Requirement: DBU012 outdated or end-of-support runtime
The system SHALL report a warning for a job cluster, task cluster or all-purpose cluster whose `spark_version` LTS release has passed its end-of-support date in the catalog, or is older than every release in it. It SHALL report an info finding when that date is within `rules.DBU012.warn_days` (default 90). Dates SHALL be compared against an injectable reference date, which the CLI sets to the current UTC date.

#### Scenario: Runtime past end of support
- **WHEN** the reference date is 2026-11-08 and a job cluster uses `13.3.x-scala2.12` (end of support 2026-08-22)
- **THEN** DBU012 is reported as a warning

#### Scenario: Runtime close to end of support
- **WHEN** the reference date is 2026-11-08 and a job cluster uses `14.3.x-scala2.12` (end of support 2027-02-01, 85 days away)
- **THEN** DBU012 is reported as info

#### Scenario: Supported runtime
- **WHEN** a job cluster uses `16.4.x-scala2.12` (end of support 2028-05-09)
- **THEN** DBU012 is not reported

#### Scenario: Older than every listed release
- **WHEN** a job cluster uses `10.4.x-scala2.12`
- **THEN** DBU012 is reported as a warning

#### Scenario: Unlisted runtime
- **WHEN** a job cluster uses a non-LTS release such as `15.0.x-scala2.12`
- **THEN** DBU012 is not reported
