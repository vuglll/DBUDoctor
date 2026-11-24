<!-- dbudoctor:bundle=shop;target=prod -->
### DBUDoctor · `shop` → `prod`: **+$280 / month** (+300% on changed resources)

| Resource | Change | Before | After | Δ / month | Confidence |
|---|---|---|---|---|---|
| postgres_endpoint `prod_primary` | autoscaling_limit_max_cu `8` → `16`; autoscaling_limit_min_cu `2` → `8` | $93.29 | $373 | **+$280** | Medium · runs all month |

_Excludes 1 change without a Medium or High monthly estimate._

**Rate changes** (no monthly figure until calibration)
- postgres_endpoint `dev_primary`: autoscaling_limit_min_cu `1` → `2`; no_suspension unset → `true`, $0.06 → $0.13 per active hour

**New findings**
- warning · [DBU015](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU015.md) · postgres_endpoint `dev_primary` on branch `dev` never scales to zero (`no_suspension: true`): about $93.29 a month at 2 CU even when idle. Let non-production branches scale to zero.
- info · [DBU015](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU015.md) · postgres_endpoint `prod_primary` keeps at least 8 CU (limit 8) at $0.51 per active hour: the minimum is billed for every active hour, and autoscaling adds capacity when it is busy.

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: not set, so scheduled classic jobs show rates only (set estimation.default_run_minutes).
- postgres_endpoint `dev_primary`: up to $187 a month if active all month at 4 CU; always active (no_suspension): 2 CU for 730 h; up to $187 a month at 4 CU.
- postgres_endpoint `prod_primary`: always active (no_suspension): 2 CU for 730 h; up to $373 a month at 8 CU; always active (no_suspension): 8 CU for 730 h; up to $746 a month at 16 CU.
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
