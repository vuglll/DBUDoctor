## ADDED Requirements

### Requirement: DBU002 all-purpose cluster auto-termination
The system SHALL report DBU002 for a `clusters` resource as a warning when it sets `autotermination_minutes: 0`, and as info when it omits `autotermination_minutes` or sets it above `rules.DBU002.max_minutes` (default 60). The message SHALL give the cluster's rate per hour when it is known, and for a disabled timeout the rate over a 730-hour month.

#### Scenario: Auto-termination disabled
- **WHEN** an Azure cluster with a driver and 4 workers of `Standard_DS3_v2` (3.75 DBU per hour at 0.55 per DBU, VMs 1.145 per hour) sets `autotermination_minutes: 0`
- **THEN** DBU002 is reported as a warning, and the message says an idle cluster bills $3.21 per hour (3.75 × 0.55 + 1.145 = 3.2075), up to $2,341 a month (3.2075 × 730)

#### Scenario: Long idle window
- **WHEN** the same cluster sets `autotermination_minutes: 240`
- **THEN** DBU002 is reported as info, and the message says each idle stretch bills up to $12.83 (4 hours × 3.2075)

#### Scenario: Value not set
- **WHEN** a cluster omits `autotermination_minutes`
- **THEN** DBU002 is reported as info, and the message says that the effective default depends on the deployment engine, and that the Clusters API does not auto-terminate a cluster without the field

#### Scenario: Short idle window
- **WHEN** a cluster sets `autotermination_minutes: 30`
- **THEN** DBU002 is not reported

#### Scenario: Threshold is configurable
- **WHEN** `rules.DBU002.max_minutes` is 240 and a cluster sets `autotermination_minutes: 240`
- **THEN** DBU002 is not reported

## MODIFIED Requirements

### Requirement: Report only what the pull request introduces
The system SHALL identify a finding by rule ID, bundle, resource type, resource key and field path. It SHALL suppress findings whose identity also exists in the base configuration at the same or a higher severity, and SHALL report them when `--report-existing` is set. A finding whose severity is higher than the base finding with the same identity SHALL count as new.

#### Scenario: Pre-existing problem
- **WHEN** a continuous pipeline exists, unchanged, in both base and head
- **THEN** DBU004 is not reported for it

#### Scenario: Problem introduced
- **WHEN** the head changes a pipeline to `continuous: true`
- **THEN** DBU004 is reported for it

#### Scenario: Report existing on request
- **WHEN** the same unchanged continuous pipeline exists and `--report-existing` is set
- **THEN** DBU004 is reported for it, marked as existing

#### Scenario: Severity rises
- **WHEN** a cluster's `autotermination_minutes` changes from 120 (DBU002 info) to 0 (DBU002 warning)
- **THEN** DBU002 is reported as a new warning
