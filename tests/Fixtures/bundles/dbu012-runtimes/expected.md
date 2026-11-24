<!-- dbudoctor:bundle=etl;target=prod -->
### DBUDoctor · `etl` → `prod`: no monthly estimate

**Rate changes** (no monthly figure until calibration)
- job `fresh`: added, no schedule, — → $1.36 per active hour
- job `nightly`: job_clusters[main].new_cluster.spark_version `16.4.x-scala2.12` → `13.3.x-scala2.12`, $1.36 → $1.36 per active hour
- job `reports`: added, no schedule, — → $1.36 per active hour

**New findings**
- warning · [DBU012](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU012.md) · job `nightly`, job cluster `main` uses Databricks Runtime 13.3 LTS, which reached end of support on 2026-08-22: it gets no more security fixes, and newer runtimes run the same work faster. Move to a current LTS release, such as 17.3 LTS, after testing.
- info · [DBU012](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU012.md) · job `reports`, job cluster `main` uses Databricks Runtime 14.3 LTS, which reaches end of support on 2027-02-01, in 84 days. Move to a current LTS release, such as 17.3 LTS, after testing.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
