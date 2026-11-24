using System.CommandLine;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Rules;

namespace DBUDoctor.Cli;

public static class Program
{
    public static Task<int> Main(string[] args) => BuildRoot().Parse(args).InvokeAsync();

    public static RootCommand BuildRoot()
    {
        var root = new RootCommand($"DBUDoctor {ToolInfo.Version}: estimates what a Databricks bundle change will cost.");
        root.Subcommands.Add(BuildDiff());
        root.Subcommands.Add(BuildSkill());
        return root;
    }

    private static Command BuildDiff()
    {
        var target = new Option<string?>("--target") { Description = "Bundle target to estimate. Default: the first target with mode: production." };
        var baseRef = new Option<string?>("--base") { Description = "Base git ref. Default: the pull request base, or the merge-base with the remote default branch." };
        var headRef = new Option<string?>("--head") { Description = "Head git ref. Default: the pull request head, or the working tree." };
        var baseJson = new Option<string?>("--base-json") { Description = "Pre-resolved `bundle validate --output json` for the base (with --head-json; runs no git or databricks)." };
        var headJson = new Option<string?>("--head-json") { Description = "Pre-resolved `bundle validate --output json` for the head." };
        var format = new Option<string?>("--format") { Description = "console (default), markdown or json." };
        format.AcceptOnlyFromAmong("console", "markdown", "json");
        var output = new Option<string?>("--output") { Description = "Write the report to this file instead of standard output." };
        var githubComment = new Option<bool>("--github-comment") { Description = "Create or update the sticky pull request comment (GitHub Actions)." };
        var reportExisting = new Option<bool>("--report-existing") { Description = "Also report findings that already exist in the base." };
        var config = new Option<string?>("--config") { Description = "Configuration file. Default: .dbudoctor.yml in the repository root." };
        var vars = new Option<string[]>("--var")
        {
            Description = "Bundle variable for `bundle validate`, as name=value. Repeat for more than one.",
            Arity = System.CommandLine.ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        var refreshPrices = new Option<bool>("--refresh-prices") { Description = "Fetch current Azure prices for the bundle's region (cached for the UTC day)." };

        var command = new Command("diff", "Report the cost change between two git refs of a bundle.")
        {
            target, baseRef, headRef, baseJson, headJson, format, output, githubComment, reportExisting, config, refreshPrices, vars,
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            bool? Flag(Option<bool> option) => parse.GetResult(option) is null ? null : parse.GetValue(option);
            var arguments = new DiffArguments
            {
                Flags = new CliFlags
                {
                    Target = parse.GetValue(target),
                    Config = parse.GetValue(config),
                    Format = parse.GetValue(format),
                    Base = parse.GetValue(baseRef),
                    Head = parse.GetValue(headRef),
                    ReportExisting = Flag(reportExisting),
                    RefreshPrices = Flag(refreshPrices),
                    GitHubComment = Flag(githubComment),
                },
                BaseJson = parse.GetValue(baseJson),
                HeadJson = parse.GetValue(headJson),
                Output = parse.GetValue(output),
                Vars = parse.GetValue(vars) ?? [],
            };
            return await DiffCommand.RunAsync(arguments, DiffEnvironment.FromProcess(), cancellationToken).ConfigureAwait(false);
        });
        return command;
    }

    private static Command BuildSkill()
    {
        var dir = new Option<string>("--dir") { Description = "Directory to write the skill to, such as .claude/skills/dbudoctor.", Required = true };
        var force = new Option<bool>("--force") { Description = "Overwrite an existing directory." };
        var export = new Command("export", "Write the agent skill (SKILL.md and references) for this version.") { dir, force };
        export.SetAction(parse => SkillExportCommand.Run(parse.GetValue(dir)!, parse.GetValue(force), Environment.CurrentDirectory, Console.Out, Console.Error));
        return new Command("skill", "Agent skill commands.") { export };
    }
}
