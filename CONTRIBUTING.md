# Contributing to DBUDoctor

Thanks for helping. DBUDoctor's value is trust: a number must say how it was obtained, and a finding must explain itself and name the cases where it doesn't apply.

Everyone taking part follows the [code of conduct](CODE_OF_CONDUCT.md). Security issues go through a private report, not an issue: see [SECURITY.md](SECURITY.md).

## Setup

You need the .NET 10 SDK. For spec work you also need the [OpenSpec](https://github.com/Fission-AI/OpenSpec) CLI, and to try git mode, the [Databricks CLI](https://docs.databricks.com/dev-tools/cli/install.html). The PyPI wheel builder in `tools/wheels/` needs Python 3.9 or later (`python3 -m unittest discover -s tools/wheels`).

```bash
dotnet restore DBUDoctor.slnx
dotnet build DBUDoctor.slnx --no-restore
dotnet test DBUDoctor.slnx --no-build
```

The build treats warnings as errors, including the Native AOT and trim analyzers. After adding a dependency or a new serialized type, also run an AOT publish for your platform:

```bash
dotnet publish src/DBUDoctor.Cli -c Release -r osx-arm64   # or linux-x64, linux-arm64, win-x64
```

Ground rules (they come from `openspec/config.yaml`):

- Money and DBU arithmetic use `decimal`. Round only when formatting.
- Prices, node-type DBU rates and warehouse sizes live in `catalog/`, never in code, each with `as_of` and `source`.
- The cost engine and the rules are pure functions of the two resolved documents, the configuration and the catalog. Network access stays at the edges (`--refresh-prices`, GitHub comments).
- Exit codes and the `--format json` output are a public contract: change them only additively.
- Never hand-edit generated files: `catalog/prices/azure/` (run `tools/CatalogGen`) and `skills/dbudoctor/` (run `dbudoctor skill export --dir skills/dbudoctor --force`).
- Golden files (`tests/Fixtures/bundles/*/expected.*`) change only when an output change is intended: `UPDATE_GOLDEN=1 dotnet test`, and say why in the commit message.

## How changes are made

Every behavior change goes through OpenSpec: propose `openspec/changes/<name>/` (proposal, design, spec deltas, tasks), discuss it in a draft pull request, implement against the tasks, then `openspec archive <name>`.

### Branches

- **`dev`** is the default branch. Open pull requests against `dev`; they are squash-merged.
- **`main`** holds releases only.

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/): `feat(rules): …`, `fix(cost): …`, `chore(catalog): …`, `docs(openspec): …`.

### Cutting a release

1. On `dev`, set `<Version>` in `Directory.Build.props`, regenerate the agent skill (it states the version), and turn the **Unreleased** section of `CHANGELOG.md` into `## X.Y.Z`.
2. Merge `dev` into `main` through a pull request, using **Create a merge commit**. Squashing would give `main` a commit that `dev` doesn't have, and later release pull requests would show old changes again.
3. Tag the merge commit on `main` and push the tag:

   ```bash
   git tag vX.Y.Z && git push origin vX.Y.Z
   ```

   The `Release` workflow checks the tag against `main`, `<Version>`, and the changelog. It runs the tests and publishes the Native AOT binaries, checksums, .NET tool package, and agent skill to GitHub Releases. After configuring Trusted Publishing, set the repository Actions variable `PUBLISH_NUGET` or `PUBLISH_PYPI` to `true` to enable the corresponding job. NuGet uses the `nuget` environment and the `NUGET_USER` variable. PyPI uses the `pypi` environment.

   Run the manual **PyPI test upload** workflow to verify packaging through the `testpypi` environment before publishing to PyPI. It uploads `<version>.dev<run>` to TestPyPI. Each uploaded version can be used only once.
4. Merge `main` back into `dev` with a merge commit, so the release merge commit is in `dev`'s history too.

### The price catalog

The weekly `Refresh the catalog` workflow runs `tools/CatalogGen` and opens a pull request with the price changes; review it like code. It generates `catalog/prices/azure/` from the Azure Retail Prices API, and `catalog/prices/{aws,gcp}/` plus `catalog/node-types/{aws,gcp}.yaml` from the Databricks pricing calculator data. To run it yourself:

```bash
dotnet run --project tools/CatalogGen -- --catalog catalog                       # every cloud and region
dotnet run --project tools/CatalogGen -- --catalog catalog --clouds aws,gcp
dotnet run --project tools/CatalogGen -- --catalog catalog --clouds azure --regions westeurope,eastus
```

The summary lists price changes, values left out because the source contradicts itself, and workload or region names the generator doesn't know yet (add them to the mapping tables in `src/DBUDoctor.Core/Pricing/`).

`catalog/node-types/azure.yaml` has no API: it comes from the DBU Count tables on the Azure pricing page. Cite the page in `source` and update `as_of` when you change it.

## Adding a rule

Rules ship one OpenSpec change at a time (design D14), in the order in [`docs/roadmap.md`](docs/roadmap.md):

1. Open a change named `add-dbu0xx-<slug>`. Its `specs/guardrail-rules/spec.md` holds `## ADDED Requirements` with the draft from [`docs/rule-drafts.md`](docs/rule-drafts.md). Re-check the field names and sources first: drafts age.
2. Commit `docs(openspec): propose DBU0xx, <title>`.
3. Implement `feat(rules): add DBU0xx, <title>`:
   - a class implementing `IRule` in `src/DBUDoctor.Core/Rules/`, added to `RuleRegistry.All`. The engine, the diff and the reporters do not change;
   - complete `RuleMetadata`: title, when it fires, how to fix it, when to suppress it. A rule without them does not build, and the skill generator rejects empty values;
   - positive, negative and new-only tests, and a fixture bundle;
   - `docs/rules/DBU0xx.md` with **Why it matters**, **Example**, **How to fix** and **When to suppress** (a test checks the sections);
   - the regenerated agent skill (`dbudoctor skill export --dir skills/dbudoctor --force`).
4. After merge, archive the change with `openspec archive`.
5. Release when two to four rules are merged on `dev`.

Rule IDs are reserved in the roadmap and never reused.
