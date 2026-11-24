## Context

Bundles (Databricks Asset Bundles, now documented as Declarative Automation Bundles) define jobs, pipelines, clusters and SQL warehouses in YAML. `databricks bundle validate --target <t> --output json` prints the fully resolved configuration for a target, with includes merged, variables substituted, and target overrides and presets applied. That JSON is the input for everything in this change.

Classic compute costs come in two parts: Databricks DBUs plus the cloud provider's VMs. Serverless compute costs come in one part: DBUs at a higher rate that includes the machines. DBU consumption depends on node type, worker count, Photon, runtime and frequency, and configuration shows only some of these. The design therefore separates what configuration can price exactly (an hourly rate) from what needs a duration or history (a monthly figure), and labels every number with how it was obtained.

On Azure, the VM half is often the larger half. For example, a Small classic SQL warehouse is about $2.64 per hour in DBUs plus $3.89 per hour in VMs. Azure publishes both kinds of price through the public Retail Prices API, so this change prices Azure completely and treats the other clouds as DBU-only until VM snapshots exist for them.

This is the foundation change: the engine plus three rules. History from system tables comes in `add-usage-calibration`. The remaining rules come one change at a time, as planned in `docs/roadmap.md`.

## Goals / Non-Goals

**Goals**

- Show the cost delta of a pull request per resource, with the assumptions visible.
- Never present a guess as a fact. Every monthly figure carries a confidence level.
- Price Azure classic compute in full (DBUs and VMs, per region, under the user's commitment model).
- Work with only `git` and the Databricks CLI.
- Ship as a single binary and a GitHub Action.
- Prove the rule engine with three rules, and make adding the next rule a small, repeatable change.
- Keep the price and DBU catalog open, so others can reuse it.

**Non-Goals (this change)**

- Calibration from system tables (`add-usage-calibration`).
- Rules beyond DBU001, DBU004 and DBU010 (`docs/roadmap.md`).
- Pricing Databricks Apps and Lakebase (roadmap), and Model Serving and Vector Search.
- Storage, egress and networking, including Azure managed disks and public IPs. Reports list these as not modeled.
- VM prices for AWS and GCP.
- Resolving bundle YAML without the CLI.
- Posting to GitLab, Azure DevOps or Bitbucket. The Markdown output works anywhere, but posting is GitHub-only.

## Architecture

```
git refs ─► worktree ─► databricks bundle validate -o json ─► BundleModel (base, head)
                                                                  │
                                         ResourceDiff ◄───────────┘
                                              │
    Catalog (embedded) ───────┐               ▼
    Azure Retail Prices (opt.)┼──► CostEngine ──► Estimates ─┐
                              │                              ├─► Report (console | md | json)
                              └──► RuleEngine ──► Findings ──┘        │
                                                                      ├─► PR comment / job summary
                                                                      └─► exit code (policy)
```

`add-usage-calibration` later adds `system.billing.*` as another input to the CostEngine.

Repository layout:

```
src/DBUDoctor.Cli          command line, composition root
src/DBUDoctor.Core         bundle model, diff, catalog, cost engine, rules, reporting
src/DBUDoctor.Databricks   CLI runner (later also the SQL Statement Execution client)
src/DBUDoctor.GitHub       comment upsert, job summary
catalog/                   prices, node types, warehouse sizes, not-modeled (YAML, embedded at build)
tools/CatalogGen           Azure Retail Prices API → catalog/prices/azure/*.yaml
action/                    GitHub Action (action.yml)
docs/rules/                one page per rule
docs/roadmap.md            release plan and rule order
skills/dbudoctor/          generated agent skill (SKILL.md + references), committed
tests/                     unit, golden-file and fixture bundles
```

Command line (mirrors `efdoctor analyze`):

```
dbudoctor diff [--target <name>] [--base <ref>] [--head <ref>]
               [--base-json <file> --head-json <file>]
               [--format console|markdown|json] [--output <file>]
               [--github-comment] [--report-existing] [--config <file>]
               [--refresh-prices]
dbudoctor skill export --dir <path> [--force]
```

## Decisions

### D1. Input is `bundle validate -o json` at both refs, not `bundle plan`

`bundle plan` compares the configuration with what is deployed. That reflects past deployments, not the pull request base. DBUDoctor needs base versus head, so it runs `bundle validate --target <t> --output json` in a temporary `git worktree` of the base ref and in the head checkout, then diffs the two documents.

- Rejected: parsing YAML directly. Includes, variable lookups, complex variables, target merges and presets would all need re-implementing.
- Rejected: `bundle plan`, because it has the wrong baseline.
- `--base-json` and `--head-json` accept pre-resolved JSON. This is the test seam, it lets advanced CI produce the JSON itself, and it is all that release 0.1 needs.
- When the installed CLI supports it, the hidden `--include-locations` flag adds a `__locations` map, which DBUDoctor uses to point annotations at a file and line. The flag is hidden and may change, so it is optional.

### D2. Estimate the production target

Development-mode targets pause schedules and triggers and prefix resource names, so their cost is meaningless. `target` defaults to the first target with `mode: production`, and it can be set explicitly.

### D3. .NET 10, Native AOT, no reflection

- Single self-contained binaries for linux-x64, linux-arm64, osx-arm64 and win-x64.
- System.Text.Json source generation and System.CommandLine 2.
- Plain `HttpClient` calls to the GitHub and Azure Retail Prices REST APIs. No SDKs that rely on reflection.
- All money and DBU arithmetic uses `decimal`, never `double`. In floating point, 12 × 0.70 is 8.3999…
- Also published as a `dotnet tool`.
- A PyPI wheel that wraps the binary, so Python teams can run `pip install dbudoctor`, is a parallel track on the roadmap.

### D4. Two kinds of number: rate and monthly

- **Rate:** cost per active hour, computed from configuration and the catalog. Deterministic.
- **Monthly:** rate × hours per run × runs per month. Needs both frequency and duration.

Every monthly figure carries a confidence level:

| Level | Meaning |
|---|---|
| High | Calibrated from observed history. Reserved for `add-usage-calibration`, and not produced by this change |
| Medium | Frequency comes from configuration, and duration comes from `estimation.default_run_minutes` or a continuous resource |
| Low | A classic/serverless comparison without VM prices |
| Unknown | Rate only. No monthly figure is shown |

Headline totals include High and Medium estimates only, and say how many changes they exclude. Until calibration ships, serverless jobs and pipelines, SQL warehouses, all-purpose clusters and event-triggered jobs show rates only. That is honest, and it is the reason calibration comes right after.

### D5. Scaling assumption

When compute size changes, runtime may change too.

- `constant_runtime` (default): DBUs scale with size and runtime stays the same. This is an upper bound for scale-ups and an optimistic bound for scale-downs.
- `linear_speedup`: cost per run stays constant when workers change.

The report names the assumption it used.

### D6. Price catalog

`catalog/` lives in the repository and is embedded in the binary at build time:

- `prices/<cloud>/<region>.yaml`: list $/DBU by product family and SKU, plus VM prices per node type and commitment column where available. Every entry has `as_of` and `source`.
- `node-types/<cloud>.yaml`: DBU per hour per instance type, and how Photon is priced for that cloud (see below).
- `sql-warehouses.yaml`: DBU per hour per cluster size.
- `not-modeled.yaml`: per cloud, the cost categories reports list as not modeled.

Later changes add more catalog files when the rules that need them ship: `defaults.yaml` (DBU002, DBU003), `runtimes.yaml` (DBU012) and `serverless.yaml` (Apps and Lakebase).

Every report prints the catalog `as_of` date and says "list-price estimate, not an invoice".

**Photon is priced differently per cloud**, so the catalog entry says which method applies (`dbu_count`, `multiplier`, `sku` or `none`). Azure's pricing page lists a separate "with Photon" DBU count per VM for Jobs compute (mostly 2.5× the standard count) and All-purpose compute (2.0×), so Azure stores those counts per node type (`dbu_count`), and the Photon SKUs carry their own $/DBU. Where a cloud publishes one multiplier per VM family, the catalog stores that; where it publishes Photon as its own SKU rate, the catalog stores that SKU instead.

**Seed values.** Verify all of these against the source before release.

AWS us-east-1, Premium, list prices checked October 2026:

| Product | $/DBU |
|---|---|
| Jobs compute (classic) | 0.15 |
| Jobs serverless | 0.35 |
| All-purpose (classic) | 0.55 |
| All-purpose serverless | 0.75 |
| SQL classic | 0.22 |
| SQL pro | 0.55 |
| SQL serverless | 0.70 |

Azure, Premium tier only. The Standard tier was retired on October 1, 2026, and remaining Standard workspaces were upgraded to Premium. These are third-party figures checked November 2026 with the region not stated; the generator replaces them with Retail Prices API values per region.

| Product | $/DBU |
|---|---|
| Jobs compute (classic) | 0.30 |
| Pipelines Core (classic) | 0.30 |
| All-purpose (classic) | 0.55 |
| SQL classic | 0.22 |
| SQL pro | 0.55 |
| Jobs serverless | 0.47 |
| SQL serverless | 0.70 |

SQL warehouse DBU per hour per cluster: 2X-Small 4, X-Small 6, Small 12, Medium 24, Large 40, X-Large 80, 2X-Large 144, 3X-Large 272, 4X-Large 528. 5X-Large (Beta) is not on the Azure pricing pages, but the Databricks pricing calculator data lists it at 1,042 (checked November 2026, with the same values as above for the other sizes).

### D7. Node-type DBU rates are data, not code

There is no known public API that gives DBU per hour per instance type, and the Azure Retail Prices API doesn't include it either. `node-types/*.yaml` is maintained from the published instance-type tables and from community pull requests. Azure's pricing page lists a DBU Count per VM: for example, DS3 v2 0.75, DS4 v2 1.5, DS5 v2 3.0, D4ds v4 0.75 and D8ds v4 1.5. `add-usage-calibration` adds a drift check against observed usage.

### D8. Findings are new-only by default

Each finding is identified by rule ID, bundle, resource type, resource key and field path. A finding that already exists in base is suppressed unless `--report-existing` is set. YAML comments are lost in the resolved JSON, so suppressions live in `.dbudoctor.yml`, and each one requires a reason.

### D9. One sticky comment per bundle and target

A hidden marker `<!-- dbudoctor:bundle=<name>;target=<target> -->` identifies the comment, which is updated in place on every push. If the token cannot write to the pull request (for example, a pull request from a fork), the report goes to `$GITHUB_STEP_SUMMARY` instead. Required permission: `pull-requests: write`.

### D10. Exit codes

- 0: no policy violated.
- 1: a configured policy was violated.
- 2: invalid input or an internal error.

This is the same convention as EFDoctor.

### D11. Azure is the reference cloud

- **Prices.** The Azure Retail Prices API (`https://prices.azure.com/api/retail/prices`) is public and needs no authentication. It covers `serviceName eq 'Azure Databricks'` (DBU meters) and `'Virtual Machines'` (VM meters), with pay-as-you-go, spot and reservation prices. Savings-plan prices need `api-version=2023-01-01-preview`, and filter values are case-sensitive in that version. USD is the official currency; other currencies are for reference only.
- **Generator.** `tools/CatalogGen` runs weekly in a scheduled workflow. It fetches Premium DBU meters and Linux VM meters, normalizes them into `catalog/prices/azure/<armRegionName>.yaml`, and opens a pull request so a human reviews every price change. A mapping table turns meter names into product families (jobs, all-purpose, SQL classic, pro and serverless, pipelines Core, Pro and Advanced, jobs serverless). The catalog has its own release rhythm, independent of rule releases.
- **Live refresh.** `--refresh-prices` queries the API at run time for the bundle's region, caches the result for the UTC day, and falls back to the embedded snapshot with a warning when the API is unreachable.
- **Tier.** The Azure catalog holds Premium only, because the Standard tier was retired on October 1, 2026.
- **DBU counts.** DBU counts per VM are not in the API, so they come from `node-types/azure.yaml` (D7).
- **Other clouds.** AWS and GCP are DBU-only. The AWS price list is a very large bulk file, so its VM snapshot is a parallel track on the roadmap.
- **AWS and GCP DBU prices and node types.** The Databricks pricing calculator loads public JSON files (`https://www.databricks.com/en-pricing-assets/data/pricing/AWS.json` and `GCP.json`), one row per instance type, workload and plan. `tools/CatalogGen` reads them too: list $/DBU per SKU for the Premium and Enterprise plans (classic SKUs are the same in every region; SQL pro, SQL serverless and serverless SKUs vary by region), and DBU per hour per instance type, with separate "(Photon)" rows (checked November 2026: AWS 2.9× for Jobs and 2.0× for All-purpose, GCP 2.5× and 2.0×), so all three clouds use the `dbu_count` Photon method. The files use display names for regions ("US East (N. Virginia)"), which a mapping table turns into region codes; GCP lists most regions under two spellings. The files are undocumented and may change, so generator contract tests and the weekly reviewed pull request guard them, as for Azure. Gov and SAP rows are left out.

### D12. Commitments, discounts and surcharges

- **Plan tier.** `pricing.tier` picks the list price column: `premium` (default) or `enterprise`. AWS and GCP publish both (for example, AWS Jobs compute $0.15 and $0.20); Azure has Premium only. DBU counts do not depend on the tier.
- **Effective price.** Effective $/DBU = list × (1 − discount + surcharge).
- **`pricing.discounts`.** Discounts by product family, plus `all` as a default for every DBU SKU. `all` models the Azure DBCU pre-purchase (up to 37% off pay-as-you-go DBU prices for 1 or 3 years). DBCUs draw down at each workload's list price, so a uniform percentage is exact, not an approximation. A family key overrides `all`.
- **`pricing.surcharge`.** A fraction of list DBU spend that is added on top. For example, the Azure Mission Critical add-on is 30% of list price.
- **`pricing.infra.commitment`.** Picks the VM price column: `payg` (default), `spot`, `reserved_1y`, `reserved_3y`, `savings_plan_1y` or `savings_plan_3y`.
- **What each commitment covers.** DBCUs never cover VMs, and VM commitments never cover serverless.

### D13. What is not modeled, and said so

Every report ends with the cost categories that the bundle's cloud bills and DBUDoctor doesn't model, taken from `not-modeled.yaml`:

- **Azure:** managed disks, blob storage, public IP addresses, network egress and NAT.
- **Every cloud:** storage.
- **Not visible from configuration:** idle VMs in instance pools, and VM time during cluster startup. Azure bills VMs from the Starting phase.

### D14. Rules ship one change at a time

The foundation ships three rules, chosen because together they show the cost story: DBU001 (all-purpose compute), DBU004 (continuous pipeline) and DBU010 (frequency jump). After this change is archived, each further rule follows the same flow as EFDoctor's rules:

1. Propose the rule as its own OpenSpec change. The change adds one requirement to `guardrail-rules`, with drafts ready in `docs/rule-drafts.md`.
2. Implement it with positive, negative and new-only tests, plus a `docs/rules/DBU0xx.md` page.
3. Archive the change.
4. Release two to four rules at a time.

Rule IDs are reserved in `docs/roadmap.md`, never reused, and may ship out of order, as EFDoctor's do.

Every rule declares metadata that feeds its docs page and the agent skill: its title, when it fires, how to fix it and when to suppress it. The build fails when any of these is missing, so the skill can't fall behind the rules.

### D15. An agent skill, generated from rule metadata (0.2)

The GitHub Action needs no skill; the skill is for the loop before the pull request. Coding agents increasingly write bundle changes themselves, and Databricks ships its own agent skills for bundles. A DBUDoctor skill tells the agent to run `dbudoctor diff` after editing bundle configuration and to fix findings before a human sees the PR. The same skill helps AI reviewers in CI, and handles "fix the DBUDoctor findings" requests after the comment.

- **Format.** The Agent Skills layout:
  - `skills/dbudoctor/SKILL.md`, with YAML front matter (`name`, and a `description` that says when to use it);
  - `references/rules.md`: fix and suppression guidance per rule;
  - `references/output.md`: JSON fields, confidence levels and exit codes.

  Agents load the references only when they need them.
- **Generated, never hand-edited.** The skill is built from rule metadata and the output schema. The generated folder is committed, so skill installers that read GitHub repositories work, and CI fails when the committed copy is stale.
- **Size budget.** SKILL.md stays under 200 lines; detail lives in the references.
- **Distribution.** The skill is committed in the repository, attached to each GitHub Release, and written by `dbudoctor skill export --dir <path>` (for example, `.claude/skills/dbudoctor` for Claude Code).
- **Timing.** It ships in 0.2, because 0.1 only reads pre-resolved JSON, so an agent couldn't run it against a working tree.
- **Guardrail, written into the skill.** Never weaken production configuration only to silence a finding. Suggest a suppression with a reason instead, and only with the user's agreement.

## Cost-relevant fields

A change to any other field does not count as a cost change.

| Type | Fields |
|---|---|
| `jobs` | `job_clusters[*].new_cluster.*` (node types, `num_workers`, `autoscale`, `runtime_engine`, `spark_version`, `instance_pool_id`, `*_attributes.*`, `custom_tags`), `tasks[*].existing_cluster_id`, `tasks[*].job_cluster_key`, `tasks[*].new_cluster.*`, `tasks[*].environment_key`, `tasks[*].timeout_seconds`, `tasks[*].pipeline_task`, `schedule.*`, `trigger.*`, `continuous.*`, `performance_target`, `timeout_seconds`, `max_concurrent_runs`, `tags`, `budget_policy_id` |
| `pipelines` | `clusters[*].*` (including `autoscale.mode`), `continuous`, `development`, `edition`, `photon`, `serverless`, `budget_policy_id` |
| `clusters` | node types, `num_workers`, `autoscale`, `autotermination_minutes`, `runtime_engine`, `spark_version`, `instance_pool_id`, `*_attributes.*`, `custom_tags` |
| `sql_warehouses` | `cluster_size`, `min_num_clusters`, `max_num_clusters`, `auto_stop_mins`, `warehouse_type`, `enable_serverless_compute`, `spot_instance_policy`, `tags` |

The list includes fields that only later rules read (for example, `spark_version` for DBU012), so the diff doesn't need to change when those rules ship.

## Cost formulas

Notation: `p(sku)` is the effective $/DBU after D12, `d(node)` is the node type's DBU per hour, `m` is the Photon multiplier (1 when Photon is off), and `vm(node)` is the VM price per hour in the chosen commitment column.

**Classic job, pipeline or all-purpose cluster**

- `dbu_per_hour = m × (d(driver) + W × d(worker))`. `W` is `num_workers`. For autoscale, `W` is `max_workers` (`estimation.autoscale: max`, the default), `(min + max) / 2` (`mid`) or `min_workers` (`min`).
- `rate = dbu_per_hour × p(sku)`.
- With cloud infrastructure on, also add `vm(driver) + W × vm(worker)`. Infrastructure is on when `pricing.include_cloud_infra` is `true`, or when it is `auto` (the default) and the catalog has VM prices for the cloud and region. That means Azure for now.
- The SKU depends on context: a job cluster uses Jobs compute, `existing_cluster_id` uses all-purpose, and a pipeline cluster uses the pipeline edition (Core, Pro or Advanced).
- Serverless resources never get an infrastructure line.

**Monthly cost for classic jobs:** `rate × default_run_minutes / 60 × runs_per_month` when `estimation.default_run_minutes` is set, with confidence Medium. Otherwise Unknown.

**Runs per month**

| Trigger | Runs per month |
|---|---|
| `schedule.quartz_cron_expression` with `timezone_id` | fire times in the 365 days after the reference instant ÷ 12 |
| `schedule.pause_status: PAUSED` | 0 |
| `trigger.periodic` | (8,760 h ÷ interval in hours) ÷ 12 |
| `trigger.file_arrival`, `trigger.table_update` | Unknown (observed runs arrive with calibration) |
| `continuous` (job) | always on: 730 hours per month |
| none (API or `run_job_task` only) | Unknown |

The reference instant defaults to 1 January of the current UTC year and is injectable for tests. The cron evaluator must support Quartz syntax (the seconds field, `?`, `L`, `W` and `#`). It also exposes the shortest interval between runs, which DBU016 will use. A month is 730 hours.

**Serverless jobs and pipelines:** DBUs cannot be derived from configuration. The report shows the $/DBU and runs per month, with the monthly figure as Unknown ("needs history") until calibration.

**Continuous pipelines and jobs:** `monthly = rate × 730`, with confidence Medium. The autoscale assumption applies.

**SQL warehouses:** `rate = dbu(size) × min_num_clusters × p(type)`, plus VMs for classic and pro warehouses when infrastructure is on. The SKU comes from `warehouse_type` (CLASSIC or PRO) and `enable_serverless_compute`. Rate only until calibration.

**All-purpose clusters:** rate only until calibration.

**Classic ↔ serverless:** classic prices exclude cloud VMs, while serverless prices include them. On Azure the infrastructure line makes the comparison fair. Elsewhere the delta is marked Low and the report says so.

## Example `.dbudoctor.yml`

```yaml
target: prod
bundles: [etl, bi]
cloud: azure               # inferred from *.azuredatabricks.net when omitted
region: westeurope
currency: USD
pricing:
  discounts:               # fraction off list price
    all: 0.20              # e.g. Azure DBCU pre-purchase (up to 37%)
    sql: 0.25              # a family key overrides 'all'
  surcharge: 0.0           # e.g. 0.30 for the Azure Mission Critical add-on
  tier: premium            # premium | enterprise (AWS and GCP; Azure is Premium only)
  overrides:               # exact $/DBU by SKU
    PREMIUM_JOBS_COMPUTE: 0.24
  include_cloud_infra: auto  # auto | true | false
  infra:
    commitment: payg       # payg | spot | reserved_1y | reserved_3y | savings_plan_1y | savings_plan_3y
estimation:
  autoscale: max           # max | mid | min
  scaling: constant_runtime  # constant_runtime | linear_speedup
  default_run_minutes: 20  # turns schedules into monthly figures (confidence Medium)
rules:
  DBU010: { factor: 4, min_runs: 100 }
suppressions:
  - rule: DBU001
    resource: jobs.legacy_*
    reason: Moving off the shared cluster in Q1 (JIRA-123)
report:
  comment_on_no_change: false
fail_on:
  monthly_increase: 500
  percent_increase: 25
  severity: error          # only findings raised to 'error' fail the build
  rules: [DBU004]
  unknown_cost: false
```

## Report example (Markdown, release 0.2, Azure)

Assumptions behind the numbers:

- Seed prices: Jobs compute and Pipelines Core at $0.30 per DBU, serverless SQL at $0.70.
- DS3 v2 nodes: 0.75 DBU per hour, plus $0.229 per hour for the VM.
- Job clusters: a driver plus 4 workers. The pipeline autoscales up to 2 workers.
- Runs last 20 minutes (`default_run_minutes`).

```markdown
<!-- dbudoctor:bundle=analytics;target=prod -->
### DBUDoctor · `analytics` → `prod`: **+$2,651 / month** (+480% on changed resources)

| Resource | Change | Before | After | Δ / month | Confidence |
|---|---|---|---|---|---|
| job `orders_sync` | schedule hourly → every 15 min | $552 | $2,209 | **+$1,657** | Medium · 20-min runs (configured) |
| pipeline `events` | added, continuous | — | $994 | **+$994** | Medium · runs all month |

**Rate changes** (no monthly figure until calibration)
- sql_warehouse `bi_wh`: size `Small` → `Medium`, $8.40 → $16.80 per active hour

**New findings**
- warning · DBU004 · pipeline `events` is continuous: it runs around the clock (~730 h / month).
- warning · DBU010 · job `orders_sync` runs 4.0× as often (730 → 2,920 runs / month).
- warning · DBU001 · job `report_refresh`, task `refresh` runs on all-purpose cluster `0601-…`. Jobs compute costs about 55% of the all-purpose DBU rate here.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, pay-as-you-go VMs, catalog 2026-11-08.
- Scaling: constant runtime. Autoscale: at max.
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress.
List-price estimate. Not an invoice.
```

## Risks / Trade-offs

- **Wrong numbers destroy trust.** Mitigations: confidence labels, visible assumptions, rate-only rows instead of guesses, a "not modeled" footer, "not an invoice", and golden-file tests.
- **The first releases show many rate-only rows.** That is deliberate. Calibration (0.3) is next, so the gap is short.
- **Catalog goes stale.** Mitigations: `as_of` on every entry, and the weekly Azure generator with human review.
- **The Retail Prices API changes meter names or shape.** Mitigations: generator contract tests, and generator output that always goes through a reviewed pull request.
- **CLI output changes** (the JSON shape, hidden flags). Mitigations: a minimum supported version, and contract tests against several CLI versions in CI.
- **Databricks adds cost to Genie Data Review.** Mitigations: an open catalog, vendor neutrality, Azure-complete pricing, and shipping fast.
- **Noise.** Mitigations: new-only findings, thresholds and suppressions with reasons.
- **Secrets.** Tokens are never logged, and they are masked in GitHub logs with `::add-mask::`.

## Migration Plan

Not applicable (new project). This change covers releases 0.1 and 0.2. `add-usage-calibration` is 0.3, and the rule releases from 0.4 on are planned in `docs/roadmap.md`.

## Open Questions

1. Name: DBUDoctor. "DBU" is Databricks' billing term, used here descriptively. Do a quick trademark check, then reserve the NuGet ID, the GitHub repo and the PyPI name. Keep "Databricks" out of the product name.
2. Is there an API for DBU per hour per node type, or for the Azure Photon DBU counts per VM family?
3. Does `bundle validate -o json` need workspace auth for every bundle? Checked with CLI v1.24.0 on `databricks/bundle-examples`: yes, it exits 1 without credentials, but it still prints the fully resolved configuration on standard output. 0.2 treats the exit code as a failure (exit 2, per the spec). Using the printed configuration when only authentication failed, or an offline YAML resolver, becomes its own change.
4. Which Retail Prices meter names map to each product family?
5. License: Apache-2.0 is proposed.
