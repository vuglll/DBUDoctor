---
name: dbudoctor
description: Estimates what a Databricks bundle change will cost and flags costly configuration. Use after editing databricks.yml or bundle resources (jobs, pipelines, clusters, SQL warehouses), before opening a pull request that touches them, when asked what a bundle change will cost, and when asked to fix DBUDoctor findings.
---

# DBUDoctor

Generated for DBUDoctor 0.4.0 from rule metadata. Do not edit by hand: run `dbudoctor skill export --dir <path> --force` to update it.

DBUDoctor compares a Databricks bundle at two git refs and reports the cost change per resource, with a confidence level for every monthly figure, plus findings for costly patterns.

## When to use

- After you edit bundle configuration: `databricks.yml`, or any included resource YAML for jobs, pipelines, clusters or SQL warehouses.
- Before you open a pull request that changes bundle configuration.
- When the user asks what a change will cost, or asks you to fix the findings in a DBUDoctor pull request comment.

## How to run

From the repository root, with your edits in the working tree:

```bash
dbudoctor diff --format json
```

- It compares the working tree with the merge-base of `HEAD` and the remote default branch. Use `--base <ref>` to compare with another ref.
- It estimates the first target with `mode: production`. Use `--target <name>` for another target.
- If `bundle validate` fails with "no value assigned to required variable X", ask the user for the value and pass `--var X=<value>` (repeatable).
- It needs `git` and the Databricks CLI (v0.239.0 or later), which runs `databricks bundle validate`. If the CLI needs workspace authentication, use the user's existing profile; never ask for or print tokens.
- With pre-resolved JSON, run `dbudoctor diff --base-json base.json --head-json head.json --format json` instead.

If `dbudoctor` is not on PATH, install it with `pip install dbudoctor`, or `dotnet tool install --global DBUDoctor` (needs the .NET 10 SDK), or download a binary for your platform from https://github.com/vuglll/DBUDoctor/releases and check its SHA-256 checksum. Ask the user before installing anything.

## Reading results

Exit codes:

- `0`: no configured policy is violated.
- `1`: a policy in `.dbudoctor.yml` (`fail_on`) is violated; `policy.violations` says which.
- `2`: invalid input or an internal error; the message is on standard error. Fix the input, do not retry blindly.

Confidence levels:

- `High`: calibrated from observed history.
- `Medium`: frequency from configuration and a configured run duration, or a resource that runs all month.
- `Low`: a comparison that is not like for like, such as classic compute without VM prices against serverless.
- `Unknown`: no monthly figure; only an hourly rate, or "needs history".

When you report results to the user:

- Give the confidence with every monthly figure, for example "+$1,657 / month (Medium: 20-minute runs configured)".
- Never present an Unknown or Low figure as a fact. Say what it depends on.
- Give rate-only changes as hourly rates ("$8.40 → $16.80 per active hour"), not as monthly guesses.
- Mention the costs the report lists as not modeled when they matter, and that figures are list-price estimates, not an invoice.

Field reference: [references/output.md](references/output.md).

## Fixing findings

Each finding has a `ruleId`, a resource, a `fieldPath` and a message. Look up the rule in [references/rules.md](references/rules.md) for how to fix it and when suppressing it is right:

- `DBU001` Task runs on all-purpose compute
- `DBU002` All-purpose cluster auto-termination
- `DBU003` SQL warehouse auto-stop
- `DBU004` Continuous pipeline
- `DBU005` Continuous job
- `DBU007` Job without a timeout
- `DBU008` Missing cost-attribution tags
- `DBU009` Large on-demand cluster
- `DBU010` Sharp frequency increase
- `DBU011` Scheduled serverless job in performance-optimized mode
- `DBU012` Outdated or end-of-support runtime
- `DBU013` Large Databricks App
- `DBU014` Serving endpoint without scale-to-zero
- `DBU015` Lakebase endpoint sizing
- `DBU016` Classic job cluster on a short schedule
- `DBU017` Spot without fallback
- `DBU018` Pipeline cluster on legacy autoscaling
- `DBU019` Custom tag collides with a default tag

After a fix, run `dbudoctor diff --format json` again and check that the finding is gone and the cost change is what the user expects.

## Guardrails

- Never weaken production configuration only to silence a finding: do not shrink clusters, pause schedules, remove targets or change `mode` just to make DBUDoctor pass.
- When a finding is intended, suggest a suppression with a reason instead, and add it only with the user's agreement:

  ```yaml
  # .dbudoctor.yml
  suppressions:
    - rule: DBU001
      resource: jobs.nightly_report
      reason: Needs the shared cluster's cached tables until the Q1 migration (JIRA-123)
  ```

- Do not edit `.dbudoctor.yml` policies (`fail_on`) to make a check pass.
- Do not invent prices or durations. If a figure is Unknown, say so and say what would make it known (a `region`, `estimation.default_run_minutes`, or calibration).
