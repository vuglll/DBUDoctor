## ADDED Requirements

### Requirement: Lakebase Autoscaling endpoints
The system SHALL price a Lakebase endpoint per active hour as `autoscaling_limit_min_cu` × the catalog's DBU per CU × the Database Serverless price. With scale to zero disabled (`no_suspension: true`) it SHALL show that rate × 730 as the monthly estimate, confidence Medium, and the ceiling at `autoscaling_limit_max_cu` × 730. With scale to zero enabled, the monthly estimate SHALL be Unknown, with the same ceiling. A disabled endpoint SHALL cost 0.

#### Scenario: Always-active endpoint
- **WHEN** an AWS endpoint has min 2 CU, max 8 CU and `no_suspension: true`, the catalog has 0.230 DBU per CU, and Database Serverless costs 0.50 per DBU
- **THEN** the estimate is 167.90 per month (2 × 0.230 × 0.50 × 730), up to 671.60 (8 × 0.230 × 0.50 × 730), with confidence Medium

#### Scenario: Scale-to-zero endpoint
- **WHEN** the same endpoint keeps scale to zero enabled
- **THEN** the monthly estimate is Unknown, the rate is 0.23 per active hour, and the ceiling is 671.60

#### Scenario: Cloud without a CU rate
- **WHEN** an endpoint is on GCP, which has no DBU per CU in the catalog
- **THEN** its rate is Unknown, with a reason naming GCP
