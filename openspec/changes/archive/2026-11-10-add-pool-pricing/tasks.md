## 1. Configuration

- [x] 1.1 `EstimationSettings.InstancePools`, loader, known paths and JSON Schema (ConfigTests: valid map, wrong shape)

## 2. Cost

- [x] 2.1 `CostEngine.Rate` resolves worker and driver node types through the map, with the assumption and the unmapped reason (CostTests: mapped, driver pool, unmapped)
- [x] 2.2 End-to-end check: a pool-backed job analyzed into a monthly figure (CostTests.Pool_backed_bundle_gets_a_monthly_figure)

## 3. Docs

- [x] 3.1 README (example config, limitations), JSON Schema, CHANGELOG

## 4. Verification

- [x] 4.1 All tests green, `openspec validate add-pool-pricing --strict` passing
