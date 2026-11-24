<!-- dbudoctor:bundle=streaming;target=prod -->
### DBUDoctor · `streaming` → `prod`: no monthly estimate

**Rate changes** (no monthly figure until calibration)
- job `events_ingest`: schedule every 15 min → continuous, $2.27 → $2.27 per active hour

**New findings**
- warning · [DBU005](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU005.md) · job `events_ingest` is continuous: a new run starts within a minute of the last, so it runs around the clock at $2.27 per hour, about $1,657 a month.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
- job `events_ingest`: continuous: runs all month (730 h).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
