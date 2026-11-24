## ADDED Requirements

### Requirement: DBU009 large on-demand cluster
The system SHALL report an info finding for a classic job or pipeline cluster whose worker count (the maximum, for autoscale) is at least `rules.DBU009.min_workers` (default 8) and whose availability is explicitly `ON_DEMAND`, `ON_DEMAND_AZURE` or `ON_DEMAND_GCP`. Where the catalog has on-demand and spot prices for the worker type, the message SHALL give the hourly saving of spot workers.

#### Scenario: Large on-demand cluster
- **WHEN** an Azure job cluster of `Standard_DS3_v2` autoscales to 16 workers with `azure_attributes.availability: ON_DEMAND_AZURE`, and the catalog prices the VM at 0.229 on demand and 0.05 spot
- **THEN** DBU009 is reported, suggesting `SPOT_WITH_FALLBACK_AZURE`, with a saving of up to $2.86 per hour (16 × (0.229 − 0.05))

#### Scenario: Availability not set
- **WHEN** a large job cluster does not set availability
- **THEN** DBU009 is not reported

#### Scenario: Small cluster
- **WHEN** an on-demand job cluster has 4 workers
- **THEN** DBU009 is not reported
