using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.ProjectModel.Tests;

/// <summary>A drawn shape's box follows its curves, not just its points (issue #84: a handle pulled out bows the outline past them).</summary>
public class AnchorRingCurveBoundsTests
{
    [Fact]
    public void A_rectangle_and_an_ellipse_have_the_box_they_were_drawn_in()
    {
        var box = new Rect2D(10, 20, 40, 30);

        Assert.Equal(box, AnchorRing.CurveBounds(AnchorRing.Rectangle(box)));
        var oval = AnchorRing.CurveBounds(AnchorRing.Ellipse(box));
        Assert.Equal(box.Left, oval.Left, 9);
        Assert.Equal(box.Top, oval.Top, 9);
        Assert.Equal(box.Right, oval.Right, 9);
        Assert.Equal(box.Bottom, oval.Bottom, 9);
    }

    [Fact]
    public void An_edge_bowed_out_by_its_handles_widens_the_box_past_the_points()
    {
        // A straight edge from (0,0) to (30,0), both handles pulled 20mm up: the curve peaks 15mm up.
        IReadOnlyList<ShapeAnchor> arch =
        [
            new(new Point2D(0, 0), new Point2D(0, 0), new Point2D(0, -20), AnchorHandleKind.Corner),
            new(new Point2D(30, 0), new Point2D(30, -20), new Point2D(30, 0), AnchorHandleKind.Corner)
        ];

        var bounds = AnchorRing.CurveBounds(arch, closed: false);

        Assert.Equal(-15, bounds.Top, 9);
        Assert.Equal(0, bounds.Bottom, 9);
        Assert.Equal(0, bounds.Left, 9);
        Assert.Equal(30, bounds.Right, 9);
        Assert.Equal(0, AnchorRing.BoundingBox(arch).Height);
    }

    [Fact]
    public void An_open_line_has_no_closing_edge_to_bow()
    {
        IReadOnlyList<ShapeAnchor> line =
        [
            new(new Point2D(0, 0), new Point2D(0, 10), new Point2D(0, 0), AnchorHandleKind.Corner),
            new(new Point2D(10, 0), new Point2D(10, 0), new Point2D(10, 10), AnchorHandleKind.Corner)
        ];

        Assert.Equal(0, AnchorRing.CurveBounds(line, closed: false).Height, 9);
        Assert.True(AnchorRing.CurveBounds(line, closed: true).Height > 5);
    }

    [Fact]
    public void A_shape_elements_bounds_are_its_curve_bounds()
    {
        IReadOnlyList<ShapeAnchor> arch =
        [
            new(new Point2D(0, 0), new Point2D(0, 0), new Point2D(0, -20), AnchorHandleKind.Corner),
            new(new Point2D(30, 0), new Point2D(30, -20), new Point2D(30, 0), AnchorHandleKind.Corner)
        ];
        var shape = new ShapeElement(ElementId.New(), ElementLayer.Background, arch, Closed: false, new ShapeStyle(null, null, 0.5));

        Assert.Equal(-15, PanelElements.Bounds(shape).Top, 9);
    }
}
