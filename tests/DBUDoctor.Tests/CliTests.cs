using System.Net;
using DBUDoctor.Cli;
using DBUDoctor.Core;
using DBUDoctor.Core.Config;
using DBUDoctor.Databricks;
using DBUDoctor.GitHub;

namespace DBUDoctor.Tests;

/// <summary>Runs real git, and fakes `databricks` by returning the resolved.json next to databricks.yml.</summary>
internal sealed class FakeDatabricks : IProcessRunner
{
    public const string Path = "/fake/databricks";

    public string Version { get; set; } = "Databricks CLI v1.24.0";
    public bool RejectLocations { get; set; }
    public Func<string, ProcessResult?>? FailIn { get; set; }
    public List<IReadOnlyList<string>> Calls { get; } = [];

    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        if (fileName != Path)
        {
            return ProcessRunner.Instance.Run(fileName, arguments, workingDirectory);
        }

        Calls.Add(arguments);
        if (arguments is ["--version"])
        {
            return new ProcessResult(0, Version + "\n", "");
        }

        if (RejectLocations && arguments.Contains("--include-locations"))
        {
            return new ProcessResult(1, "", "Error: unknown flag: --include-locations");
        }

        if (FailIn?.Invoke(workingDirectory) is { } failure)
        {
            return failure;
        }

        var resolved = System.IO.Path.Combine(workingDirectory, "resolved.json");
        return File.Exists(resolved)
            ? new ProcessResult(0, File.ReadAllText(resolved), "Warning: something minor")
            : new ProcessResult(1, "", "Error: no resolved.json");
    }
}

internal sealed class TempRepo : IDisposable
{
    public TempRepo()
    {
        Root = Directory.CreateTempSubdirectory("dbud-repo-").FullName;
        Git("init", "--quiet", "--initial-branch=main");
        Git("config", "user.email", "test@example.com");
        Git("config", "user.name", "Test");
        Git("config", "commit.gpgsign", "false");
    }

    public string Root { get; }

    public void Write(string relative, string content)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Bundle(string dir, string resolvedJson, string targets = "targets:\n  dev:\n    mode: development\n  prod:\n    mode: production\n")
    {
        Write(Path.Combine(dir, "databricks.yml"), "bundle:\n  name: " + Path.GetFileName(dir) + "\n" + targets);
        Write(Path.Combine(dir, "resolved.json"), resolvedJson);
    }

    public string Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "--quiet", "-m", message);
        return Git("rev-parse", "HEAD").Trim();
    }

    public string Git(params string[] args)
    {
        var result = ProcessRunner.Instance.Run("git", args, Root);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', args)}: {result.StandardError}");
        return result.StandardOutput;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public class CliTests
{
    private static string Resolved(string name, string resources) =>
        $$"""{ "bundle": { "name": "{{name}}", "target": "prod" }, "workspace": { "host": "{{Bundle.AzureHost}}" }, "resources": {{resources}} }""";

    private static (DiffEnvironment Env, StringWriter Out, StringWriter Error) Environment(string cwd, FakeDatabricks runner, Dictionary<string, string?>? extra = null)
    {
        var vars = new Dictionary<string, string?> { ["DATABRICKS_CLI_PATH"] = FakeDatabricks.Path, ["DBUDOCTOR_REGION"] = "westeurope" };
        foreach (var (k, v) in extra ?? []) vars[k] = v;
        var output = new StringWriter();
        var error = new StringWriter();
        return (new DiffEnvironment
        {
            Variables = vars,
            WorkingDirectory = cwd,
            Out = output,
            Error = error,
            Processes = runner,
            Catalog = TestCatalog.Load,
            Reference = Run.Reference,
            ToolVersion = "0.0.0-test",
        }, output, error);
    }

    [Fact]
    public async Task Base_json_without_head_json_is_invalid_input()
    {
        var (env, _, error) = Environment(Path.GetTempPath(), new FakeDatabricks());
        Assert.Equal(2, await DiffCommand.RunAsync(new DiffArguments { BaseJson = "a.json" }, env));
        Assert.Contains("--base-json and --head-json go together", error.ToString());
    }

    [Fact]
    public async Task Offline_comparison_runs_no_git_or_databricks()
    {
        var dir = Repo.Path("tests", "Fixtures", "bundles", "azure-analytics");
        var runner = new FakeDatabricks();
        var (env, output, error) = Environment(dir, runner);

        var exit = await DiffCommand.RunAsync(new DiffArguments { BaseJson = "base.json", HeadJson = "head.json", Flags = new CliFlags { Format = "markdown" } }, env);

        Assert.True(exit == 1, error.ToString());
        Assert.Empty(runner.Calls);
        Assert.Contains("**+$2,651 / month** (+480% on changed resources)", output.ToString());
    }

    [Fact]
    public async Task Invalid_config_is_exit_code_2()
    {
        var dir = Directory.CreateTempSubdirectory("dbud-").FullName;
        File.WriteAllText(Path.Combine(dir, "c.yml"), "pricng: {}\n");
        File.WriteAllText(Path.Combine(dir, "b.json"), "{}");
        var (env, _, error) = Environment(dir, new FakeDatabricks());

        Assert.Equal(2, await DiffCommand.RunAsync(new DiffArguments { BaseJson = "b.json", HeadJson = "b.json", Flags = new CliFlags { Config = "c.yml" } }, env));
        Assert.Contains("pricng", error.ToString());
    }

    [Fact]
    public void Missing_cli_prints_minimum_version_and_install_command()
    {
        var ex = Assert.Throws<DoctorException>(() => DatabricksCli.Locate(new FakeDatabricks(), new Dictionary<string, string?> { ["PATH"] = "/nonexistent" }));
        Assert.Contains("not found on PATH", ex.Message);
        Assert.Contains($"v{DatabricksCli.MinimumVersion}", ex.Message);
        Assert.Contains("brew install databricks", ex.Message);
    }

    [Fact]
    public void Old_cli_is_rejected()
    {
        var runner = new FakeDatabricks { Version = "Databricks CLI v0.200.1" };
        var ex = Assert.Throws<DoctorException>(() => DatabricksCli.Locate(runner, new Dictionary<string, string?> { ["DATABRICKS_CLI_PATH"] = FakeDatabricks.Path }));
        Assert.Contains("v0.200.1", ex.Message);
    }

    [Fact]
    public void Legacy_python_cli_is_rejected()
    {
        var runner = new FakeDatabricks { Version = "Version 0.18.0" };
        Assert.Throws<DoctorException>(() => DatabricksCli.Locate(runner, new Dictionary<string, string?> { ["DATABRICKS_CLI_PATH"] = FakeDatabricks.Path }));
    }

    [Fact]
    public void Validate_retries_without_locations_when_the_flag_is_unknown()
    {
        var dir = Directory.CreateTempSubdirectory("dbud-").FullName;
        File.WriteAllText(Path.Combine(dir, "resolved.json"), "{}");
        var runner = new FakeDatabricks { RejectLocations = true };

        var result = new DatabricksCli(runner, FakeDatabricks.Path).Validate(dir, "prod", "head");

        Assert.False(result.LocationsIncluded);
        Assert.Equal(2, runner.Calls.Count);
        Assert.DoesNotContain("--include-locations", runner.Calls[1]);
        Assert.Equal("Warning: something minor", result.Diagnostics);
    }

    [Fact]
    public async Task Git_mode_diffs_every_bundle_and_removes_the_worktree()
    {
        using var repo = new TempRepo();
        repo.Bundle("etl", Resolved("etl", """{ "pipelines": { "p": { "continuous": false } } }"""));
        repo.Bundle("bi", Resolved("bi", "{}"));
        repo.Write(".hidden/databricks.yml", "bundle: { name: hidden }\n");
        repo.Write("node_modules/x/databricks.yml", "bundle: { name: vendored }\n");
        var baseSha = repo.Commit("base");
        repo.Bundle("etl", Resolved("etl", """{ "pipelines": { "p": { "continuous": true } } }"""));

        var runner = new FakeDatabricks();
        var (env, output, error) = Environment(repo.Root, runner);
        var exit = await DiffCommand.RunAsync(new DiffArguments { Flags = new CliFlags { Base = baseSha, Format = "json" } }, env);

        Assert.True(exit == 0, error.ToString());
        var json = System.Text.Json.Nodes.JsonNode.Parse(output.ToString())!;
        Assert.Equal(["bi", "etl"], json["bundles"]!.AsArray().Select(b => (string)b!["root"]!));
        Assert.Equal("DBU004", (string)json["bundles"]![1]!["findings"]![0]!["ruleId"]!);
        Assert.All(runner.Calls.Where(c => c[0] == "bundle"), c => Assert.Equal(["bundle", "validate", "--target", "prod", "--output", "json", "--include-locations"], c));
        Assert.Single(repo.Git("worktree", "list").Trim().Split('\n'));
        Assert.Equal("", repo.Git("status", "--porcelain").Replace("?? etl/resolved.json\n", "", StringComparison.Ordinal).Replace(" M etl/resolved.json\n", "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Vars_are_passed_to_validate_at_both_refs()
    {
        using var repo = new TempRepo();
        repo.Bundle("etl", Resolved("etl", "{}"));
        var baseSha = repo.Commit("base");
        var runner = new FakeDatabricks();
        var (env, _, error) = Environment(repo.Root, runner);

        var exit = await DiffCommand.RunAsync(new DiffArguments { Flags = new CliFlags { Base = baseSha }, Vars = ["cluster_pool_id=0101-abc", "env=a=b"] }, env);

        Assert.True(exit == 0, error.ToString());
        var validates = runner.Calls.Where(c => c is ["bundle", "validate", ..]).ToList();
        Assert.Equal(2, validates.Count);
        Assert.All(validates, c => Assert.Equal(
            ["bundle", "validate", "--target", "prod", "--output", "json", "--var", "cluster_pool_id=0101-abc", "--var", "env=a=b", "--include-locations"], c));
    }

    [Fact]
    public async Task Missing_required_variable_suggests_var()
    {
        using var repo = new TempRepo();
        repo.Bundle("etl", Resolved("etl", "{}"));
        var baseSha = repo.Commit("base");
        var runner = new FakeDatabricks { FailIn = _ => new ProcessResult(1, "", "Error: no value assigned to required variable cluster_pool_id. Variables are usually assigned in databricks.yml") };
        var (env, _, error) = Environment(repo.Root, runner);

        Assert.Equal(2, await DiffCommand.RunAsync(new DiffArguments { Flags = new CliFlags { Base = baseSha } }, env));
        Assert.Contains("Pass `--var cluster_pool_id=<value>` to dbudoctor diff, or set BUNDLE_VAR_cluster_pool_id.", error.ToString());
    }

    [Theory]
    [InlineData("cluster_pool_id")]
    [InlineData("=value")]
    [InlineData("1abc=x")]
    [InlineData("a b=x")]
    public async Task Malformed_var_is_exit_code_2(string var)
    {
        using var repo = new TempRepo();
        repo.Bundle("etl", Resolved("etl", "{}"));
        var baseSha = repo.Commit("base");
        var runner = new FakeDatabricks();
        var (env, _, error) = Environment(repo.Root, runner);

        Assert.Equal(2, await DiffCommand.RunAsync(new DiffArguments { Flags = new CliFlags { Base = baseSha }, Vars = [var] }, env));
        Assert.Contains("the format is `name=value`", error.ToString());
        Assert.DoesNotContain(runner.Calls, c => c is ["bundle", ..]);
    }

    [Fact]
    public async Task Var_with_pre_resolved_json_is_exit_code_2()
    {
        var dir = Repo.Path("tests", "Fixtures", "bundles", "azure-analytics");
        var (env, _, error) = Environment(dir, new FakeDatabricks());

        Assert.Equal(2, await DiffCommand.RunAsync(new DiffArguments { BaseJson = "base.json", HeadJson = "head.json", Vars = ["a=b"] }, env));
        Assert.Contains("--var applies to `bundle validate`", error.ToString());
    }

    [Fact]
    public void Var_values_keep_equals_signs_and_the_last_value_wins()
    {
        var vars = BundleVariable.Parse(["a=1", "b=x=y", "empty=", "a=2"]);
        Assert.Equal([new BundleVariable("b", "x=y"), new BundleVariable("empty", ""), new BundleVariable("a", "2")], vars);
        Assert.Equal(["--var", "b=x=y", "--var", "empty=", "--var", "a=2"], BundleVariable.Arguments(vars));
    }

    [Fact]
    public void Summary_passes_vars()
    {
        var runner = new FakeDatabricks { FailIn = _ => new ProcessResult(1, "", "Error: not authenticated") };
        new DatabricksCli(runner, FakeDatabricks.Path).Summary(Path.GetTempPath(), "prod", [new BundleVariable("cluster_pool_id", "p")]);
        Assert.Equal(["bundle", "summary", "--target", "prod", "--output", "json", "--var", "cluster_pool_id=p"], runner.Calls.Single());
    }

    [Fact]
    public async Task Head_failure_exits_2_with_diagnostics_and_still_removes_the_worktree()
    {
        using var repo = new TempRepo();
        repo.Bundle("etl", Resolved("etl", "{}"));
        var baseSha = repo.Commit("base");
        var runner = new FakeDatabricks { FailIn = dir => !dir.Contains("dbudoctor-", StringComparison.Ordinal) ? new ProcessResult(1, "", "Error: unknown field: clusterz\n  at resources.jobs.x") : null };
        var (env, _, error) = Environment(repo.Root, runner);

        var exit = await DiffCommand.RunAsync(new DiffArguments { Flags = new CliFlags { Base = baseSha } }, env);

        Assert.Equal(2, exit);
        Assert.Contains("unknown field: clusterz", error.ToString());
        Assert.Single(repo.Git("worktree", "list").Trim().Split('\n'));
    }

    [Fact]
    public async Task New_bundle_has_an_empty_base()
    {
        using var repo = new TempRepo();
        repo.Write("README.md", "x");
        var baseSha = repo.Commit("base");
        repo.Bundle("etl", Resolved("etl", """{ "pipelines": { "p": { "continuous": true } } }"""));
        var (env, output, error) = Environment(repo.Root, new FakeDatabricks());

        var exit = await DiffCommand.RunAsync(new DiffArguments { Flags = new CliFlags { Base = baseSha, Format = "json" } }, env);

        Assert.True(exit == 0, error.ToString());
        var change = System.Text.Json.Nodes.JsonNode.Parse(output.ToString())!["bundles"]![0]!["changes"]![0]!;
        Assert.Equal("added", (string)change["kind"]!);
    }

    [Fact]
    public async Task No_production_target_asks_for_target()
    {
        using var repo = new TempRepo();
        repo.Bundle("etl", Resolved("etl", "{}"), targets: "targets:\n  dev:\n    mode: development\n");
        var baseSha = repo.Commit("base");
        var (env, _, error) = Environment(repo.Root, new FakeDatabricks());

        Assert.Equal(2, await DiffCommand.RunAsync(new DiffArguments { Flags = new CliFlags { Base = baseSha } }, env));
        Assert.Contains("--target", error.ToString());
    }

    [Fact]
    public void Pull_request_event_supplies_both_refs()
    {
        var eventPath = Path.Combine(Directory.CreateTempSubdirectory("dbud-").FullName, "event.json");
        File.WriteAllText(eventPath, """{ "number": 7, "pull_request": { "number": 7, "base": { "sha": "aaa" }, "head": { "sha": "bbb" } } }""");
        var env = new Dictionary<string, string?> { ["GITHUB_EVENT_NAME"] = "pull_request", ["GITHUB_EVENT_PATH"] = eventPath };

        var refs = new Git(new FakeDatabricks(), Path.GetTempPath()).ResolveRefs(null, null, env);

        Assert.Equal(("aaa", "bbb"), (refs.Base, refs.Head));
    }

    [Fact]
    public void Local_run_uses_the_merge_base_with_the_default_branch_and_the_working_tree()
    {
        using var repo = new TempRepo();
        repo.Write("a", "1");
        var first = repo.Commit("one");
        repo.Git("update-ref", "refs/remotes/origin/main", first);
        repo.Git("switch", "--quiet", "-c", "feature");
        repo.Write("a", "2");
        repo.Commit("two");

        var refs = new Git(ProcessRunner.Instance, repo.Root).ResolveRefs(null, null, new Dictionary<string, string?>());

        Assert.Equal(first, refs.Base);
        Assert.Null(refs.Head);
    }

    [Fact]
    public void Configured_bundles_must_exist()
    {
        using var repo = new TempRepo();
        repo.Bundle("etl", "{}");
        Assert.Equal(["etl"], BundleDiscovery.Discover(repo.Root, ["etl/"]));
        Assert.Throws<DoctorException>(() => BundleDiscovery.Discover(repo.Root, ["missing"]));
    }
}

public class StickyCommentTests
{
    private const string Marker = "<!-- dbudoctor:bundle=analytics;target=prod -->";

    private static PullRequestTarget Target(string? token = "t", string? summary = null) =>
        new("https://api.github.test", "o/r", 7, token, summary);

    [Fact]
    public async Task First_run_creates_the_comment()
    {
        var handler = new FakeHandler(r => r.Method == HttpMethod.Get ? AzureRetailPricesContractTests.Json("[]") : new HttpResponseMessage(HttpStatusCode.Created));
        var outcome = await new StickyComment(new HttpClient(handler), TextWriter.Null).PublishAsync(Target(), Marker, Marker + "\nbody", true, false);

        Assert.Equal(CommentOutcome.Created, outcome);
        var post = handler.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("https://api.github.test/repos/o/r/issues/7/comments", post.Url);
        Assert.Contains("dbudoctor:bundle=analytics", post.Body);
    }

    [Fact]
    public async Task Second_push_edits_the_existing_comment()
    {
        var handler = new FakeHandler(r => r.Method == HttpMethod.Get
            ? AzureRetailPricesContractTests.Json($$"""[ { "id": 1, "body": "other" }, { "id": 42, "body": "{{Marker}}\nold" } ]""")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var outcome = await new StickyComment(new HttpClient(handler), TextWriter.Null).PublishAsync(Target(), Marker, Marker + "\nnew", true, false);

        Assert.Equal(CommentOutcome.Updated, outcome);
        Assert.Equal("https://api.github.test/repos/o/r/issues/comments/42", handler.Requests.Single(r => r.Method == HttpMethod.Patch).Url);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Fork_without_write_token_writes_the_job_summary()
    {
        var summary = Path.Combine(Directory.CreateTempSubdirectory("dbud-").FullName, "summary.md");
        var handler = new FakeHandler(r => r.Method == HttpMethod.Get ? AzureRetailPricesContractTests.Json("[]") : new HttpResponseMessage(HttpStatusCode.Forbidden));
        var log = new StringWriter();

        var outcome = await new StickyComment(new HttpClient(handler), log).PublishAsync(Target(summary: summary), Marker, Marker + "\nreport", true, false);

        Assert.Equal(CommentOutcome.WroteJobSummary, outcome);
        Assert.Contains("report", File.ReadAllText(summary));
        Assert.Contains("pull-requests: write", log.ToString());
    }

    [Fact]
    public async Task Nothing_to_say_creates_no_comment()
    {
        var handler = new FakeHandler(r => AzureRetailPricesContractTests.Json("[]"));
        var outcome = await new StickyComment(new HttpClient(handler), TextWriter.Null).PublishAsync(Target(), Marker, "x", hasChanges: false, commentOnNoChange: false);

        Assert.Equal(CommentOutcome.Skipped, outcome);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task No_token_writes_the_job_summary()
    {
        var summary = Path.Combine(Directory.CreateTempSubdirectory("dbud-").FullName, "summary.md");
        var outcome = await new StickyComment(new HttpClient(new FakeHandler(_ => throw new InvalidOperationException())), TextWriter.Null)
            .PublishAsync(Target(token: null, summary: summary), Marker, "report", true, false);
        Assert.Equal(CommentOutcome.WroteJobSummary, outcome);
    }

    [Fact]
    public void Target_comes_from_the_actions_environment()
    {
        var eventPath = Path.Combine(Directory.CreateTempSubdirectory("dbud-").FullName, "event.json");
        File.WriteAllText(eventPath, """{ "pull_request": { "number": 12 } }""");
        var target = PullRequestTarget.FromEnvironment(new Dictionary<string, string?>
        {
            ["GITHUB_REPOSITORY"] = "o/r", ["GITHUB_EVENT_PATH"] = eventPath, ["GITHUB_TOKEN"] = "secret",
        });

        Assert.Equal(12, target.Number);
        Assert.Equal("https://api.github.com", target.ApiUrl);
    }
}
