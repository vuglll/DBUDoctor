# Research notes: additional rules and Azure cost (2026-11-08)

These are the sources behind DBUDoctor's foundation change (`add-pr-cost-diff`) and the rules planned in `docs/roadmap.md`. Re-verify the prices before release. Third-party figures are marked as such.

## Azure cost facts

- **Standard tier is gone.** It was retired on October 1, 2026, and new Standard workspaces stopped on April 1, 2026, so model Premium only. Remaining Standard workspaces were upgraded to Premium automatically.
  - Azure pricing page: https://azure.microsoft.com/en-us/pricing/details/databricks/
  - https://www.usage.ai/blogs/azure/databricks-cost-commitments/
- **Two bills on classic compute.** Azure bills VMs and DBUs, plus managed disks, blob storage and public IP addresses. Enabling Photon increases the DBU count.
  - Azure pricing page footnotes: https://azure.microsoft.com/en-in/pricing/details/databricks/
- **The VM half can dominate.** A Small classic SQL warehouse is about $2.64/h in DBUs plus $3.89/h in VMs. https://www.doit.com/blog/databricks-pricing/
- **VM billing starts early.** VMs are charged from the "Starting" phase until the cores are released. https://learn.microsoft.com/en-us/answers/questions/1862844/visual-studio-50-credit-and-databricks-issues
- **Azure Premium $/DBU, checked 5 Nov 2026 (third party, region not stated):** Jobs 0.30, DLT/pipelines Core 0.30, All-purpose 0.55, SQL Pro 0.55, Jobs Serverless 0.47, Serverless SQL 0.70, and Serverless All-purpose at about $1, which appears in the text only. https://www.activepieces.com/blog/azure-databricks-pricing-dbu-rates-and-costs-2026.md
  - Official cross-check: DBCU draw-down for Jobs compute Premium is 0.30 units per DBU. https://docs.azure.cn/en-us/cost-management-billing/reservations/prepay-databricks-reserved-capacity
- **DBU count per VM** comes from the pricing page only (not from the API): DS3 v2 0.75, DS4 v2 1.5, DS5 v2 3.0; D4ds v4 0.75, D8ds v4 1.5.
  - https://azure.microsoft.com/zh-tw/pricing/details/databricks/
  - https://www.azure.cn/en-us/pricing/details/databricks/
- **SQL warehouse DBU/h:** 2X-Small 4, X-Small 6, Small 12, Medium 24, Large 40, X-Large 80, 2X-Large 144, 3X-Large 272, 4X-Large 528. https://learn.microsoft.com/azure/databricks/resources/pricing
- **Retail Prices API** (`https://prices.azure.com/api/retail/prices`):
  - public and unauthenticated;
  - `serviceName` values `Azure Databricks` and `Virtual Machines`, case-sensitive in `2023-01-01-preview`;
  - reservation prices via `priceType`; savings-plan prices only in the preview API version;
  - USD is official, other currencies are for reference.
  - https://learn.microsoft.com/rest/api/cost-management/retail-prices/azure-retail-prices
- **DBCU pre-purchase.**
  - Up to 37% off pay-as-you-go DBUs, for 1 or 3 years. It applies to DBUs only, not to VMs, storage or networking.
  - Usage draws down at the workload's list price, across all workloads and tiers.
  - https://learn.microsoft.com/en-ca/Azure/cost-management-billing/reservations/reservation-discount-databricks
  - Older tier table (1-year: 25k DBCU at 6% … 2M at 33%; 3-year: 75k at 8% … 6M at 37%): https://synccomputing.com/?p=1943
- **VM commitments.** Reserved VM Instances, savings plans and spot apply to classic VMs only. https://www.usage.ai/blogs/azure/databricks-cost-commitments/
- **Mission Critical add-on.** 30% of list price is added to Azure Databricks spend in the workspace. https://azure.microsoft.com/hu-hu/pricing/details/databricks/
- **Default tags** propagate to VMs, NICs and disks: `Vendor`, `ClusterId`, `ClusterName`, `Creator`, `RunName`, `JobId` (jobs compute only), `SqlEndpointId` on warehouses, and pool tags on pools.
  - A custom tag whose key matches a default key is renamed with an `x_` prefix.
  - Pool-backed clusters lose cluster tags on the cloud invoice, so put the tags on the pool, or use `system.billing.usage` for attribution.
  - https://learn.microsoft.com/en-us/azure/databricks/admin/account-settings/usage-detail-tags
  - https://databricks-solutions.github.io/starter-journey/docs/04-cost-monitoring/tag-compute-and-jobs
- **Databricks Apps.**
  - Sizes: Medium 0.5, Large 1, XLarge 3 DBU/h; Medium is the default. https://docs.databricks.com/aws/en/dev-tools/databricks-apps/compute-size
  - Billed against the Interactive Serverless SKU. https://learn.microsoft.com/en-us/azure/databricks/resources/pricing
  - Billed the whole time the app is Running; there is no GA idle stop, and a deploy starts the app.
    - https://community.databricks.com/t5/data-engineering/query-on-databricks-apps-idle-time-out/m-p/174695
    - https://community.databricks.com/t5/technical-blog/databricks-apps-for-platform-admins-part-1-backend-architecture/ba-p/152208
- **Lakebase Autoscaling.**
  - Price: $0.111/CU-hour (AWS list; varies by cloud and region). Storage is $0.35/GB-month and PITR $0.20/GB-month. https://docs.databricks.com/aws/en/oltp/projects/pricing
  - DBU equivalence: 0.213 DBU of the Database Serverless SKU per CU-hour. https://learn.microsoft.com/en-us/azure/databricks/resources/pricing
  - Defaults through the API and CLI: the production branch is 1 CU with scale-to-zero disabled; child branches have scale-to-zero on (5 minutes); the UI uses higher defaults. https://developers.databricks.com/docs/lakebase/configuration
  - Always-On pricing gives a 25% lower price on baseline capacity when scale-to-zero is off (secondary source; find the Databricks blog). https://traeai.com/articles/1fdadd49-b41a-411e-963c-4317875a9d71
  - The bundle resource is `postgres_projects`, with endpoints and branches. https://learn.microsoft.com/en-us/Azure/databricks/oltp/projects/manage-with-bundles

## Evidence for rules DBU012–DBU019 (roadmap)

| Rule | Evidence |
|---|---|
| DBU001 (updated) | All-purpose to Jobs ratio differs by cloud: AWS 0.15 vs 0.55, Azure 0.30 vs 0.55 (sources above) |
| DBU012 runtime EoS | "Use up-to-date runtimes" (cost best practices); EoS dates: 13.3 LTS Aug 22 2026, 14.3 LTS Feb 1 2027, 15.4 LTS Aug 19 2027, 16.4 LTS May 9 2028, 17.3 LTS Oct 22 2028, 18 LTS Jun 10 2029. https://learn.microsoft.com/azure/databricks/lakehouse-architecture/cost-optimization/best-practices · https://docs.databricks.com/aws/en/release-notes/runtime/eos |
| DBU013 large app | Apps sizes and always-running billing (above) |
| DBU014 serving scale-to-zero | Without scale-to-zero, the minimum charge depends on minimum provisioned concurrency; scale-to-zero is not recommended for production. https://docs.databricks.com/aws/en/machine-learning/model-serving/create-manage-serving-endpoints · https://community.databricks.com/t5/generative-ai/model-serving-endpoints-scaling-from-zero-forever/m-p/150047 |
| DBU015 Lakebase sizing | Defaults and scale-to-zero docs (above); the production branch is always-on by design |
| DBU016 short schedule | Each classic run pays for startup; pools and serverless avoid it; Azure bills VMs from Starting (above) |
| DBU017 spot without fallback | Microsoft guidance on spot for critical jobs (`first_on_demand`, fallback); Datadog IaC rule. https://techcommunity.microsoft.com/blog/microsoftmissioncriticalblog/-/4402018 · https://docs.datadoghq.com/security/code_security/iac_security/iac_rules/terraform-databricks-cluster-azure-attributes/ |
| DBU018 legacy autoscaling | Enhanced autoscaling optimizes cost and is the default for new pipelines. https://docs.databricks.com/aws/en/ldp/auto-scaling |
| DBU019 tag collision | `x_` renaming of colliding custom tags (tags doc above) |

## Backlog: rule candidates after DBU019 (IDs from DBU020)

- Quality monitor refreshed more often than daily. Data profiling runs on its own compute, which is tagged `LakehouseMonitoring`.
- Overlapping runs: `max_concurrent_runs` > 1 with a schedule shorter than the observed runtime (needs calibration).
- Retry amplification: high `max_retries` together with `retry_on_timeout: true`.
- Tasks that each have their own `new_cluster` instead of a shared job cluster, so every task pays for startup.
- GPU node type with a non-ML runtime.
- `warehouse_type: CLASSIC` for a new warehouse.
- Large jump in a warehouse's `max_num_clusters` (diff rule).
- Clusters without `policy_id` (governance, opt-in).
- Photon on short, small jobs (needs calibration).
- Continuous serverless pipelines: Standard performance mode applies only to triggered pipelines.
- Pool-backed clusters with required tags (tag the pool instead).
- Model Serving GPU workload types (GPU DBU/h: T4 10.48, A10G 20, and so on). https://docs.databricks.com/aws/resources/pricing
- Vector Search endpoints (Standard 4 DBU/h per unit, storage-optimized 18.29). They are not a bundle resource today.

## Still to verify

- Bundle field names for the app compute size and for Lakebase endpoints (run `databricks bundle schema`).
- Azure Photon DBU counts per VM family, and the 5X-Large warehouse DBU/h.
- Retail Prices meter names for the serverless and Database Serverless SKUs.
- Whether Lakebase Always-On pricing applies on Azure, and how it is applied.
- The official current Azure $/DBU per region, taken from the Retail Prices API rather than third-party trackers.
