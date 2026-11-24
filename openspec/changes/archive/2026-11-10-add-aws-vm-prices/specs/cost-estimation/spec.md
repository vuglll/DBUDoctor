## ADDED Requirements

### Requirement: AWS VM prices from the AWS Price List
The system SHALL generate AWS VM prices from the public AWS Price List bulk files, which need no credentials: per region and per instance type in the AWS node-type table, the On-Demand price as `payg` and Standard, No Upfront 1-year and 3-year reservations as `reserved_1y` and `reserved_3y`, all per hour. Only Linux, shared-tenancy rows without pre-installed software and with capacity status `Used` SHALL count. Conflicting values SHALL be left out and reported by the generator.

#### Scenario: AWS classic job cluster
- **WHEN** an AWS job cluster has a driver and 2 workers of `m5.xlarge`, rated 0.69 DBU per hour, with an On-Demand price of 0.192 per hour, and Jobs compute costs 0.15 per DBU
- **THEN** the rate is 3 × 0.69 × 0.15 + 3 × 0.192 = 0.8865 per hour, shown as 0.3105 for DBUs and 0.576 for VMs

#### Scenario: AWS spot commitment
- **WHEN** the cloud is AWS and `pricing.infra.commitment` is `spot`, which the AWS catalog does not hold
- **THEN** the VM part of classic rates is Unknown, with the reason that there is no spot VM price for the node type in the region

### Requirement: AWS SQL warehouse VM layout
The system SHALL price the VMs of classic and pro SQL warehouses on AWS with the documented layout: an `i3.2xlarge` driver for 2X-Small and X-Small, `i3.4xlarge` for Small, `i3.8xlarge` for Medium and Large, and `i3.16xlarge` from X-Large up, with `i3.2xlarge` workers in the same counts as on Azure.

#### Scenario: AWS pro warehouse
- **WHEN** an AWS pro warehouse is Small with one cluster
- **THEN** its VM part is one `i3.4xlarge` plus 4 × `i3.2xlarge` at the selected commitment

## MODIFIED Requirements

### Requirement: AWS and GCP prices from the Databricks pricing data
The system SHALL ship AWS and GCP catalogs generated from the data behind the Databricks pricing calculator. They SHALL contain, per region, list $/DBU for the Premium and Enterprise tiers, and per cloud, DBU per hour for each instance type, with Photon DBU counts where published. GCP catalogs SHALL contain no VM prices; AWS VM prices come from the AWS Price List. Conflicting values for one SKU, tier and region SHALL be left out of the catalog and reported by the generator, not resolved by guessing.

#### Scenario: AWS region
- **WHEN** the cloud is AWS and `region` is `us-east-1`
- **THEN** Jobs compute is priced from the `us-east-1` catalog entry, and an `m5.xlarge` node is rated at its published DBU per hour

#### Scenario: Region-specific serverless price
- **WHEN** the cloud is GCP and SQL serverless has a different list price in `asia-northeast1` than in `us-central1`
- **THEN** each region's catalog file holds its own price

#### Scenario: Conflicting source values
- **WHEN** the pricing data lists two different Enterprise SQL pro prices for one region
- **THEN** that price is absent from the region's catalog file, the generator's summary names it, and rates that need it are Unknown

### Requirement: Cloud infrastructure for classic compute
The system SHALL add VM cost for the driver and workers of classic compute in either case:
- `pricing.include_cloud_infra` is `true`;
- `pricing.include_cloud_infra` is `auto` (the default) and the catalog has VM prices for the cloud and region.

It SHALL use the VM price column selected by `pricing.infra.commitment`, and SHALL never add infrastructure cost to serverless resources.

#### Scenario: Azure classic job cluster
- **WHEN** an Azure job cluster has a driver and 4 workers of a VM type rated 0.75 DBU per hour with a pay-as-you-go VM price of 0.229 per hour, and Jobs compute costs 0.30 per DBU
- **THEN** the rate is (0.75 + 4 × 0.75) × 0.30 + 5 × 0.229 = 2.27 per hour, shown as 1.125 for DBUs and 1.145 for VMs

#### Scenario: Cloud without VM prices
- **WHEN** the cloud is GCP, `pricing.include_cloud_infra` is `auto`, and the catalog has no GCP VM prices
- **THEN** classic rates contain DBUs only, and the report states that cloud VMs are not included

#### Scenario: Reserved VMs
- **WHEN** `pricing.infra.commitment` is `reserved_3y`
- **THEN** the VM part of every classic rate uses the catalog's 3-year reservation price for the node type
