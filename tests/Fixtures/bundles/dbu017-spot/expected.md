<!-- dbudoctor:bundle=batch;target=prod -->
### DBUDoctor · `batch` → `prod`: no monthly estimate

**Rate changes** (no monthly figure until calibration)
- job `hourly`: added, no schedule, — → $2.27 per active hour
- job `nightly`: job_clusters[main].new_cluster.azure_attributes.availability `SPOT_WITH_FALLBACK_AZURE` → `SPOT_AZURE`; job_clusters[main].new_cluster.azure_attributes.first_on_demand `1` → `0`, $2.27 → $2.27 per active hour

**New findings**
- info · [DBU017](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU017.md) · job `nightly`, job cluster `main` runs on spot capacity without fallback (`SPOT_AZURE`): an eviction or a shortage can fail the run, and the retry pays again. The driver runs on spot too (`first_on_demand` is 0), so one eviction ends the run. Use `SPOT_WITH_FALLBACK_AZURE` with `first_on_demand: 1`.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
