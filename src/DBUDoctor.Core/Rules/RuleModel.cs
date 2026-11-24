using DBUDoctor.Core.Config;
using DBUDoctor.Core.Cost;
using DBUDoctor.Core.Model;

namespace DBUDoctor.Core.Rules;

public sealed record RuleParameter(string Name, decimal Default, string Description);

/// <summary>A rule setting that takes a list of strings, such as DBU008's <c>required_tags</c>. Empty by default.</summary>
public sealed record RuleListParameter(string Name, string Description);

/// <summary>
/// What every rule declares (design D14). The metadata feeds the rule's docs page and the agent
/// skill. Every member is required, so a rule without one does not compile; the skill generator
/// also rejects empty values.
/// </summary>
public sealed record RuleMetadata
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required Severity DefaultSeverity { get; init; }

    /// <summary>When the rule fires, in one or two sentences.</summary>
    public required string WhenItFires { get; init; }

    /// <summary>How to fix a finding.</summary>
    public required string HowToFix { get; init; }

    /// <summary>When suppressing a finding is the right call.</summary>
    public required string WhenToSuppress { get; init; }

    public IReadOnlyList<RuleParameter> Parameters { get; init; } = [];

    public IReadOnlyList<RuleListParameter> ListParameters { get; init; } = [];

    public string DocsUrl => $"{ToolInfo.RepositoryUrl}/blob/main/docs/rules/{Id}.md";
}

public sealed record Finding
{
    public required string RuleId { get; init; }

    public required Severity Severity { get; init; }

    public required string Bundle { get; init; }

    public required string ResourceType { get; init; }

    public required string ResourceKey { get; init; }

    /// <summary>The field path inside the resource, such as <c>tasks[refresh].existing_cluster_id</c>.</summary>
    public required string FieldPath { get; init; }

    public required string Message { get; init; }

    public required string DocsUrl { get; init; }

    public SourceLocation? Location { get; init; }

    /// <summary>The same finding exists in the base configuration (reported only with --report-existing).</summary>
    public bool Existing { get; init; }

    /// <summary>Identity for new-only reporting (design D8).</summary>
    public string Identity => $"{RuleId}|{Bundle}|{ResourceType}|{ResourceKey}|{FieldPath}";
}

/// <summary>What a rule sees: the configuration it checks, and the base configuration when there is one.</summary>
public sealed class RuleContext
{
    public required string Bundle { get; init; }

    public required ResolvedBundle Current { get; init; }

    /// <summary>The base configuration, or null when the rule runs on the base itself (to build the baseline).</summary>
    public ResolvedBundle? Previous { get; init; }

    public required PricingContext Prices { get; init; }

    public required DateTimeOffset Reference { get; init; }

    /// <summary>The date that end-of-support checks compare against: injectable, today in the CLI.</summary>
    public DateOnly Today { get; init; }

    public required IReadOnlyDictionary<string, decimal> Parameters { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Lists { get; init; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>A list setting, empty when not configured.</summary>
    public IReadOnlyList<string> List(string name) => Lists.TryGetValue(name, out var values) ? values : [];

    public required Severity Severity { get; init; }

    /// <summary>The user set <c>rules.&lt;id&gt;.severity</c>: it wins over the severity a rule picks per finding.</summary>
    public bool SeverityConfigured { get; init; }

    public required RuleMetadata Metadata { get; init; }

    public decimal Parameter(string name) =>
        Parameters.TryGetValue(name, out var value) ? value : Metadata.Parameters.First(p => p.Name == name).Default;

    /// <param name="severity">This finding's severity when the rule grades its findings; null for the rule's severity.</param>
    public Finding Finding(string type, string key, string fieldPath, string message, Severity? severity = null) => new()
    {
        RuleId = Metadata.Id,
        Severity = SeverityConfigured || severity is null ? Severity : severity.Value,
        Bundle = Bundle,
        ResourceType = type,
        ResourceKey = key,
        FieldPath = fieldPath,
        Message = message,
        DocsUrl = Metadata.DocsUrl,
        Location = Current.FindLocation($"resources.{type}.{key}.{fieldPath}"),
    };
}

public interface IRule
{
    RuleMetadata Metadata { get; }

    IEnumerable<Finding> Evaluate(RuleContext context);
}

public static class ToolInfo
{
    public const string RepositoryUrl = "https://github.com/vuglll/DBUDoctor";

    public static string Version
    {
        get
        {
            var informational = typeof(ToolInfo).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion ?? "0.0.0";
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }
    }
}
