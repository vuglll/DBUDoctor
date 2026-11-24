## 1. Catalog

- [x] 1.1 `PREMIUM_DATABASE_SERVERLESS_COMPUTE` and the `database` family (schema discounts updated)
- [x] 1.2 Generator mappings: Azure "Premium Database Serverless Compute DBU", calculator "Lakebase" (DatabricksPricingTests and AzureRetailPrices contract tests)
- [x] 1.3 `catalog/serverless.yaml` DBU per CU for AWS and Azure
- [x] 1.4 Regenerate the catalog with `tools/CatalogGen`

## 2. Cost

- [x] 2.1 `postgres_endpoints` as a priced type with its cost-relevant fields and model
- [x] 2.2 `CostEngine.EstimatePostgresEndpoint` with the ceiling (LakebasePricingTests: always active, scale to zero, GCP)
- [x] 2.3 `monthlyCeiling` in the JSON report, its schema and the skill's output reference

## 3. Verification

- [x] 3.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed (JSON gains `monthlyCeiling: null`), `openspec validate add-lakebase-pricing --strict` passing
