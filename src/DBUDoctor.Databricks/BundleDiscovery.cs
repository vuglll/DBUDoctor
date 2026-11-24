using DBUDoctor.Core;
using YamlDotNet.RepresentationModel;

namespace DBUDoctor.Databricks;

public static class BundleDiscovery
{
    public static readonly IReadOnlyList<string> RootFileNames = ["databricks.yml", "databricks.yaml"];

    private static readonly HashSet<string> Vendored = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "vendor", "venv", "site-packages", "__pycache__", "bin", "obj", "dist", "build", "target", "third_party",
    };

    /// <summary>
    /// Bundle roots relative to <paramref name="repositoryRoot"/>, in ordinal order: the configured
    /// <c>bundles</c>, or every directory with a databricks.yml outside hidden and vendored directories.
    /// </summary>
    public static IReadOnlyList<string> Discover(string repositoryRoot, IReadOnlyList<string>? configured)
    {
        if (configured is { Count: > 0 })
        {
            var roots = new List<string>();
            foreach (var entry in configured)
            {
                var relative = Normalize(entry);
                if (RootFile(Path.Combine(repositoryRoot, relative)) is null)
                {
                    throw new DoctorException($"`bundles` lists `{entry}`, but {Path.Combine(repositoryRoot, relative)} has no databricks.yml.");
                }

                roots.Add(relative);
            }

            return roots.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        }

        var found = new List<string>();
        Walk(repositoryRoot, repositoryRoot, found);
        if (found.Count == 0)
        {
            throw new DoctorException($"No databricks.yml found under {repositoryRoot}. Set `bundles` in .dbudoctor.yml, or pass --base-json and --head-json.");
        }

        return found.Order(StringComparer.Ordinal).ToList();
    }

    public static string? RootFile(string directory) =>
        RootFileNames.Select(n => Path.Combine(directory, n)).FirstOrDefault(File.Exists);

    /// <summary>
    /// The default target (design D2): the first target in databricks.yml with <c>mode: production</c>.
    /// Targets defined only in included files are not seen; set <c>target</c> for those.
    /// </summary>
    public static string? ProductionTarget(string bundleDirectory)
    {
        if (RootFile(bundleDirectory) is not { } file)
        {
            return null;
        }

        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(File.ReadAllText(file)));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new DoctorException($"{file}:{ex.Start.Line}: invalid YAML: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root ||
            !root.Children.TryGetValue(new YamlScalarNode("targets"), out var targets) || targets is not YamlMappingNode targetMap)
        {
            return null;
        }

        foreach (var (name, value) in targetMap.Children)
        {
            if (value is YamlMappingNode target &&
                target.Children.TryGetValue(new YamlScalarNode("mode"), out var mode) &&
                mode is YamlScalarNode { Value: "production" } &&
                name is YamlScalarNode { Value: { } targetName })
            {
                return targetName;
            }
        }

        return null;
    }

    private static void Walk(string root, string directory, List<string> found)
    {
        if (RootFile(directory) is not null)
        {
            found.Add(Normalize(Path.GetRelativePath(root, directory)));
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(directory);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (name.StartsWith('.') || Vendored.Contains(name))
            {
                continue;
            }

            Walk(root, child, found);
        }
    }

    private static string Normalize(string relative)
    {
        var path = relative.Replace('\\', '/').Trim('/');
        return path.Length == 0 || path == "." ? "." : path;
    }
}
