using System.Globalization;
using System.Text;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Rules;

namespace DBUDoctor.Core.Skill;

/// <summary>
/// Generates the agent skill (design D15) in the Agent Skills layout. Per-rule content comes only from
/// rule metadata; output fields come from <see cref="OutputFields"/>. Never edit the generated files.
/// </summary>
public static class SkillGenerator
{
    public const int MaxSkillLines = 200;
    public const string SkillFile = "SKILL.md";
    public const string RulesFile = "references/rules.md";
    public const string OutputFile = "references/output.md";

    /// <summary>Generated files by relative path, with '\n' line endings.</summary>
    public static IReadOnlyDictionary<string, string> Generate(IReadOnlyList<RuleMetadata> rules, string version)
    {
        Validate(rules);
        var skill = Skill(rules, version);
        var lines = skill.Count(c => c == '\n');
        if (lines >= MaxSkillLines)
        {
            throw new DoctorException($"{SkillFile} has {lines} lines; the budget is under {MaxSkillLines}. Move detail into the reference files.");
        }

        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [SkillFile] = skill,
            [RulesFile] = RulesReference(rules, version),
            [OutputFile] = OutputReference(version),
        };
    }

    /// <summary>Fails, naming the rule, when any metadata field is missing.</summary>
    public static void Validate(IEnumerable<RuleMetadata> rules)
    {
        foreach (var rule in rules)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(rule.Title)) missing.Add("title");
            if (string.IsNullOrWhiteSpace(rule.WhenItFires)) missing.Add("when it fires");
            if (string.IsNullOrWhiteSpace(rule.HowToFix)) missing.Add("how to fix");
            if (string.IsNullOrWhiteSpace(rule.WhenToSuppress)) missing.Add("when to suppress");
            if (missing.Count > 0)
            {
                throw new DoctorException($"Rule {rule.Id} is missing metadata: {string.Join(", ", missing)}. Every rule needs a title, when it fires, how to fix it and when to suppress it.");
            }
        }
    }

    private static string Skill(IReadOnlyList<RuleMetadata> rules, string version)
    {
        var b = new StringBuilder();
        b.Append("---\n");
        b.Append("name: dbudoctor\n");
        b.Append("description: Estimates what a Databricks bundle change will cost and flags costly configuration. Use after editing databricks.yml or bundle resources (jobs, pipelines, clusters, SQL warehouses), before opening a pull request that touches them, when asked what a bundle change will cost, and when asked to fix DBUDoctor findings.\n");
        b.Append("---\n\n");
        b.Append("# DBUDoctor\n\n");
        b.Append("Generated for DBUDoctor ").Append(version).Append(" from rule metadata. Do not edit by hand: run `dbudoctor skill export --dir <path> --force` to update it.\n\n");
        b.Append("DBUDoctor compares a Databricks bundle at two git refs and reports the cost change per resource, with a confidence level for every monthly figure, plus findings for costly patterns.\n\n");

        b.Append("## When to use\n\n");
        b.Append("- After you edit bundle configuration: `databricks.yml`, or any included resource YAML for jobs, pipelines, clusters or SQL warehouses.\n");
        b.Append("- Before you open a pull request that changes bundle configuration.\n");
        b.Append("- When the user asks what a change will cost, or asks you to fix the findings in a DBUDoctor pull request comment.\n\n");

        b.Append("## How to run\n\n");
        b.Append("From the repository root, with your edits in the working tree:\n\n");
        b.Append("```bash\ndbudoctor diff --format json\n```\n\n");
        b.Append("- It compares the working tree with the merge-base of `HEAD` and the remote default branch. Use `--base <ref>` to compare with another ref.\n");
        b.Append("- It estimates the first target with `mode: production`. Use `--target <name>` for another target.\n");
        b.Append("- If `bundle validate` fails with \"no value assigned to required variable X\", ask the user for the value and pass `--var X=<value>` (repeatable).\n");
        b.Append("- It needs `git` and the Databricks CLI (v0.239.0 or later), which runs `databricks bundle validate`. If the CLI needs workspace authentication, use the user's existing profile; never ask for or print tokens.\n");
        b.Append("- With pre-resolved JSON, run `dbudoctor diff --base-json base.json --head-json head.json --format json` instead.\n\n");
        b.Append("If `dbudoctor` is not on PATH, install it with `pip install dbudoctor`, or `dotnet tool install --global DBUDoctor` (needs the .NET 10 SDK), or download a binary for your platform from ")
         .Append(ToolInfo.RepositoryUrl).Append("/releases and check its SHA-256 checksum. Ask the user before installing anything.\n\n");

        b.Append("## Reading results\n\n");
        b.Append("Exit codes:\n\n");
        b.Append("- `0`: no configured policy is violated.\n");
        b.Append("- `1`: a policy in `.dbudoctor.yml` (`fail_on`) is violated; `policy.violations` says which.\n");
        b.Append("- `2`: invalid input or an internal error; the message is on standard error. Fix the input, do not retry blindly.\n\n");
        b.Append("Confidence levels:\n\n");
        b.Append("- `High`: calibrated from observed history.\n");
        b.Append("- `Medium`: frequency from configuration and a configured run duration, or a resource that runs all month.\n");
        b.Append("- `Low`: a comparison that is not like for like, such as classic compute without VM prices against serverless.\n");
        b.Append("- `Unknown`: no monthly figure; only an hourly rate, or \"needs history\".\n\n");
        b.Append("When you report results to the user:\n\n");
        b.Append("- Give the confidence with every monthly figure, for example \"+$1,657 / month (Medium: 20-minute runs configured)\".\n");
        b.Append("- Never present an Unknown or Low figure as a fact. Say what it depends on.\n");
        b.Append("- Give rate-only changes as hourly rates (\"$8.40 → $16.80 per active hour\"), not as monthly guesses.\n");
        b.Append("- Mention the costs the report lists as not modeled when they matter, and that figures are list-price estimates, not an invoice.\n\n");
        b.Append("Field reference: [references/output.md](references/output.md).\n\n");

        b.Append("## Fixing findings\n\n");
        b.Append("Each finding has a `ruleId`, a resource, a `fieldPath` and a message. Look up the rule in [references/rules.md](references/rules.md) for how to fix it and when suppressing it is right:\n\n");
        foreach (var rule in rules)
        {
            b.Append("- `").Append(rule.Id).Append("` ").Append(rule.Title).Append('\n');
        }

        b.Append('\n');
        b.Append("After a fix, run `dbudoctor diff --format json` again and check that the finding is gone and the cost change is what the user expects.\n\n");

        b.Append("## Guardrails\n\n");
        b.Append("- Never weaken production configuration only to silence a finding: do not shrink clusters, pause schedules, remove targets or change `mode` just to make DBUDoctor pass.\n");
        b.Append("- When a finding is intended, suggest a suppression with a reason instead, and add it only with the user's agreement:\n\n");
        b.Append("  ```yaml\n  # .dbudoctor.yml\n  suppressions:\n    - rule: DBU001\n      resource: jobs.nightly_report\n      reason: Needs the shared cluster's cached tables until the Q1 migration (JIRA-123)\n  ```\n\n");
        b.Append("- Do not edit `.dbudoctor.yml` policies (`fail_on`) to make a check pass.\n");
        b.Append("- Do not invent prices or durations. If a figure is Unknown, say so and say what would make it known (a `region`, `estimation.default_run_minutes`, or calibration).\n");
        return b.ToString();
    }

    private static string RulesReference(IReadOnlyList<RuleMetadata> rules, string version)
    {
        var b = new StringBuilder();
        b.Append("# DBUDoctor rules\n\n");
        b.Append("Generated for DBUDoctor ").Append(version).Append(" from rule metadata. Do not edit by hand.\n\n");
        b.Append("Change a rule's severity or parameters, or turn it off, under `rules` in `.dbudoctor.yml`, for example `rules: { DBU010: { severity: error, factor: 4 } }`. ");
        b.Append("Suppress one finding with a `suppressions` entry: a `rule`, a `resource` selector (`<type>.<key>`, wildcards allowed) and a `reason`, which is required.\n");
        foreach (var rule in rules)
        {
            b.Append("\n## ").Append(rule.Id).Append(": ").Append(rule.Title).Append("\n\n");
            b.Append("Default severity: ").Append(EnumNames.Of(rule.DefaultSeverity)).Append(". Docs: ").Append(rule.DocsUrl).Append("\n\n");
            b.Append("**When it fires.** ").Append(rule.WhenItFires).Append("\n\n");
            b.Append("**How to fix.** ").Append(rule.HowToFix).Append("\n\n");
            b.Append("**When to suppress.** ").Append(rule.WhenToSuppress).Append('\n');
            if (rule.Parameters.Count > 0 || rule.ListParameters.Count > 0)
            {
                b.Append("\n**Parameters.**\n\n");
                foreach (var parameter in rule.Parameters)
                {
                    b.Append("- `").Append(parameter.Name).Append("` (default ")
                     .Append(parameter.Default.ToString(CultureInfo.InvariantCulture)).Append("): ").Append(parameter.Description).Append('\n');
                }

                foreach (var parameter in rule.ListParameters)
                {
                    b.Append("- `").Append(parameter.Name).Append("` (a list, empty by default): ").Append(parameter.Description).Append('\n');
                }
            }
        }

        return b.ToString();
    }

    private static string OutputReference(string version)
    {
        var b = new StringBuilder();
        b.Append("# DBUDoctor JSON output\n\n");
        b.Append("Generated for DBUDoctor ").Append(version).Append(". `dbudoctor diff --format json` prints one JSON document. The schema is `schemas/report.schema.json` (`schemaVersion: 1`); fields are only ever added.\n\n");
        b.Append("Amounts are unrounded decimals in `currency` (USD list prices). Monthly figures assume a 730-hour month.\n\n");
        foreach (var (section, fields) in OutputFields.Sections)
        {
            b.Append("## ").Append(section).Append("\n\n");
            b.Append("| Field | Meaning |\n|---|---|\n");
            foreach (var (name, meaning) in fields)
            {
                b.Append("| `").Append(name).Append("` | ").Append(meaning).Append(" |\n");
            }

            b.Append('\n');
        }

        b.Append("## Exit codes\n\n");
        b.Append("| Code | Meaning |\n|---|---|\n");
        b.Append("| 0 | No configured policy is violated. |\n");
        b.Append("| 1 | A `fail_on` policy is violated; see `bundles[].policy.violations`. |\n");
        b.Append("| 2 | Invalid input (configuration, bundle JSON, missing CLI) or an internal error. Nothing is printed to standard output. |\n");
        return b.ToString();
    }
}

/// <summary>Documentation of the JSON output fields, kept next to the renderer; a test checks it against the schema.</summary>
public static class OutputFields
{
    public static readonly IReadOnlyList<(string Section, IReadOnlyList<(string Name, string Meaning)> Fields)> Sections =
    [
        ("Top level",
        [
            ("schemaVersion", "Always 1 for this layout."),
            ("tool.name, tool.version", "`dbudoctor` and the version that produced the report."),
            ("exitCode", "The process exit code: 0 or 1."),
            ("policyViolated", "True when any bundle violates a `fail_on` policy."),
            ("bundles[]", "One report per bundle and target."),
        ]),
        ("bundles[]",
        [
            ("bundle, target, root", "Bundle name, target, and bundle root relative to the repository (null for JSON input)."),
            ("cloud, region, currency", "Where prices come from. A null region makes rates Unknown."),
            ("catalogAsOf", "The oldest catalog date used for this report."),
            ("cloudInfraIncluded, commitment", "Whether classic rates include cloud VMs, and the VM price column used."),
            ("tier", "The plan tier of the list prices: `premium` or `enterprise`."),
            ("scaling, autoscale, defaultRunMinutes", "Estimation assumptions from `.dbudoctor.yml`."),
            ("calibration", "Null unless calibration is on; then `target`, `windowDays`, `calibratedChanges`, `notFound`, `livePrices` and `notes[]`."),
            ("hasCostRelevantChanges", "False when nothing priced changed and there are no new findings."),
            ("totals.monthlyDelta, totals.baseMonthly", "Headline monthly change and the base total of the changed resources it includes (Medium and High only)."),
            ("totals.percentChange", "Delta as a percentage of `baseMonthly`; null when that is 0."),
            ("totals.includedChanges, totals.excludedChanges", "How many changed resources are in the headline, and how many are not."),
            ("changes[]", "One entry per added, removed or modified priced resource."),
            ("findings[]", "Rule findings: new ones, plus existing ones with `--report-existing`."),
            ("suppressedFindings, existingFindingsHidden", "Findings suppressed in `.dbudoctor.yml`, and pre-existing findings not shown."),
            ("possibleRenames[]", "A removal and an addition with identical settings (`type`, `from`, `to`)."),
            ("notPriced[]", "Changed resources of types this version does not price."),
            ("assumptions[], notModeled[], warnings[]", "Report-level assumptions, cost categories not modeled, and warnings (such as a missing region)."),
            ("policy.violated, policy.violations[], policy.notes[]", "Policy results; notes say when a policy was skipped."),
        ]),
        ("changes[]",
        [
            ("type, key, kind", "Resource type (`jobs`, `pipelines`, `clusters`, `sql_warehouses`), key, and `added`, `removed` or `modified`."),
            ("summary", "What changed, such as `schedule hourly → every 15 min`."),
            ("category", "`monthly` (has a monthly figure), `rate` (hourly rate only), `needs_history` (serverless) or `unknown` (rate unknown)."),
            ("confidence", "`High`, `Medium`, `Low` or `Unknown`."),
            ("inTotals", "True when the change is in the headline totals."),
            ("monthlyDelta", "After minus before, per month; null without a monthly figure on both sides."),
            ("notes[]", "Caveats, such as a classic/serverless comparison without VM prices."),
            ("before, after", "The estimate on each side; null when the resource is absent on that side."),
        ]),
        ("before / after estimates",
        [
            ("sku, pricePerDbu", "DBU SKU and effective $/DBU after discounts, surcharge and overrides."),
            ("dbuPerHour, dbuCostPerHour, vmCostPerHour, ratePerHour", "Hourly DBUs and costs; `ratePerHour` is the cost per active hour."),
            ("serverless, cloudInfraIncluded", "Compute kind, and whether VMs are in the rate."),
            ("runsPerMonth, frequency", "Runs per month (null when unknown) and a description such as `hourly`."),
            ("monthly, confidence, monthlyNote", "Monthly figure (null when Unknown), its confidence, and why it is missing."),
            ("monthlyCeiling", "The most a month can cost at the maximum size, for Lakebase endpoints; null otherwise."),
            ("history", "Observed usage behind a calibrated figure (`runs`, `days`, `windowDays`, `dbusPerRun`, `monthlyDbus`); null when the figure comes from configuration."),
            ("assumptions[], unknownReasons[]", "What the estimate assumes, and why the rate is unknown."),
        ]),
        ("findings[]",
        [
            ("ruleId, severity", "Rule ID such as `DBU001`, and `info`, `warning` or `error`."),
            ("resourceType, resourceKey, fieldPath", "Where the finding is, such as `jobs`, `nightly`, `tasks[refresh].existing_cluster_id`."),
            ("message, docsUrl", "What is wrong, and the rule's documentation page."),
            ("existing", "True when the finding also exists in base (only with `--report-existing`)."),
            ("location", "`file` and `line` when the Databricks CLI supplied locations; otherwise null."),
        ]),
    ];
}
