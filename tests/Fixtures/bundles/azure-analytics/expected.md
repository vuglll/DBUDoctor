<!-- dbudoctor:bundle=analytics;target=prod -->
### DBUDoctor · `analytics` → `prod`: **+$2,651 / month** (+480% on changed resources)

| Resource | Change | Before | After | Δ / month | Confidence |
|---|---|---|---|---|---|
| job `orders_sync` | schedule hourly → every 15 min | $552 | $2,209 | **+$1,657** | Medium · 20-min runs (configured) |
| pipeline `events` | added, continuous | — | $994 | **+$994** | Medium · runs all month |

_Excludes 2 changes without a Medium or High monthly estimate._

**Rate changes** (no monthly figure until calibration)
- job `report_refresh`: tasks[refresh].existing_cluster_id unset → `0601-182128-dcbte59m`; tasks[refresh].job_cluster_key `main` → unset; +3 more, $1.36 per active hour → priced elsewhere
- sql_warehouse `bi_wh`: cluster_size `Small` → `Medium`, $8.40 → $16.80 per active hour

**New findings**
- warning · [DBU001](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU001.md) · job `report_refresh`, task `refresh` runs on all-purpose cluster `0601-182128-…`. Jobs compute costs about 55% of the all-purpose DBU rate here.
- warning · [DBU004](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU004.md) · pipeline `events` is continuous: it runs around the clock (~730 h / month).
- warning · [DBU010](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU010.md) · job `orders_sync` runs 4.0× as often (730 → 2,920 runs / month). Cron `0 0 * * * ?` → `0 0/15 * * * ?`.

**Policy violated**
- `fail_on.monthly_increase`: monthly increase +$2,651 is above the limit of $500.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: 20 minutes (estimation.default_run_minutes).
- job `orders_sync`: 20-minute runs (configured).
- job `report_refresh`: 20-minute runs (configured); tasks on existing all-purpose clusters (refresh) are priced with those clusters.
- pipeline `events`: autoscale at max: pipeline cluster priced with 2 workers (1–2); continuous: runs all month (730 h).
- sql_warehouse `bi_wh`: 1 cluster running (min_num_clusters); scales up to 2.
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
