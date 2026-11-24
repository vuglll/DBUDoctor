<!-- dbudoctor:bundle=finance;target=prod -->
### DBUDoctor · `finance` → `prod`: no monthly estimate

**Rate changes** (no monthly figure until calibration)
- job `forecast`: added, no schedule, — → $1.36 per active hour
- job `split`: added, no schedule, — → $1.36 per active hour
- sql_warehouse `bi`: added, — → $9.60 per active hour

**Needs history** (DBUs per run are not in configuration)
- job `serverless_report`: added, no schedule, serverless. Jobs serverless at $0.47 per DBU, monthly cost needs history

**New findings**
- warning · [DBU008](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU008.md) · job `forecast` lacks required tag `cost_center` (rules.DBU008.required_tags), so billing cannot attribute its cost.
- warning · [DBU008](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU008.md) · sql_warehouse `bi` lacks required tag `cost_center` (rules.DBU008.required_tags), so billing cannot attribute its cost.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
- job `serverless_report`: serverless job: DBUs per run cannot be derived from configuration.
- sql_warehouse `bi`: warehouse_type not set: priced as PRO.
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
