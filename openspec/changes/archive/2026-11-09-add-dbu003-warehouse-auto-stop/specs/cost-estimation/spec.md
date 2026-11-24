## ADDED Requirements

### Requirement: Platform defaults catalog
The catalog SHALL hold platform defaults that apply when a bundle omits a field in `defaults.yaml`, with an `as_of` date and a `source`. It SHALL start with `sql_warehouse.auto_stop_mins`. The embedded catalog SHALL include the file; a catalog directory without it SHALL still load, with no defaults.

#### Scenario: Default from the catalog
- **WHEN** the catalog's `defaults.yaml` sets `sql_warehouse.auto_stop_mins: 120`
- **THEN** a warehouse without `auto_stop_mins` is treated as stopping after 120 idle minutes

#### Scenario: Catalog without defaults
- **WHEN** a catalog directory has no `defaults.yaml`
- **THEN** the catalog loads, and no default applies
