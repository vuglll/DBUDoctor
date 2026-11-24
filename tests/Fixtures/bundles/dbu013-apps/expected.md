<!-- dbudoctor:bundle=portal;target=prod -->
### DBUDoctor · `portal` → `prod`: **+$2,190 / month** (+600% on changed resources)

| Resource | Change | Before | After | Δ / month | Confidence |
|---|---|---|---|---|---|
| app `admin` | added, always on, serverless | — | $365 | **+$365** | Medium · runs all month |
| app `portal` | compute_size `MEDIUM` → `XLARGE` | $365 | $2,190 | **+$1,825** | Medium · runs all month |

**New findings**
- info · [DBU013](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU013.md) · app `portal` runs on XLARGE compute: 3 DBU per hour per instance, 6.0× MEDIUM, about $2,190 a month while it runs.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
- app `admin`: compute size not set: priced as MEDIUM (the default); running all month (730 h): an app bills for its size while it runs.
- app `portal`: running all month (730 h): an app bills for its size while it runs.
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
