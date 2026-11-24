<!-- dbudoctor:bundle=lakehouse;target=prod -->
### DBUDoctor · `lakehouse` → `prod`: **+$93.06 / month** (+99% on changed resources)

> Cloud VMs are not included in classic rates: the catalog has no VM prices for AWS.

| Resource | Change | Before | After | Δ / month | Confidence |
|---|---|---|---|---|---|
| job `ingest` | job_clusters[m].new_cluster.node_type_id `m5.xlarge` → `m5.2xlarge` | $94.43 | $187 | **+$93.06** | Medium · 15-min runs (configured) |

_Excludes 2 changes without a Medium or High monthly estimate._

**Rate changes** (no monthly figure until calibration)
- sql_warehouse `bi`: classic → serverless, $6.60 → $8.40 per active hour (Low: classic/pro cost excludes cloud VMs; serverless includes them)

**Needs history** (DBUs per run are not in configuration)
- job `ingest_v2`: added, no schedule. All-purpose compute at $0.55 per DBU, runs on an all-purpose cluster: its cost depends on that cluster's usage

**New findings**
- warning · [DBU001](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU001.md) · job `ingest_v2`, task `t` runs on all-purpose cluster `0123-456789-…`. Jobs compute costs about 27% of the all-purpose DBU rate here.
- info · [DBU007](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU007.md) · job `ingest_v2` has no timeout: neither the job nor its tasks set one.

**Policy violated**
- `fail_on.rules`: DBU001 is reported for jobs.ingest_v2.

<details><summary>Assumptions</summary>

- Prices: AWS us-east-1, Premium tier, DBUs only (cloud VMs not included), catalog 2026-10-15.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: 15 minutes (estimation.default_run_minutes).
- job `ingest`: 15-minute runs (configured); constant runtime: compute size changed, run duration unchanged.
- job `ingest_v2`: tasks on existing all-purpose clusters (t) are priced with those clusters.
</details>

Not modeled on AWS: storage, cloud VMs for classic compute, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
