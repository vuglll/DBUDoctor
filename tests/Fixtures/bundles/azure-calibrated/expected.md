<!-- dbudoctor:bundle=analytics;target=prod -->
### DBUDoctor · `analytics` → `prod`: **+$12,196 / month** (+460% on changed resources)

| Resource | Change | Before | After | Δ / month | Confidence |
|---|---|---|---|---|---|
| job `nightly_report` | performance_target unset → `STANDARD` | $274 | $192 | -$82.13 | Low · 30 runs observed |
| job `orders_sync` | schedule hourly → every 15 min; job_clusters[main].new_cluster.num_workers `4` → `6` | $2,651 | $14,848 | **+$12,196** | High · 60 runs observed |

_Excludes 2 changes without a Medium or High monthly estimate._

**Rate changes** (no monthly figure: no history)
- sql_warehouse `bi`: cluster_size `Small` → `Medium`, $8.40 → $16.80 per active hour

**New findings**
- warning · [DBU010](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU010.md) · job `orders_sync` runs 4.0× as often (730 → 2,920 runs / month). Cron `0 0 * * * ?` → `0 0/15 * * * ?`.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, list prices from system.billing.list_prices, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs without history show rates only (set estimation.default_run_minutes).
- Calibration: history from system.billing.usage over the last 30 days in target `prod`, 2 calibrated; 1 not found in the target. High confidence assumes the next month looks like the last one.
- Calibration: no `bundle summary` for JSON input; jobs matched by name.
- job `nightly_report`: calibrated from 30 runs, median 20 DBUs per run in the last 30 days (system.billing.usage); performance mode PERFORMANCE_OPTIMIZED → STANDARD: DBUs × 0.7 (estimation.standard_mode_factor).
- job `orders_sync`: calibrated from 60 runs, median 6 DBUs per run in the last 30 days (system.billing.usage); observed DBUs scaled by 5.25 / 3.75 DBU per hour (constant runtime).
- sql_warehouse `bi`: not found in calibration target `prod`: estimated without history.
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
