using System.Globalization;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Diff;

namespace DBUDoctor.Core.Calibration;

/// <summary>
/// Runs the calibration queries for the resources a diff touches and turns the rows into a
/// <see cref="UsageHistory"/>. Never throws for query failures: they become warnings or notes, and
/// estimates fall back to configuration (spec: "Calibration failure").
/// </summary>
public static class HistoryCollector
{
    public const string BillingGrant =
        "grant USE CATALOG on `system`, USE SCHEMA and SELECT on `system.billing`, and CAN USE on the warehouse";

    public static async Task<UsageHistory> CollectAsync(
        ISqlRunner sql,
        IReadOnlyDictionary<(string Type, string Key), string> deployedIds,
        IEnumerable<(string Type, string Key)> notFound,
        CalibrationSettings settings,
        Cloud? cloud,
        string target,
        CancellationToken cancellationToken)
    {
        var history = new UsageHistory { Target = target, WindowDays = settings.WindowDays, MinRuns = settings.MinRuns };
        foreach (var missing in notFound)
        {
            history.NotFound.Add(missing);
        }

        foreach (var group in deployedIds.Where(p => ResourceIds.IsValid(p.Key.Type, p.Value)).GroupBy(p => p.Key.Type, StringComparer.Ordinal))
        {
            var byId = group.GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(p => p.Key.Key).ToList(), StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<IReadOnlyList<string?>> rows;
            try
            {
                rows = await sql.QueryAsync(UsageQueries.Usage(group.Key, byId.Keys.Order(StringComparer.Ordinal).ToList(), settings.WindowDays), cancellationToken).ConfigureAwait(false);
            }
            catch (SqlQueryException ex) when (ex.IsPermission)
            {
                history.Warnings.Add($"Calibration skipped: cannot read `system.billing.usage` ({ex.ErrorCode}). To enable it, {BillingGrant}.");
                return history;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                history.Warnings.Add($"Calibration skipped: the history query failed ({ex.Message}).");
                return history;
            }

            foreach (var (id, keys) in byId)
            {
                foreach (var key in keys)
                {
                    history.Resources[(group.Key, key)] = new ResourceHistory { Type = group.Key, Key = key, Id = id, WindowDays = settings.WindowDays };
                }
            }

            Read(rows, group.Key, byId, history);
        }

        await ReadListPricesAsync(sql, cloud, history, cancellationToken).ConfigureAwait(false);
        await ReadNodesAsync(sql, history, cancellationToken).ConfigureAwait(false);
        return history;
    }

    private static void Read(IReadOnlyList<IReadOnlyList<string?>> rows, string type, Dictionary<string, List<string>> byId, UsageHistory history)
    {
        var latest = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Count < 7 || row[0] is not { } id || !byId.TryGetValue(id, out var keys) || UsageQueries.Decimal(row[6]) is not { } dbus)
            {
                continue;
            }

            var day = DateOnly.TryParse(row[5], CultureInfo.InvariantCulture, out var d) ? d : (DateOnly?)null;
            foreach (var key in keys)
            {
                var resource = history.Resources[(type, key)];
                resource.TotalDbus += dbus;
                if (day is { } usageDay)
                {
                    resource.Days.Add(usageDay);
                }

                if (row[1] is { } run)
                {
                    resource.Runs[run] = resource.Runs.GetValueOrDefault(run) + dbus;
                }

                if (row[2] is { } sku)
                {
                    resource.Skus[sku] = resource.Skus.GetValueOrDefault(sku) + dbus;
                }

                if (row[3] is { } serverless)
                {
                    resource.Serverless = serverless.Equals("true", StringComparison.OrdinalIgnoreCase);
                }

                var last = latest.GetValueOrDefault(id);
                if (day is null || day >= last)
                {
                    resource.PerformanceTarget = row[4] ?? resource.PerformanceTarget;
                    latest[id] = day ?? last;
                }
            }
        }

        foreach (var resource in history.Resources.Values.Where(r => r.Type == type))
        {
            // A day can come back empty after corrections net to zero.
            foreach (var empty in resource.Skus.Where(s => s.Value == 0).Select(s => s.Key).ToList())
            {
                resource.Skus.Remove(empty);
            }
        }
    }

    private static async Task ReadListPricesAsync(ISqlRunner sql, Cloud? cloud, UsageHistory history, CancellationToken cancellationToken)
    {
        if (cloud is null || history.Resources.Count == 0)
        {
            return;
        }

        try
        {
            var rows = await sql.QueryAsync(UsageQueries.ListPrices(cloud.Value.ToString().ToUpperInvariant()), cancellationToken).ConfigureAwait(false);
            var latest = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (row.Count < 3 || row[0] is not { } sku || UsageQueries.Decimal(row[1]) is not { } price)
                {
                    continue;
                }

                var start = row[2] ?? "";
                if (!latest.TryGetValue(sku, out var seen) || string.CompareOrdinal(start, seen) > 0)
                {
                    latest[sku] = start;
                    history.ListPrices[sku] = price;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            history.Notes.Add("list prices from the catalog: `system.billing.list_prices` is not readable");
        }
    }

    private static async Task ReadNodesAsync(ISqlRunner sql, UsageHistory history, CancellationToken cancellationToken)
    {
        var jobIds = history.Resources.Values
            .Where(r => r.Type == ResourceTypes.Jobs && r.Serverless != true && r.TotalDbus > 0)
            .Select(r => r.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (jobIds.Count == 0)
        {
            return;
        }

        try
        {
            var rows = await sql.QueryAsync(UsageQueries.NodeTimeline(jobIds, history.WindowDays), cancellationToken).ConfigureAwait(false);
            // Only single-node-type clusters without Photon give a clean DBU per node-hour.
            var clusters = rows
                .Where(r => r.Count >= 5 && r[0] is not null && r[1] is not null)
                .GroupBy(r => r[0]!, StringComparer.Ordinal)
                .Where(g => g.Select(r => r[1]).Distinct(StringComparer.Ordinal).Count() == 1 &&
                            g.All(r => !string.Equals(r[2], "true", StringComparison.OrdinalIgnoreCase)))
                .Select(g => (NodeType: g.First()[1]!, Dbus: UsageQueries.Decimal(g.First()[3]) ?? 0, Hours: g.Sum(r => UsageQueries.Decimal(r[4]) ?? 0)));
            foreach (var node in clusters.Where(c => c.Hours > 0).GroupBy(c => c.NodeType, StringComparer.Ordinal))
            {
                history.Nodes.Add(new NodeObservation(node.Key, node.Sum(c => c.Dbus), node.Sum(c => c.Hours)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            history.Notes.Add("drift check skipped: `system.compute.node_timeline` is not readable");
        }
    }
}
