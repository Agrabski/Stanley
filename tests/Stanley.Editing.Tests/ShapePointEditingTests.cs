using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Editing.Tests;

/// <summary>Edit Points and the Freeform tool (issue #84): reshaping a shape point by point.</summary>
public class ShapePointEditingTests
{
    private static readonly ShapeStyle Pen = ShapeEditing.DefaultStyle;

    private static ShapeElement Square() =>
        ShapeEditing.Rectangle(new Rect2D(10, 10, 40, 40), Pen, ElementLayer.Background).Value;

    private static ShapeElement Oval() =>
        ShapeEditing.Ellipse(new Rect2D(10, 10, 40, 20), Pen, ElementLayer.Background).Value;

    private static ShapeElement Zigzag() =>
        new(ElementId.New(), ElementLayer.Background,
            [AnchorRing.Corner(new Point2D(0, 0)), AnchorRing.Corner(new Point2D(10, 10)), AnchorRing.Corner(new Point2D(20, 0))], Closed: false, Pen);

    private static void Near(Point2D expected, Point2D actual, double tolerance = 1e-6)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
    }

    /// <summary>Points along the whole outline, to compare two outlines by.</summary>
    private static List<Point2D> Trace(ShapeElement shape, int perEdge = 20)
    {
        var points = new List<Point2D>();
        for (var s = 0; s < ShapePointEditing.SegmentCount(shape); s++)
        {
            for (var i = 0; i <= perEdge; i++)
                points.Add(ShapePointEditing.PointOn(shape, s, (double)i / perEdge));
        }
        return points;
    }

    private static double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Gap(Point2D p, ShapeElement shape) => ShapePointEditing.Nearest(shape, p)!.Value.Distance;

    [Fact]
    public void Moving_a_point_carries_its_handles_along()
    {
        var oval = Oval();
        var before = oval.Anchors[1];

        var moved = ShapePointEditing.MovePoint(oval, 1, new Point2D(before.Point.X + 5, before.Point.Y - 2)).Value;

        var after = moved.Anchors[1];
        Near(new Point2D(before.InHandle.X + 5, before.InHandle.Y - 2), after.InHandle);
        Near(new Point2D(before.OutHandle.X + 5, before.OutHandle.Y - 2), after.OutHandle);
        Assert.Equal(oval.Anchors[0], moved.Anchors[0]);
        Assert.False(ShapePointEditing.MovePoint(oval, 9, default).IsValid);
    }

    [Fact]
    public void Pulling_a_smooth_points_handle_swings_the_other_one_round_keeping_its_length()
    {
        var oval = Oval();
        var top = oval.Anchors[0]; // smooth, handles level either side
        var inLength = Distance(top.Point, top.InHandle);

        var bent = ShapePointEditing.MoveHandle(oval, 0, HandleSide.Out, new Point2D(top.Point.X + 10, top.Point.Y + 10)).Value.Anchors[0];

        Near(new Point2D(top.Point.X + 10, top.Point.Y + 10), bent.OutHandle);
        var s = inLength / Math.Sqrt(2);
        Near(new Point2D(top.Point.X - s, top.Point.Y - s), bent.InHandle);
        Assert.Equal(AnchorHandleKind.Smooth, bent.HandleKind);
    }

    [Fact]
    public void Alt_dragging_a_handle_breaks_the_curve_into_a_corner()
    {
        var oval = Oval();
        var top = oval.Anchors[0];

        var broken = ShapePointEditing.MoveHandle(oval, 0, HandleSide.In, new Point2D(top.Point.X - 3, top.Point.Y + 8), independent: true).Value.Anchors[0];

        Assert.Equal(AnchorHandleKind.Corner, broken.HandleKind);
        Assert.Equal(top.OutHandle, broken.OutHandle);
        Near(new Point2D(top.Point.X - 3, top.Point.Y + 8), broken.InHandle);
    }

    [Fact]
    public void Adding_a_point_to_a_curve_keeps_the_outline_exactly_as_it_was()
    {
        var oval = Oval();

        var (split, index) = ShapePointEditing.InsertPoint(oval, 1, 0.3).Value;

        Assert.Equal(2, index);
        Assert.Equal(5, split.Anchors.Count);
        Assert.Equal(AnchorHandleKind.Smooth, split.Anchors[index].HandleKind);
        Near(ShapePointEditing.PointOn(oval, 1, 0.3), split.Anchors[index].Point);
        foreach (var p in Trace(split))
            Assert.True(Gap(p, oval) < 1e-3, $"{p} strayed off the original outline");
        foreach (var p in Trace(oval))
            Assert.True(Gap(p, split) < 1e-3, $"{p} is no longer covered");
    }

    [Fact]
    public void Adding_a_point_to_a_straight_edge_makes_a_corner_that_pulls_out_into_two_straight_edges()
    {
        var square = Square();

        var (split, index) = ShapePointEditing.InsertPoint(square, 0, 0.5).Value; // the top edge

        Assert.Equal(1, index);
        var added = split.Anchors[index];
        Assert.Equal(AnchorHandleKind.Corner, added.HandleKind);
        Near(new Point2D(30, 10), added.Point);
        Assert.Equal(added.Point, added.InHandle);
        Assert.Equal(added.Point, added.OutHandle);

        var roof = ShapePointEditing.MovePoint(split, index, new Point2D(30, 0)).Value;
        Near(new Point2D(20, 5), ShapePointEditing.PointOn(roof, 0, 0.5)); // still a straight edge
    }

    [Fact]
    public void A_point_can_be_added_on_the_edge_that_closes_a_shape()
    {
        var square = Square();

        var (split, index) = ShapePointEditing.InsertPoint(square, 3, 0.5).Value; // left edge, back to the start

        Assert.Equal(4, index);
        Near(new Point2D(10, 30), split.Anchors[index].Point);
        Assert.False(ShapePointEditing.InsertPoint(Zigzag(), 2, 0.5).IsValid); // a line has no closing edge
    }

    [Fact]
    public void The_nearest_spot_on_the_outline_is_found_on_the_right_edge()
    {
        var spot = ShapePointEditing.Nearest(Square(), new Point2D(52, 33))!.Value;

        Assert.Equal(1, spot.Segment); // the right edge
        Near(new Point2D(50, 33), spot.Point, 1e-4);
        Assert.Equal(2, spot.Distance, 4);
    }

    [Fact]
    public void Deleting_points_stops_at_three_for_a_shape_and_two_for_a_line()
    {
        var square = Square();
        var triangle = ShapePointEditing.DeletePoint(square, 2).Value;

        Assert.Equal(3, triangle.Anchors.Count);
        Assert.DoesNotContain(triangle.Anchors, a => a.Point == new Point2D(50, 50));
        Assert.False(ShapePointEditing.DeletePoint(triangle, 0).IsValid);

        var line = ShapePointEditing.DeletePoint(Zigzag(), 1).Value;
        Assert.Equal(2, line.Anchors.Count);
        Assert.False(ShapePointEditing.DeletePoint(line, 0).IsValid);
    }

    [Fact]
    public void A_corner_made_smooth_gets_handles_along_the_line_between_its_neighbours()
    {
        var square = Square();

        var rounded = ShapePointEditing.SetPointKind(square, 1, AnchorHandleKind.Smooth).Value.Anchors[1]; // top right (50,10)

        Assert.Equal(AnchorHandleKind.Smooth, rounded.HandleKind);
        // Neighbours (10,10) and (50,50): the handles run parallel to the diagonal between them.
        var inDir = (rounded.Point.X - rounded.InHandle.X, rounded.Point.Y - rounded.InHandle.Y);
        var outDir = (rounded.OutHandle.X - rounded.Point.X, rounded.OutHandle.Y - rounded.Point.Y);
        Assert.Equal(1, inDir.Item1 / inDir.Item2, 6);
        Assert.Equal(1, outDir.Item1 / outDir.Item2, 6);
        Assert.Equal(40.0 / 3, Distance(rounded.Point, rounded.InHandle), 6);
        Assert.Equal(40.0 / 3, Distance(rounded.Point, rounded.OutHandle), 6);
    }

    [Fact]
    public void A_smooth_point_made_sharp_folds_its_handles_onto_it()
    {
        var sharp = ShapePointEditing.SetPointKind(Oval(), 0, AnchorHandleKind.Corner).Value.Anchors[0];

        Assert.Equal(AnchorHandleKind.Corner, sharp.HandleKind);
        Assert.Equal(sharp.Point, sharp.InHandle);
        Assert.Equal(sharp.Point, sharp.OutHandle);
    }

    [Fact]
    public void A_cusp_made_smooth_keeps_its_handle_lengths_but_lines_them_up()
    {
        var oval = Oval();
        var top = oval.Anchors[0];
        var cusp = ShapePointEditing.MoveHandle(oval, 0, HandleSide.Out, new Point2D(top.Point.X + 4, top.Point.Y + 4), independent: true).Value;
        var inLength = Distance(top.Point, cusp.Anchors[0].InHandle);

        var smooth = ShapePointEditing.SetPointKind(cusp, 0, AnchorHandleKind.Smooth).Value.Anchors[0];

        Assert.Equal(inLength, Distance(smooth.Point, smooth.InHandle), 6);
        Assert.Equal(Math.Sqrt(32), Distance(smooth.Point, smooth.OutHandle), 6);
        var cross = (smooth.OutHandle.X - smooth.Point.X) * (smooth.InHandle.Y - smooth.Point.Y) - (smooth.OutHandle.Y - smooth.Point.Y) * (smooth.InHandle.X - smooth.Point.X);
        Assert.Equal(0, cross, 6);
    }

    [Fact]
    public void A_lines_end_made_smooth_aims_its_one_handle_at_its_neighbour()
    {
        var end = ShapePointEditing.SetPointKind(Zigzag(), 0, AnchorHandleKind.Smooth).Value.Anchors[0];

        Assert.Equal(end.Point, end.InHandle);
        Near(new Point2D(10.0 / 3, 10.0 / 3), end.OutHandle);
        Assert.Single(ShapePointEditing.VisibleHandles(Zigzag() with { Anchors = [end, .. Zigzag().Anchors.Skip(1)] }, 0));
    }

    [Fact]
    public void Only_handles_pulled_away_from_their_point_and_shaping_an_edge_show()
    {
        Assert.Empty(ShapePointEditing.VisibleHandles(Square(), 0));
        Assert.Equal(2, ShapePointEditing.VisibleHandles(Oval(), 0).Count);

        var smoothLine = ShapePointEditing.SetPointKind(Zigzag(), 1, AnchorHandleKind.Smooth).Value;
        Assert.Equal(2, ShapePointEditing.VisibleHandles(smoothLine, 1).Count);
        Assert.Empty(ShapePointEditing.VisibleHandles(smoothLine, 7));
    }

    [Fact]
    public void Closing_a_line_joins_its_ends_and_opening_a_shape_cuts_it_at_a_point()
    {
        var closed = ShapePointEditing.Close(Zigzag()).Value;
        Assert.True(closed.Closed);
        Assert.Equal(3, closed.Anchors.Count);
        Assert.False(ShapePointEditing.Close(closed).IsValid);

        var (opened, end) = ShapePointEditing.OpenAt(Square(), 2).Value;
        Assert.False(opened.Closed);
        Assert.Equal(5, opened.Anchors.Count);
        Assert.Equal(4, end);
        Assert.Equal(new Point2D(50, 50), opened.Anchors[0].Point);
        Assert.Equal(new Point2D(50, 50), opened.Anchors[end].Point);
        Assert.Equal(new Point2D(10, 50), opened.Anchors[1].Point); // still runs the same way round
        Assert.False(ShapePointEditing.OpenAt(opened, 0).IsValid);
    }

    [Fact]
    public void Closing_a_line_drawn_back_onto_its_start_merges_the_two_ends()
    {
        var loop = new ShapeElement(ElementId.New(), ElementLayer.Background,
            [AnchorRing.Corner(new Point2D(0, 0)), AnchorRing.Corner(new Point2D(10, 0)), AnchorRing.Corner(new Point2D(10, 10)), AnchorRing.Corner(new Point2D(0, 0))],
            Closed: false, Pen);

        var closed = ShapePointEditing.Close(loop).Value;

        Assert.Equal(3, closed.Anchors.Count);
        Assert.True(closed.Closed);
    }

    [Fact]
    public void A_line_too_short_to_close_stays_open()
    {
        var two = Zigzag() with { Anchors = Zigzag().Anchors.Take(2).ToList() };

        Assert.False(ShapePointEditing.Close(two).IsValid);
    }

    [Fact]
    public void Freeform_points_make_a_line_or_with_the_first_point_clicked_again_a_closed_shape()
    {
        IReadOnlyList<ShapeAnchor> points = [ShapePointEditing.Placed(new Point2D(0, 0)), ShapePointEditing.Placed(new Point2D(20, 0)), ShapePointEditing.Placed(new Point2D(10, 15))];

        var line = ShapePointEditing.Freeform(points, closed: false, Pen, ElementLayer.Foreground).Value;
        Assert.False(line.Closed);
        Assert.Equal(ElementLayer.Foreground, line.Layer);
        Assert.Equal(3, line.Anchors.Count);

        Assert.True(ShapePointEditing.Freeform(points, closed: true, Pen, ElementLayer.Background).Value.Closed);
        Assert.False(ShapePointEditing.Freeform(points.Take(2).ToList(), closed: true, Pen, ElementLayer.Background).Value.Closed);
        Assert.False(ShapePointEditing.Freeform(points.Take(1).ToList(), closed: false, Pen, ElementLayer.Background).IsValid);
        Assert.False(ShapePointEditing.Freeform([ShapePointEditing.Placed(new Point2D(0, 0)), ShapePointEditing.Placed(new Point2D(0.2, 0.1))], closed: false, Pen, ElementLayer.Background).IsValid);
    }

    [Fact]
    public void A_freeform_point_dragged_as_it_is_placed_is_smooth_with_mirrored_handles()
    {
        var smooth = ShapePointEditing.Placed(new Point2D(10, 10), new Point2D(14, 13));

        Assert.Equal(AnchorHandleKind.Smooth, smooth.HandleKind);
        Assert.Equal(new Point2D(14, 13), smooth.OutHandle);
        Assert.Equal(new Point2D(6, 7), smooth.InHandle);
        Assert.Equal(AnchorHandleKind.Corner, ShapePointEditing.Placed(new Point2D(10, 10), new Point2D(10, 10)).HandleKind);
    }

    [Fact]
    public void A_shape_bowed_out_by_a_handle_resizes_by_its_outline_not_just_its_points()
    {
        var oval = Oval();
        var top = oval.Anchors[0].Point;
        var peaked = ShapePointEditing.MoveHandle(ShapePointEditing.SetPointKind(oval, 0, AnchorHandleKind.Corner).Value, 0, HandleSide.Out, new Point2D(top.X + 5, top.Y - 30), independent: true).Value;
        var box = PanelElements.Bounds(peaked);
        Assert.True(box.Top < top.Y - 5, "the curve bows up past the top point");

        var resized = ShapeEditing.Resize(peaked, box with { Height = box.Height * 2 }).Value;

        var after = PanelElements.Bounds(resized);
        Assert.Equal(box.Top, after.Top, 6);
        Assert.Equal(box.Height * 2, after.Height, 6);
        Assert.Equal(box.Width, after.Width, 6);
    }

    [Fact]
    public void Shift_snaps_to_the_nearest_45_degrees_keeping_the_distance()
    {
        Near(new Point2D(10, 0), ShapePointEditing.SnapAngle(new Point2D(0, 0), new Point2D(9.9, 1.4)), 0.02);
        var diagonal = ShapePointEditing.SnapAngle(new Point2D(0, 0), new Point2D(10, 9));
        Assert.Equal(diagonal.X, diagonal.Y, 6);
        Assert.Equal(Math.Sqrt(181), Math.Sqrt(diagonal.X * diagonal.X + diagonal.Y * diagonal.Y), 6);
    }
}
