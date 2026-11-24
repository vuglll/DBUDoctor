<!-- dbudoctor:bundle=analytics_sql;target=prod -->
### DBUDoctor · `analytics_sql` → `prod`: no monthly estimate

**Rate changes** (no monthly figure until calibration)
- sql_warehouse `adhoc`: added, serverless, — → $8.40 per active hour
- sql_warehouse `bi`: auto_stop_mins `10` → `0`, $8.40 → $8.40 per active hour
- sql_warehouse `etl`: added, — → $19.20 per active hour
- sql_warehouse `quick`: added, serverless, — → $2.80 per active hour

**New findings**
- info · [DBU003](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU003.md) · sql_warehouse `adhoc` does not set `auto_stop_mins`, so the API default of 120 minutes applies (limit 30): each idle stretch bills up to $16.80 ($8.40 per hour).
- warning · [DBU003](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU003.md) · sql_warehouse `bi` never auto-stops (`auto_stop_mins: 0`): idle, it bills $8.40 per hour, up to $6,132 a month.
- info · [DBU003](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU003.md) · sql_warehouse `etl` waits 45 minutes idle before it stops (limit 30): each idle stretch bills up to $14.40 ($19.20 per hour).

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
