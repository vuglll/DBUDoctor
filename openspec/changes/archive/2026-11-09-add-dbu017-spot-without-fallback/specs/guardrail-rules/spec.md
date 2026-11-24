## ADDED Requirements

### Requirement: DBU017 spot without fallback
The system SHALL report an info finding for a classic job or pipeline cluster whose availability is spot without fallback: `SPOT` on AWS, `SPOT_AZURE` on Azure or `PREEMPTIBLE_GCP` on GCP. The message SHALL suggest the fallback variant and `first_on_demand` of at least 1, and SHALL say when the driver runs on spot because `first_on_demand` is 0 (explicitly, or by the AWS default).

#### Scenario: Azure spot without fallback
- **WHEN** a job cluster sets `azure_attributes.availability: SPOT_AZURE`
- **THEN** DBU017 is reported, suggesting `SPOT_WITH_FALLBACK_AZURE`

#### Scenario: Spot with fallback
- **WHEN** a job cluster sets `azure_attributes.availability: SPOT_WITH_FALLBACK_AZURE`
- **THEN** DBU017 is not reported

#### Scenario: Driver on spot
- **WHEN** a job cluster sets `aws_attributes.availability: SPOT` without `first_on_demand`
- **THEN** DBU017 is reported, and the message says the driver runs on spot too
