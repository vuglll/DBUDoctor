using DBUDoctor.Core.Diff;
using DBUDoctor.Core.Model;

namespace DBUDoctor.Tests;

public class DiffTests
{
    [Fact]
    public void Field_change_is_reported_with_old_and_new_value()
    {
        var diff = BundleDiffer.Diff(
            Bundle.Of("""{ "sql_warehouses": { "bi_wh": { "cluster_size": "Small" } } }"""),
            Bundle.Of("""{ "sql_warehouses": { "bi_wh": { "cluster_size": "Medium" } } }"""));

        var change = Assert.Single(diff.Changed);
        Assert.Equal(ChangeKind.Modified, change.Kind);
        Assert.Equal("bi_wh", change.Key);
        Assert.Equal(new FieldChange("cluster_size", "Small", "Medium"), Assert.Single(change.Fields));
    }

    [Fact]
    public void Non_cost_change_is_ignored()
    {
        var before = Bundle.Job("""{ "description": "a", "tasks": [ { "task_key": "t", "notebook_task": { "notebook_path": "/a" } } ] }""");
        var after = Bundle.Job("""{ "description": "b", "tasks": [ { "task_key": "t", "notebook_task": { "notebook_path": "/b" } } ] }""");

        var diff = BundleDiffer.Diff(before, after);

        Assert.Equal(ChangeKind.Unchanged, Assert.Single(diff.Resources).Kind);
        Assert.Empty(diff.Changed);
    }

    [Fact]
    public void Reordering_tasks_is_not_a_change()
    {
        var before = Bundle.Job("""{ "tasks": [ { "task_key": "a", "existing_cluster_id": "1" }, { "task_key": "b", "existing_cluster_id": "2" } ] }""");
        var after = Bundle.Job("""{ "tasks": [ { "task_key": "b", "existing_cluster_id": "2" }, { "task_key": "a", "existing_cluster_id": "1" } ] }""");

        Assert.Empty(BundleDiffer.Diff(before, after).Changed);
    }

    [Fact]
    public void Cluster_fields_inside_tasks_and_job_clusters_are_keyed()
    {
        var diff = BundleDiffer.Diff(Bundle.Job(Bundle.ClassicJob()), Bundle.Job(Bundle.ClassicJob(cluster: Bundle.Cluster(workers: 8))));

        var field = Assert.Single(Assert.Single(diff.Changed).Fields);
        Assert.Equal("job_clusters[main].new_cluster.num_workers", field.Path);
        Assert.Equal(("4", "8"), (field.OldValue, field.NewValue));
    }

    [Fact]
    public void Renamed_key_is_a_removal_and_an_addition_with_a_rename_note()
    {
        var diff = BundleDiffer.Diff(
            Bundle.Of("""{ "jobs": { "orders": { "schedule": { "quartz_cron_expression": "0 0 * * * ?" } } } }"""),
            Bundle.Of("""{ "jobs": { "orders_v2": { "schedule": { "quartz_cron_expression": "0 0 * * * ?" } } } }"""));

        Assert.Equal([ChangeKind.Removed, ChangeKind.Added], diff.Changed.OrderBy(c => c.Kind == ChangeKind.Added).Select(c => c.Kind));
        Assert.Equal(new PossibleRename("jobs", "orders", "orders_v2"), Assert.Single(diff.PossibleRenames));
    }

    [Fact]
    public void Unsupported_type_is_listed_as_not_priced()
    {
        var diff = BundleDiffer.Diff(Bundle.Of("{}"), Bundle.Of("""{ "dashboards": { "dash": { "display_name": "dash" } } }"""));

        Assert.Empty(diff.Changed);
        Assert.Equal(new UnpricedChange("dashboards", "dash", ChangeKind.Added), Assert.Single(diff.Unpriced));
    }

    [Fact]
    public void Unsupported_type_does_not_affect_totals()
    {
        var report = Run.Analyze(Bundle.Of("{}"), Bundle.Of("""{ "dashboards": { "dash": { "display_name": "dash" } } }"""));

        Assert.Equal(0, report.HeadlineDelta);
        Assert.Single(report.Unpriced);
        Assert.Contains("**Not priced in this version**\n- dashboards `dash` (added)", Core.Reporting.MarkdownRenderer.Render(report));
    }

    [Fact]
    public void Empty_base_reports_every_head_resource_as_added()
    {
        var diff = BundleDiffer.Diff(ResolvedBundle.Empty, Bundle.Of("""{ "jobs": { "a": {} }, "pipelines": { "p": {} } }"""));

        Assert.All(diff.Changed, c => Assert.Equal(ChangeKind.Added, c.Kind));
        Assert.Equal(2, diff.Changed.Count());
    }

    [Fact]
    public void Locations_map_paths_to_file_and_line()
    {
        var bundle = ResolvedBundle.Parse("""
            { "resources": { "jobs": { "a": {} } },
              "__locations": { "version": 1, "files": ["databricks.yml", "resources/a.yml"],
                               "locations": { "resources.jobs.a": [[1, 3, 5]] } } }
            """, "t");

        Assert.Equal(new SourceLocation("resources/a.yml", 3), bundle.FindLocation("resources.jobs.a.tasks[x].existing_cluster_id"));
    }

    [Fact]
    public void Missing_locations_yield_no_location()
    {
        Assert.Null(Bundle.Of("""{ "jobs": { "a": {} } }""").FindLocation("resources.jobs.a"));
    }

    [Fact]
    public void Invalid_json_is_an_input_error()
    {
        var ex = Assert.Throws<Core.DoctorException>(() => ResolvedBundle.Parse("{ not json", "head.json"));
        Assert.Contains("head.json", ex.Message);
    }

    [Fact]
    public void Unresolved_variable_in_a_typed_field_is_named()
    {
        // What validate prints when it stops before resolving variables (for example without auth).
        var json = """{ "bundle": { "name": "b" }, "resources": { "pipelines": { "etl": { "serverless": "${var.serverless_enabled}" } } } }""";

        var ex = Assert.Throws<Core.DoctorException>(() => ResolvedBundle.Parse(json, "head.json"));

        Assert.Contains("`resources.pipelines.etl.serverless` is the unresolved reference `${var.serverless_enabled}`", ex.Message);
        Assert.Contains("did not resolve the bundle's variables", ex.Message);
    }

    [Theory]
    [InlineData("$.a['x.y'].b[1]", 2)]
    [InlineData("$.a['x.y'].b[5]", null)]
    [InlineData("$.missing", null)]
    public void Error_paths_are_followed(string path, int? expected)
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""{ "a": { "x.y": { "b": [1, 2] } } }""");
        Assert.Equal(expected, ResolvedBundle.Find(doc.RootElement, path)?.GetInt32());
    }
}
