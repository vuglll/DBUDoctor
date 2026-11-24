## ADDED Requirements

### Requirement: DBU015 Lakebase endpoint sizing
The system SHALL report a warning for a Lakebase endpoint with scale-to-zero disabled (`no_suspension: true`) on a branch that is not a production branch, and an info finding for any endpoint whose `autoscaling_limit_min_cu` is at least `rules.DBU015.max_min_cu` (default 8). Production branches SHALL be the branch with ID `production` and any branch listed in `rules.DBU015.production_branches`.

#### Scenario: Always-on development branch
- **WHEN** an endpoint on branch `dev` sets `no_suspension: true`
- **THEN** DBU015 is reported as a warning

#### Scenario: Always-on production branch
- **WHEN** an endpoint on branch `production` sets `no_suspension: true` with `autoscaling_limit_min_cu: 2`
- **THEN** DBU015 is not reported

#### Scenario: High minimum
- **WHEN** any endpoint sets `autoscaling_limit_min_cu: 8`
- **THEN** DBU015 is reported as info
