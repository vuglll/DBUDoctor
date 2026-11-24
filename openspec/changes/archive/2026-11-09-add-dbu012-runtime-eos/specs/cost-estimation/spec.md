## ADDED Requirements

### Requirement: Runtime support catalog
The catalog SHALL hold Databricks Runtime LTS end-of-support dates in `runtimes.yaml`, keyed by `major.minor`, with an `as_of` date and a `source`. The embedded catalog SHALL include the file; a catalog directory without it SHALL still load, with no dates.

#### Scenario: Dates from the catalog
- **WHEN** the embedded catalog loads
- **THEN** 13.3 has end of support 2026-08-22, and the oldest listed release is 11.3

#### Scenario: Catalog without runtimes
- **WHEN** a catalog directory has no `runtimes.yaml`
- **THEN** the catalog loads, and DBU012 reports nothing
