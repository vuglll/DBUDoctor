<!-- dbudoctor:bundle=reports;target=prod -->
### DBUDoctor · `reports` → `prod`: no monthly estimate

**Needs history** (DBUs per run are not in configuration)
- job `nightly`: performance_target `STANDARD` → unset. Jobs serverless at $0.47 per DBU, 30.4 runs / month, monthly cost needs history
- job `weekly`: added, daily, serverless. Jobs serverless at $0.47 per DBU, 30.4 runs / month, monthly cost needs history

**New findings**
- info · [DBU011](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU011.md) · job `nightly` runs serverless tasks daily in performance-optimized mode (the default). Standard mode uses the same SKU but consumes fewer DBUs, and starts in 4 to 6 minutes; for scheduled batch work, set `performance_target: STANDARD`.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
- job `nightly`: serverless job: DBUs per run cannot be derived from configuration.
- job `weekly`: serverless job: DBUs per run cannot be derived from configuration.
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
