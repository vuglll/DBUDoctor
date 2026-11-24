using System.Globalization;
using DBUDoctor.Core.Config;
using DBUDoctor.Core.Model;
using DBUDoctor.Core.Pricing;
using DBUDoctor.Core.Reporting;
using DBUDoctor.Core.Scheduling;

namespace DBUDoctor.Core.Cost;

/// <summary>
/// Prices one side of a bundle from configuration and the catalog (design: "Cost formulas"). A pure
/// function of its inputs: no I/O, no clock (the reference instant is passed in).
/// </summary>
public sealed class CostEngine(PricingContext prices, DateTimeOffset reference)
{
    public PricingContext Prices => prices;

    private enum Workload
    {
        Jobs,
        AllPurpose,
        Pipelines,
    }

    private sealed record ClassicRate(decimal? Dbu, decimal? Vm, string Sku, List<string> Assumptions, List<string> Unknown);

    public CostEstimate? Estimate(ResolvedBundle bundle, string type, string key)
    {
        var resources = bundle.Document.Resources;
        return type switch
        {
            Diff.ResourceTypes.Jobs when resources?.Jobs?.TryGetValue(key, out var job) == true => EstimateJob(job),
            Diff.ResourceTypes.Pipelines when resources?.Pipelines?.TryGetValue(key, out var pipeline) == true => EstimatePipeline(pipeline),
            Diff.ResourceTypes.Clusters when resources?.Clusters?.TryGetValue(key, out var cluster) == true => EstimateCluster(cluster),
            Diff.ResourceTypes.SqlWarehouses when resources?.SqlWarehouses?.TryGetValue(key, out var warehouse) == true => EstimateWarehouse(warehouse),
            Diff.ResourceTypes.Apps when resources?.Apps?.TryGetValue(key, out var app) == true => EstimateApp(app),
            Diff.ResourceTypes.PostgresEndpoints when resources?.PostgresEndpoints?.TryGetValue(key, out var endpoint) == true => EstimatePostgresEndpoint(endpoint),
            _ => null,
        };
    }

    public CostEstimate EstimateJob(Job job)
    {
        var frequency = RunFrequency.ForJob(job, reference);
        var jobClusters = (job.JobClusters ?? []).Where(c => c.JobClusterKey is not null)
            .GroupBy(c => c.JobClusterKey!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().NewCluster, StringComparer.Ordinal);

        var usedJobClusters = new SortedSet<string>(StringComparer.Ordinal);
        var newClusters = new List<(string Task, ClusterSpec Spec)>();
        var existingClusterTasks = new List<string>();
        var serverlessTasks = new List<string>();
        var unknown = new List<string>();

        foreach (var task in Flatten(job.Tasks ?? []))
        {
            var name = task.TaskKey ?? "?";
            if (task.ExistingClusterId is not null)
            {
                existingClusterTasks.Add(name);
            }
            else if (task.JobClusterKey is { } clusterKey)
            {
                if (jobClusters.ContainsKey(clusterKey))
                {
                    usedJobClusters.Add(clusterKey);
                }
                else
                {
                    unknown.Add($"task `{name}` uses job cluster `{clusterKey}`, which the job does not define");
                }
            }
            else if (task.NewCluster is { } spec)
            {
                newClusters.Add((name, spec));
            }
            else if (!task.UsesOtherCompute)
            {
                serverlessTasks.Add(name);
            }
        }

        var hasClassic = usedJobClusters.Count > 0 || newClusters.Count > 0;
        if (!hasClassic && serverlessTasks.Count > 0)
        {
            return Serverless(Skus.JobsServerless, frequency, "serverless job");
        }

        if (!hasClassic && unknown.Count == 0)
        {
            return PricedElsewhere(existingClusterTasks, frequency);
        }

        decimal dbu = 0, vm = 0, dbuCost = 0;
        var skus = new SortedSet<string>(StringComparer.Ordinal);
        var assumptions = new List<string>();
        foreach (var key in usedJobClusters)
        {
            var spec = jobClusters[key];
            if (spec is null)
            {
                unknown.Add($"job cluster `{key}` has no new_cluster");
                continue;
            }

            Accumulate(Rate(spec, Workload.Jobs, $"job cluster `{key}`"));
        }

        foreach (var (task, spec) in newClusters)
        {
            Accumulate(Rate(spec, Workload.Jobs, $"task `{task}` cluster"));
        }

        // Each cluster is priced at its own SKU: a Photon cluster uses the Photon SKU.
        void Accumulate(ClassicRate rate)
        {
            dbu += rate.Dbu ?? 0;
            vm += rate.Vm ?? 0;
            skus.Add(rate.Sku);
            var clusterPrice = prices.EffectivePrice(rate.Sku, out var reason);
            if (reason is not null)
            {
                unknown.Add(reason);
            }

            dbuCost += (rate.Dbu ?? 0) * (clusterPrice ?? 0);
            assumptions.AddRange(rate.Assumptions);
            unknown.AddRange(rate.Unknown);
        }

        if (usedJobClusters.Count + newClusters.Count > 1)
        {
            assumptions.Add("all of the job's clusters are active for the whole run");
        }

        if (serverlessTasks.Count > 0)
        {
            assumptions.Add($"serverless tasks ({string.Join(", ", serverlessTasks)}) not included: they need history");
        }

        var sku = skus.Count == 1 ? skus.Min! : Skus.JobsCompute;
        if (skus.Count > 1)
        {
            assumptions.Add("clusters with and without Photon: the price per DBU is a blend of both SKUs");
        }

        // The blended $/DBU reproduces each cluster's own SKU price in the DBU cost.
        var price = dbu > 0 ? dbuCost / dbu : prices.EffectivePrice(sku, out _);
        var estimate = Build(sku, dbu, vm, price, unknown, assumptions, frequency);
        if (existingClusterTasks.Count > 0)
        {
            estimate.Assumptions.Add($"tasks on existing all-purpose clusters ({string.Join(", ", existingClusterTasks)}) are priced with those clusters");
        }

        if (estimate.RatePerHour is null)
        {
            return estimate;
        }

        return Monthly(estimate, frequency);
    }

    public CostEstimate EstimatePipeline(Pipeline pipeline)
    {
        if (pipeline.Serverless == true)
        {
            var serverless = Serverless(Skus.JobsServerless, null, "serverless pipeline");
            serverless.Assumptions.Add("serverless pipelines are priced at the Jobs serverless rate");
            return serverless;
        }

        var edition = (pipeline.Edition ?? "ADVANCED").ToUpperInvariant();
        var sku = edition switch
        {
            "CORE" => Skus.DltCore,
            "PRO" => Skus.DltPro,
            _ => Skus.DltAdvanced,
        };

        var unknown = new List<string>();
        var assumptions = new List<string>();
        if (pipeline.Edition is null)
        {
            assumptions.Add("edition not set: priced as ADVANCED (the default)");
        }

        var cluster = pipeline.Clusters?.FirstOrDefault(c => c.Label is null or "default");
        decimal dbu = 0, vm = 0;
        string? effectiveSku = sku;
        if (cluster is null)
        {
            unknown.Add("pipeline has no default cluster in configuration (the node type comes from the workspace default)");
        }
        else
        {
            var photon = pipeline.Photon == true || cluster.IsPhoton;
            var rate = Rate(cluster, Workload.Pipelines, "pipeline cluster", photon, sku);
            dbu = rate.Dbu ?? 0;
            vm = rate.Vm ?? 0;
            effectiveSku = rate.Sku;
            assumptions.AddRange(rate.Assumptions);
            unknown.AddRange(rate.Unknown);
        }

        var price = prices.EffectivePrice(effectiveSku, out var reason);
        if (reason is not null)
        {
            unknown.Add(reason);
        }

        var estimate = Build(effectiveSku, dbu, vm, price, unknown, assumptions, null);
        if (estimate.RatePerHour is null)
        {
            return estimate;
        }

        if (pipeline.Continuous == true)
        {
            estimate.Monthly = estimate.RatePerHour * RunFrequency.HoursPerMonth;
            estimate.Confidence = Confidence.Medium;
            estimate.Assumptions.Add("continuous: runs all month (730 h)");
        }
        else
        {
            estimate.Confidence = Confidence.Unknown;
            estimate.MonthlyNote = "triggered pipeline: runs come from the jobs or schedules that start it";
        }

        return estimate;
    }

    public CostEstimate EstimateCluster(ClusterSpec cluster)
    {
        var rate = Rate(cluster, Workload.AllPurpose, "cluster");
        var price = prices.EffectivePrice(rate.Sku, out var reason);
        if (reason is not null)
        {
            rate.Unknown.Add(reason);
        }

        var estimate = Build(rate.Sku, rate.Dbu ?? 0, rate.Vm ?? 0, price, rate.Unknown, rate.Assumptions, null);
        estimate.Confidence = Confidence.Unknown;
        estimate.MonthlyNote = "all-purpose cluster: needs history";
        return estimate;
    }

    public CostEstimate EstimateWarehouse(SqlWarehouse warehouse)
    {
        var unknown = new List<string>();
        var assumptions = new List<string>();
        var serverless = warehouse.EnableServerlessCompute == true;
        var type = (warehouse.WarehouseType ?? "").ToUpperInvariant();
        if (!serverless && type.Length == 0)
        {
            assumptions.Add("warehouse_type not set: priced as PRO");
            type = "PRO";
        }

        var sku = serverless ? Skus.SqlServerless : type == "CLASSIC" ? Skus.SqlClassic : Skus.SqlPro;
        var clusters = warehouse.MinNumClusters ?? 1;
        var size = warehouse.ClusterSize;
        decimal? dbuPerCluster = null;
        if (size is null)
        {
            unknown.Add("cluster_size not set");
        }
        else if (prices.Catalog.Warehouses.Sizes.TryGetValue(size, out var sizeValue))
        {
            prices.Used(sizeValue.Provenance);
            dbuPerCluster = sizeValue.Value;
        }
        else
        {
            unknown.Add($"warehouse size `{size}` is not in the catalog");
        }

        if (warehouse.MaxNumClusters is { } max && max > clusters)
        {
            assumptions.Add($"{Plural(clusters, "cluster")} running (min_num_clusters); scales up to {max}");
        }

        decimal vm = 0;
        var infra = !serverless && prices.InfraIncluded;
        if (infra && size is not null && dbuPerCluster is not null)
        {
            if (prices.Cloud is { } cloud &&
                prices.Catalog.Warehouses.VmLayouts.TryGetValue(cloud, out var layouts) &&
                layouts.TryGetValue(size, out var layout))
            {
                var driver = prices.VmPrice(layout.Driver);
                var worker = prices.VmPrice(layout.Worker);
                if (driver is null || worker is null)
                {
                    unknown.Add($"no {EnumNames.Of(prices.Pricing.Commitment)} VM price for {(driver is null ? layout.Driver : layout.Worker)} in {prices.Region}");
                }
                else
                {
                    vm = (driver.Value + layout.Workers * worker.Value) * clusters;
                }
            }
            else
            {
                unknown.Add($"no VM layout for warehouse size `{size}`");
            }
        }

        var price = prices.EffectivePrice(sku, out var reason);
        if (reason is not null)
        {
            unknown.Add(reason);
        }

        var estimate = Build(sku, (dbuPerCluster ?? 0) * clusters, vm, price, unknown, assumptions, null, serverless, infra);
        estimate.Confidence = Confidence.Unknown;
        estimate.MonthlyNote = "SQL warehouse: needs history";
        return estimate;
    }

    /// <summary>
    /// An app bills for its compute size × instances for as long as it runs, at the Interactive
    /// (all-purpose) Serverless price. Priced as running all month (confidence Medium).
    /// </summary>
    public CostEstimate EstimateApp(App app)
    {
        var unknown = new List<string>();
        var assumptions = new List<string>();
        var table = prices.Catalog.Serverless;
        var size = (app.ComputeSize ?? table.AppDefaultSize).ToUpperInvariant();
        if (app.ComputeSize is null)
        {
            assumptions.Add($"compute size not set: priced as {table.AppDefaultSize} (the default)");
        }

        decimal? perInstance = null;
        if (table.AppSizes.TryGetValue(size, out var value))
        {
            prices.Used(value.Provenance);
            perInstance = value.Value;
        }
        else
        {
            unknown.Add($"app compute size `{size}` is not in the catalog");
        }

        var instances = (prices.Estimation.Autoscale == AutoscaleAssumption.Min ? app.ComputeMinInstances : app.ComputeMaxInstances ?? app.ComputeMinInstances) ?? 1;
        if (app.ComputeMinInstances is { } min && app.ComputeMaxInstances is { } max && min != max)
        {
            assumptions.Add($"{instances} instances ({min}–{max}; estimation.autoscale)");
        }
        else if (instances > 1)
        {
            assumptions.Add($"{instances} instances");
        }

        var price = prices.EffectivePrice(Skus.AllPurposeServerless, out var reason);
        if (reason is not null)
        {
            unknown.Add(reason);
        }

        var estimate = Build(Skus.AllPurposeServerless, (perInstance ?? 0) * instances, 0, price, unknown, assumptions, new RunFrequency(null, true, "always on", null, null, null), serverless: true, infra: false);
        if (estimate.RatePerHour is { } rate)
        {
            estimate.Monthly = rate * RunFrequency.HoursPerMonth;
            estimate.Confidence = Confidence.Medium;
            estimate.Assumptions.Add("running all month (730 h): an app bills for its size while it runs");
        }

        return estimate;
    }

    /// <summary>
    /// A Lakebase Autoscaling compute: CU × DBU per CU × the Database Serverless price per active hour.
    /// Always active (<c>no_suspension: true</c>): the minimum size for 730 hours (Medium), up to the
    /// maximum size. With scale to zero the active time is unknown, so the rate is shown without a
    /// monthly figure, up to the maximum size for 730 hours.
    /// </summary>
    public CostEstimate EstimatePostgresEndpoint(PostgresEndpoint endpoint)
    {
        var unknown = new List<string>();
        var assumptions = new List<string>();
        CatalogValue? perCu = null;
        if (prices.Cloud is { } cloud && prices.Catalog.Serverless.LakebaseDbuPerCu.TryGetValue(cloud, out var value))
        {
            prices.Used(value.Provenance);
            perCu = value;
        }
        else
        {
            unknown.Add($"no Lakebase DBU per CU for {(prices.Cloud is { } c ? EnumNames.Display(c) : "this cloud")} in the catalog");
        }

        var min = endpoint.MinCu;
        var max = endpoint.MaxCu ?? min;
        if (min is null)
        {
            unknown.Add("autoscaling_limit_min_cu not set (the size comes from the project defaults)");
        }

        var price = prices.EffectivePrice(Skus.DatabaseServerless, out var reason);
        if (reason is not null)
        {
            unknown.Add(reason);
        }

        var alwaysOn = endpoint.NoSuspension == true;
        var estimate = Build(Skus.DatabaseServerless, (min ?? 0) * (perCu?.Value ?? 0), 0, price, unknown, assumptions,
            alwaysOn ? new RunFrequency(null, true, "always active", null, null, null) : null, serverless: true, infra: false);
        if (endpoint.Disabled == true)
        {
            estimate.Monthly = 0;
            estimate.Confidence = Confidence.Medium;
            estimate.Assumptions.Add("disabled: no compute runs");
            return estimate;
        }

        if (estimate.RatePerHour is not { } rate || perCu is null || price is null)
        {
            return estimate;
        }

        var ceiling = (max ?? 0) * perCu.Value * price.Value * RunFrequency.HoursPerMonth;
        if (alwaysOn)
        {
            estimate.Monthly = rate * RunFrequency.HoursPerMonth;
            estimate.Confidence = Confidence.Medium;
            estimate.Assumptions.Add($"always active (no_suspension): {Format.Number(min!.Value)} CU for 730 h");
            if (max > min)
            {
                estimate.MonthlyCeiling = ceiling;
                estimate.Assumptions.Add($"up to {Format.Money(ceiling)} a month at {Format.Number(max!.Value)} CU");
            }
        }
        else
        {
            estimate.Confidence = Confidence.Unknown;
            estimate.MonthlyNote = "scales to zero: monthly cost depends on active time";
            estimate.MonthlyCeiling = ceiling;
            estimate.Assumptions.Add($"up to {Format.Money(ceiling)} a month if active all month at {Format.Number(max!.Value)} CU");
        }

        return estimate;
    }

    /// <summary>A job with no compute of its own: an all-purpose cluster, pipelines, warehouses or other jobs carry its cost.</summary>
    private CostEstimate PricedElsewhere(List<string> existingClusterTasks, RunFrequency frequency)
    {
        var onAllPurpose = existingClusterTasks.Count > 0;
        var estimate = new CostEstimate
        {
            Sku = onAllPurpose ? Skus.AllPurposeCompute : null,
            PricePerDbu = onAllPurpose ? prices.EffectivePrice(Skus.AllPurposeCompute, out _) : null,
            PricedElsewhere = true,
            Frequency = frequency,
            Confidence = Confidence.Unknown,
            MonthlyNote = onAllPurpose
                ? "runs on an all-purpose cluster: its cost depends on that cluster's usage"
                : "runs on pipelines, SQL warehouses or other jobs, which are priced there",
        };
        estimate.Assumptions.Add(onAllPurpose
            ? $"tasks on existing all-purpose clusters ({string.Join(", ", existingClusterTasks)}) are priced with those clusters"
            : "tasks run on compute priced elsewhere (pipelines, SQL warehouses or other jobs)");
        return estimate;
    }

    private CostEstimate Serverless(string sku, RunFrequency? frequency, string what)
    {
        var price = prices.EffectivePrice(sku, out var reason);
        var estimate = new CostEstimate
        {
            Sku = sku,
            PricePerDbu = price,
            Serverless = true,
            Frequency = frequency,
            Confidence = Confidence.Unknown,
            MonthlyNote = "needs history",
        };
        estimate.Assumptions.Add($"{what}: DBUs per run cannot be derived from configuration");
        if (reason is not null)
        {
            estimate.UnknownReasons.Add(reason);
        }

        return estimate;
    }

    private CostEstimate Build(string sku, decimal dbu, decimal vm, decimal? price, List<string> unknown, List<string> assumptions, RunFrequency? frequency, bool serverless = false, bool? infra = null)
    {
        var known = unknown.Count == 0 && price is not null;
        var infraIncluded = infra ?? (!serverless && prices.InfraIncluded);
        var estimate = new CostEstimate
        {
            Sku = sku,
            PricePerDbu = price,
            DbuPerHour = known ? dbu : null,
            DbuCostPerHour = known ? dbu * price!.Value : null,
            VmCostPerHour = known && infraIncluded ? vm : null,
            RatePerHour = known ? (dbu * price!.Value) + (infraIncluded ? vm : 0) : null,
            Serverless = serverless,
            InfraIncluded = infraIncluded,
            Frequency = frequency,
            HoursPerRun = prices.Estimation.DefaultRunMinutes / 60m,
            Confidence = Confidence.Unknown,
        };
        estimate.Assumptions.AddRange(assumptions.Distinct(StringComparer.Ordinal));
        estimate.UnknownReasons.AddRange(unknown.Distinct(StringComparer.Ordinal));
        return estimate;
    }

    private CostEstimate Monthly(CostEstimate estimate, RunFrequency frequency)
    {
        if (frequency.AlwaysOn)
        {
            estimate.Monthly = estimate.RatePerHour * RunFrequency.HoursPerMonth;
            estimate.Confidence = Confidence.Medium;
            estimate.Assumptions.Add("continuous: runs all month (730 h)");
        }
        else if (frequency.RunsPerMonth is 0)
        {
            estimate.Monthly = 0;
            estimate.Confidence = Confidence.Medium;
        }
        else if (frequency.RunsPerMonth is { } runs && prices.Estimation.DefaultRunMinutes is { } minutes)
        {
            estimate.Monthly = estimate.RatePerHour * minutes / 60m * runs;
            estimate.Confidence = Confidence.Medium;
            estimate.Assumptions.Add($"{minutes.ToString("0.##", CultureInfo.InvariantCulture)}-minute runs (configured)");
        }
        else
        {
            estimate.Confidence = Confidence.Unknown;
            estimate.MonthlyNote = frequency.UnknownReason ?? "run duration unknown: set `estimation.default_run_minutes`";
        }

        return estimate;
    }

    /// <summary>
    /// <c>dbu_per_hour = m × (d(driver) + W × d(worker))</c>, plus <c>vm(driver) + W × vm(worker)</c>
    /// when infrastructure is included. Photon follows the cloud's catalog method.
    /// </summary>
    private ClassicRate Rate(ClusterSpec spec, Workload workload, string what, bool? photonOverride = null, string? skuOverride = null)
    {
        var assumptions = new List<string>();
        var unknown = new List<string>();
        var baseSku = skuOverride ?? (workload == Workload.AllPurpose ? Skus.AllPurposeCompute : Skus.JobsCompute);
        var photon = photonOverride ?? spec.IsPhoton;
        var sku = baseSku;

        var pools = prices.Estimation.InstancePools;
        string? FromPool(string pool)
        {
            if (pools.TryGetValue(pool, out var mapped))
            {
                assumptions.Add($"node type `{mapped}` from instance pool `{pool}` (estimation.instance_pools)");
                return mapped;
            }

            unknown.Add($"{what}: node type comes from instance pool `{pool}`, which the bundle does not show; set `estimation.instance_pools.{pool}` to the pool's node type");
            return null;
        }

        var workerType = spec.NodeTypeId ?? (spec.InstancePoolId is { } workerPool ? FromPool(workerPool) : null);
        var driverType = spec.DriverNodeTypeId
            ?? (spec.DriverInstancePoolId is { } driverPool && driverPool != spec.InstancePoolId ? FromPool(driverPool) : workerType);
        if (workerType is null || driverType is null)
        {
            if (spec.InstancePoolId is null && spec.NodeTypeId is null)
            {
                unknown.Add($"{what}: node type not set (it comes from a cluster policy or the workspace default)");
            }

            return new ClassicRate(null, null, sku, assumptions, unknown);
        }

        var workers = Workers(spec, assumptions, what);
        var cloudName = prices.Cloud is { } c ? EnumNames.Display(c) : "this cloud";

        decimal? NodeDbu(string id)
        {
            var node = prices.Node(id);
            if (node is null)
            {
                // Without a node-type table (cloud unknown) the missing-prices reason already says why.
                if (prices.NodeTypes is not null)
                {
                    unknown.Add($"node type `{id}` is not in the {cloudName} catalog");
                }

                return null;
            }

            var standard = workload == Workload.AllPurpose ? node.DbuAllPurpose ?? node.Dbu : node.Dbu;
            if (!photon)
            {
                return standard;
            }

            switch (prices.NodeTypes!.PhotonMethod)
            {
                case PhotonMethod.DbuCount:
                    var photonDbu = workload == Workload.AllPurpose ? node.PhotonDbuAllPurpose : node.PhotonDbuJobs;
                    if (photonDbu is null)
                    {
                        unknown.Add($"no Photon DBU count for `{id}` in the {cloudName} catalog");
                    }

                    return photonDbu;
                case PhotonMethod.Multiplier:
                    if (node.Family is { } family && prices.NodeTypes.PhotonMultipliers.TryGetValue(family, out var m))
                    {
                        return standard * m;
                    }

                    unknown.Add($"no Photon multiplier for `{id}` in the {cloudName} catalog");
                    return null;
                case PhotonMethod.Sku:
                    return standard;
                default:
                    unknown.Add($"Photon pricing for {cloudName} is not in the catalog");
                    return null;
            }
        }

        var driverDbu = NodeDbu(driverType!);
        var workerDbu = driverType == workerType ? driverDbu : NodeDbu(workerType);
        if (photon)
        {
            if (Skus.PhotonOf(baseSku) is { } photonSku && prices.ListPrice(photonSku) is not null)
            {
                sku = photonSku;
            }

            assumptions.Add(workload == Workload.Pipelines && prices.NodeTypes?.PhotonMethod == PhotonMethod.DbuCount
                ? "Photon: pipeline DBU counts taken from the Jobs compute Photon column"
                : "Photon enabled");
        }

        decimal? dbu = driverDbu is not null && workerDbu is not null ? driverDbu + (workers * workerDbu) : null;

        decimal? vm = 0;
        if (prices.InfraIncluded)
        {
            var driverVm = prices.VmPrice(driverType!);
            var workerVm = driverType == workerType ? driverVm : prices.VmPrice(workerType);
            if (driverVm is null || workerVm is null)
            {
                var missing = driverVm is null ? driverType : workerType;
                unknown.Add($"no {EnumNames.Of(prices.Pricing.Commitment)} VM price for `{missing}` in {prices.Region}");
                vm = null;
            }
            else
            {
                vm = driverVm + (workers * workerVm);
            }
        }

        return new ClassicRate(dbu, vm, sku, assumptions, unknown);
    }

    private decimal Workers(ClusterSpec spec, List<string> assumptions, string what)
    {
        if (spec.Autoscale is { } autoscale && (autoscale.MinWorkers is not null || autoscale.MaxWorkers is not null))
        {
            var min = autoscale.MinWorkers ?? autoscale.MaxWorkers ?? 0;
            var max = autoscale.MaxWorkers ?? min;
            var (count, label) = prices.Estimation.Autoscale switch
            {
                AutoscaleAssumption.Min => ((decimal)min, "min"),
                AutoscaleAssumption.Mid => ((min + max) / 2m, "mid"),
                _ => ((decimal)max, "max"),
            };
            assumptions.Add($"autoscale at {label}: {what} priced with {count.ToString("0.#", CultureInfo.InvariantCulture)} workers ({min}–{max})");
            return count;
        }

        return spec.NumWorkers ?? 0;
    }

    internal static IEnumerable<JobTask> Flatten(IEnumerable<JobTask> tasks)
    {
        foreach (var task in tasks)
        {
            if (task.ForEachTask?.Task is { } inner)
            {
                inner.TaskKey ??= task.TaskKey;
                yield return inner;
            }
            else
            {
                yield return task;
            }
        }
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
