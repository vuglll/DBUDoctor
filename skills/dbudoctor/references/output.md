# DBUDoctor JSON output

Generated for DBUDoctor 0.4.0. `dbudoctor diff --format json` prints one JSON document. The schema is `schemas/report.schema.json` (`schemaVersion: 1`); fields are only ever added.

Amounts are unrounded decimals in `currency` (USD list prices). Monthly figures assume a 730-hour month.

## Top level

| Field | Meaning |
|---|---|
| `schemaVersion` | Always 1 for this layout. |
| `tool.name, tool.version` | `dbudoctor` and the version that produced the report. |
| `exitCode` | The process exit code: 0 or 1. |
| `policyViolated` | True when any bundle violates a `fail_on` policy. |
| `bundles[]` | One report per bundle and target. |

## bundles[]

| Field | Meaning |
|---|---|
| `bundle, target, root` | Bundle name, target, and bundle root relative to the repository (null for JSON input). |
| `cloud, region, currency` | Where prices come from. A null region makes rates Unknown. |
| `catalogAsOf` | The oldest catalog date used for this report. |
| `cloudInfraIncluded, commitment` | Whether classic rates include cloud VMs, and the VM price column used. |
| `tier` | The plan tier of the list prices: `premium` or `enterprise`. |
| `scaling, autoscale, defaultRunMinutes` | Estimation assumptions from `.dbudoctor.yml`. |
| `calibration` | Null unless calibration is on; then `target`, `windowDays`, `calibratedChanges`, `notFound`, `livePrices` and `notes[]`. |
| `hasCostRelevantChanges` | False when nothing priced changed and there are no new findings. |
| `totals.monthlyDelta, totals.baseMonthly` | Headline monthly change and the base total of the changed resources it includes (Medium and High only). |
| `totals.percentChange` | Delta as a percentage of `baseMonthly`; null when that is 0. |
| `totals.includedChanges, totals.excludedChanges` | How many changed resources are in the headline, and how many are not. |
| `changes[]` | One entry per added, removed or modified priced resource. |
| `findings[]` | Rule findings: new ones, plus existing ones with `--report-existing`. |
| `suppressedFindings, existingFindingsHidden` | Findings suppressed in `.dbudoctor.yml`, and pre-existing findings not shown. |
| `possibleRenames[]` | A removal and an addition with identical settings (`type`, `from`, `to`). |
| `notPriced[]` | Changed resources of types this version does not price. |
| `assumptions[], notModeled[], warnings[]` | Report-level assumptions, cost categories not modeled, and warnings (such as a missing region). |
| `policy.violated, policy.violations[], policy.notes[]` | Policy results; notes say when a policy was skipped. |

## changes[]

| Field | Meaning |
|---|---|
| `type, key, kind` | Resource type (`jobs`, `pipelines`, `clusters`, `sql_warehouses`), key, and `added`, `removed` or `modified`. |
| `summary` | What changed, such as `schedule hourly → every 15 min`. |
| `category` | `monthly` (has a monthly figure), `rate` (hourly rate only), `needs_history` (serverless) or `unknown` (rate unknown). |
| `confidence` | `High`, `Medium`, `Low` or `Unknown`. |
| `inTotals` | True when the change is in the headline totals. |
| `monthlyDelta` | After minus before, per month; null without a monthly figure on both sides. |
| `notes[]` | Caveats, such as a classic/serverless comparison without VM prices. |
| `before, after` | The estimate on each side; null when the resource is absent on that side. |

## before / after estimates

| Field | Meaning |
|---|---|
| `sku, pricePerDbu` | DBU SKU and effective $/DBU after discounts, surcharge and overrides. |
| `dbuPerHour, dbuCostPerHour, vmCostPerHour, ratePerHour` | Hourly DBUs and costs; `ratePerHour` is the cost per active hour. |
| `serverless, cloudInfraIncluded` | Compute kind, and whether VMs are in the rate. |
| `runsPerMonth, frequency` | Runs per month (null when unknown) and a description such as `hourly`. |
| `monthly, confidence, monthlyNote` | Monthly figure (null when Unknown), its confidence, and why it is missing. |
| `monthlyCeiling` | The most a month can cost at the maximum size, for Lakebase endpoints; null otherwise. |
| `history` | Observed usage behind a calibrated figure (`runs`, `days`, `windowDays`, `dbusPerRun`, `monthlyDbus`); null when the figure comes from configuration. |
| `assumptions[], unknownReasons[]` | What the estimate assumes, and why the rate is unknown. |

## findings[]

| Field | Meaning |
|---|---|
| `ruleId, severity` | Rule ID such as `DBU001`, and `info`, `warning` or `error`. |
| `resourceType, resourceKey, fieldPath` | Where the finding is, such as `jobs`, `nightly`, `tasks[refresh].existing_cluster_id`. |
| `message, docsUrl` | What is wrong, and the rule's documentation page. |
| `existing` | True when the finding also exists in base (only with `--report-existing`). |
| `location` | `file` and `line` when the Databricks CLI supplied locations; otherwise null. |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | No configured policy is violated. |
| 1 | A `fail_on` policy is violated; see `bundles[].policy.violations`. |
| 2 | Invalid input (configuration, bundle JSON, missing CLI) or an internal error. Nothing is printed to standard output. |
