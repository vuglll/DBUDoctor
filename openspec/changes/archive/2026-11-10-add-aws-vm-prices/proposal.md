## Why

Classic compute on AWS is priced for DBUs only: the AWS catalog has no EC2 prices, so a job cluster's rate leaves out the instances, often half the bill or more. Reports say "cloud VMs not included", and classic → serverless comparisons on AWS are marked Low. The roadmap lists AWS VM prices as a parallel track around 0.6.

## What Changes

- `tools/CatalogGen` reads the public AWS Price List bulk files (no AWS credentials), one CSV per region, streamed. It keeps Linux, shared tenancy, no pre-installed software, capacity status `Used`, for the instance types in `catalog/node-types/aws.yaml`.
- Each AWS price sheet gains a `vm:` section with:
  - `payg`: the On-Demand hourly price;
  - `reserved_1y`, `reserved_3y`: Standard, No Upfront reservations, hourly.
- `catalog/sql-warehouses.yaml` gains the AWS layout for classic and pro warehouses: driver `i3.2xlarge` (2X-Small, X-Small), `i3.4xlarge` (Small), `i3.8xlarge` (Medium, Large) or `i3.16xlarge` (X-Large and up); every worker `i3.2xlarge`; worker counts as on Azure.
- `catalog/not-modeled.yaml` lists AWS items outside the VM price: EBS volumes, S3, data transfer, NAT gateways.
- With the default `pricing.include_cloud_infra: auto`, AWS classic rates now include instances, and AWS classic → serverless comparisons are no longer marked Low for missing VMs.

Not in this change:
- **Spot.** AWS publishes spot prices only through `DescribeSpotPriceHistory`, which needs credentials. `pricing.infra.commitment: spot` on AWS makes the VM part Unknown, with a reason naming the missing column; `include_cloud_infra: false` returns to DBU-only rates.
- **Savings plans.** They are in a separate offer file (`AWSComputeSavingsPlan`); a later change can add them. `savings_plan_1y` and `savings_plan_3y` on AWS are Unknown the same way.
- `--refresh-prices` stays Azure-only.

Facts, checked 2026-11-10:
- https://pricing.us-east-1.amazonaws.com/offers/v1.0/aws/AmazonEC2/current/region_index.json lists 107 regions with `currentVersionUrl`; the per-region CSV is about 180–300 MB (us-east-1 303 MB).
- https://docs.databricks.com/aws/en/compute/sql-warehouse/warehouse-behavior, "Sizing and cluster provisioning" under "Classic and pro SQL warehouses": the driver per size as above, and "All workers use `i3.2xlarge` instances". The page notes serverless may use other types, so serverless keeps DBU-only pricing (its DBU price includes compute).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `cost-estimation`: AWS catalogs gain VM prices; the AWS warehouse layout; the "cloud without VM prices" example moves to GCP.

## Impact

- Code: an AWS Price List client and normalizer in `src/DBUDoctor.Core/Pricing/Aws/`, wired into `tools/CatalogGen` for `--clouds aws`; the generator header for AWS no longer says "DBU prices only".
- Catalog: every `catalog/prices/aws/*.yaml` regenerated with `vm:`; `sql-warehouses.yaml` and `not-modeled.yaml` updated.
- The weekly `Refresh the catalog` workflow downloads about 3–4 GB for the 16 AWS regions; files are streamed, not stored.
- Golden outputs for AWS fixtures change: classic rates gain a VM part.
