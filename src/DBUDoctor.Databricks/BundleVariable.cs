using System.Text.RegularExpressions;
using DBUDoctor.Core;

namespace DBUDoctor.Databricks;

/// <summary>A bundle variable from <c>dbudoctor diff --var name=value</c>, passed on to the Databricks CLI.</summary>
public sealed partial record BundleVariable(string Name, string Value)
{
    /// <summary>Parses <c>name=value</c> arguments. The value may contain <c>=</c> and may be empty.</summary>
    public static IReadOnlyList<BundleVariable> Parse(IEnumerable<string>? arguments)
    {
        var variables = new List<BundleVariable>();
        foreach (var argument in arguments ?? [])
        {
            var separator = argument.IndexOf('=', StringComparison.Ordinal);
            var name = separator > 0 ? argument[..separator] : null;
            if (name is null || !NamePattern().IsMatch(name))
            {
                throw new DoctorException($"--var `{argument}` is not valid: the format is `name=value`, with a variable name of letters, digits, `_` and `-`.");
            }

            variables.RemoveAll(v => v.Name == name);
            variables.Add(new BundleVariable(name, argument[(separator + 1)..]));
        }

        return variables;
    }

    /// <summary>The CLI arguments: <c>--var name=value</c> per variable.</summary>
    public static IEnumerable<string> Arguments(IReadOnlyList<BundleVariable>? variables) =>
        (variables ?? []).SelectMany(v => new[] { "--var", $"{v.Name}={v.Value}" });

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_-]*$")]
    private static partial Regex NamePattern();
}
