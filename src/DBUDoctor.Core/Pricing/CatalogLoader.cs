using System.Globalization;
using System.Reflection;
using DBUDoctor.Core.Config;
using YamlDotNet.RepresentationModel;

namespace DBUDoctor.Core.Pricing;

/// <summary>
/// Loads the catalog from the copy embedded at build time, or from a directory laid out like
/// <c>catalog/</c>. File-level <c>as_of</c> and <c>source</c> apply to every entry that does not set
/// its own.
/// </summary>
public static class CatalogLoader
{
    private const string ResourcePrefix = "catalog/";

    public static PriceCatalog LoadEmbedded()
    {
        var assembly = typeof(CatalogLoader).Assembly;
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            files[name[ResourcePrefix.Length..].Replace('\\', '/')] = reader.ReadToEnd();
        }

        return Load(files);
    }

    public static PriceCatalog LoadDirectory(string directory)
    {
        var files = Directory.EnumerateFiles(directory, "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(directory, f).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);
        return Load(files);
    }

    /// <param name="files">Catalog files by relative path, such as <c>prices/azure/westeurope.yaml</c>.</param>
    public static PriceCatalog Load(IReadOnlyDictionary<string, string> files)
    {
        var prices = new Dictionary<(Cloud, string), PriceSheet>();
        var nodeTypes = new Dictionary<Cloud, NodeTypeTable>();
        WarehouseTable? warehouses = null;
        NotModeledTable? notModeled = null;
        var defaults = CatalogDefaults.None;
        var runtimes = RuntimeTable.None;
        var serverless = ServerlessTable.None;

        foreach (var (path, text) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var root = Parse(path, text);
            if (path.StartsWith("prices/", StringComparison.Ordinal))
            {
                var sheet = ReadPrices(path, root);
                prices[(sheet.Cloud, sheet.Region)] = sheet;
            }
            else if (path.StartsWith("node-types/", StringComparison.Ordinal))
            {
                var table = ReadNodeTypes(path, root);
                nodeTypes[table.Cloud] = table;
            }
            else if (path == "sql-warehouses.yaml")
            {
                warehouses = ReadWarehouses(path, root);
            }
            else if (path == "not-modeled.yaml")
            {
                notModeled = ReadNotModeled(path, root);
            }
            else if (path == "defaults.yaml")
            {
                defaults = ReadDefaults(path, root);
            }
            else if (path == "runtimes.yaml")
            {
                runtimes = ReadRuntimes(path, root);
            }
            else if (path == "serverless.yaml")
            {
                serverless = ReadServerless(path, root);
            }

            // Other files are allowed, so later changes can add catalog files (defaults.yaml, runtimes.yaml)
            // without breaking older loaders.
        }

        return new PriceCatalog
        {
            Prices = prices,
            NodeTypes = nodeTypes,
            Warehouses = warehouses ?? throw new DoctorException("Catalog is missing sql-warehouses.yaml."),
            NotModeled = notModeled ?? throw new DoctorException("Catalog is missing not-modeled.yaml."),
            Defaults = defaults,
            Runtimes = runtimes,
            Serverless = serverless,
        };
    }

    public static PriceSheet ParsePriceSheet(string path, string text) => ReadPrices(path, Parse(path, text));

    private static YamlMappingNode Parse(string path, string text)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new DoctorException($"catalog/{path}:{ex.Start.Line}: invalid YAML: {ex.Message}", ex);
        }

        return stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode mapping
            ? mapping
            : throw new DoctorException($"catalog/{path}: expected a mapping.");
    }

    private static PriceSheet ReadPrices(string path, YamlMappingNode root)
    {
        var file = Header(path, root);
        Dictionary<string, CatalogValue> DbuSection(string key)
        {
            var section = new Dictionary<string, CatalogValue>(StringComparer.Ordinal);
            if (Child(root, key) is YamlMappingNode dbuNode)
            {
                foreach (var (k, v) in dbuNode.Children)
                {
                    var sku = Scalar(path, k);
                    if (!Skus.All.Contains(sku, StringComparer.Ordinal))
                    {
                        throw Error(path, k, $"unknown SKU `{sku}`.");
                    }

                    section[sku] = Value(path, v, file, "price");
                }
            }

            return section;
        }

        var dbu = DbuSection("dbu");
        var dbuEnterprise = DbuSection("dbu_enterprise");

        var vmFile = Child(root, "vm_source") is YamlScalarNode { Value: { Length: > 0 } vmSource }
            ? new Provenance(file.AsOf, vmSource)
            : null;
        var vm = new Dictionary<string, VmPrice>(StringComparer.Ordinal);
        if (Child(root, "vm") is YamlMappingNode vmNode)
        {
            foreach (var (k, v) in vmNode.Children)
            {
                var nodeType = Scalar(path, k);
                if (v is not YamlMappingNode columns)
                {
                    throw Error(path, v, $"VM `{nodeType}` must be a mapping of commitment columns.");
                }

                var values = new Dictionary<Commitment, decimal>();
                foreach (var (ck, cv) in columns.Children)
                {
                    var column = Scalar(path, ck);
                    if (!EnumNames.Commitments.TryGetValue(column, out var commitment))
                    {
                        throw Error(path, ck, $"unknown commitment column `{column}`.");
                    }

                    values[commitment] = Number(path, cv);
                }

                vm[nodeType] = new VmPrice(values, vmFile ?? file);
            }
        }

        return new PriceSheet
        {
            Cloud = CloudOf(path, root),
            Region = RequiredString(path, root, "region"),
            Currency = Child(root, "currency") is YamlScalarNode { Value: { } currency } ? currency : "USD",
            Dbu = dbu,
            DbuEnterprise = dbuEnterprise,
            Vm = vm,
            VmProvenance = vmFile,
        };
    }

    private static NodeTypeTable ReadNodeTypes(string path, YamlMappingNode root)
    {
        var file = Header(path, root);
        var photonMethod = PhotonMethod.None;
        var multipliers = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (Child(root, "photon") is YamlMappingNode photon)
        {
            photonMethod = RequiredString(path, photon, "method") switch
            {
                "dbu_count" => PhotonMethod.DbuCount,
                "multiplier" => PhotonMethod.Multiplier,
                "sku" => PhotonMethod.Sku,
                "none" => PhotonMethod.None,
                var other => throw Error(path, photon, $"unknown photon method `{other}`. Allowed: dbu_count, multiplier, sku, none."),
            };
            if (Child(photon, "multipliers") is YamlMappingNode m)
            {
                foreach (var (k, v) in m.Children)
                {
                    multipliers[Scalar(path, k)] = Number(path, v);
                }
            }
        }

        var nodes = new Dictionary<string, NodeType>(StringComparer.Ordinal);
        if (Child(root, "node_types") is YamlMappingNode nodeTypes)
        {
            foreach (var (k, v) in nodeTypes.Children)
            {
                var id = Scalar(path, k);
                if (v is not YamlMappingNode entry)
                {
                    throw Error(path, v, $"node type `{id}` must be a mapping.");
                }

                var provenance = EntryProvenance(path, entry, file);
                nodes[id] = new NodeType(
                    id,
                    Number(path, Child(entry, "dbu") ?? throw Error(path, entry, $"node type `{id}` has no `dbu`.")),
                    OptionalNumber(path, entry, "dbu_all_purpose"),
                    OptionalNumber(path, entry, "photon_dbu_jobs"),
                    OptionalNumber(path, entry, "photon_dbu_all_purpose"),
                    Child(entry, "family") is YamlScalarNode { Value: { } family } ? family : null,
                    provenance);
            }
        }

        return new NodeTypeTable
        {
            Cloud = CloudOf(path, root),
            PhotonMethod = photonMethod,
            PhotonMultipliers = multipliers,
            NodeTypes = nodes,
            Provenance = file,
        };
    }

    private static WarehouseTable ReadWarehouses(string path, YamlMappingNode root)
    {
        var file = Header(path, root);
        var sizes = new Dictionary<string, CatalogValue>(StringComparer.Ordinal);
        if (Child(root, "sizes") is YamlMappingNode sizeNode)
        {
            foreach (var (k, v) in sizeNode.Children)
            {
                sizes[Scalar(path, k)] = Value(path, v, file, "dbu");
            }
        }

        var layouts = new Dictionary<Cloud, IReadOnlyDictionary<string, WarehouseVmLayout>>();
        if (Child(root, "vm_layout") is YamlMappingNode layoutNode)
        {
            foreach (var (ck, cv) in layoutNode.Children)
            {
                var cloudName = Scalar(path, ck);
                if (!EnumNames.Clouds.TryGetValue(cloudName, out var cloud))
                {
                    throw Error(path, ck, $"unknown cloud `{cloudName}`.");
                }

                var perSize = new Dictionary<string, WarehouseVmLayout>(StringComparer.Ordinal);
                foreach (var (sk, sv) in ((YamlMappingNode)cv).Children)
                {
                    var entry = (YamlMappingNode)sv;
                    perSize[Scalar(path, sk)] = new WarehouseVmLayout(
                        RequiredString(path, entry, "driver"),
                        RequiredString(path, entry, "worker"),
                        (int)Number(path, Child(entry, "workers") ?? throw Error(path, entry, "missing `workers`.")));
                }

                layouts[cloud] = perSize;
            }
        }

        return new WarehouseTable { Sizes = sizes, VmLayouts = layouts, Provenance = file };
    }

    /// <summary>Optional: a catalog without defaults.yaml has no defaults, and rules that need one skip.</summary>
    private static CatalogDefaults ReadDefaults(string path, YamlMappingNode root)
    {
        var file = Header(path, root);
        CatalogValue? autoStop = null;
        if (Child(root, "sql_warehouse") is YamlMappingNode warehouse && Child(warehouse, "auto_stop_mins") is { } node)
        {
            if (node is not YamlScalarNode { Value: { } text } ||
                !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
                (minutes != 0 && minutes < 10))
            {
                throw new DoctorException($"catalog/{path}: `sql_warehouse.auto_stop_mins` must be 0 or a whole number of at least 10 minutes.");
            }

            autoStop = new CatalogValue(minutes, file);
        }

        return new CatalogDefaults { WarehouseAutoStopMinutes = autoStop };
    }

    /// <summary>Optional, like defaults.yaml: without it, DBU012 has no dates and reports nothing.</summary>
    private static RuntimeTable ReadRuntimes(string path, YamlMappingNode root)
    {
        var file = Header(path, root);
        var dates = new Dictionary<Version, (DateOnly, Provenance)>();
        if (Child(root, "runtimes") is not YamlMappingNode runtimes)
        {
            throw new DoctorException($"catalog/{path}: `runtimes` must be a mapping of versions such as \"15.4\".");
        }

        foreach (var (keyNode, valueNode) in runtimes.Children)
        {
            var key = (keyNode as YamlScalarNode)?.Value;
            if (key is null || !Version.TryParse(key, out var version) || version.Build >= 0 ||
                valueNode is not YamlMappingNode entry ||
                Child(entry, "end_of_support") is not YamlScalarNode { Value: { } text } ||
                !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new DoctorException($"catalog/{path}: runtime `{key}` needs a major.minor key and an `end_of_support` date (yyyy-MM-dd).");
            }

            dates[version] = (date, EntryProvenance(path, entry, file));
        }

        return new RuntimeTable { EndOfSupport = dates };
    }

    /// <summary>Optional: without serverless.yaml, apps are Unknown.</summary>
    private static ServerlessTable ReadServerless(string path, YamlMappingNode root)
    {
        var file = Header(path, root);
        var sizes = new Dictionary<string, CatalogValue>(StringComparer.OrdinalIgnoreCase);
        var defaultSize = "MEDIUM";
        if (Child(root, "apps") is YamlMappingNode apps)
        {
            if (Child(apps, "sizes") is not YamlMappingNode sizeNode)
            {
                throw new DoctorException($"catalog/{path}: `apps.sizes` must map compute sizes to DBU per hour.");
            }

            foreach (var (keyNode, valueNode) in sizeNode.Children)
            {
                var key = (keyNode as YamlScalarNode)?.Value;
                if (key is null || valueNode is not YamlScalarNode { Value: { } text } ||
                    !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbu) || dbu <= 0)
                {
                    throw new DoctorException($"catalog/{path}: app size `{key}` needs a positive DBU per hour.");
                }

                sizes[key] = new CatalogValue(dbu, file);
            }

            if (Child(apps, "default_size") is YamlScalarNode { Value: { } d })
            {
                defaultSize = d;
            }
        }

        var perCu = new Dictionary<Cloud, CatalogValue>();
        if (Child(root, "lakebase") is YamlMappingNode lakebase && Child(lakebase, "dbu_per_cu") is YamlMappingNode clouds)
        {
            foreach (var (keyNode, valueNode) in clouds.Children)
            {
                var key = (keyNode as YamlScalarNode)?.Value;
                if (key is null || !EnumNames.Clouds.TryGetValue(key, out var cloud) || valueNode is not YamlMappingNode entry ||
                    Child(entry, "value") is not YamlScalarNode { Value: { } text } ||
                    !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbu) || dbu <= 0)
                {
                    throw new DoctorException($"catalog/{path}: `lakebase.dbu_per_cu.{key}` needs a cloud name and a positive `value`.");
                }

                perCu[cloud] = new CatalogValue(dbu, EntryProvenance(path, entry, file));
            }
        }

        return new ServerlessTable { AppSizes = sizes, AppDefaultSize = defaultSize, LakebaseDbuPerCu = perCu };
    }

    private static NotModeledTable ReadNotModeled(string path, YamlMappingNode root)
    {
        _ = Header(path, root);
        var byCloud = new Dictionary<Cloud, IReadOnlyList<string>>();
        foreach (var (name, cloud) in EnumNames.Clouds)
        {
            if (Child(root, name) is { } node)
            {
                byCloud[cloud] = Strings(path, node);
            }
        }

        return new NotModeledTable
        {
            Everywhere = Child(root, "all") is { } all ? Strings(path, all) : [],
            ByCloud = byCloud,
            NotVisible = Child(root, "not_visible") is { } nv ? Strings(path, nv) : [],
            WithoutVmPrices = RequiredString(path, root, "without_vm_prices"),
        };
    }

    private static Provenance Header(string path, YamlMappingNode root)
    {
        var asOf = RequiredString(path, root, "as_of");
        if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new DoctorException($"catalog/{path}: `as_of` must be a date (yyyy-MM-dd), not `{asOf}`.");
        }

        return new Provenance(date, RequiredString(path, root, "source"));
    }

    private static Provenance EntryProvenance(string path, YamlMappingNode entry, Provenance file)
    {
        var asOf = Child(entry, "as_of") is YamlScalarNode { Value: { } a } &&
                   DateOnly.TryParseExact(a, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : file.AsOf;
        var source = Child(entry, "source") is YamlScalarNode { Value: { } s } ? s : file.Source;
        return new Provenance(asOf, source);
    }

    private static CatalogValue Value(string path, YamlNode node, Provenance file, string valueKey)
    {
        if (node is YamlMappingNode entry)
        {
            var value = Child(entry, valueKey) ?? throw Error(path, entry, $"missing `{valueKey}`.");
            return new CatalogValue(Number(path, value), EntryProvenance(path, entry, file));
        }

        return new CatalogValue(Number(path, node), file);
    }

    private static Cloud CloudOf(string path, YamlMappingNode root)
    {
        var name = RequiredString(path, root, "cloud");
        return EnumNames.Clouds.TryGetValue(name, out var cloud) ? cloud : throw new DoctorException($"catalog/{path}: unknown cloud `{name}`.");
    }

    private static YamlNode? Child(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string RequiredString(string path, YamlMappingNode node, string key) =>
        Child(node, key) is YamlScalarNode { Value: { Length: > 0 } value } ? value : throw Error(path, node, $"missing `{key}`.");

    private static string Scalar(string path, YamlNode node) =>
        node is YamlScalarNode { Value: { } value } ? value : throw Error(path, node, "expected a string.");

    private static decimal Number(string path, YamlNode node) =>
        node is YamlScalarNode { Value: { } value } && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw Error(path, node, "expected a number.");

    private static decimal? OptionalNumber(string path, YamlMappingNode node, string key) =>
        Child(node, key) is { } value ? Number(path, value) : null;

    private static List<string> Strings(string path, YamlNode node) =>
        node is YamlSequenceNode sequence ? sequence.Children.Select(c => Scalar(path, c)).ToList() : throw Error(path, node, "expected a list.");

    private static DoctorException Error(string path, YamlNode node, string message) =>
        new($"catalog/{path}:{node.Start.Line}: {message}");
}
