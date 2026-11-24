<!-- dbudoctor:bundle=finance;target=prod -->
### DBUDoctor · `finance` → `prod`: no monthly estimate

**Rate changes** (no monthly figure until calibration)
- job `ledger`: job_clusters[main].new_cluster.custom_tags.vendor unset → `finance`, $1.36 → $1.36 per active hour
- job `ok`: added, no schedule, — → $1.36 per active hour

**New findings**
- info · [DBU019](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU019.md) · job `ledger` sets custom tag `vendor`, which collides with the Databricks default tag `Vendor`: cloud billing shows it as `x_vendor`, so cost reports grouped by `vendor` miss it.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
