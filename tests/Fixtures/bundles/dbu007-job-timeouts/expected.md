<!-- dbudoctor:bundle=etl;target=prod -->
### DBUDoctor · `etl` → `prod`: no monthly estimate

**Rate changes** (no monthly figure until calibration)
- job `backfill`: added, daily, — → $2.27 per active hour
- job `nightly_etl`: timeout_seconds `7200` → unset, $2.27 → $2.27 per active hour

**New findings**
- info · [DBU007](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU007.md) · job `backfill` has no timeout: the job sets none, nor does task `transform`, and a stuck run keeps billing $2.27 per hour until someone cancels it.
- info · [DBU007](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU007.md) · job `nightly_etl` has no timeout: neither the job nor its tasks set one, and a stuck run keeps billing $2.27 per hour until someone cancels it.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
