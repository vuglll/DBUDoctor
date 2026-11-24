<!-- dbudoctor:bundle=streams;target=prod -->
### DBUDoctor · `streams` → `prod`: no monthly estimate

**Rate changes** (no monthly figure until calibration)
- pipeline `events`: clusters[default].autoscale.mode `ENHANCED` → `LEGACY`, $4.09 → $4.09 per active hour

**New findings**
- info · [DBU018](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU018.md) · pipeline `events`, cluster `default` uses legacy autoscaling (`autoscale.mode: LEGACY`): enhanced autoscaling adds and removes workers as the workload changes, and it is the default for new pipelines. Set `mode: ENHANCED`.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
- pipeline `events`: autoscale at max: pipeline cluster priced with 8 workers (1–8).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
