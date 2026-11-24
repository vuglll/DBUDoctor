using DBUDoctor.Core;
using DBUDoctor.Core.Analysis;
using DBUDoctor.Core.Calibration;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Model;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Reporting;
using DBUDoctor.Core.Rules;
using DBUDoctor.Core.Scheduling;
using DBUDoctor.Databricks;
using DBUDoctor.GitHub;

namespace DBUDoctor.Cli;

public sealed record DiffArguments
{
    public CliFlags Flags { get; init; } = new();
    public string? BaseJson { get; init; }
    public string? HeadJson { get; init; }
    public string? Output { get; init; }

    /// <summary><c>--var name=value</c> arguments for <c>bundle validate</c>.</summary>
    public IReadOnlyList<string> Vars { get; init; } = [];
}

/// <summary>Everything <c>dbudoctor diff</c> touches outside the pure engine, injectable for tests.</summary>
public sealed class DiffEnvironment
{
    public required IReadOnlyDictionary<string, string?> Variables { get; init; }
    public required string WorkingDirectory { get; init; }
    public required TextWriter Out { get; init; }
    public required TextWriter Error { get; init; }
    public IProcessRunner Processes { get; init; } = ProcessRunner.Instance;
    public Func<HttpClient> Http { get; init; } = () => new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    public TimeProvider Time { get; init; } = TimeProvider.System;
    public Func<PriceCatalog>? Catalog { get; init; }

    /// <summary>Replaces the Statement Execution API client (calibration tests).</summary>
    public Func<DatabricksCredentials, ISqlRunner>? Sql { get; init; }
    public DateTimeOffset? Reference { get; init; }
    public string ToolVersion { get; init; } = ToolInfo.Version;

    public static DiffEnvironment FromProcess() => new()
    {
        Variables = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string?)e.Value, StringComparer.Ordinal),
        WorkingDirectory = Environment.CurrentDirectory,
        Out = Console.Out,
        Error = Console.Error,
    };
}

public static class DiffCommand
{
    public static async Task<int> RunAsync(DiffArguments args, DiffEnvironment env, CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunCoreAsync(args, env, cancellationToken).ConfigureAwait(false);
        }
        catch (DoctorException ex)
        {
            await env.Error.WriteLineAsync("dbudoctor: " + ex.Message).ConfigureAwait(false);
            return ExitCodes.InvalidInput;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await env.Error.WriteLineAsync($"dbudoctor: internal error: {ex.GetType().Name}: {ex.Message}").ConfigureAwait(false);
            return ExitCodes.InvalidInput;
        }
    }

    private static async Task<int> RunCoreAsync(DiffArguments args, DiffEnvironment env, CancellationToken cancellationToken)
    {
        if ((args.BaseJson is null) != (args.HeadJson is null))
        {
            throw new DoctorException("--base-json and --head-json go together: pass both, or neither.");
        }

        var jsonMode = args.BaseJson is not null;
        var variables = BundleVariable.Parse(args.Vars);
        if (jsonMode && variables.Count > 0)
        {
            throw new DoctorException("--var applies to `bundle validate`, which does not run with --base-json and --head-json.");
        }

        Git? git = null;
        string root;
        if (jsonMode)
        {
            root = env.WorkingDirectory;
        }
        else
        {
            git = new Git(env.Processes, env.WorkingDirectory);
            root = git.RepositoryRoot();
        }

        var (configPath, explicitConfig) = SettingsResolver.ConfigPath(args.Flags, env.Variables, root);
        var config = ConfigLoader.Load(Path.GetFullPath(configPath, env.WorkingDirectory), explicitConfig, RuleRegistry.Parameters);
        var options = SettingsResolver.Apply(config, args.Flags, env.Variables);
        // DBUDOCTOR_CATALOG_DIR replaces the embedded catalog with a directory laid out like catalog/.
        var catalog = env.Catalog?.Invoke()
            ?? (env.Variables.GetValueOrDefault("DBUDOCTOR_CATALOG_DIR") is { Length: > 0 } catalogDir
                ? CatalogLoader.LoadDirectory(Path.GetFullPath(catalogDir, env.WorkingDirectory))
                : CatalogLoader.LoadEmbedded());
        var reference = env.Reference ?? RunFrequency.DefaultReference(env.Time);
        var fallbackHost = env.Variables.GetValueOrDefault("DATABRICKS_HOST");

        var inputs = jsonMode
            ? [new ResolvedInput(FromJson(Path.GetFullPath(args.BaseJson!, env.WorkingDirectory), Path.GetFullPath(args.HeadJson!, env.WorkingDirectory), config), null, null)]
            : ResolveWithCli(git!, root, config, options, env, variables);

        using var calibrationHttp = config.Calibration.Enabled ? env.Http() : null;
        CalibrationRunner? calibration = null;
        if (calibrationHttp is not null)
        {
            calibrationHttp.DefaultRequestHeaders.UserAgent.ParseAdd($"dbudoctor/{env.ToolVersion}");
            calibration = new CalibrationRunner(
                config.Calibration,
                env.Variables,
                env.Processes,
                TryLocateCli(env),
                calibrationHttp,
                PriceRefresher.DefaultCacheDirectory(env.Variables),
                env.Time,
                env.Sql);
        }

        var reports = new List<BundleReport>();
        var refreshed = new Dictionary<(Cloud?, string?), (PriceCatalog, string?)>();
        foreach (var (input, summaryJson, summaryError) in inputs)
        {
            var warnings = new List<string>();
            var bundleCatalog = catalog;
            if (options.RefreshPrices)
            {
                var cloud = config.Cloud ?? SettingsResolver.InferCloud(input.Head.WorkspaceHost ?? input.Base.WorkspaceHost ?? fallbackHost);
                if (!refreshed.TryGetValue((cloud, config.Region), out var result))
                {
                    using var http = env.Http();
                    http.DefaultRequestHeaders.UserAgent.ParseAdd($"dbudoctor/{env.ToolVersion}");
                    var refresher = new PriceRefresher(http, PriceRefresher.DefaultCacheDirectory(env.Variables), env.Time);
                    result = await refresher.RefreshAsync(catalog, cloud, config.Region, cancellationToken).ConfigureAwait(false);
                    refreshed[(cloud, config.Region)] = result;
                    if (result.Item2 is { } warning)
                    {
                        await env.Error.WriteLineAsync("warning: " + warning).ConfigureAwait(false);
                    }
                }

                bundleCatalog = result.Item1;
                if (result.Item2 is { } w)
                {
                    warnings.Add(w);
                }
            }

            UsageHistory? history = null;
            if (calibration is not null)
            {
                var cloud = config.Cloud ?? SettingsResolver.InferCloud(input.Head.WorkspaceHost ?? input.Base.WorkspaceHost ?? fallbackHost);
                history = await calibration.RunAsync(new CalibrationRequest(input.Base, input.Head, input.Target, cloud, summaryJson, summaryError), cancellationToken).ConfigureAwait(false);
                foreach (var warning in history.Warnings)
                {
                    await env.Error.WriteLineAsync("warning: " + warning).ConfigureAwait(false);
                }
            }

            reports.Add(BundleAnalyzer.Analyze(input, config, bundleCatalog, reference, options.ReportExisting, fallbackHost, warnings, history, DateOnly.FromDateTime(env.Time.GetUtcNow().UtcDateTime)));
        }

        var run = new RunReport { Bundles = reports };
        var output = options.Format switch
        {
            "json" => JsonRenderer.Render(run, env.ToolVersion),
            "markdown" => MarkdownRenderer.Render(run),
            _ => ConsoleRenderer.Render(run),
        };

        if (args.Output is { } outputPath)
        {
            var full = Path.GetFullPath(outputPath, env.WorkingDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, output, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await env.Out.WriteAsync(output).ConfigureAwait(false);
        }

        if (options.GitHubComment)
        {
            await PublishCommentsAsync(run, config, env, args.Output is not null, cancellationToken).ConfigureAwait(false);
        }

        return run.ExitCode;
    }

    private static BundleInput FromJson(string baseJson, string headJson, DoctorConfig config)
    {
        var baseBundle = ResolvedBundle.Load(baseJson);
        var headBundle = ResolvedBundle.Load(headJson);
        var name = headBundle.Name ?? baseBundle.Name ?? Path.GetFileNameWithoutExtension(headJson);
        var target = headBundle.Target ?? baseBundle.Target ?? config.Target ?? "default";
        return new BundleInput(name, target, baseBundle, headBundle);
    }

    private sealed record ResolvedInput(BundleInput Input, string? SummaryJson, string? SummaryError);

    private static string? TryLocateCli(DiffEnvironment env)
    {
        try
        {
            return DatabricksCli.Locate(env.Processes, env.Variables).Executable;
        }
        catch (DoctorException)
        {
            return null;
        }
    }

    private static List<ResolvedInput> ResolveWithCli(Git git, string root, DoctorConfig config, RunOptions options, DiffEnvironment env, IReadOnlyList<BundleVariable> variables)
    {
        var bundles = BundleDiscovery.Discover(root, config.Bundles);
        var refs = git.ResolveRefs(options.Base, options.Head, env.Variables);
        var cli = DatabricksCli.Locate(env.Processes, env.Variables);
        env.Error.WriteLine($"dbudoctor: comparing {Short(refs.Base)} ({refs.Source}) with {(refs.Head is null ? "the working tree" : Short(refs.Head))}.");

        var inputs = new List<ResolvedInput>();
        using var baseTree = git.AddWorktree(refs.Base);
        using var headTree = refs.Head is null ? null : git.AddWorktree(refs.Head);
        var headRoot = headTree?.Path ?? root;
        foreach (var relative in bundles)
        {
            var headDir = Path.GetFullPath(Path.Combine(headRoot, relative));
            var baseDir = Path.GetFullPath(Path.Combine(baseTree.Path, relative));
            if (BundleDiscovery.RootFile(headDir) is null)
            {
                env.Error.WriteLine($"dbudoctor: {relative} has no databricks.yml at the head ref; skipped.");
                continue;
            }

            var target = config.Target
                ?? BundleDiscovery.ProductionTarget(headDir)
                ?? throw new DoctorException($"No target in {relative}/databricks.yml has `mode: production`. Pass --target <name> or set `target` in .dbudoctor.yml.");

            var head = ResolvedBundle.Parse(cli.Validate(headDir, target, "the head ref", variables).Json, $"{relative} (head)");
            var baseBundle = BundleDiscovery.RootFile(baseDir) is null
                ? ResolvedBundle.Empty
                : ResolvedBundle.Parse(cli.Validate(baseDir, target, $"the base ref {Short(refs.Base)}", variables).Json, $"{relative} (base)");
            var name = head.Name ?? Path.GetFileName(Path.GetFullPath(Path.Combine(root, relative)));

            // The deployed IDs belong to the base: it is what the calibration target runs today.
            string? summaryJson = null, summaryError = null;
            if (config.Calibration.Enabled && BundleDiscovery.RootFile(baseDir) is not null)
            {
                (summaryJson, summaryError) = cli.Summary(baseDir, config.Calibration.Target ?? target, variables);
            }

            inputs.Add(new ResolvedInput(new BundleInput(name, target, baseBundle, head, relative), summaryJson, summaryError));
        }

        return inputs;
    }

    private static async Task PublishCommentsAsync(RunReport run, DoctorConfig config, DiffEnvironment env, bool reportInFile, CancellationToken cancellationToken)
    {
        var target = PullRequestTarget.FromEnvironment(env.Variables);
        // Workflow commands are read from standard output, so mask the token only when the report went
        // to a file; GitHub already masks github.token and secrets on its own.
        if (target.Token is { } token && reportInFile && env.Variables.GetValueOrDefault("GITHUB_ACTIONS") == "true")
        {
            await env.Out.WriteLineAsync($"::add-mask::{token}").ConfigureAwait(false);
        }

        using var http = env.Http();
        var publisher = new StickyComment(http, env.Error);
        foreach (var report in run.Bundles)
        {
            var outcome = await publisher.PublishAsync(target, report.Marker, MarkdownRenderer.Render(report), report.HasCostRelevantChanges, config.CommentOnNoChange, cancellationToken).ConfigureAwait(false);
            await env.Error.WriteLineAsync($"dbudoctor: {report.Bundle}/{report.Target}: comment {outcome.ToString().ToLowerInvariant()}.").ConfigureAwait(false);
        }
    }

    private static string Short(string reference) => reference.Length == 40 ? reference[..12] : reference;
}
