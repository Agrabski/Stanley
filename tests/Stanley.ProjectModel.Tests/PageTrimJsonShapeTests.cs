using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Tests;

public class PageTrimJsonShapeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("stanley-page-trim-json-tests").FullName;

    [Fact]
    public void PageTrim_serializes_size_as_its_own_nested_object()
    {
        var path = Path.Combine(_dir, "manifest.json");
        var manifest = new SeriesManifest("My Comic", new PageTrim(MetricPaperSizes.Size(MetricPaperSize.A4), 3), []);

        ProjectJson.Write(path, manifest);
        var json = File.ReadAllText(path);

        Assert.Contains("    \"size\": {\n      \"heightMm\": 297,\n      \"widthMm\": 210\n    }", json);
    }

    [Fact]
    public void A_metric_paper_preset_round_trips_through_a_project()
    {
        var path = Path.Combine(_dir, "manifest.json");
        var manifest = new SeriesManifest("My Comic", new PageTrim(MetricPaperSizes.Size(MetricPaperSize.A5), 3), [IssueId.New()]);

        ProjectJson.Write(path, manifest);
        var reloaded = ProjectJson.Read<SeriesManifest>(path);

        Assert.Equal(MetricPaperSizes.Size(MetricPaperSize.A5), reloaded.DefaultPageTrim.Size);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
