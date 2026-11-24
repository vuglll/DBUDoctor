using DBUDoctor.Core.Calibration;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Diff;
using DBUDoctor.Core.Model;

namespace DBUDoctor.Databricks;

/// <summary>One bundle to calibrate: its two sides and, when the CLI ran, its <c>bundle summary</c> output.</summary>
public sealed record CalibrationRequest(
    ResolvedBundle Base,
    ResolvedBundle Head,
    string Target,
    Cloud? Cloud,
    string? SummaryJson,
    string? SummaryError);

/// <summary>
/// Calibration for one bundle: auth, deployed IDs, then the history queries through the daily cache.
/// Every failure becomes a warning on the returned history; the run itself never fails (spec:
/// "Calibration failure").
/// </summary>
public sealed class CalibrationRunner(
    CalibrationSettings settings,
    IReadOnlyDictionary<string, string?> env,
    IProcessRunner processes,
    string? cliExecutable,
    HttpClient http,
    string cacheDirectory,
    TimeProvider time,
    Func<DatabricksCredentials, ISqlRunner>? sqlFactory = null)
{
    /// <summary>Statements actually sent to the warehouse (not served from the cache).</summary>
    public int Executed { get; private set; }

    public async Task<UsageHistory> RunAsync(CalibrationRequest request, CancellationToken cancellationToken)
    {
        var target = settings.Target ?? request.Target;
        var wanted = BundleDiffer.Diff(request.Base, request.Head).Changed
            .Where(c => c.Kind != ChangeKind.Added)
            .Select(c => (c.Type, c.Key))
            .Distinct()
            .ToList();
        if (wanted.Count == 0)
        {
            return new UsageHistory { Target = target, WindowDays = settings.WindowDays, MinRuns = settings.MinRuns };
        }

        try
        {
            var credentials = await DatabricksAuth.ResolveAsync(
                request.Base.WorkspaceHost ?? request.Head.WorkspaceHost, env, http, processes, cliExecutable, cancellationToken).ConfigureAwait(false);
            var sql = new CachingSqlRunner(
                () => sqlFactory?.Invoke(credentials) ?? new StatementExecutionClient(http, credentials, settings.WarehouseId!),
                cacheDirectory,
                credentials.Host,
                time);

            var notes = new List<string>();
            var ids = new Dictionary<(string Type, string Key), string>();
            if (request.SummaryJson is { } summary)
            {
                foreach (var (key, id) in DeployedIds.FromSummary(summary))
                {
                    ids[key] = id;
                }
            }
            else
            {
                notes.Add(request.SummaryError is { } error
                    ? $"`bundle summary` failed ({error}); jobs matched by name"
                    : "no `bundle summary` for JSON input; jobs matched by name");
            }

            await MatchJobsByNameAsync(sql, request.Base, wanted, ids, cancellationToken).ConfigureAwait(false);

            var found = wanted.Where(ids.ContainsKey).ToDictionary(k => k, k => ids[k]);
            var history = await HistoryCollector.CollectAsync(
                sql, found, wanted.Where(k => !ids.ContainsKey(k)), settings, request.Cloud, target, cancellationToken).ConfigureAwait(false);
            history.Notes.InsertRange(0, notes);
            Executed += sql.Executed;
            return history;
        }
        catch (CalibrationUnavailableException ex)
        {
            var history = new UsageHistory { Target = target, WindowDays = settings.WindowDays, MinRuns = settings.MinRuns };
            history.Warnings.Add($"Calibration skipped: {ex.Message}.");
            return history;
        }
        catch (SqlQueryException ex)
        {
            var history = new UsageHistory { Target = target, WindowDays = settings.WindowDays, MinRuns = settings.MinRuns };
            history.Warnings.Add(ex.IsPermission
                ? $"Calibration skipped: cannot read `system.billing.usage` ({ex.ErrorCode}). To enable it, {HistoryCollector.BillingGrant}."
                : $"Calibration skipped: {ex.Message}.");
            return history;
        }
        catch (HttpRequestException ex)
        {
            var history = new UsageHistory { Target = target, WindowDays = settings.WindowDays, MinRuns = settings.MinRuns };
            history.Warnings.Add($"Calibration skipped: the workspace is unreachable ({ex.Message}).");
            return history;
        }
    }

    /// <summary>The name-matching fallback for jobs the summary did not map (design C2).</summary>
    private async Task MatchJobsByNameAsync(ISqlRunner sql, ResolvedBundle baseBundle, List<(string Type, string Key)> wanted, Dictionary<(string Type, string Key), string> ids, CancellationToken cancellationToken)
    {
        var byName = wanted
            .Where(k => k.Type == ResourceTypes.Jobs && !ids.ContainsKey(k))
            .Select(k => (Key: k, Name: baseBundle.Document.Resources?.Jobs?.GetValueOrDefault(k.Key)?.Name))
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .ToList();
        if (byName.Count == 0)
        {
            return;
        }

        var rows = await sql.QueryAsync(DeployedIds.JobsByName(byName.Select(x => x.Name!).Distinct(StringComparer.Ordinal).ToList(), settings.WindowDays), cancellationToken).ConfigureAwait(false);
        var resolved = DeployedIds.ReadJobsByName(rows);
        foreach (var (key, name) in byName)
        {
            if (resolved.TryGetValue(name!, out var id))
            {
                ids[key] = id;
            }
        }
    }
}
