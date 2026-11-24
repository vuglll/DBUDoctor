using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DBUDoctor.Core.Config;

namespace DBUDoctor.Core.Pricing.Aws;

/// <summary>
/// Reads the public AWS Price List bulk files for EC2 (no credentials): the region index, then one CSV
/// per region, streamed record by record because a region's file is a few hundred MB.
/// </summary>
public sealed class AwsPriceListClient(HttpClient http)
{
    public const string Host = "https://pricing.us-east-1.amazonaws.com";

    public const string RegionIndexUrl = Host + "/offers/v1.0/aws/AmazonEC2/current/region_index.json";

    /// <summary>The CSV URL of the current EC2 offer per region code.</summary>
    public async Task<IReadOnlyDictionary<string, string>> RegionCsvUrlsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(RegionIndexUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("regions", out var regions) && regions.ValueKind == JsonValueKind.Object)
        {
            foreach (var region in regions.EnumerateObject())
            {
                if (region.Value.TryGetProperty("currentVersionUrl", out var url) && url.GetString() is { } path && path.EndsWith(".json", StringComparison.Ordinal))
                {
                    urls[region.Name] = Host + path[..^".json".Length] + ".csv";
                }
            }
        }

        return urls;
    }

    /// <summary>Every record of one region's CSV, including the metadata lines before the header.</summary>
    public async IAsyncEnumerable<IReadOnlyList<string>> RecordsAsync(string url, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await Csv.ReadRecordAsync(reader, cancellationToken).ConfigureAwait(false) is { } record)
        {
            yield return record;
        }
    }
}

/// <summary>A small RFC 4180 reader: quoted fields, doubled quotes, line breaks inside quotes.</summary>
public static class Csv
{
    public static async Task<IReadOnlyList<string>?> ReadRecordAsync(TextReader reader, CancellationToken cancellationToken = default)
    {
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            return null;
        }

        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        while (true)
        {
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }

            if (!quoted)
            {
                break;
            }

            line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            field.Append('\n');
        }

        fields.Add(field.ToString());
        return fields;
    }

    public static IEnumerable<IReadOnlyList<string>> ReadAll(string text)
    {
        using var reader = new StringReader(text);
        while (ReadRecordAsync(reader).GetAwaiter().GetResult() is { } record)
        {
            yield return record;
        }
    }
}

/// <summary>
/// Turns EC2 price list records into VM prices per instance type: On-Demand as <c>payg</c>, and Standard
/// No Upfront reservations as <c>reserved_1y</c> and <c>reserved_3y</c>, all per hour. Only Linux,
/// shared-tenancy, unlicensed rows without pre-installed software, capacity status <c>Used</c>, count.
/// </summary>
public static class AwsPriceListNormalizer
{
    public sealed record Result(
        IReadOnlyDictionary<string, IReadOnlyDictionary<Commitment, decimal>> Prices,
        IReadOnlyList<string> Conflicts);

    private static readonly string[] Required =
    [
        "TermType", "Unit", "PricePerUnit", "Currency", "LeaseContractLength", "PurchaseOption", "OfferingClass",
        "Product Family", "Instance Type", "Tenancy", "Operating System", "License Model", "operation",
        "CapacityStatus", "Pre Installed S/W", "MarketOption",
    ];

    public static async Task<Result> NormalizeAsync(IAsyncEnumerable<IReadOnlyList<string>> records, IReadOnlySet<string> instanceTypes, CancellationToken cancellationToken = default)
    {
        var state = new State(instanceTypes);
        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            state.Add(record);
        }

        return state.Finish();
    }

    public static Result Normalize(IEnumerable<IReadOnlyList<string>> records, IReadOnlySet<string> instanceTypes)
    {
        var state = new State(instanceTypes);
        foreach (var record in records)
        {
            state.Add(record);
        }

        return state.Finish();
    }

    private sealed class State(IReadOnlySet<string> instanceTypes)
    {
        private readonly Dictionary<(string Type, Commitment Column), decimal> _prices = [];
        private readonly HashSet<(string Type, Commitment Column)> _conflicted = [];
        private readonly List<string> _conflicts = [];
        private Dictionary<string, int>? _columns;

        public void Add(IReadOnlyList<string> record)
        {
            if (_columns is null)
            {
                if (record.Count > 0 && record[0] == "SKU")
                {
                    _columns = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (var i = 0; i < record.Count; i++)
                    {
                        _columns.TryAdd(record[i], i);
                    }

                    var missing = Required.Where(c => !_columns.ContainsKey(c)).ToList();
                    if (missing.Count > 0)
                    {
                        throw new DoctorException($"The AWS price list has no column {string.Join(", ", missing.Select(m => $"`{m}`"))}; its layout may have changed. Nothing was written.");
                    }
                }

                return;
            }

            string Get(string column) => _columns[column] < record.Count ? record[_columns[column]] : "";

            var type = Get("Instance Type");
            if (!instanceTypes.Contains(type) ||
                Get("Product Family") != "Compute Instance" ||
                Get("Tenancy") != "Shared" ||
                Get("Operating System") != "Linux" ||
                Get("License Model") != "No License required" ||
                Get("Pre Installed S/W") != "NA" ||
                Get("operation") != "RunInstances" ||
                Get("CapacityStatus") != "Used" ||
                Get("MarketOption") != "OnDemand" ||
                Get("Unit") != "Hrs" ||
                Get("Currency") != "USD")
            {
                return;
            }

            Commitment? column = Get("TermType") switch
            {
                "OnDemand" => Commitment.Payg,
                "Reserved" when Get("OfferingClass") == "standard" && Get("PurchaseOption") == "No Upfront" => Get("LeaseContractLength") switch
                {
                    "1yr" => Commitment.Reserved1y,
                    "3yr" => Commitment.Reserved3y,
                    _ => null,
                },
                _ => null,
            };
            if (column is not { } c ||
                !decimal.TryParse(Get("PricePerUnit"), NumberStyles.Float, CultureInfo.InvariantCulture, out var price) ||
                price <= 0)
            {
                return;
            }

            // Strip trailing zeros: the list writes 0.2040000000.
            price = decimal.Round(price, 6) / 1.000000000000000000000000000000000m;
            var key = (type, c);
            if (_conflicted.Contains(key))
            {
                return;
            }

            if (_prices.TryGetValue(key, out var existing) && existing != price)
            {
                _prices.Remove(key);
                _conflicted.Add(key);
                _conflicts.Add($"{type} {EnumNames.Of(c)}: {existing.ToString(CultureInfo.InvariantCulture)} and {price.ToString(CultureInfo.InvariantCulture)}");
                return;
            }

            _prices[key] = price;
        }

        public Result Finish()
        {
            if (_columns is null)
            {
                throw new DoctorException("The AWS price list has no header row; its layout may have changed. Nothing was written.");
            }

            var prices = _prices
                .GroupBy(kv => kv.Key.Type, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyDictionary<Commitment, decimal>)g.ToDictionary(kv => kv.Key.Column, kv => kv.Value),
                    StringComparer.Ordinal);
            return new Result(prices, _conflicts);
        }
    }
}
