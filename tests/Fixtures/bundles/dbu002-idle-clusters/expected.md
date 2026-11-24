<!-- dbudoctor:bundle=workspace_clusters;target=prod -->
### DBUDoctor · `workspace_clusters` → `prod`: no monthly estimate

**Rate changes** (no monthly figure until calibration)
- cluster `adhoc`: added, — → $1.28 per active hour
- cluster `bi`: added, — → $3.21 per active hour
- cluster `etl_dev`: added, — → $3.21 per active hour
- cluster `shared`: autotermination_minutes `120` → `0`, $3.21 → $3.21 per active hour

**New findings**
- info · [DBU002](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU002.md) · cluster `adhoc` does not set `autotermination_minutes`. The effective default depends on the deployment engine; the Clusters API does not auto-terminate a cluster without it. Idle, it bills $1.28 per hour.
- info · [DBU002](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU002.md) · cluster `bi` waits 240 minutes idle before it terminates (limit 60): each idle stretch bills up to $12.83 ($3.21 per hour).
- warning · [DBU002](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU002.md) · cluster `shared` never auto-terminates (`autotermination_minutes: 0`): idle, it bills $3.21 per hour, up to $2,341 a month.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
