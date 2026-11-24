## ADDED Requirements

### Requirement: Databricks Apps
The system SHALL price an app as running all month: its compute size's DBU per hour × instances × 730 × the Interactive Serverless price, with confidence Medium and the assumption "running all month". When no compute size is set, it SHALL use MEDIUM. Sizes come from the catalog: MEDIUM 0.5, LARGE 1 and XLARGE 3 DBU per hour. Instances SHALL follow `estimation.autoscale` between `compute_min_instances` and `compute_max_instances`, and default to 1.

#### Scenario: Medium to Large
- **WHEN** an app changes from MEDIUM (0.5 DBU per hour) to LARGE (1 DBU per hour), and the Interactive Serverless price is 0.80 per DBU
- **THEN** the estimate goes from 292 to 584 per month (0.5 × 0.80 × 730 and 1 × 0.80 × 730), with confidence Medium

#### Scenario: Size not set
- **WHEN** an app is added without a compute size
- **THEN** it is priced as MEDIUM

#### Scenario: Several instances
- **WHEN** a LARGE app sets `compute_min_instances: 2` and `compute_max_instances: 3`, priced at max instances, and the price is 0.80
- **THEN** it is estimated at 1,752 per month (3 × 1 × 0.80 × 730)
