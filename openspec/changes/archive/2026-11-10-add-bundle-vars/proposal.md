## Why

Bundles often declare variables without a default, such as `cluster_pool_id`, which deploy pipelines pass with `--var`. `databricks bundle validate` fails without them, so `dbudoctor diff` fails before it can price anything. Today the only way around it is a `BUNDLE_VAR_<name>` environment variable per value. A user hit this on a real bundle on 2026-11-10 ("no value assigned to required variable cluster_pool_id").

## What Changes

- `dbudoctor diff --var name=value`, repeatable. Each value is passed to the Databricks CLI as `--var name=value` for `bundle validate` at both refs, and for `bundle summary` when calibration runs.
- Names must be valid bundle variable names (letters, digits, `_` and `-`, not starting with a digit); a malformed `--var` exits 2 with a message. `--var` together with `--base-json`/`--head-json` exits 2, since no CLI runs.
- When `bundle validate` fails with "no value assigned to required variable X", the error adds a hint: pass `--var X=<value>` or set `BUNDLE_VAR_X`.
- The GitHub Action needs no change: its `args` input passes `--var`.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `bundle-diff`: bundle variables for `bundle validate`.

## Impact

- Code: the `diff` option, `DiffArguments`, `DatabricksCli.Validate` and `Summary`, the error hint.
- Docs: README (usage, Action example), the agent skill (regenerated, since it lists options), CHANGELOG.
