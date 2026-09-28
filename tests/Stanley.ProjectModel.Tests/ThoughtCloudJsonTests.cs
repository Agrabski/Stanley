using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Tests;

/// <summary>The fields a thought cloud (Insert › Thought cloud, issue #68) added to <see cref="Panel"/>: written only when set, so older panel files read - and write back - unchanged.</summary>
public class ThoughtCloudJsonTests
{
    private static Panel RectanglePanel() =>
        new(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 100, 80)), null, [], []);

    private static Panel CloudPanel() =>
        new(PanelId.New(), PanelShapes.Cloud(new Rect2D(20, 20, 60, 40)), null, [], [], Kind: PanelKind.Cloud);

    [Fact]
    public void A_panel_is_a_rectangle_unless_it_says_otherwise()
    {
        var rectangle = ProjectJson.Serialize(RectanglePanel());
        var cloud = ProjectJson.Serialize(CloudPanel());

        Assert.DoesNotContain("kind", rectangle, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"cloud\"", cloud, StringComparison.Ordinal);
        Assert.Equal(PanelKind.Rectangle, ProjectJson.Deserialize<Panel>(rectangle).Kind);
        Assert.Equal(PanelKind.Cloud, ProjectJson.Deserialize<Panel>(cloud).Kind);
    }

    [Fact]
    public void A_cloud_panel_round_trips_with_its_shape_and_kind()
    {
        var cloud = CloudPanel();
        var json = ProjectJson.Serialize(cloud);
        var read = ProjectJson.Deserialize<Panel>(json);

        Assert.Equivalent(cloud, read, strict: true);
        Assert.Equal(PanelKind.Cloud, read.Kind);
        Assert.Equal(PanelShapes.Lobes * PanelShapes.AnchorsPerLobe, read.Shape.Anchors.Count);
    }

    [Fact]
    public void A_panel_file_from_before_thought_clouds_reads_as_a_rectangle_with_no_trail()
    {
        var json = ProjectJson.Serialize(RectanglePanel());
        Assert.DoesNotContain("kind", json, StringComparison.Ordinal);
        Assert.DoesNotContain("trail", json, StringComparison.Ordinal);

        var read = ProjectJson.Deserialize<Panel>(json);

        Assert.Equal(PanelKind.Rectangle, read.Kind);
        Assert.Null(read.Trail);
    }

    [Fact]
    public void A_thought_trail_is_written_only_when_set_and_round_trips()
    {
        var plain = ProjectJson.Serialize(CloudPanel());
        var trailed = CloudPanel() with { Trail = new ThoughtTrail(0.25, new Point2D(15, 90)) };

        Assert.DoesNotContain("trail", plain, StringComparison.Ordinal);
        Assert.Null(ProjectJson.Deserialize<Panel>(plain).Trail);

        var read = ProjectJson.Deserialize<Panel>(ProjectJson.Serialize(trailed));
        Assert.Equal(trailed.Trail, read.Trail);
    }
}
