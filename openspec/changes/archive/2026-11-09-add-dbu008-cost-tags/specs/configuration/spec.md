## ADDED Requirements

### Requirement: List-valued rule settings
The system SHALL accept list-valued rule parameters declared by a rule, such as `rules.DBU008.required_tags`, as a list of strings. It SHALL reject a scalar where a list is expected, and an undeclared key, with exit code 2 and the file and line.

#### Scenario: Required tags
- **WHEN** `.dbudoctor.yml` sets `rules.DBU008.required_tags: [team, cost_center]`
- **THEN** DBU008 checks both keys

#### Scenario: Scalar instead of a list
- **WHEN** `.dbudoctor.yml` sets `rules.DBU008.required_tags: team`
- **THEN** DBUDoctor exits with code 2 and names `rules.DBU008.required_tags`
