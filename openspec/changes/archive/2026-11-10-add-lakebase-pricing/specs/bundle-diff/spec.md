## MODIFIED Requirements

### Requirement: Typed diff of cost-relevant resources
The system SHALL compare base and head resources by resource type and resource key, and classify each one as added, removed, modified or unchanged. For each modified resource, it SHALL list every changed cost-relevant field with its path, old value and new value. The priced resource types SHALL be `apps`, `jobs`, `pipelines`, `clusters`, `postgres_endpoints` and `sql_warehouses`. Other resource types SHALL be listed as not priced.

#### Scenario: Field change
- **WHEN** `resources.sql_warehouses.bi_wh.cluster_size` changes from `Small` to `Medium`
- **THEN** `bi_wh` is modified, with one changed field: `cluster_size: Small → Medium`

#### Scenario: Non-cost change is ignored
- **WHEN** only a job's `description` or a task's `notebook_path` changes
- **THEN** the job is reported as unchanged for cost purposes

#### Scenario: Renamed resource key
- **WHEN** a resource key changes from `orders` to `orders_v2` with identical settings
- **THEN** the diff reports one removal and one addition, and the report notes a possible rename

#### Scenario: Unsupported resource type
- **WHEN** the head adds a `dashboards` resource
- **THEN** the report lists it under "not priced in this version", and it does not affect totals

#### Scenario: App size change
- **WHEN** `resources.apps.portal.compute_size` changes from `MEDIUM` to `LARGE`
- **THEN** `portal` is modified, with one changed field: `compute_size: MEDIUM → LARGE`

#### Scenario: Lakebase size change
- **WHEN** `resources.postgres_endpoints.primary.autoscaling_limit_min_cu` changes from 1 to 4
- **THEN** `primary` is modified, with one changed field: `autoscaling_limit_min_cu: 1 → 4`
