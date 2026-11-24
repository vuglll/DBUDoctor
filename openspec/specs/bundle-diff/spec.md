# bundle-diff Specification

## Purpose

Compares the resolved bundle at two git refs and lists the added, removed and modified resources and their cost-relevant fields, so that cost and rules see exactly what a pull request changes.

## Requirements

### Requirement: Resolve configuration at two refs
The system SHALL obtain the resolved bundle configuration for a base ref and a head ref by running `databricks bundle validate --target <target> --output json` once per ref, and SHALL use only that output as the source of resource definitions.

#### Scenario: Pull request on GitHub
- **WHEN** DBUDoctor runs in a GitHub Actions `pull_request` event without `--base` or `--head`
- **THEN** it uses the event's base SHA as the base ref and its head SHA as the head ref

#### Scenario: Local run
- **WHEN** DBUDoctor runs outside CI without `--base`
- **THEN** it uses the merge-base of `HEAD` and the remote default branch as the base ref, and the current working tree as the head

#### Scenario: Working tree is untouched
- **WHEN** DBUDoctor resolves the base ref
- **THEN** it checks the ref out into a temporary `git worktree`, removes the worktree on exit (including on failure), and leaves the user's working tree and index unchanged

### Requirement: Accept pre-resolved JSON
The system SHALL accept `--base-json <file>` and `--head-json <file>` containing `bundle validate` output. When both are given, it SHALL NOT invoke git or the Databricks CLI.

#### Scenario: Offline comparison
- **WHEN** DBUDoctor runs with `--base-json base.json --head-json head.json`
- **THEN** it diffs the two documents without running `git` or `databricks`

### Requirement: Handle missing or failing inputs
The system SHALL exit with code 2 and a clear message when the Databricks CLI is missing or older than the minimum supported version, or when `bundle validate` fails for the head ref. It SHALL treat a base ref that has no bundle root as an empty configuration.

#### Scenario: CLI not installed
- **WHEN** the `databricks` executable is not on `PATH`
- **THEN** DBUDoctor exits with code 2 and prints the minimum supported version and the install command

#### Scenario: Head configuration invalid
- **WHEN** `bundle validate` fails for the head ref
- **THEN** DBUDoctor exits with code 2 and prints the CLI diagnostics from standard error

#### Scenario: New bundle in this pull request
- **WHEN** the base ref has no `databricks.yml` at the bundle root
- **THEN** the base configuration is empty, and every head resource is reported as added

### Requirement: Discover multiple bundles
The system SHALL diff every bundle listed in `bundles` in the configuration file. When that list is absent, it SHALL diff every directory that contains a `databricks.yml`, outside hidden and vendored directories. It SHALL report each bundle separately.

#### Scenario: Monorepo
- **WHEN** a repository has `etl/databricks.yml` and `bi/databricks.yml` and no `bundles` setting
- **THEN** DBUDoctor produces one diff and one report section per bundle

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

### Requirement: Source locations
The system SHALL request location information from the Databricks CLI when the installed version supports it, and SHALL attach a file and line to resources and changed fields. When location information is unavailable, it SHALL continue without locations.

#### Scenario: Locations unavailable
- **WHEN** the installed CLI rejects `--include-locations`
- **THEN** DBUDoctor retries without the flag, and the report omits file and line

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
