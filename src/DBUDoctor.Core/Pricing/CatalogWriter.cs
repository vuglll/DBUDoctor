using System.Globalization;
using System.Text;
using DBUDoctor.Core.Config;

namespace DBUDoctor.Core.Pricing;

/// <summary>Writes catalog files deterministically, for tools/CatalogGen and the --refresh-prices cache.</summary>
public static class CatalogWriter
{
    /// <summary>A price sheet in the layout of <c>prices/&lt;cloud&gt;/&lt;region&gt;.yaml</c>.</summary>
    public static string Prices(PriceSheet sheet, Provenance provenance, string header)
    {
        var b = new StringBuilder();
        foreach (var line in header.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            b.Append("# ").Append(line.TrimEnd()).Append('\n');
        }

        b.Append("cloud: ").Append(EnumNames.Of(sheet.Cloud)).Append('\n');
        b.Append("region: ").Append(sheet.Region).Append('\n');
        b.Append("currency: ").Append(sheet.Currency).Append('\n');
        b.Append("as_of: ").Append(provenance.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        b.Append("source: ").Append(provenance.Source).Append('\n');
        if (sheet.VmProvenance is { } vmProvenance && sheet.Vm.Count > 0 && vmProvenance.Source != provenance.Source)
        {
            b.Append("vm_source: ").Append(vmProvenance.Source).Append('\n');
        }

        b.Append("dbu:\n");
        foreach (var (sku, value) in sheet.Dbu.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            b.Append("  ").Append(sku).Append(": ").Append(Format(value.Value)).Append('\n');
        }

        if (sheet.DbuEnterprise.Count > 0)
        {
            b.Append("dbu_enterprise:\n");
            foreach (var (sku, value) in sheet.DbuEnterprise.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                b.Append("  ").Append(sku).Append(": ").Append(Format(value.Value)).Append('\n');
            }
        }

        if (sheet.Vm.Count == 0)
        {
            b.Append("vm: {}\n");
        }
        else
        {
            b.Append("vm:\n");
            foreach (var (type, price) in sheet.Vm.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var cols = price.Columns.OrderBy(c => c.Key).Select(c => $"{EnumNames.Of(c.Key)}: {Format(c.Value)}");
                b.Append("  ").Append(type).Append(": { ").Append(string.Join(", ", cols)).Append(" }\n");
            }
        }

        return b.ToString();
    }

    /// <summary>A node-type table in the layout of <c>node-types/&lt;cloud&gt;.yaml</c>, with the dbu_count Photon method.</summary>
    public static string NodeTypes(Cloud cloud, IReadOnlyDictionary<string, NodeType> nodes, Provenance provenance, string header)
    {
        var b = new StringBuilder();
        Header(b, header);
        b.Append("cloud: ").Append(EnumNames.Of(cloud)).Append('\n');
        b.Append("as_of: ").Append(provenance.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        b.Append("source: ").Append(provenance.Source).Append('\n');
        b.Append("photon:\n  method: dbu_count\n");
        b.Append("node_types:\n");
        foreach (var (id, node) in nodes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var parts = new List<string> { $"dbu: {Format(node.Dbu)}" };
            if (node.DbuAllPurpose is { } ap) parts.Add($"dbu_all_purpose: {Format(ap)}");
            if (node.PhotonDbuJobs is { } pj) parts.Add($"photon_dbu_jobs: {Format(pj)}");
            if (node.PhotonDbuAllPurpose is { } pa) parts.Add($"photon_dbu_all_purpose: {Format(pa)}");
            b.Append("  ").Append(Key(id)).Append(": { ").Append(string.Join(", ", parts)).Append(" }\n");
        }

        return b.ToString();
    }

    private static void Header(StringBuilder b, string header)
    {
        foreach (var line in header.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            b.Append("# ").Append(line.TrimEnd()).Append('\n');
        }
    }

    // Instance names such as "m5.xlarge" are plain YAML keys; quote anything unusual.
    private static string Key(string id) =>
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') ? id : "\"" + id.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Format(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
