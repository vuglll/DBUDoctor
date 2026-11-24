## 1. Databricks access

- [x] 1.1 Auth: reuse CLI auth through `databricks auth token` where available, otherwise PAT or OAuth M2M from `DATABRICKS_*`
- [x] 1.2 SQL Statement Execution API client with polling, timeouts and chunked results (AOT-safe, plain `HttpClient`)

## 2. Identity and queries

- [ ] 2.1 Map bundle resources to deployed IDs from `bundle summary --output json`, with name matching as a fallback, plus a contract test across CLI versions
  - Done: mapping from `resources.<type>.<key>.id`, the job-name fallback, and a contract test pinned to the shape the CLI source declares. Open: recorded `bundle summary` output from real CLI versions, which needs a deployed bundle and workspace auth
- [x] 2.2 Usage queries per resource type (jobs and runs, warehouses, clusters, pipelines) and the `list_prices` query
- [x] 2.3 Validate ID formats before inlining them, with tests for injection attempts
- [x] 2.4 Cache results by workspace, query hash and UTC day

## 3. Estimates

- [x] 3.1 Calibrated formulas for classic jobs, including runs per month from observed runs for event-driven jobs
- [x] 3.2 Azure infrastructure from observed runtime (`run_hours = DBUs / DBU per hour`)
- [x] 3.3 Serverless jobs and pipelines from history, plus the performance-mode factor (confidence Low)
- [x] 3.4 SQL warehouses and all-purpose clusters from observed DBUs
- [x] 3.5 Confidence rules (`calibration.min_runs` or 7 days for High)
- [x] 3.6 Drift check through `system.compute.node_timeline`, warning above 10%

## 4. Configuration and failure handling

- [x] 4.1 `calibration.*` settings, schema and validation (`warehouse_id` required when enabled)
- [x] 4.2 Degrade gracefully: warn about missing permissions or an unreachable warehouse, continue without history, keep the exit code

## 5. Docs and release

- [x] 5.1 Docs: least-privilege setup, the cost of calibration, an example workflow with GitHub OIDC and `actions/cache`
- [x] 5.2 Golden tests with recorded query results
- [ ] 5.3 Calibrate on a real Azure workspace and compare the estimates with the following month's billing
  - Needs a workspace with deployed bundles and billing history, then a month of billing to compare against
- [ ] 5.4 Release 0.3, then archive this change
