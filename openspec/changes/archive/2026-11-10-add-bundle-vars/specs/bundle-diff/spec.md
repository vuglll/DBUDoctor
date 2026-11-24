## ADDED Requirements

### Requirement: Bundle variables
The system SHALL accept `--var name=value`, repeatable, and SHALL pass each one as `--var name=value` to every `databricks bundle validate` and `bundle summary` it runs. It SHALL exit with code 2 for a malformed `--var`, or for `--var` with pre-resolved JSON. When `bundle validate` reports a required variable without a value, the error SHALL name that variable and suggest `--var` or `BUNDLE_VAR_<name>`.

#### Scenario: Required variable passed
- **WHEN** a bundle declares `cluster_pool_id` without a default and the user runs `dbudoctor diff --var cluster_pool_id=0101-abc`
- **THEN** both refs are validated with `--var cluster_pool_id=0101-abc`

#### Scenario: Required variable missing
- **WHEN** `bundle validate` fails with "no value assigned to required variable cluster_pool_id"
- **THEN** the error suggests `--var cluster_pool_id=<value>` or `BUNDLE_VAR_cluster_pool_id`

#### Scenario: Malformed variable
- **WHEN** the user passes `--var cluster_pool_id`
- **THEN** DBUDoctor exits with code 2 and says the format is `name=value`
