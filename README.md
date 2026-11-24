# DBUDoctor

DBUDoctor shows what a Databricks bundle change will cost, in the pull request, before it merges. It resolves the bundle at the pull request base and head, prices every changed job, pipeline, cluster and SQL warehouse, flags costly patterns, and keeps one comment on the pull request up to date.

```markdown
### DBUDoctor · `analytics` → `prod`: **+$2,651 / month** (+480% on changed resources)

| Resource | Change | Before | After | Δ / month | Confidence |
|---|---|---|---|---|---|
| job `orders_sync` | schedule hourly → every 15 min | $552 | $2,209 | **+$1,657** | Medium · 20-min runs (configured) |
| pipeline `events` | added, continuous | — | $994 | **+$994** | Medium · runs all month |

**Rate changes** (no monthly figure until calibration)
- sql_warehouse `bi_wh`: cluster_size `Small` → `Medium`, $8.40 → $16.80 per active hour

**New findings**
- warning · DBU004 · pipeline `events` is continuous: it runs around the clock (~730 h / month).
- warning · DBU010 · job `orders_sync` runs 4.0× as often (730 → 2,920 runs / month).
- warning · DBU001 · job `report_refresh`, task `refresh` runs on all-purpose cluster `0601-…`. Jobs compute costs about 55% of the all-purpose DBU rate here.
```

Azure is priced in full: DBUs and VMs, per region, from the public Azure Retail Prices API. AWS and GCP DBUs are priced per region and for the Premium or Enterprise plan, from the data behind the Databricks pricing calculator. AWS VMs come from the public AWS Price List (On-Demand and Standard No Upfront reservations). GCP VM prices are not in the catalog yet.

## Quick start

DBUDoctor needs `git` and the [Databricks CLI](https://docs.databricks.com/dev-tools/cli/install.html) v0.239.0 or later, which it uses to run `databricks bundle validate`.

Install it with pip (no .NET needed; also works in a notebook as `%pip install dbudoctor`):

```bash
pip install dbudoctor
```

Or as a .NET tool:

```bash
dotnet tool install --global DBUDoctor
```

Or download a binary for linux-x64, linux-arm64, osx-arm64 or win-x64 from [Releases](https://github.com/vuglll/DBUDoctor/releases) and check it against `SHA256SUMS`. The Linux binaries and wheels need glibc 2.34 or later (for example Ubuntu 22.04, and Databricks Runtime 13.3 LTS and later); there is no Intel macOS build yet.

Add `.dbudoctor.yml` to the repository root. `region` is the one setting most repositories need:

```yaml
region: westeurope            # cloud is inferred from workspace.host
estimation:
  default_run_minutes: 20     # turns schedules into monthly figures
  instance_pools:             # only if clusters use instance pools: pool ID → node type
    0101-120000-pool-abc: Standard_DS3_v2
fail_on:
  monthly_increase: 500       # exit code 1 above +$500 / month
```

Then, from a branch with bundle changes:

```bash
dbudoctor diff                      # compare with the merge-base of the default branch
dbudoctor diff --format markdown    # the pull request comment
dbudoctor diff --format json        # for scripts and agents
```

`bundle validate` resolves the bundle against the workspace, so the Databricks CLI needs credentials (a profile locally, a service principal in CI). With pre-resolved JSON, nothing else is needed:

```bash
dbudoctor diff --base-json base.json --head-json head.json
```

### In GitHub Actions

See [docs/examples/dbudoctor.yml](docs/examples/dbudoctor.yml). The Action downloads the binary for the runner, verifies its checksum, and keeps one comment per bundle and target. It needs `pull-requests: write`; on pull requests from forks it writes the job summary instead.

### For coding agents

`skills/dbudoctor/` is an agent skill that tells coding agents to run `dbudoctor diff` after editing bundle configuration and to fix findings before opening a pull request. Install it with:

```bash
dbudoctor skill export --dir .claude/skills/dbudoctor
```

## Command line

```
dbudoctor diff [--target <name>] [--base <ref>] [--head <ref>]
               [--base-json <file> --head-json <file>]
               [--format console|markdown|json] [--output <file>]
               [--github-comment] [--report-existing] [--config <file>]
               [--refresh-prices] [--var <name>=<value> ...]
dbudoctor skill export --dir <path> [--force]
```

`--var name=value` (repeatable) passes a bundle variable to `bundle validate` at both refs, for variables without a default such as `--var cluster_pool_id=0101-abc`; `BUNDLE_VAR_<name>` environment variables work too. In the GitHub Action, pass it through `args`.

Exit codes: `0` no policy violated, `1` a `fail_on` policy violated, `2` invalid input or an internal error.

Settings come from flags first, then `DBUDOCTOR_*` environment variables (`DBUDOCTOR_TARGET`, `DBUDOCTOR_REGION`, `DBUDOCTOR_CLOUD`, `DBUDOCTOR_FORMAT`, …), then `.dbudoctor.yml`, then defaults. The file's JSON Schema is [schemas/dbudoctor.schema.json](schemas/dbudoctor.schema.json); the JSON report's is [schemas/report.schema.json](schemas/report.schema.json).

## How estimates are made

- **Input.** `databricks bundle validate --target <t> --output json` at the base and head refs, for the first target with `mode: production` unless `--target` is given. Only cost-relevant fields count as changes: node types, workers, autoscale, Photon, schedules and triggers, warehouse size and type, and so on.
- **Rate per active hour.** For classic compute: `(driver DBU + workers × worker DBU) × $/DBU`, plus the driver and worker VMs when the catalog has VM prices (Azure and AWS). Autoscaling clusters are priced at `max_workers` by default (`estimation.autoscale`). Photon uses the cloud's method from the catalog: on Azure, a higher DBU count per VM. SQL warehouses use the DBUs per cluster size, and on Azure and AWS classic and pro warehouses add the VMs behind them.
- **Prices.** `$/DBU = list × (1 − discount + surcharge)`. On AWS and GCP, `pricing.tier: enterprise` uses Enterprise list prices instead of Premium (Azure has Premium only). `pricing.discounts.all` models an Azure DBCU pre-purchase, a family key such as `sql` overrides it, `pricing.surcharge` models add-ons such as Mission Critical, and `pricing.overrides` sets an exact SKU price. `pricing.infra.commitment` picks the VM column: `payg`, `spot`, `reserved_1y`, `reserved_3y`, `savings_plan_1y` or `savings_plan_3y`. AWS has `payg`, `reserved_1y` and `reserved_3y` only; the other columns make the VM part Unknown.
- **Runs per month.** Quartz cron schedules are evaluated in the job's time zone over the 365 days from 1 January, divided by 12; periodic triggers likewise. File-arrival and table-update triggers have no known frequency.
- **Monthly figure.** `rate × default_run_minutes / 60 × runs per month` for scheduled classic jobs, and `rate × 730` for continuous jobs and pipelines.
- **Scaling.** When compute size changes, `constant_runtime` (default) keeps run duration, and `linear_speedup` keeps cost per run.
- **Apps.** A Databricks App is priced as running all month: its compute size (MEDIUM 0.5, LARGE 1, XLARGE 3 DBU per hour, from `catalog/serverless.yaml`) × instances × 730 × the Interactive Serverless price (confidence Medium).
- **Lakebase.** A Lakebase Autoscaling compute costs min CU × DBU per CU × the Database Serverless price per active hour. Always active (`no_suspension: true`), that runs 730 hours (confidence Medium), up to the max CU; with scale to zero, the rate and the ceiling are shown. AWS and Azure only.
- **Calibration (opt-in).** With `calibration.enabled`, DBUDoctor reads the deployed resources' usage from `system.billing.usage` on a SQL warehouse you name, and prices observed DBUs per run (or per month) at the current `system.billing.list_prices`. See [docs/calibration.md](docs/calibration.md) for the grants, the cost and an example workflow.

The catalog is in [catalog/](catalog/). It is regenerated weekly and reviewed in a pull request: Azure from the Retail Prices API (50 regions), AWS (16 regions, 575 instance types) and GCP (15 regions, 157 machine types) from the Databricks pricing calculator data, and AWS VMs from the AWS Price List. `--refresh-prices` fetches today's Azure prices for your region instead. Every report prints the catalog date.

## Confidence levels

| Level | Meaning |
|---|---|
| High | Calibrated from observed history ([calibration](docs/calibration.md)): enough runs or days of usage in the window. |
| Medium | Frequency from configuration and a configured run duration, or a resource that runs all month. |
| Low | A comparison that is not like for like (classic compute without VM prices against serverless), thin history, or a serverless performance-mode change. |
| Unknown | No monthly figure. The report shows the hourly rate, or "needs history". |

Headline totals include High and Medium only, and say how many changes they exclude. Without history, serverless jobs and pipelines, SQL warehouses, all-purpose clusters and event-triggered jobs show rates: a rate is a fact, a guessed monthly figure is not. [Calibration](docs/calibration.md) turns them into monthly figures from what the deployed resources really used.

## Costs not modeled

Every report ends with what the bundle's cloud bills and DBUDoctor does not model:

- **Azure:** managed disks, blob storage, public IP addresses, network egress and NAT.
- **Every cloud:** storage.
- **AWS:** EBS volumes, S3 storage, data transfer and NAT gateways.
- **GCP:** cloud VMs for classic compute (DBUs only for now).
- **Not visible in configuration:** idle VMs in instance pools, and VM time during cluster startup.

Figures are list-price estimates, not an invoice.

## Limitations

- `bundle validate` needs workspace credentials; DBUDoctor does not resolve bundle YAML without the CLI.
- GCP: DBUs only; its VM prices, and AWS GovCloud, are not in the catalog yet. AWS spot and savings plan prices are not in the catalog either.
- The AWS and GCP prices come from the undocumented files behind the Databricks pricing calculator; the weekly refresh and its tests catch layout changes.
- Instance pools hide the node type: pool-backed clusters are Unknown until `estimation.instance_pools` maps the pool ID to its node type. The pool's spot or on-demand setting is not visible, so VMs use `pricing.infra.commitment`.
- Comments are posted on GitHub only; the Markdown works anywhere.
- Model Serving, Vector Search and Lakebase storage are not priced.

## Rules

| ID | Rule |
|---|---|
| [DBU001](docs/rules/DBU001.md) | Task runs on all-purpose compute |
| [DBU002](docs/rules/DBU002.md) | All-purpose cluster auto-termination |
| [DBU003](docs/rules/DBU003.md) | SQL warehouse auto-stop |
| [DBU004](docs/rules/DBU004.md) | Continuous pipeline |
| [DBU005](docs/rules/DBU005.md) | Continuous job |
| [DBU007](docs/rules/DBU007.md) | Job without a timeout |
| [DBU008](docs/rules/DBU008.md) | Missing cost-attribution tags |
| [DBU009](docs/rules/DBU009.md) | Large on-demand cluster |
| [DBU010](docs/rules/DBU010.md) | Sharp frequency increase |
| [DBU011](docs/rules/DBU011.md) | Scheduled serverless job in performance-optimized mode |
| [DBU012](docs/rules/DBU012.md) | Outdated or end-of-support runtime |
| [DBU013](docs/rules/DBU013.md) | Large Databricks App |
| [DBU014](docs/rules/DBU014.md) | Serving endpoint without scale-to-zero |
| [DBU015](docs/rules/DBU015.md) | Lakebase endpoint sizing |
| [DBU016](docs/rules/DBU016.md) | Classic job cluster on a short schedule |
| [DBU017](docs/rules/DBU017.md) | Spot without fallback |
| [DBU018](docs/rules/DBU018.md) | Pipeline cluster on legacy autoscaling |
| [DBU019](docs/rules/DBU019.md) | Custom tag collides with a default tag |

Findings are new-only: a finding that already exists in the base is reported only with `--report-existing`. Suppress a finding in `.dbudoctor.yml` with a reason:

```yaml
suppressions:
  - rule: DBU001
    resource: jobs.legacy_*
    reason: Moving off the shared cluster in Q1 (JIRA-123)
```

## Roadmap

Rules DBU001–DBU019 have shipped (DBU006 was retired), and calibration from system tables is experimental. Next: verifying calibration on real workspaces, and rules from DBU020. See [docs/roadmap.md](docs/roadmap.md).

## Contributing

Issues and pull requests are welcome; new rules make good first contributions. See [CONTRIBUTING.md](CONTRIBUTING.md) and the [code of conduct](CODE_OF_CONDUCT.md). Report security issues privately, as described in [SECURITY.md](SECURITY.md).

## License

Apache-2.0. "Databricks" and "DBU" are used descriptively; DBUDoctor is not affiliated with Databricks.
