using DBUDoctor.Cli;
using DBUDoctor.Core;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Rules;
using DBUDoctor.Core.Skill;

namespace DBUDoctor.Tests;

public class SkillTests
{
    private static IReadOnlyList<RuleMetadata> Rules => RuleRegistry.All.Select(r => r.Metadata).ToList();

    private static IReadOnlyDictionary<string, string> Generate() => SkillGenerator.Generate(Rules, ToolInfo.Version);

    [Fact]
    public void Committed_skill_matches_a_fresh_generation()
    {
        // CI also runs `dbudoctor skill export --dir skills/dbudoctor --force` and `git diff --exit-code`.
        // To update after an intended change: UPDATE_SKILL=1 dotnet test
        var generated = Generate();
        var directory = Repo.Path("skills", "dbudoctor");
        if (Environment.GetEnvironmentVariable("UPDATE_SKILL") == "1")
        {
            foreach (var (file, content) in generated)
            {
                var path = Path.Combine(directory, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }
        }

        var stale = generated.Where(f => !File.Exists(Path.Combine(directory, f.Key)) || File.ReadAllText(Path.Combine(directory, f.Key)) != f.Value)
            .Select(f => f.Key).ToList();
        Assert.True(stale.Count == 0, $"skills/dbudoctor is stale; differs: {string.Join(", ", stale)}. Run `dbudoctor skill export --dir skills/dbudoctor --force`.");
    }

    [Fact]
    public void Skill_has_front_matter_and_the_required_sections()
    {
        var skill = Generate()[SkillGenerator.SkillFile];
        Assert.StartsWith("---\nname: dbudoctor\ndescription: ", skill, StringComparison.Ordinal);
        foreach (var section in new[] { "## When to use", "## How to run", "## Reading results", "## Fixing findings", "## Guardrails" })
        {
            Assert.Contains(section, skill);
        }

        Assert.Contains("dbudoctor diff --format json", skill);
        Assert.Contains($"Generated for DBUDoctor {ToolInfo.Version}", skill);
    }

    [Fact]
    public void Reading_results_names_confidence_levels_and_exit_codes()
    {
        var skill = Generate()[SkillGenerator.SkillFile];
        var section = skill[skill.IndexOf("## Reading results", StringComparison.Ordinal)..skill.IndexOf("## Fixing findings", StringComparison.Ordinal)];
        foreach (var token in new[] { "`High`", "`Medium`", "`Low`", "`Unknown`", "`0`", "`1`", "`2`" })
        {
            Assert.Contains(token, section);
        }
    }

    [Fact]
    public void Guardrails_forbid_weakening_production_configuration()
    {
        var skill = Generate()[SkillGenerator.SkillFile];
        var guardrails = skill[skill.IndexOf("## Guardrails", StringComparison.Ordinal)..];
        Assert.Contains("Never weaken production configuration only to silence a finding", guardrails);
        Assert.Contains("only with the user's agreement", guardrails);
    }

    [Fact]
    public void Install_instructions_name_the_tool_package_and_binaries()
    {
        var skill = Generate()[SkillGenerator.SkillFile];
        Assert.Contains("pip install dbudoctor", skill);
        Assert.Contains("dotnet tool install --global DBUDoctor", skill);
        Assert.Contains("/releases", skill);
    }

    [Fact]
    public void Skill_is_under_the_line_budget_and_the_budget_grows_with_rules_only_in_references()
    {
        var files = Generate();
        Assert.True(files[SkillGenerator.SkillFile].Count(c => c == '\n') < SkillGenerator.MaxSkillLines);

        var many = Enumerable.Range(1, 19).Select(i => Rule($"DBU{i:000}")).ToList();
        var grown = SkillGenerator.Generate(many, "1.0.0");
        Assert.True(grown[SkillGenerator.SkillFile].Count(c => c == '\n') < SkillGenerator.MaxSkillLines);
        Assert.Contains("## DBU019", grown[SkillGenerator.RulesFile]);
    }

    [Fact]
    public void Budget_exceeded_fails_with_the_line_count()
    {
        var huge = Enumerable.Range(1, 200).Select(i => Rule($"DBU{i:000}")).ToList();
        var ex = Assert.Throws<DoctorException>(() => SkillGenerator.Generate(huge, "1.0.0"));
        Assert.Contains("lines; the budget is under 200", ex.Message);
    }

    [Fact]
    public void New_rule_appears_in_the_references()
    {
        var files = SkillGenerator.Generate([.. Rules, Rule("DBU002")], "1.0.0");
        Assert.Contains("## DBU002: Title DBU002", files[SkillGenerator.RulesFile]);
        Assert.Contains("Fix DBU002", files[SkillGenerator.RulesFile]);
        Assert.Contains("Suppress DBU002", files[SkillGenerator.RulesFile]);
    }

    [Fact]
    public void Missing_fix_guidance_fails_and_names_the_rule()
    {
        var ex = Assert.Throws<DoctorException>(() => SkillGenerator.Generate([Rule("DBU002") with { HowToFix = " " }], "1.0.0"));
        Assert.Contains("DBU002", ex.Message);
        Assert.Contains("how to fix", ex.Message);
    }

    [Fact]
    public void Export_creates_the_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dbud-skill-" + Guid.NewGuid().ToString("N"), ".claude", "skills", "dbudoctor");
        var exit = SkillExportCommand.Run(dir, false, Path.GetTempPath(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(dir, "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(dir, "references", "rules.md")));
        Assert.True(File.Exists(Path.Combine(dir, "references", "output.md")));
    }

    [Fact]
    public void Export_refuses_an_existing_folder_without_force()
    {
        var dir = Directory.CreateTempSubdirectory("dbud-skill-").FullName;
        File.WriteAllText(Path.Combine(dir, "keep.txt"), "x");
        var error = new StringWriter();

        Assert.Equal(2, SkillExportCommand.Run(dir, false, Path.GetTempPath(), TextWriter.Null, error));
        Assert.Contains("--force", error.ToString());
        Assert.False(File.Exists(Path.Combine(dir, "SKILL.md")));

        Assert.Equal(0, SkillExportCommand.Run(dir, true, Path.GetTempPath(), TextWriter.Null, TextWriter.Null));
        Assert.True(File.Exists(Path.Combine(dir, "SKILL.md")));
    }

    private static RuleMetadata Rule(string id) => new()
    {
        Id = id,
        Title = "Title " + id,
        DefaultSeverity = Severity.Warning,
        WhenItFires = "Fires " + id,
        HowToFix = "Fix " + id,
        WhenToSuppress = "Suppress " + id,
    };
}
