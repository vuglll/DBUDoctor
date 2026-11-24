<!-- dbudoctor:bundle=sync;target=prod -->
### DBUDoctor · `sync` → `prod`: no monthly estimate

**Rate changes** (no monthly figure until calibration)
- job `orders_sync`: schedule every 30 min → every 5 min, $1.36 → $1.36 per active hour

**New findings**
- warning · [DBU010](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU010.md) · job `orders_sync` runs 6.0× as often (1,460 → 8,760 runs / month). Cron `0 0/30 * * * ?` → `0 0/5 * * * ?`.
- info · [DBU016](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU016.md) · job `orders_sync` runs every 5 min on job cluster `main`, which has no instance pool: it starts a cluster about 8,760 times a month, and every run pays for start-up. Consider serverless, an `instance_pool_id`, or a continuous job.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
