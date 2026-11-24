## Why

A cluster that takes its instances from an instance pool (`instance_pool_id`) has no `node_type_id`: the pool defines the instance type, and pools are workspace objects outside the bundle. DBUDoctor marks such clusters Unknown, so a bundle that runs on pools gets no rates or monthly figures at all. A user hit this on their first real bundle (2026-11-10), where every job cluster used `${var.cluster_pool_id}`.

## What Changes

- New setting `estimation.instance_pools`: a map from pool ID to the pool's node type, for example `0101-120000-pool-abc: Standard_DS3_v2`. Keys are the pool IDs as `bundle validate` resolves them, so a variable such as `${var.cluster_pool_id}` maps through its value.
- A cluster with `instance_pool_id` and no `node_type_id` uses the mapped node type for its workers, and for its driver unless `driver_node_type_id` is set; a `driver_instance_pool_id` maps the same way. DBUs and VMs are then priced as for any classic cluster, with the assumption "node type `X` from instance pool `id` (estimation.instance_pools)".
- An unmapped pool stays Unknown, and the reason now names the setting to add: "set `estimation.instance_pools.<id>` to the pool's node type".
- Idle pool instances remain listed under "not modeled" (they bill VMs, not DBUs); the pool's own spot or on-demand setting is not visible, so VMs use `pricing.infra.commitment` as for other clusters.

Not in this change: looking pools up through the workspace API (`/api/2.0/instance-pools/get`) when calibration credentials exist, which a later change can add.

Facts, checked 2026-11-10:
- https://docs.databricks.com/aws/en/compute/pool-best-practices: a cluster attached to a pool uses the pool's instance type; the driver can use a different pool (`driver_instance_pool_id`). Idle pool instances incur cloud provider costs but no DBU charges.
- Jobs and clusters API: `instance_pool_id` and `node_type_id` are mutually exclusive on a cluster spec.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `cost-estimation`: pool-backed clusters priced from configured node types.
- `configuration`: the `estimation.instance_pools` setting.

## Impact

- Code: `EstimationSettings.InstancePools`, the config loader and schema, `CostEngine.Rate`.
- Docs: README (example config and limitations), CHANGELOG.
