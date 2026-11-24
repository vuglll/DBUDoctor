## ADDED Requirements

### Requirement: DBU016 classic job cluster on a short schedule
The system SHALL report an info finding for a job that runs on a classic job cluster without `instance_pool_id` and whose schedule or periodic trigger has a shortest interval between runs under `rules.DBU016.min_interval_minutes` (default 15). The message SHALL say that every run pays for cluster start-up, give the runs per month, and suggest serverless, a pool or a continuous job.

#### Scenario: Every five minutes on classic compute
- **WHEN** a job on a classic job cluster uses `0 0/5 * * * ?` in UTC
- **THEN** DBU016 is reported as info, and the message says the job starts a cluster about 8,760 times a month (12 runs an hour × 730 hours)

#### Scenario: Serverless or pooled
- **WHEN** the same schedule runs on serverless compute, or on a job cluster with `instance_pool_id`
- **THEN** DBU016 is not reported

#### Scenario: Periodic trigger
- **WHEN** `rules.DBU016.min_interval_minutes` is 120 and a job on a classic job cluster has a periodic trigger every hour (periodic triggers count in hours, days or weeks)
- **THEN** DBU016 is reported

#### Scenario: Interval at the threshold
- **WHEN** a job on a classic job cluster runs every 15 minutes
- **THEN** DBU016 is not reported

#### Scenario: Paused schedule
- **WHEN** the short schedule is paused
- **THEN** DBU016 is not reported
