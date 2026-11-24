using System.Globalization;
using DBUDoctor.Core.Pricing;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace DBUDoctor.Core.Config;

/// <summary>
/// Reads <c>.dbudoctor.yml</c>. Unknown keys and invalid values are errors that name the path and
/// line. The accepted keys mirror <c>schemas/dbudoctor.schema.json</c>; a test keeps the two in sync.
/// </summary>
public static class ConfigLoader
{
    public const string DefaultFileName = ".dbudoctor.yml";

    /// <summary>Every key path the loader accepts. <c>*</c> stands for a map key chosen by the user.</summary>
    public static readonly IReadOnlyList<string> KnownPaths =
    [
        "target", "bundles", "cloud", "region", "currency",
        "pricing", "pricing.discounts", "pricing.discounts.*", "pricing.surcharge", "pricing.overrides", "pricing.overrides.*",
        "pricing.include_cloud_infra", "pricing.infra", "pricing.infra.commitment", "pricing.tier",
        "estimation", "estimation.autoscale", "estimation.scaling", "estimation.default_run_minutes", "estimation.standard_mode_factor",
        "estimation.instance_pools", "estimation.instance_pools.*",
        "calibration", "calibration.enabled", "calibration.warehouse_id", "calibration.target", "calibration.window_days", "calibration.min_runs",
        "rules", "rules.*", "rules.*.enabled", "rules.*.severity", "rules.*.*",
        "suppressions", "suppressions[].rule", "suppressions[].resource", "suppressions[].reason",
        "report", "report.comment_on_no_change",
        "fail_on", "fail_on.monthly_increase", "fail_on.percent_increase", "fail_on.severity", "fail_on.rules", "fail_on.unknown_cost",
    ];

    /// <summary>Loads the file, or returns defaults when <paramref name="path"/> is null or (for the default file) missing.</summary>
    /// <param name="ruleParameters">Known rule IDs and the parameter names each one accepts.</param>
    public static DoctorConfig Load(string? path, bool explicitPath, IReadOnlyDictionary<string, IReadOnlyCollection<string>> ruleParameters)
    {
        if (path is null || !File.Exists(path))
        {
            if (explicitPath)
            {
                throw new DoctorException($"Configuration file not found: {path}");
            }

            return new DoctorConfig();
        }

        var config = Parse(File.ReadAllText(path), path, ruleParameters);
        config.SourcePath = path;
        return config;
    }

    public static DoctorConfig Parse(string text, string fileName, IReadOnlyDictionary<string, IReadOnlyCollection<string>> ruleParameters)
    {
        var config = new DoctorConfig();
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException ex)
        {
            throw new DoctorException($"{fileName}:{ex.Start.Line}: invalid YAML: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is YamlScalarNode { Value: null or "" })
        {
            return config;
        }

        var reader = new Reader(fileName);
        var root = reader.Mapping(stream.Documents[0].RootNode, "");
        foreach (var (keyNode, value) in root.Children)
        {
            var key = reader.Key(keyNode);
            switch (key)
            {
                case "target": config.Target = reader.String(value, key); break;
                case "bundles": config.Bundles = reader.StringList(value, key); break;
                case "cloud": config.Cloud = reader.Enum(value, key, EnumNames.Clouds); break;
                case "region": config.Region = reader.String(value, key); break;
                case "currency":
                    config.Currency = reader.String(value, key);
                    if (config.Currency != "USD")
                    {
                        throw reader.Error(value, $"`currency` must be USD: the catalog holds USD list prices only.");
                    }

                    break;
                case "pricing": ReadPricing(reader, value, config.Pricing); break;
                case "estimation": ReadEstimation(reader, value, config.Estimation); break;
                case "calibration": ReadCalibration(reader, value, config.Calibration); break;
                case "rules": ReadRules(reader, value, config.Rules, ruleParameters); break;
                case "suppressions": ReadSuppressions(reader, value, config.Suppressions, ruleParameters); break;
                case "report":
                    foreach (var (k, v) in reader.Mapping(value, key).Children)
                    {
                        switch (reader.Key(k))
                        {
                            case "comment_on_no_change": config.CommentOnNoChange = reader.Bool(v, "report.comment_on_no_change"); break;
                            default: throw reader.Unknown(k, "report");
                        }
                    }

                    break;
                case "fail_on": ReadFailOn(reader, value, config.FailOn, ruleParameters); break;
                default: throw reader.Unknown(keyNode, "");
            }
        }

        return config;
    }

    private static void ReadPricing(Reader reader, YamlNode node, PricingSettings pricing)
    {
        foreach (var (keyNode, value) in reader.Mapping(node, "pricing").Children)
        {
            switch (reader.Key(keyNode))
            {
                case "discounts":
                    foreach (var (k, v) in reader.Mapping(value, "pricing.discounts").Children)
                    {
                        var family = reader.Key(k);
                        if (family != ProductFamily.All && !ProductFamily.Known.Contains(family, StringComparer.Ordinal))
                        {
                            throw reader.Error(k, $"unknown product family `{family}` in `pricing.discounts`. Allowed: {string.Join(", ", new[] { ProductFamily.All }.Concat(ProductFamily.Known))}.");
                        }

                        pricing.Discounts[family] = reader.Fraction(v, $"pricing.discounts.{family}");
                    }

                    break;
                case "surcharge": pricing.Surcharge = reader.NonNegative(value, "pricing.surcharge"); break;
                case "tier": pricing.Tier = reader.Enum(value, "pricing.tier", EnumNames.Tiers); break;
                case "overrides":
                    foreach (var (k, v) in reader.Mapping(value, "pricing.overrides").Children)
                    {
                        var sku = reader.Key(k);
                        if (!Skus.All.Contains(sku, StringComparer.Ordinal))
                        {
                            throw reader.Error(k, $"unknown SKU `{sku}` in `pricing.overrides`. Allowed: {string.Join(", ", Skus.All)}.");
                        }

                        pricing.Overrides[sku] = reader.Positive(v, $"pricing.overrides.{sku}");
                    }

                    break;
                case "include_cloud_infra": pricing.IncludeCloudInfra = reader.Enum(value, "pricing.include_cloud_infra", EnumNames.InfraModes); break;
                case "infra":
                    foreach (var (k, v) in reader.Mapping(value, "pricing.infra").Children)
                    {
                        switch (reader.Key(k))
                        {
                            case "commitment": pricing.Commitment = reader.Enum(v, "pricing.infra.commitment", EnumNames.Commitments); break;
                            default: throw reader.Unknown(k, "pricing.infra");
                        }
                    }

                    break;
                default: throw reader.Unknown(keyNode, "pricing");
            }
        }
    }

    private static void ReadEstimation(Reader reader, YamlNode node, EstimationSettings estimation)
    {
        foreach (var (keyNode, value) in reader.Mapping(node, "estimation").Children)
        {
            switch (reader.Key(keyNode))
            {
                case "autoscale": estimation.Autoscale = reader.Enum(value, "estimation.autoscale", EnumNames.Autoscales); break;
                case "scaling": estimation.Scaling = reader.Enum(value, "estimation.scaling", EnumNames.Scalings); break;
                case "default_run_minutes": estimation.DefaultRunMinutes = reader.Positive(value, "estimation.default_run_minutes"); break;
                case "standard_mode_factor":
                    var factor = reader.Positive(value, "estimation.standard_mode_factor");
                    estimation.StandardModeFactor = factor <= 1 ? factor : throw reader.Error(value, "`estimation.standard_mode_factor` must be greater than 0 and at most 1, such as 0.7.");
                    break;
                case "instance_pools":
                    foreach (var (k, v) in reader.Mapping(value, "estimation.instance_pools").Children)
                    {
                        var pool = reader.Key(k);
                        var nodeType = reader.String(v, $"estimation.instance_pools.{pool}");
                        estimation.InstancePools[pool] = string.IsNullOrWhiteSpace(nodeType)
                            ? throw reader.Error(v, $"`estimation.instance_pools.{pool}` must be the pool's node type, such as Standard_DS3_v2.")
                            : nodeType.Trim();
                    }

                    break;
                default: throw reader.Unknown(keyNode, "estimation");
            }
        }
    }

    private static void ReadCalibration(Reader reader, YamlNode node, CalibrationSettings calibration)
    {
        foreach (var (keyNode, value) in reader.Mapping(node, "calibration").Children)
        {
            switch (reader.Key(keyNode))
            {
                case "enabled": calibration.Enabled = reader.Bool(value, "calibration.enabled"); break;
                case "warehouse_id":
                    var id = reader.String(value, "calibration.warehouse_id");
                    calibration.WarehouseId = Calibration.ResourceIds.IsWarehouseId(id)
                        ? id
                        : throw reader.Error(value, $"`calibration.warehouse_id` must be a SQL warehouse ID (16 hexadecimal characters, as in /sql/warehouses/<id>), not `{id}`.");
                    break;
                case "target": calibration.Target = reader.String(value, "calibration.target"); break;
                case "window_days": calibration.WindowDays = reader.WholeNumber(value, "calibration.window_days", 1, 365); break;
                case "min_runs": calibration.MinRuns = reader.WholeNumber(value, "calibration.min_runs", 1, 10_000); break;
                default: throw reader.Unknown(keyNode, "calibration");
            }
        }
    }

    private static void ReadRules(Reader reader, YamlNode node, Dictionary<string, RuleSettings> rules, IReadOnlyDictionary<string, IReadOnlyCollection<string>> ruleParameters)
    {
        foreach (var (keyNode, value) in reader.Mapping(node, "rules").Children)
        {
            var id = reader.Key(keyNode);
            if (!ruleParameters.TryGetValue(id, out var parameters))
            {
                throw reader.Error(keyNode, $"unknown rule `{id}` in `rules`. Known rules: {string.Join(", ", ruleParameters.Keys.Order(StringComparer.Ordinal))}.");
            }

            bool? enabled = null;
            Severity? severity = null;
            var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
            var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var (k, v) in reader.Mapping(value, $"rules.{id}").Children)
            {
                var name = reader.Key(k);
                switch (name)
                {
                    case "enabled": enabled = reader.Bool(v, $"rules.{id}.enabled"); break;
                    case "severity": severity = reader.Enum(v, $"rules.{id}.severity", EnumNames.Severities); break;
                    default:
                        if (parameters.Contains(name + "[]"))
                        {
                            lists[name] = v is YamlScalarNode { Value: null or "" or "~" or "null" } ? [] : reader.StringList(v, $"rules.{id}.{name}");
                            break;
                        }

                        if (!parameters.Contains(name))
                        {
                            var allowed = new[] { "enabled", "severity" }.Concat(parameters.Select(p => p.TrimEnd('[', ']')));
                            throw reader.Error(k, $"unknown key `{name}` in `rules.{id}`. Allowed: {string.Join(", ", allowed)}.");
                        }

                        values[name] = reader.Positive(v, $"rules.{id}.{name}");
                        break;
                }
            }

            rules[id] = new RuleSettings(enabled, severity, values, lists);
        }
    }

    private static void ReadSuppressions(Reader reader, YamlNode node, List<Suppression> suppressions, IReadOnlyDictionary<string, IReadOnlyCollection<string>> ruleParameters)
    {
        if (node is not YamlSequenceNode sequence)
        {
            throw reader.Error(node, "`suppressions` must be a list.");
        }

        var index = 0;
        foreach (var item in sequence.Children)
        {
            string? rule = null, resource = null, reason = null;
            foreach (var (k, v) in reader.Mapping(item, $"suppressions[{index}]").Children)
            {
                switch (reader.Key(k))
                {
                    case "rule": rule = reader.String(v, $"suppressions[{index}].rule"); break;
                    case "resource": resource = reader.String(v, $"suppressions[{index}].resource"); break;
                    case "reason": reason = reader.String(v, $"suppressions[{index}].reason"); break;
                    default: throw reader.Unknown(k, $"suppressions[{index}]");
                }
            }

            var name = $"suppressions[{index}] ({rule ?? "?"} for {resource ?? "?"})";
            if (string.IsNullOrWhiteSpace(rule) || !ruleParameters.ContainsKey(rule))
            {
                throw reader.Error(item, $"{name} needs a known `rule`. Known rules: {string.Join(", ", ruleParameters.Keys.Order(StringComparer.Ordinal))}.");
            }

            if (string.IsNullOrWhiteSpace(resource) || !resource.Contains('.', StringComparison.Ordinal))
            {
                throw reader.Error(item, $"{name} needs a `resource` selector of the form <type>.<key>, such as jobs.legacy_*.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw reader.Error(item, $"{name} has no `reason`. Every suppression needs one.");
            }

            suppressions.Add(new Suppression(rule, resource, reason.Trim(), (int)item.Start.Line));
            index++;
        }
    }

    private static void ReadFailOn(Reader reader, YamlNode node, FailOnSettings failOn, IReadOnlyDictionary<string, IReadOnlyCollection<string>> ruleParameters)
    {
        foreach (var (keyNode, value) in reader.Mapping(node, "fail_on").Children)
        {
            switch (reader.Key(keyNode))
            {
                case "monthly_increase": failOn.MonthlyIncrease = reader.NonNegative(value, "fail_on.monthly_increase"); break;
                case "percent_increase": failOn.PercentIncrease = reader.NonNegative(value, "fail_on.percent_increase"); break;
                case "severity": failOn.Severity = reader.Enum(value, "fail_on.severity", EnumNames.Severities); break;
                case "unknown_cost": failOn.UnknownCost = reader.Bool(value, "fail_on.unknown_cost"); break;
                case "rules":
                    foreach (var id in reader.StringList(value, "fail_on.rules"))
                    {
                        if (!ruleParameters.ContainsKey(id))
                        {
                            throw reader.Error(value, $"unknown rule `{id}` in `fail_on.rules`. Known rules: {string.Join(", ", ruleParameters.Keys.Order(StringComparer.Ordinal))}.");
                        }

                        failOn.Rules.Add(id);
                    }

                    break;
                default: throw reader.Unknown(keyNode, "fail_on");
            }
        }
    }

    private sealed class Reader(string fileName)
    {
        public DoctorException Error(YamlNode node, string message) =>
            new($"{fileName}:{node.Start.Line}: {message}");

        public DoctorException Unknown(YamlNode keyNode, string parent)
        {
            var key = keyNode is YamlScalarNode s ? s.Value : keyNode.ToString();
            var where = parent.Length == 0 ? "at the top level" : $"in `{parent}`";
            var prefix = parent.Length == 0 ? "" : parent + ".";
            var siblings = KnownPaths
                .Where(p => p.StartsWith(prefix, StringComparison.Ordinal) && !p[prefix.Length..].Contains('.', StringComparison.Ordinal) && !p.Contains('[', StringComparison.Ordinal))
                .Select(p => p[prefix.Length..])
                .Where(p => p != "*")
                .Distinct(StringComparer.Ordinal);
            return Error(keyNode, $"unknown key `{key}` {where} (line {keyNode.Start.Line}). Allowed: {string.Join(", ", siblings)}.");
        }

        public YamlMappingNode Mapping(YamlNode node, string path)
        {
            if (node is YamlMappingNode mapping)
            {
                return mapping;
            }

            if (node is YamlScalarNode { Value: null or "" or "~" or "null" })
            {
                return new YamlMappingNode();
            }

            throw Error(node, $"`{(path.Length == 0 ? "the file" : path)}` must be a mapping.");
        }

        public string Key(YamlNode node) =>
            node is YamlScalarNode { Value: { } value } ? value : throw Error(node, "keys must be plain strings.");

        public string String(YamlNode node, string path) =>
            node is YamlScalarNode { Value: { } value } && value.Length > 0 ? value : throw Error(node, $"`{path}` must be a non-empty string.");

        public List<string> StringList(YamlNode node, string path)
        {
            if (node is YamlSequenceNode sequence)
            {
                return sequence.Children.Select(c => String(c, path)).ToList();
            }

            throw Error(node, $"`{path}` must be a list of strings.");
        }

        public bool Bool(YamlNode node, string path) =>
            node is YamlScalarNode { Value: "true" or "True" or "false" or "False" } s
                ? s.Value is "true" or "True"
                : throw Error(node, $"`{path}` must be true or false.");

        public T Enum<T>(YamlNode node, string path, IReadOnlyDictionary<string, T> allowed)
        {
            var value = node is YamlScalarNode s ? s.Value : null;
            if (value is not null && allowed.TryGetValue(value, out var result))
            {
                return result;
            }

            throw Error(node, $"invalid value `{value}` for `{path}`. Allowed values: {string.Join(", ", allowed.Keys)}.");
        }

        public decimal Number(YamlNode node, string path)
        {
            if (node is YamlScalarNode { Value: { } value } &&
                decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            throw Error(node, $"`{path}` must be a number.");
        }

        public int WholeNumber(YamlNode node, string path, int min, int max)
        {
            var value = Number(node, path);
            return value == decimal.Truncate(value) && value >= min && value <= max
                ? (int)value
                : throw Error(node, $"`{path}` must be a whole number from {min} to {max}.");
        }

        public decimal NonNegative(YamlNode node, string path)
        {
            var value = Number(node, path);
            return value >= 0 ? value : throw Error(node, $"`{path}` must not be negative.");
        }

        public decimal Positive(YamlNode node, string path)
        {
            var value = Number(node, path);
            return value > 0 ? value : throw Error(node, $"`{path}` must be greater than 0.");
        }

        public decimal Fraction(YamlNode node, string path)
        {
            var value = Number(node, path);
            return value is >= 0 and < 1 ? value : throw Error(node, $"`{path}` must be a fraction from 0 up to (not including) 1, such as 0.20 for 20% off.");
        }
    }
}
