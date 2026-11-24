## 1. Repository and build

- [x] 1.1 Create the solution: `src/DBUDoctor.Cli`, `src/DBUDoctor.Core`, `src/DBUDoctor.Databricks`, `src/DBUDoctor.GitHub`, `tools/CatalogGen`, `tests/`, `catalog/`, `action/`, `docs/rules/`
- [x] 1.2 Enable Native AOT and trimming, and treat AOT and trim warnings as errors
- [x] 1.3 CI: build, test and AOT-publish for linux-x64, linux-arm64, osx-arm64 and win-x64
- [x] 1.4 Release workflow: a GitHub Release with binaries and SHA-256 checksums, plus a NuGet `dotnet tool` package
- [x] 1.5 Add LICENSE (Apache-2.0 proposed), and a CONTRIBUTING file with "Cutting a release" (dev → main merge commit) and "Adding a rule" (the per-rule OpenSpec flow, D14) sections
- [x] 1.6 Copy `docs/roadmap.md` and `docs/rule-drafts.md` into the repository

## 2. Bundle diff

- [x] 2.1 Support `--base-json` and `--head-json` inputs (release 0.1)
- [x] 2.2 Build a source-generated model for jobs, job clusters, tasks, schedules, triggers, pipelines, clusters and SQL warehouses
- [x] 2.3 Diff by type and key over the cost-relevant fields (see design), count unsupported types, and flag possible renames
- [x] 2.4 Locate the Databricks CLI, check the minimum version, and print an install hint on failure (release 0.2)
- [x] 2.5 Resolve base and head refs (GitHub PR event, merge-base fallback), then create and always remove a temporary worktree
- [x] 2.6 Run `bundle validate --target --output json` per ref and capture the diagnostics on stderr
- [x] 2.7 Discover multiple bundle roots, and honor `bundles` from configuration
- [x] 2.8 Map `--include-locations` output to file and line, retrying without the flag when it is unsupported

## 3. Configuration

- [x] 3.1 Define the `.dbudoctor.yml` schema, including `pricing.discounts.all`, `pricing.surcharge` and `pricing.infra.commitment`, and publish it as JSON Schema
- [x] 3.2 Write the loader, with errors that name the path and line for unknown keys and invalid values
- [x] 3.3 Apply precedence: flags, then `DBUDOCTOR_*` environment variables, then the file, then defaults
- [x] 3.4 Infer the cloud from the workspace host, and require a region for region-specific prices

## 4. Price catalog

- [x] 4.1 Define the catalog schema (prices, node types, warehouse sizes, not-modeled; extensible for later files), write the loader, and embed the catalog at build time
- [x] 4.2 `tools/CatalogGen` for Azure. It must:
  - page through the Retail Prices API for `serviceName eq 'Azure Databricks'` and `'Virtual Machines'`;
  - use `priceType` Consumption and Reservation, plus savings plans through `api-version=2023-01-01-preview`;
  - keep Premium DBU meters and Linux VMs only;
  - write `catalog/prices/azure/<armRegionName>.yaml`.
- [x] 4.3 Map Retail Prices meter names to product families, with contract tests
- [x] 4.4 Scheduled weekly workflow that runs CatalogGen and opens a pull request with the price diff for human review
- [x] 4.5 Implement `--refresh-prices` (live Azure lookup for the bundle's region, cached per UTC day, embedded fallback with a warning)
- [x] 4.6 Generate AWS and GCP DBU prices per region, for the Premium and Enterprise plans, from the Databricks pricing calculator data (`AWS.json`, `GCP.json`) in `tools/CatalogGen`, with a region-name mapping, contract tests and the weekly refresh; add `pricing.tier`
- [x] 4.7 Node-type DBU tables. Azure comes from the pricing page's DBU Count column (DS3 v2 0.75, DS4 v2 1.5, DS5 v2 3.0, D4ds v4 0.75, D8ds v4 1.5, …), AWS and GCP (DBU per hour and Photon DBU counts per instance type) are generated from the same pricing calculator data
- [x] 4.8 SQL warehouse sizes (2X-Small 4 through 4X-Large 528, with 5X-Large from the pricing page) and `not-modeled.yaml`
- [x] 4.9 Add a catalog validation test: every entry has `as_of`, `source` and a positive value

## 5. Cost engine

- [x] 5.1 Build a Quartz cron evaluator with time zones (evaluate Quartz.NET `CronExpression` under AOT against writing our own parser), and expose the shortest interval between runs
- [x] 5.2 Compute runs per month for each trigger type, with an injectable reference instant
- [x] 5.3 Compute the effective price: overrides, discounts (family, then `all`) and surcharge
- [x] 5.4 Compute classic compute rates: driver, workers, autoscale assumption, Photon per cloud, and SKU by context
- [x] 5.5 Add the cloud infrastructure line (`auto`, `true` or `false`) using the commitment column
- [x] 5.6 Compute SQL warehouse rates by size, cluster count and type, with VMs for classic and pro when infrastructure is on
- [x] 5.7 Compute monthly estimates from configuration (default run duration, continuous resources), confidence levels and assumption lists
- [x] 5.8 Implement the scaling assumptions (`constant_runtime`, `linear_speedup`)
- [x] 5.9 Handle classic ↔ serverless comparisons, including the Low label when VM prices are missing

## 6. Guardrail rules

- [x] 6.1 Rule engine, finding identity, new-only baseline, `--report-existing`, and suppressions that require a reason
- [x] 6.2 DBU001: task on all-purpose compute, with the Jobs/all-purpose ratio from the catalog
- [x] 6.3 DBU004: continuous pipeline
- [x] 6.4 DBU010: sharp frequency increase
- [x] 6.5 Tests for each rule (positive, negative and new-only), and a `docs/rules/DBU0xx.md` page for each: why it matters, an example, the fix, and when to suppress it

## 7. Reporting

- [x] 7.1 Console renderer
- [x] 7.2 Markdown renderer: deterministic ordering, the sticky marker, rate-only rows, a collapsed assumptions section and the not-modeled footer
- [x] 7.3 JSON renderer with `schemaVersion: 1`, and a published JSON Schema for it
- [x] 7.4 Money formatting and rounding rules
- [x] 7.5 Policy evaluation and exit codes 0, 1 and 2, including skipping the percentage policy when the base total is 0

## 8. GitHub Action (release 0.2)

- [x] 8.1 A composite `action.yml` that downloads the binary for the runner's OS and architecture, verifies the checksum and runs it
- [x] 8.2 Create or update the sticky comment over REST, falling back to the job summary for forks and read-only tokens
- [x] 8.3 An example workflow with least-privilege permissions

## 9. Agent skill (release 0.2)

- [x] 9.1 Rule metadata: title, when it fires, how to fix, when to suppress. The build fails when a field is missing
- [x] 9.2 Generator for `skills/dbudoctor/SKILL.md`, `references/rules.md` and `references/output.md`, with golden-file tests and the 200-line budget check
- [x] 9.3 CI check that fails when the committed skill is stale
- [x] 9.4 `dbudoctor skill export --dir <path> [--force]`
- [x] 9.5 Attach the skill to each GitHub Release
- [ ] 9.6 Acceptance test with Claude Code and Codex on a fixture bundle: the agent edits the YAML, runs `dbudoctor diff`, and fixes a DBU001 finding before opening the PR
- [ ] 9.7 List the skill in public skills directories

## 10. Validation, docs and release

- [x] 10.1 Fixture bundles covering every trigger and the four priced resource types (Azure and AWS), with golden Markdown and JSON outputs
- [ ] 10.2 Run against public bundle repositories (starting with `databricks/bundle-examples`) and record the results
- [x] 10.3 README sections: quick start, how estimates are made, confidence levels, costs not modeled, limitations, and the roadmap link
- [x] 10.4 Release 0.1 (JSON in, console and Markdown out), then 0.2 (CLI resolution, the Action and the agent skill). Archive this change after 0.2
  - Released as one version, 0.2.0 (2026-11-09; 0.1 was folded in, never released on its own): a GitHub release and the `DBUDoctor` dotnet tool on nuget.org. Archive once 9.6, 9.7 and 10.2 are done
