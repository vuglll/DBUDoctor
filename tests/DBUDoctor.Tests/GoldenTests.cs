using DBUDoctor.Cli;
using DBUDoctor.Core.Config;

namespace DBUDoctor.Tests;

/// <summary>
/// Fixture bundles in tests/Fixtures/bundles/&lt;name&gt;/ (base.json, head.json, .dbudoctor.yml) against
/// expected.md and expected.json, priced with the fixed test catalog. Update the golden files only when
/// an output change is intended: UPDATE_GOLDEN=1 dotnet test, and say why in the commit message.
/// A fixture with recorded-queries.json runs with calibration: its rows replay the Statement Execution
/// API by statement purpose, with a throwaway cache.
/// </summary>
public class GoldenTests
{
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var dir in Directory.GetDirectories(Repo.Path("tests", "Fixtures", "bundles")).Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(dir), "markdown");
            data.Add(Path.GetFileName(dir), "json");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Output_matches_the_golden_file(string fixture, string format)
    {
        var dir = Repo.Path("tests", "Fixtures", "bundles", fixture);
        var output = new StringWriter();
        var error = new StringWriter();
        var variables = new Dictionary<string, string?>();
        var recordedPath = Path.Combine(dir, "recorded-queries.json");
        var cache = Path.Combine(Path.GetTempPath(), "dbudoctor-golden-" + Guid.NewGuid().ToString("N"));
        RecordedSql? recorded = null;
        if (File.Exists(recordedPath))
        {
            recorded = new RecordedSql(LoadRecorded(recordedPath));
            variables["DATABRICKS_TOKEN"] = "test-token";
            variables["DBUDOCTOR_CACHE_DIR"] = cache;
        }

        var env = new DiffEnvironment
        {
            Variables = variables,
            Sql = recorded is null ? null : _ => recorded,
            WorkingDirectory = dir,
            Out = output,
            Error = error,
            Catalog = TestCatalog.Load,
            Reference = Run.Reference,
            // A fixed clock, so end-of-support findings do not change as runtimes age.
            Time = new FixedTime(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero)),
            ToolVersion = "0.0.0-test",
        };

        int exit;
        try
        {
            exit = await DiffCommand.RunAsync(new DiffArguments { BaseJson = "base.json", HeadJson = "head.json", Flags = new CliFlags { Format = format } }, env);
        }
        finally
        {
            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, recursive: true);
            }
        }

        Assert.True(exit is 0 or 1, error.ToString());
        var expectedPath = Path.Combine(dir, format == "json" ? "expected.json" : "expected.md");
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            await File.WriteAllTextAsync(expectedPath, output.ToString());
        }

        Assert.True(File.Exists(expectedPath), $"{expectedPath} is missing; run UPDATE_GOLDEN=1 dotnet test");
        Assert.Equal(await File.ReadAllTextAsync(expectedPath), output.ToString());
    }

    private static Dictionary<string, IReadOnlyList<IReadOnlyList<string?>>> LoadRecorded(string path)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject()
            .Where(p => !p.Name.StartsWith('_'))
            .ToDictionary(
                p => p.Name,
                p => (IReadOnlyList<IReadOnlyList<string?>>)p.Value.EnumerateArray()
                    .Select(row => (IReadOnlyList<string?>)row.EnumerateArray().Select(v => v.ValueKind == System.Text.Json.JsonValueKind.Null ? null : v.GetString()).ToArray())
                    .ToList());
    }
}
