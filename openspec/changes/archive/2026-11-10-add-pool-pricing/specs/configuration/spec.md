## ADDED Requirements

### Requirement: Instance pool setting
The configuration SHALL accept `estimation.instance_pools`, a map from instance pool ID to a node type name. Keys and values SHALL be non-empty strings; any other shape SHALL be a configuration error with exit code 2.

#### Scenario: Valid map
- **WHEN** `.dbudoctor.yml` sets `estimation: { instance_pools: { pool-a: Standard_DS3_v2 } }`
- **THEN** the setting maps `pool-a` to `Standard_DS3_v2`

#### Scenario: Wrong shape
- **WHEN** `estimation.instance_pools` is a list
- **THEN** DBUDoctor exits with code 2 and names `estimation.instance_pools`
