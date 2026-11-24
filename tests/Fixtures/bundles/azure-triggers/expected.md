<!-- dbudoctor:bundle=triggers;target=prod -->
### DBUDoctor · `triggers` → `prod`: **+$1,026 / month** (+1836% on changed resources)

| Resource | Change | Before | After | Δ / month | Confidence |
|---|---|---|---|---|---|
| app `dashboard` | added, always on, serverless | — | $292 | **+$292** | Medium · runs all month |
| job `paused` | schedule paused → daily | $0.00 | $18.66 | **+$18.66** | Medium · 30-min runs (configured) |
| job `periodic` | schedule daily → every 4 h | $18.66 | $112 | **+$93.30** | Medium · 30-min runs (configured) |
| job `photon_nightly` | tasks[t].new_cluster.runtime_engine unset → `PHOTON` | $37.22 | $61.79 | **+$24.57** | Medium · 30-min runs (configured) |
| job `streaming` | added, continuous | — | $597 | **+$597** | Medium · runs all month |

_Excludes 5 changes without a Medium or High monthly estimate._

**Rate changes** (no monthly figure until calibration)
- cluster `shared`: autoscale.max_workers `4` → `8`, $2.80 → $5.03 per active hour
- job `on_file`: added, file-arrival trigger, — → $0.82 per active hour
- sql_warehouse `reporting`: classic → serverless, $4.14 → $3.36 per active hour

**Needs history** (DBUs per run are not in configuration)
- job `on_table`: added, table-update trigger, serverless. Jobs serverless at $0.38 per DBU, monthly cost needs history

**Unknown cost**
- job `on_demand`: added, no schedule. node type `Standard_Unknown_v9` is not in the Azure catalog; no payg VM price for `Standard_Unknown_v9` in westeurope

**New findings**
- warning · [DBU005](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU005.md) · job `streaming` is continuous: a new run starts within a minute of the last, so it runs around the clock at $0.82 per hour, about $597 a month.
- info · [DBU007](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU007.md) · job `on_demand` has no timeout: neither the job nor its tasks set one.
- info · [DBU007](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU007.md) · job `on_file` has no timeout: neither the job nor its tasks set one, and a stuck run keeps billing $0.82 per hour until someone cancels it.
- info · [DBU007](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU007.md) · job `on_table` has no timeout: neither the job nor its tasks set one.
- warning · [DBU010](https://github.com/vuglll/DBUDoctor/blob/main/docs/rules/DBU010.md) · job `periodic` runs 6.0× as often (30.4 → 183 runs / month).

<details><summary>Assumptions</summary>

- Prices: Azure westeurope, Premium tier, pay-as-you-go VMs, catalog 2026-11-01.
- Scaling: constant runtime. Autoscale: at max.
- Run duration: 30 minutes (estimation.default_run_minutes).
- app `dashboard`: compute size not set: priced as MEDIUM (the default); running all month (730 h): an app bills for its size while it runs.
- cluster `shared`: autoscale at max: cluster priced with 4 workers (1–4); autoscale at max: cluster priced with 8 workers (1–8); constant runtime: compute size changed, run duration unchanged.
- job `on_table`: serverless job: DBUs per run cannot be derived from configuration.
- job `paused`: 30-minute runs (configured).
- job `periodic`: 30-minute runs (configured).
- job `photon_nightly`: 30-minute runs (configured); Photon enabled; constant runtime: compute size changed, run duration unchanged.
- job `streaming`: continuous: runs all month (730 h).
</details>

Not modeled on Azure: managed disks, blob storage, public IP addresses, network egress, NAT, storage, idle VMs in instance pools, VM time during cluster startup.
List-price estimate. Not an invoice.
