using System.ComponentModel;
using System.Text.RegularExpressions;
using DBUDoctor.Core;

namespace DBUDoctor.Databricks;

public sealed record ValidateResult(string Json, string Diagnostics, bool LocationsIncluded);

/// <summary>
/// Runs <c>databricks bundle validate --target &lt;t&gt; --output json</c> (design D1). Diagnostics go to
/// standard error and the resolved configuration to standard output.
/// </summary>
public sealed partial class DatabricksCli(IProcessRunner runner, string executable)
{
    /// <summary>
    /// The oldest CLI this version supports: the first release that writes `bundle validate -o json`
    /// warnings to standard error, which keeps standard output parseable.
    /// </summary>
    public static readonly Version MinimumVersion = new(0, 239, 0);

    public static string InstallHint =>
        $"DBUDoctor needs the Databricks CLI v{MinimumVersion} or later on PATH (or in DATABRICKS_CLI_PATH).\n" +
        "Install it with one of:\n" +
        "  brew tap databricks/tap && brew install databricks\n" +
        "  curl -fsSL https://raw.githubusercontent.com/databricks/setup-cli/main/install.sh | sh\n" +
        "  winget install Databricks.DatabricksCLI\n" +
        "In GitHub Actions, add the step `uses: databricks/setup-cli@main`.\n" +
        "See https://docs.databricks.com/dev-tools/cli/install.html. Or pass --base-json and --head-json.";

    public string Executable => executable;

    /// <summary>Finds the CLI and checks its version. Throws <see cref="DoctorException"/> (exit code 2) with an install hint.</summary>
    public static DatabricksCli Locate(IProcessRunner runner, IReadOnlyDictionary<string, string?> environment)
    {
        var path = environment.GetValueOrDefault("DATABRICKS_CLI_PATH") is { Length: > 0 } explicitPath
            ? explicitPath
            : FindOnPath(environment.GetValueOrDefault("PATH"));
        if (path is null)
        {
            throw new DoctorException("The `databricks` executable was not found on PATH.\n" + InstallHint);
        }

        ProcessResult result;
        try
        {
            result = runner.Run(path, ["--version"], Environment.CurrentDirectory);
        }
        catch (Win32Exception ex)
        {
            throw new DoctorException($"Could not run {path}: {ex.Message}\n" + InstallHint, ex);
        }

        var version = ParseVersion(result.StandardOutput + " " + result.StandardError);
        if (result.ExitCode != 0 || version is null)
        {
            throw new DoctorException($"Could not read the Databricks CLI version from `{path} --version`. The legacy Python CLI (`databricks-cli`) is not supported.\n" + InstallHint);
        }

        if (version < MinimumVersion)
        {
            throw new DoctorException($"The Databricks CLI at {path} is v{version}; DBUDoctor needs v{MinimumVersion} or later.\n" + InstallHint);
        }

        return new DatabricksCli(runner, path);
    }

    public static Version? ParseVersion(string text)
    {
        var match = VersionPattern().Match(text);
        return match.Success ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value)) : null;
    }

    /// <summary>
    /// Validates the bundle in <paramref name="bundleDirectory"/>. Asks for source locations with the
    /// hidden <c>--include-locations</c> flag and retries without it when the CLI rejects the flag.
    /// </summary>
    public ValidateResult Validate(string bundleDirectory, string target, string refDescription, IReadOnlyList<BundleVariable>? variables = null)
    {
        var arguments = new List<string> { "bundle", "validate", "--target", target, "--output", "json" };
        arguments.AddRange(BundleVariable.Arguments(variables));
        var result = runner.Run(executable, [.. arguments, "--include-locations"], bundleDirectory);
        var locations = true;
        if (result.ExitCode != 0 && RejectsFlag(result.StandardError))
        {
            result = runner.Run(executable, arguments, bundleDirectory);
            locations = false;
        }

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            var diagnostics = string.IsNullOrWhiteSpace(result.StandardError) ? "(no output on standard error)" : result.StandardError.Trim();
            var hint = MissingVariable().Match(diagnostics) is { Success: true } missing
                ? $"\nPass `--var {missing.Groups[1].Value}=<value>` to dbudoctor diff, or set BUNDLE_VAR_{missing.Groups[1].Value}."
                : "";
            throw new DoctorException(
                $"`databricks bundle validate --target {target}` failed for {refDescription} in {bundleDirectory} (exit code {result.ExitCode}):\n{diagnostics}{hint}");
        }

        return new ValidateResult(result.StandardOutput, result.StandardError.Trim(), locations);
    }

    /// <summary>
    /// <c>databricks bundle summary --target &lt;t&gt; --output json</c>: the deployed IDs (design C2).
    /// Returns the JSON, or null and the CLI's error when it fails (for example without auth).
    /// </summary>
    public (string? Json, string? Error) Summary(string bundleDirectory, string target, IReadOnlyList<BundleVariable>? variables = null)
    {
        var result = runner.Run(executable, ["bundle", "summary", "--target", target, "--output", "json", .. BundleVariable.Arguments(variables)], bundleDirectory);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            var error = result.StandardError.Trim();
            var line = error.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(l => l.StartsWith("Error:", StringComparison.Ordinal)) ?? error.Split('\n').LastOrDefault();
            return (null, string.IsNullOrWhiteSpace(line) ? $"exit code {result.ExitCode}" : line.Trim());
        }

        return (result.StandardOutput, null);
    }

    [GeneratedRegex(@"no value assigned to required variable ([A-Za-z0-9_-]+)")]
    private static partial Regex MissingVariable();

    private static bool RejectsFlag(string stderr) =>
        stderr.Contains("include-locations", StringComparison.Ordinal) &&
        (stderr.Contains("unknown flag", StringComparison.OrdinalIgnoreCase) || stderr.Contains("unknown shorthand", StringComparison.OrdinalIgnoreCase));

    private static string? FindOnPath(string? pathVariable)
    {
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        var names = OperatingSystem.IsWindows() ? new[] { "databricks.exe", "databricks.cmd", "databricks" } : ["databricks"];
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    [GeneratedRegex(@"v?(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionPattern();
}
