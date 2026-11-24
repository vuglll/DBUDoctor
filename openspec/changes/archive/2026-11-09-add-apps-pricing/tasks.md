## 1. Model and diff

- [x] 1.1 `apps` as a priced type with its cost-relevant fields; the app model (DiffTests: unsupported type now uses dashboards)

## 2. Catalog and cost

- [x] 2.1 `catalog/serverless.yaml` with app sizes and the default size; `ServerlessTable` loader, optional for directory catalogs
- [x] 2.2 `CostEngine.EstimateApp`: size × instances × 730 × Interactive Serverless price, confidence Medium (AppPricingTests: medium to large, size not set, instances)

## 3. Docs

- [x] 3.1 README (how estimates are made, not priced list), CHANGELOG

## 4. Verification

- [x] 4.1 Clean build with AOT and trim warnings as errors, all tests green, golden outputs reviewed (`azure-triggers` now prices its app), `openspec validate add-apps-pricing --strict` passing
