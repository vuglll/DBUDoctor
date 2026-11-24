# Run against databricks/bundle-examples (task 10.2)

Date: 2026-11-09. bundle-examples at `1b358c3`. Databricks CLI 1.24.0. DBUDoctor `dev` after PR #2.
Driver: `tools/bundle-examples/run.py`, through `--base-json`/`--head-json`.

## Setup and limits

- **No workspace authentication.** `bundle validate` exits 1 but still prints the bundle. It stops
  before resolving variables, so `${var.…}` and `${resources.…}` references stay in the JSON. Cost
  fields that come from variables were therefore not exercised.
- **Python-defined bundles (9 of 45)** — `pydabs`, `contrib/dbt_factory` and seven in `knowledge_base/` —
  get a Python 3.12 venv with the `databricks-bundles` version their `pyproject.toml` pins (`dbt_factory`
  also needs its own package, PyYAML and `dbt-databricks`). Without auth, validate stops before its Python
  step, so the driver calls the same loader, `python -m databricks.bundles.build --phase load_resources`
  (and `apply_mutators` when declared), on validate's output. Target presets the CLI applies afterwards
  are skipped. All 9 load: `dbt_factory` expands its dbt manifest into one job with 5 tasks.
- Two bundles are empty by design: `default_minimal` and `contrib/data_engineering` (whose `assets/`
  holds only a README).
- Target: `prod`, else `production`, else the bundle's default. Cloud forced to AWS (`us-east-1`)
  for bundles with no host, so classic compute gets priced.

## Passes

1. **Every bundle as new:** an empty base against the bundle at HEAD. 45 bundles.
2. **History:** every non-merge commit that touched a bundle's YAML, parent against commit, per changed
   bundle. 127 diffs.

## Results

| | Runs | Exit 0 | Exit 2 |
|---|---|---|---|
| New | 45 | 43 | 2 |
| History | 127 | 125 | 2 |

- No crashes. Every exit 2 is one of the two `databricks_ingestion_monitoring` bundles, whose
  `serverless: ${var.serverless_monitoring_pipeline_enabled}` stays unresolved without auth. The message
  now names the field and the reference.
- No rule findings. None of the examples uses all-purpose compute for jobs, continuous pipelines, or a
  sharp frequency increase.
- Priced classic rates match a hand calculation, for example `default_python_job`: `i3.xlarge`,
  autoscale 1–4 at max = 5 DBU × $0.15 = $0.75 per active hour; `twoxs_warehouse` 2X-Small PRO =
  4 DBU × $0.55 = $2.20 per active hour.
- The 9 Python-defined bundles add 9 jobs, a pipeline, a warehouse and a schema; all are serverless, unscheduled, event-triggered or on a SQL warehouse, so they show "needs history" or "priced elsewhere".
- Most example jobs are serverless or unscheduled, so they show "needs history" or a rate, as designed.
- Non-compute resources (apps, dashboards, alerts, Genie spaces, Lakebase, vector search, schemas,
  volumes, models, experiments) are listed as not priced.
- 3 possible renames were reported, all real renames in the history.

## Bugs found and fixed

1. A daily cron in a DST time zone (`44 37 8 * * ?`, `Europe/Amsterdam`) was described as
   "cron `…`" instead of "daily": the shortest gap was measured in UTC (23 h on the transition day).
   Gaps are now measured on the wall clock. The `azure-analytics` golden file had the same mistake.
2. Classic → serverless rate lines read "$0.75 → needs history per active hour" (and
   "$1.36 → priced elsewhere per active hour"). The unit now sits on amounts only.
3. Classic → serverless changes were summarized by their first raw fields (`environment_key`, …).
   The summary now leads with "classic → serverless".
4. A cron that moved to a periodic trigger with the same frequency was summarized as raw fields with
   unquoted values (`schedule.quartz_cron_expression 21 27 9 * * ? → unset`). It now reads
   "schedule changed, still daily", and field values are quoted in backticks.
5. With the cloud unknown, a classic cluster got a second reason, "node type `i3.xlarge` is not in the
   this cloud catalog". The cloud-unknown reason is now the only one.
6. An unresolved `${…}` in a boolean or number field failed with a raw .NET conversion message. It now
   names the field and the reference, and points at the validate error.

## Still to do for 10.2

- Repeat with workspace authentication, so variables resolve.
- Other public bundle repositories.
