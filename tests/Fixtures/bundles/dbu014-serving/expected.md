<!-- dbudoctor:bundle=ml;target=dev -->
### DBUDoctor · `ml` → `dev`: no monthly estimate

**New findings**
- warning · [DBU014](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU014.md) · serving endpoint `churn`, served entity `m` does not scale to zero in a development target: it keeps a floor of 4 provisioned concurrency running while idle. Set `scale_to_zero_enabled: true`.

**Not priced in this version**
- model_serving_endpoints `batch_scoring` (added)
- model_serving_endpoints `churn` (modified)

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
