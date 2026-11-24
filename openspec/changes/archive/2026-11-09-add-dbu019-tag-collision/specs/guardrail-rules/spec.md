## ADDED Requirements

### Requirement: DBU019 custom tag collides with a default tag
The system SHALL report an info finding for a custom tag whose key matches, case-insensitively, a Databricks default tag key: `Vendor`, `ClusterId`, `ClusterName`, `Creator`, `RunName`, `JobId`, `SqlEndpointId`, `DatabricksInstancePoolId` or `DatabricksInstancePoolCreatorId`. It SHALL check every tag location DBU008 checks. The message SHALL say that cloud billing renames the custom tag with an `x_` prefix.

#### Scenario: Colliding key
- **WHEN** a job cluster sets `custom_tags: { vendor: finance }`
- **THEN** DBU019 is reported, saying that the tag appears as `x_vendor` in cloud billing

#### Scenario: Normal key
- **WHEN** a job cluster sets `custom_tags: { team: finance }`
- **THEN** DBU019 is not reported

#### Scenario: Every tag location
- **WHEN** a job's `tags`, a cluster's `custom_tags`, a warehouse's `tags.custom_tags` and a pipeline's `tags` each use a default key
- **THEN** DBU019 is reported once for each of them
