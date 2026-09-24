using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Editing.Tests;

public class PanelGuttersTests
{
    private static Panel NewPanel(Rect2D bounds) =>
        new(PanelId.New(), PanelShapes.Rectangle(bounds), Background: null, CharacterInstances: [], Bubbles: []);

    [Fact]
    public void FindAt_InsideTheGapBetweenTwoPanels_ReturnsThatGutter()
    {
        var left = NewPanel(new Rect2D(10, 10, 93, 100));
        var right = NewPanel(new Rect2D(107, 10, 93, 100));

        var hit = PanelGutters.FindAt([left, right], new Point2D(105, 50), tolerance: 1);

        Assert.NotNull(hit);
        Assert.Equal(BoundaryOrientation.Vertical, hit.Drag.Orientation);
        Assert.Equal([left.Id], hit.Drag.PanelsBefore);
        Assert.Equal([right.Id], hit.Drag.PanelsAfter);
        Assert.Equal(4, hit.Drag.Gap, 6);
        Assert.Equal(103, hit.Position, 6);
    }

    [Fact]
    public void FindAt_DeepInsideAPanel_ReturnsNull()
    {
        var left = NewPanel(new Rect2D(10, 10, 93, 100));
        var right = NewPanel(new Rect2D(107, 10, 93, 100));

        Assert.Null(PanelGutters.FindAt([left, right], new Point2D(50, 50), tolerance: 1));
    }

    [Fact]
    public void FindAt_AlignedColumnsInAGrid_GrabsTheWholeColumnLine()
    {
        var topLeft = NewPanel(new Rect2D(10, 10, 93, 100));
        var topRight = NewPanel(new Rect2D(107, 10, 93, 100));
        var bottomLeft = NewPanel(new Rect2D(10, 114, 93, 100));
        var bottomRight = NewPanel(new Rect2D(107, 114, 93, 100));

        var hit = PanelGutters.FindAt([topLeft, topRight, bottomLeft, bottomRight], new Point2D(105, 50), tolerance: 1);

        Assert.NotNull(hit);
        Assert.Equivalent(new[] { topLeft.Id, bottomLeft.Id }, hit.Drag.PanelsBefore);
        Assert.Equivalent(new[] { topRight.Id, bottomRight.Id }, hit.Drag.PanelsAfter);
    }

    [Fact]
    public void FindAt_HorizontalGutter_IsFoundToo()
    {
        var top = NewPanel(new Rect2D(10, 10, 190, 100));
        var bottom = NewPanel(new Rect2D(10, 114, 190, 100));

        var hit = PanelGutters.FindAt([top, bottom], new Point2D(100, 112), tolerance: 1);

        Assert.NotNull(hit);
        Assert.Equal(BoundaryOrientation.Horizontal, hit.Drag.Orientation);
    }
}
