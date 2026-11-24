namespace DBUDoctor.Core.Pricing;

/// <summary>Product families group SKUs for <c>pricing.discounts</c>.</summary>
public static class ProductFamily
{
    public const string All = "all";
    public const string Jobs = "jobs";
    public const string JobsServerless = "jobs_serverless";
    public const string AllPurpose = "all_purpose";
    public const string AllPurposeServerless = "all_purpose_serverless";
    public const string Pipelines = "pipelines";
    public const string Sql = "sql";
    public const string Database = "database";

    public static readonly IReadOnlyList<string> Known = [AllPurpose, AllPurposeServerless, Database, Jobs, JobsServerless, Pipelines, Sql];
}

/// <summary>
/// Premium-tier DBU SKUs, named like <c>sku_name</c> in <c>system.billing.usage</c> so calibration can
/// join on them later.
/// </summary>
public static class Skus
{
    public const string JobsCompute = "PREMIUM_JOBS_COMPUTE";
    public const string JobsComputePhoton = "PREMIUM_JOBS_COMPUTE_PHOTON";
    public const string AllPurposeCompute = "PREMIUM_ALL_PURPOSE_COMPUTE";
    public const string AllPurposeComputePhoton = "PREMIUM_ALL_PURPOSE_COMPUTE_PHOTON";
    public const string DltCore = "PREMIUM_DLT_CORE_COMPUTE";
    public const string DltCorePhoton = "PREMIUM_DLT_CORE_COMPUTE_PHOTON";
    public const string DltPro = "PREMIUM_DLT_PRO_COMPUTE";
    public const string DltProPhoton = "PREMIUM_DLT_PRO_COMPUTE_PHOTON";
    public const string DltAdvanced = "PREMIUM_DLT_ADVANCED_COMPUTE";
    public const string DltAdvancedPhoton = "PREMIUM_DLT_ADVANCED_COMPUTE_PHOTON";
    public const string SqlClassic = "PREMIUM_SQL_COMPUTE";
    public const string SqlPro = "PREMIUM_SQL_PRO_COMPUTE";
    public const string SqlServerless = "PREMIUM_SERVERLESS_SQL_COMPUTE";
    public const string JobsServerless = "PREMIUM_JOBS_SERVERLESS_COMPUTE";
    public const string AllPurposeServerless = "PREMIUM_ALL_PURPOSE_SERVERLESS_COMPUTE";
    public const string DatabaseServerless = "PREMIUM_DATABASE_SERVERLESS_COMPUTE";

    private static readonly Dictionary<string, string> Families = new(StringComparer.Ordinal)
    {
        [JobsCompute] = ProductFamily.Jobs,
        [JobsComputePhoton] = ProductFamily.Jobs,
        [AllPurposeCompute] = ProductFamily.AllPurpose,
        [AllPurposeComputePhoton] = ProductFamily.AllPurpose,
        [DltCore] = ProductFamily.Pipelines,
        [DltCorePhoton] = ProductFamily.Pipelines,
        [DltPro] = ProductFamily.Pipelines,
        [DltProPhoton] = ProductFamily.Pipelines,
        [DltAdvanced] = ProductFamily.Pipelines,
        [DltAdvancedPhoton] = ProductFamily.Pipelines,
        [SqlClassic] = ProductFamily.Sql,
        [SqlPro] = ProductFamily.Sql,
        [SqlServerless] = ProductFamily.Sql,
        [JobsServerless] = ProductFamily.JobsServerless,
        [AllPurposeServerless] = ProductFamily.AllPurposeServerless,
        [DatabaseServerless] = ProductFamily.Database,
    };

    public static IReadOnlyList<string> All { get; } = Families.Keys.Order(StringComparer.Ordinal).ToArray();

    public static string FamilyOf(string sku) =>
        Families.TryGetValue(sku, out var family) ? family : throw new ArgumentException($"Unknown SKU {sku}", nameof(sku));

    /// <summary>The Photon variant of a classic SKU, or null when the SKU has none.</summary>
    public static string? PhotonOf(string sku) => sku switch
    {
        JobsCompute => JobsComputePhoton,
        AllPurposeCompute => AllPurposeComputePhoton,
        DltCore => DltCorePhoton,
        DltPro => DltProPhoton,
        DltAdvanced => DltAdvancedPhoton,
        _ => null,
    };

    /// <summary>Short label for reports.</summary>
    public static string Label(string sku) => sku switch
    {
        JobsCompute => "Jobs compute",
        JobsComputePhoton => "Jobs compute (Photon)",
        AllPurposeCompute => "All-purpose compute",
        AllPurposeComputePhoton => "All-purpose compute (Photon)",
        DltCore => "Pipelines Core",
        DltCorePhoton => "Pipelines Core (Photon)",
        DltPro => "Pipelines Pro",
        DltProPhoton => "Pipelines Pro (Photon)",
        DltAdvanced => "Pipelines Advanced",
        DltAdvancedPhoton => "Pipelines Advanced (Photon)",
        SqlClassic => "SQL classic",
        SqlPro => "SQL pro",
        SqlServerless => "SQL serverless",
        JobsServerless => "Jobs serverless",
        AllPurposeServerless => "All-purpose serverless",
        DatabaseServerless => "Database serverless (Lakebase)",
        _ => sku,
    };
}
