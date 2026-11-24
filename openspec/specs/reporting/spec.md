# reporting Specification

## Purpose

Renders the result as console, Markdown and JSON output and as a sticky pull request comment, and applies `fail_on` policies to set the exit code.

## Requirements

### Requirement: Output formats
The system SHALL render results as console text (the default), as Markdown (`--format markdown`) or as JSON (`--format json`, with a top-level `schemaVersion`). It SHALL write to standard output, or to the file given by `--output <file>`.

#### Scenario: JSON for other tools
- **WHEN** DBUDoctor runs with `--format json`
- **THEN** the output is a single JSON document containing `schemaVersion: 1`, totals, per-resource estimates with confidence and assumptions, rate changes, findings, and the costs not modeled

### Requirement: Report content
For each bundle and target, the system SHALL show:
- the headline monthly delta, and its percentage over the base total of changed resources;
- one row per added, removed or modified priced resource that has a monthly estimate, with the change summary, before, after, delta and confidence;
- rate-only changes, in a separate list;
- new findings;
- assumptions;
- the catalog date;
- the costs not modeled;
- the statement that figures are list-price estimates, not an invoice.

#### Scenario: No cost-relevant changes
- **WHEN** the diff has no changes to priced resources and no new findings
- **THEN** the report states that there are no cost-relevant changes

#### Scenario: Rate-only change
- **WHEN** a serverless SQL warehouse changes from `Small` to `Medium` and no monthly estimate exists
- **THEN** it appears under rate changes as 8.40 → 16.80 per active hour, and not in the headline

#### Scenario: Everything is new
- **WHEN** every changed resource is added in this pull request, so the base total is 0
- **THEN** the headline shows the delta without a percentage

### Requirement: Costs not modeled
The system SHALL end every report with the cost categories that the bundle's cloud bills but DBUDoctor does not model, taken from the catalog.

#### Scenario: Azure bundle
- **WHEN** the bundle's cloud is Azure
- **THEN** the report footer lists managed disks, blob storage, public IP addresses and network egress as not modeled

#### Scenario: Cloud without VM prices
- **WHEN** the bundle's cloud has no VM prices in the catalog
- **THEN** the footer also lists cloud VMs for classic compute as not modeled

### Requirement: Deterministic output
The system SHALL produce byte-identical Markdown and JSON for identical inputs and catalog. It SHALL order bundles, resource types, resource keys and findings by ordinal comparison.

#### Scenario: Re-run
- **WHEN** DBUDoctor runs twice on the same refs with `--refresh-prices` off
- **THEN** both Markdown outputs are identical

### Requirement: Money formatting
The system SHALL format amounts in the catalog currency. Values of 100 or more SHALL be rounded to whole units, and smaller values to two decimals, rounding midpoints away from zero. Totals SHALL be computed from unrounded values.

#### Scenario: Rounding
- **WHEN** one delta is 1,657.1 and another is 12.604
- **THEN** they render as `1,657` and `12.60`

### Requirement: Sticky pull request comment
With `--github-comment`, the system SHALL create one pull request comment per bundle and target, identified by a hidden marker. On later runs, it SHALL update that comment instead of creating a new one.

#### Scenario: Second push
- **WHEN** a pull request already has a DBUDoctor comment for `analytics`/`prod`
- **THEN** the next run edits that comment

#### Scenario: Fork without a write token
- **WHEN** the token cannot write comments (for example, on a pull request from a fork)
- **THEN** DBUDoctor writes the Markdown report to the GitHub job summary, and does not fail because of the missing permission

#### Scenario: Nothing to say
- **WHEN** there are no cost-relevant changes, `report.comment_on_no_change` is false, and no previous comment exists
- **THEN** no comment is created

### Requirement: Policy thresholds and exit codes
The system SHALL exit with code 0 when no policy is violated, 1 when a configured policy is violated, and 2 on invalid input or an internal error. Policies SHALL include:
- `fail_on.monthly_increase` (an amount);
- `fail_on.percent_increase`, which is skipped when the base total is 0;
- `fail_on.severity` (the lowest finding severity that fails);
- `fail_on.rules` (rule IDs);
- `fail_on.unknown_cost`.

#### Scenario: Budget threshold
- **WHEN** `fail_on.monthly_increase` is 500 and the headline delta is 2,651
- **THEN** DBUDoctor exits with code 1, and the report names the violated policy

#### Scenario: Only a Low-confidence increase
- **WHEN** the only increase above the threshold is a Low-confidence estimate
- **THEN** the threshold is not violated, because Low estimates are excluded from the headline

#### Scenario: Percentage undefined
- **WHEN** `fail_on.percent_increase` is 25 and every changed resource is new
- **THEN** the percentage policy is not evaluated, and the report says so

#### Scenario: Failing rule
- **WHEN** `fail_on.rules` contains DBU004 and the pull request makes a pipeline continuous
- **THEN** DBUDoctor exits with code 1
