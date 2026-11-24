## 1. Generator

- [x] 1.1 AWS Price List client: region index, per-region CSV streamed line by line, with a CSV reader for quoted fields
- [x] 1.2 Normalizer: filter Linux / Shared / no pre-installed software / `Used`, keep known node types, read On-Demand and Standard No Upfront 1yr/3yr hourly prices; conflicting values are left out and reported (AwsPriceListTests on a trimmed CSV fixture)
- [x] 1.3 `tools/CatalogGen --clouds aws` writes `vm:` into each AWS price sheet, with the summary listing VM price changes and instance types missing from the price list

## 2. Catalog

- [x] 2.1 AWS warehouse layout in `catalog/sql-warehouses.yaml`, AWS items in `catalog/not-modeled.yaml`
- [x] 2.2 Regenerate the AWS catalog

## 3. Cost and docs

- [x] 3.1 Tests: AWS classic job cluster with VM cost, AWS pro warehouse with VM cost, `commitment: spot` on AWS is Unknown with a reason, GCP still DBU-only
- [x] 3.2 Update docs (README pricing section, CHANGELOG Unreleased, roadmap parallel track)

## 4. Verification

- [x] 4.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed, `openspec validate add-aws-vm-prices --strict` passing
