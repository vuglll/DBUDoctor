<!-- dbudoctor:bundle=batch;target=prod -->
### DBUDoctor · `batch` → `prod`: no monthly estimate

**Rate changes** (no monthly figure until calibration)
- job `default_avail`: added, no schedule, — → $7.72 per active hour
- job `nightly`: job_clusters[main].new_cluster.azure_attributes.availability `SPOT_WITH_FALLBACK_AZURE` → `ON_DEMAND_AZURE`, $7.72 → $7.72 per active hour
- job `small`: added, no schedule, — → $2.27 per active hour

**New findings**
- info · [DBU009](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU009.md) · job `nightly`, job cluster `main` runs up to 16 workers on on-demand VMs (`ON_DEMAND_AZURE`). Spot with fallback (`SPOT_WITH_FALLBACK_AZURE`) with `first_on_demand: 1` keeps the driver on demand. Spot workers would save up to $2.86 per hour on VMs (16 × `Standard_DS3_v2` at $0.23 on demand against $0.05 spot, catalog prices).

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
- job `default_avail`: autoscale at max: job cluster `main` priced with 16 workers (2–16).
- job `nightly`: autoscale at max: job cluster `main` priced with 16 workers (2–16).
- job `small`: autoscale at max: job cluster `main` priced with 4 workers (2–4).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
