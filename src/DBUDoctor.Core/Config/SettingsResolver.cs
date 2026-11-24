using System.Globalization;

namespace DBUDoctor.Core.Config;

/// <summary>Command-line flags. Null means "not given".</summary>
public sealed record CliFlags
{
    public string? Target { get; init; }
    public string? Config { get; init; }
    public string? Format { get; init; }
    public string? Base { get; init; }
    public string? Head { get; init; }
    public bool? ReportExisting { get; init; }
    public bool? RefreshPrices { get; init; }
    public bool? GitHubComment { get; init; }
}

/// <summary>Settings that only flags and environment variables set (not the file).</summary>
public sealed record RunOptions
{
    public string Format { get; init; } = "console";
    public string? Base { get; init; }
    public string? Head { get; init; }
    public bool ReportExisting { get; init; }
    public bool RefreshPrices { get; init; }
    public bool GitHubComment { get; init; }
}

/// <summary>
/// Precedence (highest first): command-line flags, <c>DBUDOCTOR_*</c> environment variables, the
/// configuration file, built-in defaults.
/// </summary>
public static class SettingsResolver
{
    public static readonly IReadOnlyList<string> EnvironmentVariables =
    [
        "DBUDOCTOR_CONFIG", "DBUDOCTOR_TARGET", "DBUDOCTOR_CLOUD", "DBUDOCTOR_REGION", "DBUDOCTOR_FORMAT",
        "DBUDOCTOR_BASE", "DBUDOCTOR_HEAD", "DBUDOCTOR_REPORT_EXISTING", "DBUDOCTOR_REFRESH_PRICES",
        "DBUDOCTOR_GITHUB_COMMENT", "DBUDOCTOR_DEFAULT_RUN_MINUTES", "DBUDOCTOR_COMMITMENT",
        "DBUDOCTOR_INCLUDE_CLOUD_INFRA", "DBUDOCTOR_TIER", "DBUDOCTOR_CALIBRATION", "DBUDOCTOR_WAREHOUSE_ID",
    ];

    /// <summary>The configuration file path: <c>--config</c>, then <c>DBUDOCTOR_CONFIG</c>, then <c>.dbudoctor.yml</c> in <paramref name="root"/>.</summary>
    public static (string Path, bool Explicit) ConfigPath(CliFlags flags, IReadOnlyDictionary<string, string?> env, string root)
    {
        if (flags.Config is { } fromFlag)
        {
            return (fromFlag, true);
        }

        if (Get(env, "DBUDOCTOR_CONFIG") is { } fromEnv)
        {
            return (fromEnv, true);
        }

        return (Path.Combine(root, ConfigLoader.DefaultFileName), false);
    }

    /// <summary>Applies environment variables and flags on top of the file settings, in place.</summary>
    public static RunOptions Apply(DoctorConfig config, CliFlags flags, IReadOnlyDictionary<string, string?> env)
    {
        // Environment variables override the file...
        if (Get(env, "DBUDOCTOR_TARGET") is { } target) config.Target = target;
        if (Get(env, "DBUDOCTOR_CLOUD") is { } cloud) config.Cloud = Parse(cloud, "DBUDOCTOR_CLOUD", EnumNames.Clouds);
        if (Get(env, "DBUDOCTOR_REGION") is { } region) config.Region = region;
        if (Get(env, "DBUDOCTOR_COMMITMENT") is { } commitment) config.Pricing.Commitment = Parse(commitment, "DBUDOCTOR_COMMITMENT", EnumNames.Commitments);
        if (Get(env, "DBUDOCTOR_TIER") is { } tier) config.Pricing.Tier = Parse(tier, "DBUDOCTOR_TIER", EnumNames.Tiers);
        if (Get(env, "DBUDOCTOR_INCLUDE_CLOUD_INFRA") is { } infra) config.Pricing.IncludeCloudInfra = Parse(infra, "DBUDOCTOR_INCLUDE_CLOUD_INFRA", EnumNames.InfraModes);
        if (Get(env, "DBUDOCTOR_DEFAULT_RUN_MINUTES") is { } minutes)
        {
            config.Estimation.DefaultRunMinutes = decimal.TryParse(minutes, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) && m > 0
                ? m
                : throw new DoctorException($"DBUDOCTOR_DEFAULT_RUN_MINUTES must be a positive number, not `{minutes}`.");
        }

        if (Bool(env, "DBUDOCTOR_CALIBRATION") is { } calibrate) config.Calibration.Enabled = calibrate;
        if (Get(env, "DBUDOCTOR_WAREHOUSE_ID") is { } warehouse)
        {
            config.Calibration.WarehouseId = Calibration.ResourceIds.IsWarehouseId(warehouse)
                ? warehouse
                : throw new DoctorException($"DBUDOCTOR_WAREHOUSE_ID must be a SQL warehouse ID (16 hexadecimal characters), not `{warehouse}`.");
        }

        if (config.Calibration.Enabled && config.Calibration.WarehouseId is null)
        {
            throw new DoctorException("Calibration is enabled but no warehouse is set: add `calibration.warehouse_id` to .dbudoctor.yml (or set DBUDOCTOR_WAREHOUSE_ID). The history queries run on that SQL warehouse.");
        }

        // ...and flags override both.
        if (flags.Target is { } flagTarget) config.Target = flagTarget;

        var format = flags.Format ?? Get(env, "DBUDOCTOR_FORMAT") ?? "console";
        if (format is not ("console" or "markdown" or "json"))
        {
            throw new DoctorException($"Unknown format `{format}`. Allowed: console, markdown, json.");
        }

        return new RunOptions
        {
            Format = format,
            Base = flags.Base ?? Get(env, "DBUDOCTOR_BASE"),
            Head = flags.Head ?? Get(env, "DBUDOCTOR_HEAD"),
            ReportExisting = flags.ReportExisting ?? Bool(env, "DBUDOCTOR_REPORT_EXISTING") ?? false,
            RefreshPrices = flags.RefreshPrices ?? Bool(env, "DBUDOCTOR_REFRESH_PRICES") ?? false,
            GitHubComment = flags.GitHubComment ?? Bool(env, "DBUDOCTOR_GITHUB_COMMENT") ?? false,
        };
    }

    /// <summary>
    /// The cloud from the workspace host: <c>*.azuredatabricks.net</c> is Azure, <c>*.gcp.databricks.com</c>
    /// is GCP, any other host is AWS. Null when there is no host.
    /// </summary>
    public static Cloud? InferCloud(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var name = Uri.TryCreate(host.Contains("://", StringComparison.Ordinal) ? host : "https://" + host, UriKind.Absolute, out var uri)
            ? uri.Host
            : host;
        name = name.TrimEnd('.').ToLowerInvariant();
        if (name.EndsWith(".azuredatabricks.net", StringComparison.Ordinal)) return Cloud.Azure;
        if (name.EndsWith(".gcp.databricks.com", StringComparison.Ordinal)) return Cloud.Gcp;
        return Cloud.Aws;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> env, string name) =>
        env.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static bool? Bool(IReadOnlyDictionary<string, string?> env, string name) => Get(env, name)?.ToLowerInvariant() switch
    {
        null => null,
        "1" or "true" or "yes" => true,
        "0" or "false" or "no" => false,
        var other => throw new DoctorException($"{name} must be true or false, not `{other}`."),
    };

    private static T Parse<T>(string value, string name, IReadOnlyDictionary<string, T> allowed) =>
        allowed.TryGetValue(value, out var result)
            ? result
            : throw new DoctorException($"Invalid value `{value}` for {name}. Allowed values: {string.Join(", ", allowed.Keys)}.");
}
