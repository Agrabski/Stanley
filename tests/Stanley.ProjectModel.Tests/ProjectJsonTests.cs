using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Tests;

public class ProjectJsonTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("stanley-json-tests").FullName;

    [Fact]
    public void Write_produces_alphabetically_sorted_keys_two_space_indent_and_trailing_newline()
    {
        var path = Path.Combine(_dir, "manifest.json");
        var manifest = new SeriesManifest("My Comic", new PageTrim(new PageSize(210, 297), 3), [IssueId.FromValue("issue1")]);

        ProjectJson.Write(path, manifest);
        var json = File.ReadAllText(path);

        Assert.EndsWith("\n", json);
        Assert.False(json.EndsWith("\n\n"));

        var defaultPageTrimIndex = json.IndexOf("\"defaultPageTrim\"", StringComparison.Ordinal);
        var issueIdsIndex = json.IndexOf("\"issueIds\"", StringComparison.Ordinal);
        var titleIndex = json.IndexOf("\"title\"", StringComparison.Ordinal);
        Assert.True(defaultPageTrimIndex < issueIdsIndex);
        Assert.True(issueIdsIndex < titleIndex);

        // Sorting recurses into nested objects: defaultPageTrim's own keys (bleedMm, size)
        // and size's own keys (heightMm, widthMm) must also be alphabetical.
        var bleedMmIndex = json.IndexOf("\"bleedMm\"", StringComparison.Ordinal);
        var sizeIndex = json.IndexOf("\"size\"", StringComparison.Ordinal);
        var heightMmIndex = json.IndexOf("\"heightMm\"", StringComparison.Ordinal);
        var widthMmIndex = json.IndexOf("\"widthMm\"", StringComparison.Ordinal);
        Assert.True(bleedMmIndex < sizeIndex);
        Assert.True(heightMmIndex < widthMmIndex);

        Assert.Contains("\n  \"", json); // two-space indent
    }

    [Fact]
    public void Write_then_Read_round_trips()
    {
        var path = Path.Combine(_dir, "manifest.json");
        var manifest = new SeriesManifest("My Comic", new PageTrim(new PageSize(210, 297), 3), [IssueId.FromValue("issue1"), IssueId.FromValue("issue2")]);

        ProjectJson.Write(path, manifest);
        var reloaded = ProjectJson.Read<SeriesManifest>(path);

        Assert.Equivalent(manifest, reloaded, strict: true);
    }

    [Fact]
    public void Write_creates_missing_parent_directories()
    {
        var path = Path.Combine(_dir, "nested", "deep", "manifest.json");
        ProjectJson.Write(path, new SeriesManifest("X", new PageTrim(new PageSize(1, 1), 0), []));

        Assert.True(File.Exists(path));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
