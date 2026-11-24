# configuration Specification

## Purpose

The `.dbudoctor.yml` settings, their command-line and environment overrides, and their validation: cloud, region and plan tier, price adjustments, estimation assumptions, rules, suppressions and merge policies.

## Requirements

### Requirement: Configuration file
The system SHALL read `.dbudoctor.yml` from the repository root, or from the file given by `--config`, and SHALL validate it against a published JSON Schema. When a key is unknown or a value is invalid, it SHALL exit with code 2 and name the offending path and line.

#### Scenario: Unknown key
- **WHEN** the file contains a misspelled top-level key `pricng:` on line 3
- **THEN** DBUDoctor exits with code 2 and reports the unknown key `pricng` at line 3

#### Scenario: Invalid commitment
- **WHEN** `pricing.infra.commitment` is `reserved_5y`
- **THEN** DBUDoctor exits with code 2 and lists the allowed values

#### Scenario: No file
- **WHEN** no configuration file exists
- **THEN** DBUDoctor runs with defaults

### Requirement: Precedence
The system SHALL resolve each setting from command-line flags first, then `DBUDOCTOR_*` environment variables, then the configuration file, then built-in defaults.

#### Scenario: Flag overrides file
- **WHEN** the file sets `target: staging` and the command line passes `--target prod`
- **THEN** the target is `prod`

### Requirement: Cloud and region
When `cloud` is not set, the system SHALL infer it from the resolved bundle's `workspace.host`, or from `DATABRICKS_HOST`: `*.azuredatabricks.net` means Azure, `*.gcp.databricks.com` means GCP, and any other host means AWS. It SHALL require `region` for prices that vary by region, and report those prices as Unknown when the region is missing.

#### Scenario: Azure workspace
- **WHEN** the workspace host is `adb-123.4.azuredatabricks.net` and `cloud` is unset
- **THEN** Azure prices are used

#### Scenario: Region missing
- **WHEN** the cloud is Azure and `region` is not set
- **THEN** the region-specific rates are Unknown, and the report asks for `region`

### Requirement: Defaults
The system SHALL use these defaults:
- `target`: the first target with `mode: production`
- `estimation.autoscale: max`
- `estimation.scaling: constant_runtime`
- `estimation.default_run_minutes`: not set
- `pricing.include_cloud_infra: auto`
- `pricing.infra.commitment: payg`
- `pricing.surcharge: 0`
- `pricing.tier: premium`
- currency: USD

#### Scenario: No production target
- **WHEN** no target has `mode: production` and `target` is not set
- **THEN** DBUDoctor exits with code 2 and asks for `--target`

### Requirement: Plan tier
The system SHALL price DBUs at the plan tier set by `pricing.tier`: `premium` (the default) or `enterprise`. Node-type DBU rates SHALL be the same for both tiers. Azure offers the Premium tier only, so on Azure `enterprise` SHALL make DBU rates Unknown, with a reason that names the setting.

#### Scenario: Enterprise on AWS
- **WHEN** the cloud is AWS and `pricing.tier` is `enterprise`
- **THEN** Jobs compute is priced at the catalog's Enterprise list price, and SKU overrides, discounts and surcharges apply to it as they do for Premium

#### Scenario: Enterprise on Azure
- **WHEN** the cloud is Azure and `pricing.tier` is `enterprise`
- **THEN** DBU rates are Unknown, and the reason says that Azure has the Premium tier only

#### Scenario: Invalid tier
- **WHEN** `pricing.tier` is `standard`
- **THEN** DBUDoctor exits with code 2 and lists the allowed values

### Requirement: List-valued rule settings
The system SHALL accept list-valued rule parameters declared by a rule, such as `rules.DBU008.required_tags`, as a list of strings. It SHALL reject a scalar where a list is expected, and an undeclared key, with exit code 2 and the file and line.

#### Scenario: Required tags
- **WHEN** `.dbudoctor.yml` sets `rules.DBU008.required_tags: [team, cost_center]`
- **THEN** DBU008 checks both keys

#### Scenario: Scalar instead of a list
- **WHEN** `.dbudoctor.yml` sets `rules.DBU008.required_tags: team`
- **THEN** DBUDoctor exits with code 2 and names `rules.DBU008.required_tags`

### Requirement: Instance pool setting
The configuration SHALL accept `estimation.instance_pools`, a map from instance pool ID to a node type name. Keys and values SHALL be non-empty strings; any other shape SHALL be a configuration error with exit code 2.

#### Scenario: Valid map
- **WHEN** `.dbudoctor.yml` sets `estimation: { instance_pools: { pool-a: Standard_DS3_v2 } }`
- **THEN** the setting maps `pool-a` to `Standard_DS3_v2`

#### Scenario: Wrong shape
- **WHEN** `estimation.instance_pools` is a list
- **THEN** DBUDoctor exits with code 2 and names `estimation.instance_pools`
