using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering.Tests;

/// <summary>A part's shape from an SVG (docs/shape-characters.md §9.3): the first closed path becomes the outline, fitted into the unit box; the rest is reported.</summary>
public sealed class ShapeOutlineImportTests
{
    private static ShapeOutlineImport.Result Import(string body, string viewBox = "0 0 200 100") =>
        ShapeOutlineImport.FromFile(ArtFile.Svg($"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="{viewBox}">{body}</svg>"""));

    private static void AssertFillsTheUnitBox(ModuleShape shape)
    {
        var bounds = AnchorRing.CurveBounds(shape.Anchors);
        Assert.Equal(0, bounds.X, 3);
        Assert.Equal(0, bounds.Y, 3);
        Assert.Equal(1, bounds.Width, 3);
        Assert.Equal(1, bounds.Height, 3);
    }

    /// <summary>Whether the outline, drawn filling a box, covers that box's point (unit-box coordinates).</summary>
    private static bool Covers(ModuleShape shape, double x, double y)
    {
        using var path = AnchorRingPath.ToSkPath(shape.Anchors);
        return path.Contains((float)x, (float)y);
    }

    [Fact]
    public void A_rectangle_becomes_four_corners_filling_the_box_with_its_proportions_kept_aside()
    {
        var result = Import("""<rect x="20" y="10" width="120" height="60" fill="#f00"/>""");

        Assert.Null(result.Error);
        var outline = result.Outline!;
        Assert.Equal(4, outline.Shape.Anchors.Count);
        Assert.All(outline.Shape.Anchors, a => Assert.Equal(AnchorHandleKind.Corner, a.HandleKind));
        AssertFillsTheUnitBox(outline.Shape);
        Assert.Equal(2, outline.Aspect, 3);           // 120 x 60 - what a head's Width / Height take back
        Assert.Equal(120, outline.Bounds.Width, 3);
        Assert.Empty(result.Report);
        Assert.Null(outline.Shape.Source);
    }

    [Fact]
    public void A_circle_comes_in_as_smooth_curves()
    {
        var result = Import("""<circle cx="50" cy="50" r="40"/>""", "0 0 100 100");

        var outline = result.Outline!;
        Assert.True(outline.Shape.Anchors.Count >= 3);
        Assert.All(outline.Shape.Anchors, a => Assert.Equal(AnchorHandleKind.Smooth, a.HandleKind));
        AssertFillsTheUnitBox(outline.Shape);
        Assert.Equal(1, outline.Aspect, 3);
        Assert.True(Covers(outline.Shape, 0.5, 0.5));
        Assert.False(Covers(outline.Shape, 0.03, 0.03)); // round: the box's corner is outside it
    }

    [Fact]
    public void Quadratics_and_arcs_are_curves_in_the_outline()
    {
        // A dome: a quadratic hump on top, a half-circle arc underneath, a straight side.
        var result = Import("""<path d="M10,50 Q50,0 90,50 A40,40 0 0 1 10,50 Z"/>""", "0 0 100 100");

        Assert.Null(result.Error);
        var shape = result.Outline!.Shape;
        AssertFillsTheUnitBox(shape);
        // Curved, not cut into straight lines: some handles are off their points.
        Assert.Contains(shape.Anchors, a => Distance(a.OutHandle, a.Point) > 0.05 || Distance(a.InHandle, a.Point) > 0.05);
        // The bulge of the quadratic's top and the arc's bottom are inside: the middle is covered, the outline is closed.
        Assert.True(Covers(shape, 0.5, 0.15));
        Assert.True(Covers(shape, 0.5, 0.9));
        // The quadratic's top is at y = 25 of a 10..90 box: the outline's top is the curve's, not its control point's.
        Assert.Equal(65, result.Outline.Bounds.Height, 0);
    }

    [Fact]
    public void An_open_path_is_not_a_shape()
    {
        var result = Import("""<path d="M10,10 L90,10 L90,90" fill="none" stroke="#000"/>""");

        Assert.Null(result.Outline);
        Assert.NotNull(result.Error);
        Assert.Contains("1 open path ignored", result.Report);
    }

    [Fact]
    public void The_first_closed_path_is_used_and_the_rest_reported()
    {
        var result = Import("""
            <path d="M0,0 L40,0" stroke="#000"/>
            <path d="M0,0 L50,0 L50,100 Z"/>
            <rect x="100" y="0" width="50" height="50"/>
            <ellipse cx="170" cy="50" rx="20" ry="40"/>
            """);

        var outline = result.Outline!;
        Assert.Equal(3, outline.Shape.Anchors.Count); // the triangle
        Assert.Equal(0.5, outline.Aspect, 3);
        Assert.Contains("1 open path ignored", result.Report);
        Assert.Contains("2 more shapes ignored (only the first is used)", result.Report);
    }

    [Fact]
    public void A_transformed_path_is_read_as_drawn()
    {
        var result = Import("""<g transform="scale(2,1)"><rect width="10" height="40"/></g>""");

        Assert.Equal(0.5, result.Outline!.Aspect, 3); // 20 x 40 once scaled
    }

    [Fact]
    public void A_picture_is_not_a_shape()
    {
        var result = ShapeOutlineImport.FromFile(ArtFile.Png([1, 2, 3]));

        Assert.Null(result.Outline);
        Assert.Contains("SVG", result.Error);
    }

    [Fact]
    public void The_outline_runs_clockwise_whichever_way_it_was_drawn()
    {
        var clockwise = Import("""<path d="M0,0 L100,0 L100,100 L0,100 Z"/>""").Outline!.Shape;
        var anticlockwise = Import("""<path d="M0,0 L0,100 L100,100 L100,0 Z"/>""").Outline!.Shape;

        Assert.True(ModuleOutlines.SignedArea(clockwise.Anchors) > 0);
        Assert.True(ModuleOutlines.SignedArea(anticlockwise.Anchors) > 0);
    }

    [Fact]
    public void A_closed_path_of_two_curves_is_split_into_at_least_three_points()
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(0, 50);
        builder.CubicTo(0, 0, 100, 0, 100, 50);
        builder.CubicTo(100, 100, 0, 100, 0, 50);
        builder.Close();
        using var path = builder.Detach();

        var (closed, open) = ShapeOutlineImport.Contours(path);

        Assert.Equal(0, open);
        Assert.True(Assert.Single(closed).Count >= 3);
    }

    [Fact]
    public void Keeping_proportions_puts_the_long_side_at_one_centred_and_standing_at_the_top()
    {
        var wide = Import("""<rect width="200" height="50"/>""").Outline!;

        var kept = ModuleOutlines.Refit(wide.Shape, wide.Aspect, keepProportions: true)!;
        var bounds = AnchorRing.CurveBounds(kept.Shape.Anchors);

        Assert.Equal((0, 0, 1, 0.25), (Math.Round(bounds.X, 3), Math.Round(bounds.Y, 3), Math.Round(bounds.Width, 3), Math.Round(bounds.Height, 3)));
        var tall = ModuleOutlines.Refit(wide.Shape, 0.5, keepProportions: true)!;
        var tallBounds = AnchorRing.CurveBounds(tall.Shape.Anchors);
        Assert.Equal((0.25, 0, 0.5, 1), (Math.Round(tallBounds.X, 3), Math.Round(tallBounds.Y, 3), Math.Round(tallBounds.Width, 3), Math.Round(tallBounds.Height, 3)));
    }

    private static double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
