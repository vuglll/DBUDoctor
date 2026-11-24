# DBUDoctor roadmap

The plan: ship the cost engine first, then add rules one OpenSpec change at a time, the way EFDoctor grew.

## Releases

| Release | Theme | Contents | OpenSpec change(s) |
|---|---|---|---|
| 0.1 | Engine | Pre-resolved JSON in, Azure catalog (DBUs and VMs), rates and configured monthly figures, console and Markdown out, DBU001, DBU004, DBU010 | `add-pr-cost-diff` |
| 0.2 | Launch | git and CLI resolution, GitHub Action, sticky PR comment, agent skill | `add-pr-cost-diff` (archive after release) |
| 0.3 | Real numbers | Calibration from system tables, list prices, drift check | `add-usage-calibration` |
| 0.4 | Idle money | DBU002, DBU003, DBU005, DBU016 | one change per rule |
| 0.5 | Hygiene | DBU007, DBU008, DBU012, DBU019 | one change per rule |
| 0.6 | Compute config | DBU009, DBU017, DBU018 | one change per rule |
| 0.7 | Serverless and Apps | DBU011, Apps pricing with DBU013, DBU014 | one change per rule, plus `add-apps-pricing` |
| 0.8 | Lakebase | Lakebase pricing with DBU015 | `add-lakebase-pricing`, then the rule |

The release numbers are a guide, not a promise. If a later rule is ready first, it can ship first: IDs are reserved here and may ship out of order, as EFDoctor's do.

## Next steps (as of 2026-11-10)

Rules DBU001–DBU019 are merged on `dev` (DBU006 is retired). The last release is 0.2.0, so 0.3–0.8 are in the **Unreleased** section of `CHANGELOG.md`.

### 1. Get a test workspace

Calibration and Lakebase pricing are tested only against recorded query results. They need a real workspace.

- **Databricks free trial (preferred).** 14 days, Premium, and you are account admin, which is needed to read `system.billing.usage`. Pick Azure to test the Azure pricing. Classic cluster VMs bill to your own cloud account: keep clusters small.
- **Databricks Free Edition.** Free, but serverless only (no classic pricing or node DBU check), and access to the billing tables is unconfirmed.

Sign in with `databricks auth login`, or set `DATABRICKS_HOST` and `DATABRICKS_TOKEN`. For calibration, also set `DBUDOCTOR_WAREHOUSE_ID` to a SQL warehouse ID (see `docs/calibration.md`).

### 2. Tasks that need the workspace

1. Deploy a small test bundle: a short job on a job cluster, a serverless job, a small SQL warehouse. Run them several times, then wait a few hours for billing rows to appear.
2. `add-usage-calibration` 2.1: check real `bundle summary --output json` output (more than one CLI version) against `DeployedIds`, and add it as a contract test.
3. `add-usage-calibration` 5.3: run calibration and compare its monthly figures with `system.billing.usage` (a shorter window than a month is fine at first).
4. `add-pr-cost-diff` 10.2: rerun `tools/bundle-examples/run.py` while signed in, so `bundle validate` resolves fully, and update `openspec/changes/add-pr-cost-diff/bundle-examples-run.md`.
5. `add-pr-cost-diff` 9.6: agent acceptance test (Claude Code and Codex fix a DBU001 finding on a fixture bundle).

### 3. Releases (decided by the maintainer)

- Release 0.3 or a combined release of everything unreleased, per "Cutting a release" in `CONTRIBUTING.md`. Then archive `add-usage-calibration` (task 5.4).
- `add-pr-cost-diff` 9.7 (list the skill in public directories) waits for the public repository. Archive that change after it.

### 4. After that

- Parallel tracks below (AWS savings plans, more CI systems).
- New rules from DBU020, from the backlog in `docs/research/2026-11-08-rules-and-azure.md`.
- Not done on purpose: Lakebase "Always-On" discount (no official source); Lakebase on GCP is Unknown (no per-CU rate); `spark_version` for non-LTS and 18+ runtimes is unverified.

### Setting up another computer

- .NET 10 SDK, the OpenSpec CLI, `gh` (signed in), the Databricks CLI, and Python 3.10+ for `tools/bundle-examples`.
- `git clone`, `git checkout dev`, then `dotnet test DBUDoctor.slnx`.
- Work on a branch from `dev`, open the pull request against `dev`, squash-merge after CI is green.

## Rules in order

| ID | Rule | Release | Needs first |
|---|---|---|---|
| DBU001 | Task runs on all-purpose compute | 0.1 | — |
| DBU004 | Continuous pipeline | 0.1 | — |
| DBU010 | Sharp frequency increase | 0.1 | — |
| DBU002 | All-purpose cluster auto-termination | 0.4 | — |
| DBU003 | SQL warehouse auto-stop | 0.4 | `catalog/defaults.yaml` (the `auto_stop_mins` default) |
| DBU005 | Continuous job | 0.4 | — |
| DBU016 | Classic job cluster on a short schedule | 0.4 | Shortest cron interval (built in 0.1) |
| DBU007 | Job without a timeout | 0.5 | — |
| DBU008 | Missing cost-attribution tags | 0.5 | `rules.DBU008.required_tags` setting |
| DBU012 | Outdated or end-of-support runtime | 0.5 | `catalog/runtimes.yaml`, an injectable reference date |
| DBU019 | Custom tag collides with a default tag | 0.5 | — |
| DBU009 | Large on-demand cluster | 0.6 | — |
| DBU017 | Spot without fallback | 0.6 | — |
| DBU018 | Pipeline cluster on legacy autoscaling | 0.6 | — |
| DBU011 | Scheduled serverless job in performance-optimized mode | 0.7 | — |
| DBU013 | Large Databricks App | 0.7 | Apps pricing; app field names verified with `databricks bundle schema` |
| DBU014 | Serving endpoint without scale-to-zero | 0.7 | Serving field names verified |
| DBU015 | Lakebase endpoint sizing | 0.8 | Lakebase pricing; endpoint field names verified |

DBU006 (development-mode pipeline in a production target) is retired and its ID is not reused: bundle production mode already rejects pipelines with `development: true`, so `bundle validate` fails before DBUDoctor runs.

Ready-to-paste requirement text for every rule above (and for Apps and Lakebase pricing) is in `docs/rule-drafts.md`.

## How a rule ships

1. Open a change named `add-dbu0xx-<slug>` (for example `add-dbu012-runtime-eos`). Its `specs/guardrail-rules/spec.md` holds `## ADDED Requirements` with the draft from `docs/rule-drafts.md`. Re-check the fields and sources first, because drafts age.
2. Commit `docs(openspec): propose DBU0xx, <title>`.
3. Implement `feat(rules): add DBU0xx, <title>` with:
   - positive, negative and new-only tests;
   - a fixture bundle;
   - `docs/rules/DBU0xx.md` (why it matters, an example, the fix, when to suppress);
   - rule metadata (title, when it fires, fix, suppression), which regenerates the agent skill.
4. After merge, archive the change with `openspec archive`.
5. Release when two to four rules are merged on `dev`: merge dev → main with a merge commit, per "Cutting a release" in CONTRIBUTING.

Each rule is also a good first contribution. Label the open ones `good first rule`.

## Parallel tracks

- **Price catalog.** A weekly Azure generator pull request, reviewed by a human. It runs on its own rhythm, independent of rule releases.
- **AWS VM prices.** Done after 0.3.0 (`add-aws-vm-prices`): On-Demand and reserved prices from the AWS Price List. Spot and savings plans remain.
- **PyPI wheel** wrapping the AOT binary (`pip install dbudoctor`). Done after 0.3.0 (`add-pypi-wheel`); first published with the next release.
- **More CI systems.** GitLab and Azure DevOps comments, demand-driven.

## After DBU019

The rule candidates in `docs/research/2026-11-08-rules-and-azure.md` (backlog section) get IDs from DBU020 when they are proposed.
