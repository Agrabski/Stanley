using Stanley.ProjectModel.Geometry;
using Xunit;

namespace Stanley.Editing.Tests;

public class PanelSnappingTests
{
    private static readonly Rect2D Page = new(0, 0, 210, 297);
    private static readonly PanelGrid Grid = new(10, 4);

    [Fact]
    public void SnapResize_PullsMovingEdgeToOneGutterBeforeANeighbour()
    {
        var neighbour = new Rect2D(110, 10, 90, 100);
        var result = PanelSnapping.SnapResize(new Rect2D(10, 10, 97.5, 100), RectEdges.Right, Page, [neighbour], Grid, tolerance: 2);

        Assert.Equal(106, result.Bounds.Right, 6);
        Assert.Equal(10, result.Bounds.Left, 6);
        Assert.Contains(result.Guides, g => g.Orientation == BoundaryOrientation.Vertical && Math.Abs(g.Position - 106) < 1e-6);
    }

    [Fact]
    public void SnapResize_LeavesEdgesThatArentMovingAlone()
    {
        var result = PanelSnapping.SnapResize(new Rect2D(11, 11, 100, 100), RectEdges.Right | RectEdges.Bottom, Page, [], Grid, tolerance: 2);

        Assert.Equal(11, result.Bounds.Left, 6);
        Assert.Equal(11, result.Bounds.Top, 6);
    }

    [Fact]
    public void SnapResize_OutOfTolerance_DoesNothing()
    {
        var raw = new Rect2D(30, 30, 50, 50);
        var result = PanelSnapping.SnapResize(raw, RectEdges.All, Page, [], Grid, tolerance: 2);

        Assert.Equal(raw, result.Bounds);
        Assert.Empty(result.Guides);
    }

    [Fact]
    public void SnapMove_SnapsToTheMarginWithoutResizing()
    {
        var result = PanelSnapping.SnapMove(new Rect2D(11.5, 50, 80, 60), Page, [], Grid, tolerance: 2);

        Assert.Equal(10, result.Bounds.Left, 6);
        Assert.Equal(80, result.Bounds.Width, 6);
        Assert.Equal(60, result.Bounds.Height, 6);
    }

    [Fact]
    public void LiveArea_IsThePageInsetByTheMargin()
    {
        Assert.Equal(Rect2D.FromEdges(10, 10, 200, 287), Grid.LiveArea(Page));
    }
}
