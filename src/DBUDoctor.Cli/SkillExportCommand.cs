using DBUDoctor.Core;
using DBUDoctor.Core.Rules;
using DBUDoctor.Core.Skill;

namespace DBUDoctor.Cli;

/// <summary><c>dbudoctor skill export --dir &lt;path&gt; [--force]</c>: writes the skill for the running version.</summary>
public static class SkillExportCommand
{
    public static int Run(string directory, bool force, string workingDirectory, TextWriter output, TextWriter error, string? version = null)
    {
        try
        {
            var target = Path.GetFullPath(directory, workingDirectory);
            if (Directory.Exists(target) && !force)
            {
                throw new DoctorException($"{target} already exists. Pass --force to overwrite it.");
            }

            var files = SkillGenerator.Generate(RuleRegistry.All.Select(r => r.Metadata).ToList(), version ?? ToolInfo.Version);
            Directory.CreateDirectory(target);
            foreach (var (relative, content) in files)
            {
                var path = Path.Combine(target, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }

            output.WriteLine($"Wrote the DBUDoctor skill to {target} ({string.Join(", ", files.Keys)}).");
            return ExitCodes.Ok;
        }
        catch (DoctorException ex)
        {
            error.WriteLine("dbudoctor: " + ex.Message);
            return ExitCodes.InvalidInput;
        }
    }
}
