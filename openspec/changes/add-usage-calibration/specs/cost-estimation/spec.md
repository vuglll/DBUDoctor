## ADDED Requirements

### Requirement: Calibration from system tables
When calibration is enabled, the system SHALL:
- read history only through read-only queries on `system.billing.usage` and `system.billing.list_prices`, over `calibration.window_days`;
- map bundle resources to deployed IDs using `bundle summary` output for `calibration.target`;
- validate every inlined ID against its expected format;
- cache query results per workspace, query and UTC day.

#### Scenario: Unmapped resource
- **WHEN** a resource in the diff has no deployed ID in the calibration target
- **THEN** it is estimated without history, and the report says it was not found in the target

#### Scenario: Cached results
- **WHEN** DBUDoctor runs twice on the same day for the same workspace and resources
- **THEN** the second run does not execute SQL statements

#### Scenario: Calibration failure
- **WHEN** the warehouse is unreachable or permissions are missing
- **THEN** DBUDoctor continues without history, prints a warning that names the missing permission, and does not change the exit code

### Requirement: Calibrated monthly estimates
When a priced resource has history, the system SHALL compute its monthly estimate from observed DBUs. The confidence SHALL be High when at least `calibration.min_runs` runs, or 7 days of usage, were observed in the window, and Low otherwise. For size changes, the system SHALL apply the configured scaling assumption to the observed figures.

#### Scenario: Calibrated job
- **WHEN** a classic job has 720 observed runs in the window, with a median of 6 DBUs per run at 0.15 per DBU, infrastructure is off, and its schedule changes from hourly to every 15 minutes
- **THEN** the estimate goes from 657 to 2,628 per month, with confidence High

#### Scenario: Calibrated warehouse resize
- **WHEN** a serverless warehouse used 2,520 DBUs in the last 30 days at `Small`, changes to `Medium`, and serverless SQL costs 0.70 per DBU
- **THEN** the observed DBUs scale to a 730-hour month (2,520 × 730 / 720 = 2,555), and the estimate goes from 1,788.50 to 3,577.00 per month (Medium is 24 DBU per hour against Small's 12), with confidence High

#### Scenario: Calibrated scale-up
- **WHEN** a calibrated job cluster with a driver and 4 workers of one node type changes to 8 workers of that type, under `constant_runtime`
- **THEN** the estimated DBUs per run scale by 9 / 5 = 1.8

#### Scenario: Azure infrastructure from observed runtime
- **WHEN** an Azure job uses a median of 6 DBUs per run on a cluster rated 3.75 DBU per hour, with VMs at 1.145 per hour for the whole cluster
- **THEN** each run is estimated at 1.6 hours, with a VM part of 1.832 per run alongside a DBU part of 1.80 at 0.30 per DBU

#### Scenario: Thin history
- **WHEN** a job has only 3 observed runs in the window and `calibration.min_runs` is 5
- **THEN** its estimate uses the observed runs with confidence Low

### Requirement: Live list prices
When calibration is enabled and `system.billing.list_prices` returns a current price for a SKU, the system SHALL use that price as the list price for the SKU, apply configured discounts and surcharges on top, and name `system.billing.list_prices` as the source.

#### Scenario: Price from the workspace
- **WHEN** `system.billing.list_prices` returns a current price for the Jobs compute SKU that differs from the catalog
- **THEN** the report uses the workspace price, and the assumptions name `system.billing.list_prices`

### Requirement: Serverless estimates from history
The system SHALL estimate serverless jobs and pipelines from their observed DBUs per run. For jobs, it SHALL adjust for `performance_target` changes using `estimation.standard_mode_factor` (default 0.7), with confidence Low.

#### Scenario: Switch to standard mode
- **WHEN** a serverless job with 120 observed DBUs per month at 0.35 per DBU changes `performance_target` from absent to `STANDARD`, and the factor is 0.7
- **THEN** the estimate goes from 42.00 to 29.40 per month, with confidence Low

#### Scenario: Serverless job with history
- **WHEN** an unchanged serverless job has history and a schedule change doubles its runs per month
- **THEN** its estimate doubles, with the confidence its history supports

### Requirement: Node-type drift check
Where `system.compute.node_timeline` is readable, the system SHALL compare the observed DBUs per node-hour of each calibrated node type with the catalog, and SHALL print a warning when they differ by more than 10%. When the table is not readable, the system SHALL skip the check and say so in the assumptions.

#### Scenario: Catalog drift
- **WHEN** the catalog rates a node type at 0.75 DBU per hour and observed usage shows 0.90
- **THEN** DBUDoctor prints a warning naming the node type and both values, and suggests a catalog pull request

#### Scenario: Within tolerance
- **WHEN** observed usage shows 0.78 DBU per hour for the same node type
- **THEN** no warning is printed

#### Scenario: Compute tables not readable
- **WHEN** the query on `system.compute.node_timeline` fails for lack of permission
- **THEN** no drift warning is printed, the estimate is unchanged, and the assumptions say the drift check was skipped
