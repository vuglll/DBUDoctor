# Calibration: monthly figures from usage history

Without history, DBUDoctor prices what configuration states: hourly rates, and monthly figures only where configuration implies a duration. Serverless jobs and pipelines, SQL warehouses, all-purpose clusters and event-triggered jobs show rates or "needs history".

Calibration reads what the deployed resources actually used in `system.billing.usage` and turns those rates into monthly figures:

| Resource | Monthly figure |
|---|---|
| Classic job | median DBUs per run × runs per month × $/DBU. Runs come from the schedule, or from observed runs for jobs without one. On Azure, VMs are added from the observed runtime: DBUs per run ÷ the cluster's DBU per hour. |
| Serverless job | median DBUs per run × runs per month × $/DBU. A `performance_target` change applies `estimation.standard_mode_factor` (0.7 by default), with confidence Low. |
| Pipeline, SQL warehouse, all-purpose cluster | observed DBUs scaled to a 730-hour month × $/DBU. |

When a pull request changes the compute size, observed DBUs scale by the head's DBU per hour over the base's: a driver and 4 workers going to 8 workers scales by 9 / 5 = 1.8.

Confidence is **High** with at least `calibration.min_runs` runs (5 by default) or 7 days of usage in the window, and **Low** with less. High still means "if the next month looks like the last one".

Calibration also:
- uses current list prices from `system.billing.list_prices` instead of the catalog. Your discounts and surcharge still apply on top.
- checks each calibrated node type against the catalog. When observed DBUs per node-hour differ by more than 10% (from `system.compute.node_timeline`), the report warns and suggests a catalog pull request.

A switch between classic and serverless changes the compute, so the history does not carry over. That side is estimated from configuration.

## Turn it on

```yaml
# .dbudoctor.yml
calibration:
  enabled: true
  warehouse_id: 1234567890abcdef   # the SQL warehouse that runs the queries
  # target: prod                   # whose deployed resources hold the history; default: the estimation target
  # window_days: 30
  # min_runs: 5
```

`DBUDOCTOR_CALIBRATION=true` and `DBUDOCTOR_WAREHOUSE_ID` override the file.

## Least-privilege setup

Use a service principal with read access to the billing tables and permission to use one warehouse:

```sql
GRANT USE CATALOG ON CATALOG system TO `dbudoctor-sp`;
GRANT USE SCHEMA ON SCHEMA system.billing TO `dbudoctor-sp`;
GRANT SELECT ON SCHEMA system.billing TO `dbudoctor-sp`;
-- Optional, for the drift check:
GRANT USE SCHEMA ON SCHEMA system.compute TO `dbudoctor-sp`;
GRANT SELECT ON TABLE system.compute.node_timeline TO `dbudoctor-sp`;
```

Then give the service principal **CAN USE** on the warehouse. Every statement is a read-only `SELECT`.

Deployed IDs come from `databricks bundle summary --output json` for the calibration target, so the principal also needs to read the bundle's deployment state. With `--base-json` input there is no summary: jobs are matched by name, and other resources are reported as not found.

## What it costs

The queries run on your warehouse, so they are billed like any other query:
- Use an existing shared warehouse, or a 2X-Small serverless warehouse with the shortest auto-stop.
- Only resources the pull request changes are queried, at most four small statements per bundle.
- Results are cached per workspace, statement and UTC day under the DBUDoctor cache directory (`DBUDOCTOR_CACHE_DIR`), so further runs that day start no warehouse. Keep the cache between CI runs with `actions/cache`.

## Auth

DBUDoctor tries these in order:
1. `DATABRICKS_TOKEN`: a personal access token.
2. Token federation: `DATABRICKS_AUTH_TYPE: github-oidc` (or `env-oidc` with `DATABRICKS_OIDC_TOKEN`) and `DATABRICKS_CLIENT_ID`. The GitHub token's audience is `DATABRICKS_TOKEN_AUDIENCE`, else `DATABRICKS_ACCOUNT_ID` (the federation policy's default).
3. OAuth M2M: `DATABRICKS_CLIENT_ID` and `DATABRICKS_CLIENT_SECRET`.
4. The Databricks CLI's own sign-in, through `databricks auth token`.

The workspace is `DATABRICKS_HOST`, else the bundle's `workspace.host`. Tokens are never logged or cached.

## When something is missing

Calibration never fails the run, and never changes the exit code:
- **No credentials, an unreachable warehouse or a missing grant:** a warning names the problem (for a grant, which one), and every resource is estimated from configuration.
- **A resource with no deployed ID in the target:** it is estimated without history, and the report says it was not found.
- **No usage in the window:** the resource is estimated without history.
- **`system.compute.node_timeline` not readable:** the drift check is skipped, with a note.

## Example workflow (GitHub Actions with OIDC)

Create a [federation policy](https://docs.databricks.com/aws/en/dev-tools/auth/provider-github) for the service principal that trusts this repository, then:

```yaml
name: DBUDoctor
on:
  pull_request:
    paths: ["**/*.yml", ".dbudoctor.yml"]

permissions:
  contents: read
  pull-requests: write
  id-token: write # the GitHub OIDC token for Databricks token federation

jobs:
  cost:
    runs-on: ubuntu-latest
    env:
      DATABRICKS_HOST: ${{ vars.DATABRICKS_HOST }}
      DATABRICKS_AUTH_TYPE: github-oidc
      DATABRICKS_CLIENT_ID: ${{ vars.DATABRICKS_CLIENT_ID }}
      DBUDOCTOR_CACHE_DIR: ${{ runner.temp }}/dbudoctor-cache
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: databricks/setup-cli@main

      # One cache entry per UTC day: the first run of the day queries the warehouse.
      - id: day
        run: echo "day=$(date -u +%F)" >> "$GITHUB_OUTPUT"
      - uses: actions/cache@v4
        with:
          path: ${{ runner.temp }}/dbudoctor-cache
          key: dbudoctor-${{ steps.day.outputs.day }}

      - uses: vuglll/DBUDoctor/action@v0
        with:
          target: prod
```

The `env` block applies to every step, so `bundle validate`, `bundle summary` and the calibration queries all use the same sign-in.
