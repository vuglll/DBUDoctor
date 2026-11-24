## ADDED Requirements

### Requirement: Instance pool node types
For a classic cluster with `instance_pool_id` and no `node_type_id`, the system SHALL take the worker node type from `estimation.instance_pools.<pool id>`, and the driver node type from `driver_node_type_id`, else from `estimation.instance_pools.<driver_instance_pool_id>`, else the worker node type. It SHALL then price the cluster as any classic cluster and state the assumption. A pool without a mapping SHALL leave the rate Unknown, with a reason naming the setting to add.

#### Scenario: Mapped pool
- **WHEN** a job cluster has `instance_pool_id: pool-a`, 2 workers, and `estimation.instance_pools.pool-a` is `Standard_DS3_v2`
- **THEN** its rate is priced for a `Standard_DS3_v2` driver and 2 `Standard_DS3_v2` workers, with the assumption "node type `Standard_DS3_v2` from instance pool `pool-a`"

#### Scenario: Separate driver pool
- **WHEN** the cluster also has `driver_instance_pool_id: pool-b`, mapped to `Standard_DS4_v2`
- **THEN** the driver is priced as `Standard_DS4_v2` and the workers as `Standard_DS3_v2`

#### Scenario: Unmapped pool
- **WHEN** a job cluster has `instance_pool_id: pool-x` and no mapping for it
- **THEN** the rate is Unknown, and the reason says to set `estimation.instance_pools.pool-x` to the pool's node type
