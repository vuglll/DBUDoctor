using System.Text.Json;
using System.Text.Json.Serialization;

namespace DBUDoctor.Core.Model;

// A typed view of `databricks bundle validate --output json`. Only the fields the cost engine and
// the rules read are modeled; everything else is ignored. The raw JSON is kept alongside for the
// field-level diff, so a field can be diffed before it is modeled.

public sealed class BundleDocument
{
    [JsonPropertyName("bundle")] public BundleInfo? Bundle { get; set; }
    [JsonPropertyName("workspace")] public WorkspaceInfo? Workspace { get; set; }
    [JsonPropertyName("resources")] public BundleResources? Resources { get; set; }
}

public sealed class BundleInfo
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("target")] public string? Target { get; set; }
    [JsonPropertyName("mode")] public string? Mode { get; set; }
}

public sealed class WorkspaceInfo
{
    [JsonPropertyName("host")] public string? Host { get; set; }
}

public sealed class BundleResources
{
    [JsonPropertyName("jobs")] public Dictionary<string, Job>? Jobs { get; set; }
    [JsonPropertyName("pipelines")] public Dictionary<string, Pipeline>? Pipelines { get; set; }
    [JsonPropertyName("clusters")] public Dictionary<string, ClusterSpec>? Clusters { get; set; }
    [JsonPropertyName("sql_warehouses")] public Dictionary<string, SqlWarehouse>? SqlWarehouses { get; set; }

    [JsonPropertyName("apps")] public Dictionary<string, App>? Apps { get; set; }
    [JsonPropertyName("postgres_endpoints")] public Dictionary<string, PostgresEndpoint>? PostgresEndpoints { get; set; }

    /// <summary>Not priced; read by rules (DBU014).</summary>
    [JsonPropertyName("model_serving_endpoints")] public Dictionary<string, ServingEndpoint>? ModelServingEndpoints { get; set; }
}

public sealed class App
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("compute_size")] public string? ComputeSize { get; set; }
    [JsonPropertyName("compute_min_instances")] public int? ComputeMinInstances { get; set; }
    [JsonPropertyName("compute_max_instances")] public int? ComputeMaxInstances { get; set; }
}

/// <summary>A Lakebase Autoscaling compute (endpoint) on a branch.</summary>
public sealed class PostgresEndpoint
{
    [JsonPropertyName("endpoint_id")] public string? EndpointId { get; set; }

    /// <summary>The branch, such as <c>projects/shop/branches/production</c>.</summary>
    [JsonPropertyName("parent")] public string? Parent { get; set; }

    [JsonPropertyName("autoscaling_limit_min_cu")] public decimal? MinCu { get; set; }
    [JsonPropertyName("autoscaling_limit_max_cu")] public decimal? MaxCu { get; set; }

    /// <summary>True disables scale to zero ("always active").</summary>
    [JsonPropertyName("no_suspension")] public bool? NoSuspension { get; set; }
    [JsonPropertyName("disabled")] public bool? Disabled { get; set; }
}

public sealed class ServingEndpoint
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("config")] public ServingConfig? Config { get; set; }
}

public sealed class ServingConfig
{
    [JsonPropertyName("served_entities")] public List<ServedEntity>? ServedEntities { get; set; }
    [JsonPropertyName("served_models")] public List<ServedEntity>? ServedModels { get; set; }
}

/// <summary>A served entity or (older API) served model: the parts that set its idle floor.</summary>
public sealed class ServedEntity
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("entity_name")] public string? EntityName { get; set; }
    [JsonPropertyName("model_name")] public string? ModelName { get; set; }
    [JsonPropertyName("scale_to_zero_enabled")] public bool? ScaleToZeroEnabled { get; set; }
    [JsonPropertyName("workload_size")] public string? WorkloadSize { get; set; }
    [JsonPropertyName("workload_type")] public string? WorkloadType { get; set; }
    [JsonPropertyName("min_provisioned_concurrency")] public int? MinProvisionedConcurrency { get; set; }
}

public sealed class Job
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("job_clusters")] public List<JobCluster>? JobClusters { get; set; }
    [JsonPropertyName("tasks")] public List<JobTask>? Tasks { get; set; }
    [JsonPropertyName("schedule")] public CronSchedule? Schedule { get; set; }
    [JsonPropertyName("trigger")] public JobTrigger? Trigger { get; set; }
    [JsonPropertyName("continuous")] public JobContinuous? Continuous { get; set; }
    [JsonPropertyName("environments")] public List<JsonElement>? Environments { get; set; }
    [JsonPropertyName("performance_target")] public string? PerformanceTarget { get; set; }
    [JsonPropertyName("timeout_seconds")] public int? TimeoutSeconds { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, JsonElement>? Tags { get; set; }
    [JsonPropertyName("budget_policy_id")] public string? BudgetPolicyId { get; set; }
    [JsonPropertyName("usage_policy_id")] public string? UsagePolicyId { get; set; }
}

public sealed class JobCluster
{
    [JsonPropertyName("job_cluster_key")] public string? JobClusterKey { get; set; }
    [JsonPropertyName("new_cluster")] public ClusterSpec? NewCluster { get; set; }
}

public sealed class JobTask
{
    [JsonPropertyName("task_key")] public string? TaskKey { get; set; }
    [JsonPropertyName("existing_cluster_id")] public string? ExistingClusterId { get; set; }
    [JsonPropertyName("job_cluster_key")] public string? JobClusterKey { get; set; }
    [JsonPropertyName("new_cluster")] public ClusterSpec? NewCluster { get; set; }
    [JsonPropertyName("environment_key")] public string? EnvironmentKey { get; set; }
    [JsonPropertyName("timeout_seconds")] public int? TimeoutSeconds { get; set; }
    [JsonPropertyName("pipeline_task")] public JsonElement? PipelineTask { get; set; }
    [JsonPropertyName("sql_task")] public JsonElement? SqlTask { get; set; }
    [JsonPropertyName("run_job_task")] public JsonElement? RunJobTask { get; set; }
    [JsonPropertyName("condition_task")] public JsonElement? ConditionTask { get; set; }
    [JsonPropertyName("dashboard_task")] public JsonElement? DashboardTask { get; set; }
    [JsonPropertyName("for_each_task")] public ForEachTask? ForEachTask { get; set; }

    /// <summary>
    /// Task types that run on someone else's compute (a pipeline, a warehouse, another job), or
    /// on none at all. They add no compute to this job.
    /// </summary>
    [JsonIgnore]
    public bool UsesOtherCompute =>
        PipelineTask is not null || SqlTask is not null || RunJobTask is not null ||
        ConditionTask is not null || DashboardTask is not null;

    [JsonIgnore]
    public bool HasClassicCompute => ExistingClusterId is not null || JobClusterKey is not null || NewCluster is not null;
}

public sealed class ForEachTask
{
    [JsonPropertyName("task")] public JobTask? Task { get; set; }
}

public sealed class CronSchedule
{
    [JsonPropertyName("quartz_cron_expression")] public string? QuartzCronExpression { get; set; }
    [JsonPropertyName("timezone_id")] public string? TimezoneId { get; set; }
    [JsonPropertyName("pause_status")] public string? PauseStatus { get; set; }
}

public sealed class JobTrigger
{
    [JsonPropertyName("pause_status")] public string? PauseStatus { get; set; }
    [JsonPropertyName("periodic")] public PeriodicTrigger? Periodic { get; set; }
    [JsonPropertyName("file_arrival")] public JsonElement? FileArrival { get; set; }
    [JsonPropertyName("table_update")] public JsonElement? TableUpdate { get; set; }
    [JsonPropertyName("table")] public JsonElement? Table { get; set; }
}

public sealed class PeriodicTrigger
{
    [JsonPropertyName("interval")] public int Interval { get; set; }
    [JsonPropertyName("unit")] public string? Unit { get; set; }
}

public sealed class JobContinuous
{
    [JsonPropertyName("pause_status")] public string? PauseStatus { get; set; }
}

public sealed class ClusterSpec
{
    [JsonPropertyName("cluster_name")] public string? ClusterName { get; set; }
    [JsonPropertyName("spark_version")] public string? SparkVersion { get; set; }
    [JsonPropertyName("node_type_id")] public string? NodeTypeId { get; set; }
    [JsonPropertyName("driver_node_type_id")] public string? DriverNodeTypeId { get; set; }
    [JsonPropertyName("num_workers")] public int? NumWorkers { get; set; }
    [JsonPropertyName("autoscale")] public Autoscale? Autoscale { get; set; }
    [JsonPropertyName("runtime_engine")] public string? RuntimeEngine { get; set; }
    [JsonPropertyName("instance_pool_id")] public string? InstancePoolId { get; set; }
    [JsonPropertyName("driver_instance_pool_id")] public string? DriverInstancePoolId { get; set; }
    [JsonPropertyName("autotermination_minutes")] public int? AutoterminationMinutes { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("custom_tags")] public Dictionary<string, JsonElement>? CustomTags { get; set; }
    [JsonPropertyName("aws_attributes")] public CloudAttributes? AwsAttributes { get; set; }
    [JsonPropertyName("azure_attributes")] public CloudAttributes? AzureAttributes { get; set; }
    [JsonPropertyName("gcp_attributes")] public CloudAttributes? GcpAttributes { get; set; }

    /// <summary>The explicit availability on whichever cloud block sets one, such as <c>SPOT_AZURE</c>.</summary>
    [JsonIgnore]
    public string? Availability => AwsAttributes?.Availability ?? AzureAttributes?.Availability ?? GcpAttributes?.Availability;

    [JsonIgnore]
    public bool IsPhoton =>
        string.Equals(RuntimeEngine, "PHOTON", StringComparison.OrdinalIgnoreCase) ||
        (SparkVersion?.Contains("-photon-", StringComparison.OrdinalIgnoreCase) ?? false);
}

/// <summary>The availability part of <c>aws_attributes</c>, <c>azure_attributes</c> and <c>gcp_attributes</c>.</summary>
public sealed class CloudAttributes
{
    [JsonPropertyName("availability")] public string? Availability { get; set; }
    [JsonPropertyName("first_on_demand")] public int? FirstOnDemand { get; set; }
}

public sealed class Autoscale
{
    [JsonPropertyName("min_workers")] public int? MinWorkers { get; set; }
    [JsonPropertyName("max_workers")] public int? MaxWorkers { get; set; }
    [JsonPropertyName("mode")] public string? Mode { get; set; }
}

public sealed class Pipeline
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("clusters")] public List<ClusterSpec>? Clusters { get; set; }
    [JsonPropertyName("continuous")] public bool? Continuous { get; set; }
    [JsonPropertyName("development")] public bool? Development { get; set; }
    [JsonPropertyName("edition")] public string? Edition { get; set; }
    [JsonPropertyName("photon")] public bool? Photon { get; set; }
    [JsonPropertyName("serverless")] public bool? Serverless { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, JsonElement>? Tags { get; set; }
    [JsonPropertyName("budget_policy_id")] public string? BudgetPolicyId { get; set; }
    [JsonPropertyName("usage_policy_id")] public string? UsagePolicyId { get; set; }
}

public sealed class SqlWarehouse
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("cluster_size")] public string? ClusterSize { get; set; }
    [JsonPropertyName("min_num_clusters")] public int? MinNumClusters { get; set; }
    [JsonPropertyName("max_num_clusters")] public int? MaxNumClusters { get; set; }
    [JsonPropertyName("auto_stop_mins")] public int? AutoStopMins { get; set; }
    [JsonPropertyName("warehouse_type")] public string? WarehouseType { get; set; }
    [JsonPropertyName("enable_serverless_compute")] public bool? EnableServerlessCompute { get; set; }
    [JsonPropertyName("tags")] public WarehouseTags? Tags { get; set; }
}

public sealed class WarehouseTags
{
    [JsonPropertyName("custom_tags")] public List<WarehouseTag>? CustomTags { get; set; }
}

public sealed class WarehouseTag
{
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(BundleDocument))]
internal sealed partial class BundleJsonContext : JsonSerializerContext;
