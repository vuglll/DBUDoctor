## Why

Databricks spend is decided in code review and discovered weeks later on the invoice. Jobs, pipelines, clusters and SQL warehouses are defined as code in bundles, yet a reviewer cannot see what a change costs. One-line edits pass review and add thousands per month: a cron moved from hourly to every 15 minutes, a warehouse from Small to Large, `continuous: true` on a pipeline, a task pointed at an all-purpose cluster.

Existing Databricks cost tools (DoiT SELECT, Revefi, Unravel, Seemore and others) analyze spend after it happens. Infracost proved the shift-left model for Terraform, and nothing equivalent exists for Databricks bundles. Databricks' own Genie Data Review action comments on pull requests, but its published checks cover schemas, lineage, deployed jobs, governance tags and metric definitions, not cost.

On Azure the gap is wider. Classic compute is billed twice, DBUs by Databricks and VMs by Azure, and the VM part can be larger than the DBU part, so a DBU-only view misleads. Azure also publishes its DBU and VM prices through a public, unauthenticated API, which makes it the easiest cloud to price completely.

## What Changes

This is the foundation change. It ships the engine and only the three rules that make the cost case; every other rule follows as its own change.

- New open-source CLI, DBUDoctor (`dbudoctor`), a sibling of EFDoctor. It resolves the bundle configuration at two git refs for one target and reports the cost change per resource.
- Cost model for classic job compute, serverless jobs, Lakeflow Spark Declarative Pipelines, all-purpose clusters and SQL warehouses. Prices come from a versioned, open price catalog with user overrides for negotiated discounts.
- Azure is the reference cloud. DBU and VM prices per region come from the Azure Retail Prices API, including Reserved VM, savings plan and spot prices, so classic compute is priced in full. AWS and GCP start with DBU prices only, for the Premium and Enterprise plans, generated from the data behind the Databricks pricing calculator.
- Monthly figures come from configuration: schedules, continuous resources and a configured run duration. Figures from history arrive in the follow-up change `add-usage-calibration`.
- A rule engine with three rules that make the cost case:
  - DBU001: a task runs on all-purpose compute;
  - DBU004: a pipeline runs continuously;
  - DBU010: a schedule runs sharply more often.

  The other rules ship as one OpenSpec change each, in the order set in `docs/roadmap.md`.
- Console, Markdown and JSON reports, plus a GitHub Action that keeps one sticky pull request comment up to date. Policy thresholds set the exit code.
- An agent skill (`SKILL.md`), generated from rule metadata and shipped from 0.2. It tells coding agents to run DBUDoctor after editing bundle configuration, so findings get fixed before a pull request is opened.
- A repository configuration file, `.dbudoctor.yml`.

Releases:
- 0.1: pre-resolved JSON in, Azure rates out, as console and Markdown output.
- 0.2: git and CLI resolution, the GitHub Action, the sticky comment and the agent skill.

## Capabilities

### New Capabilities

- `bundle-diff`: Resolve bundle configuration for a base ref and a head ref, then produce a typed diff of cost-relevant resources.
- `cost-estimation`: Price catalogs (including the Azure catalog from the Retail Prices API), run-frequency derivation, cost formulas per compute type, commitments and confidence levels.
- `guardrail-rules`: Rule engine, new-only reporting, suppressions and the first three rules.
- `reporting`: Output formats, the pull request comment, costs not modeled, policy thresholds and exit codes.
- `configuration`: The `.dbudoctor.yml` schema, defaults and precedence.
- `agent-skill`: The SKILL.md generated from rule metadata: its content, size budget and distribution.

### Modified Capabilities

None. This is the first change in a new repository.

## Impact

- New repository and solution. No existing code is affected.
- Runtime dependencies: `git` and the Databricks CLI (for `databricks bundle validate`).
- A scheduled workflow in the DBUDoctor repository refreshes the Azure catalog from the public Retail Prices API. Users need no Azure credentials.
- Distribution: Native AOT binaries on GitHub Releases, a `dotnet tool` package, a GitHub Action, and the agent skill (committed under `skills/dbudoctor/` and attached to each release).
- Out of scope for this change:
  - calibration from system tables (`add-usage-calibration`, release 0.3);
  - the other 16 rules, and Apps and Lakebase pricing (`docs/roadmap.md`, releases 0.4 to 0.8);
  - pricing Model Serving and Vector Search;
  - storage of any kind;
  - Azure managed disks, public IPs and egress;
  - comments on GitLab or Azure DevOps;
  - resolving bundle YAML without the Databricks CLI.
