using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Tests;

/// <summary>Sockets read off a shape's outline (docs/shape-characters.md §4.2), the hand-set escape hatch, flips and turns, and keeping hand-edited bodies buildable.</summary>
public class ModuleSocketsTests
{
    private static void Near(Point2D expected, Point2D actual, double tolerance = 1e-3)
    {
        Assert.True(Math.Abs(expected.X - actual.X) < tolerance && Math.Abs(expected.Y - actual.Y) < tolerance, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void Every_socket_is_derived_for_any_shape()
    {
        foreach (var shape in new[] { ModuleShapes.Ellipse(), ModuleShapes.RoundedRectangle(), ModuleShapes.Triangle() })
            Assert.Equal(ModuleSockets.All.Order(StringComparer.Ordinal), ModuleSockets.Derive(shape).Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_triangles_sockets_are_its_point_its_sides_near_the_top_and_its_base()
    {
        var sockets = ModuleSockets.Derive(ModuleShapes.Triangle());

        Near(new Point2D(0.5, 0), sockets[ModuleSockets.Neck]);
        // The character's own left is on the box's right, as seen from the front.
        Near(new Point2D(0.575, 0.15), sockets[ModuleSockets.LeftShoulder]);
        Near(new Point2D(0.425, 0.15), sockets[ModuleSockets.RightShoulder]);
        Near(new Point2D(0.7, 1), sockets[ModuleSockets.LeftHip]);
        Near(new Point2D(0.3, 1), sockets[ModuleSockets.RightHip]);
    }

    [Fact]
    public void An_ellipses_sockets_lie_on_its_outline()
    {
        var sockets = ModuleSockets.Derive(ModuleShapes.Ellipse());
        var edge = 0.5 * Math.Sqrt(1 - 0.7 * 0.7);

        Near(new Point2D(0.5, 0), sockets[ModuleSockets.Neck], 2e-3);
        Near(new Point2D(0.5 + edge, 0.15), sockets[ModuleSockets.LeftShoulder], 5e-3);
        Near(new Point2D(0.5 - edge, 0.15), sockets[ModuleSockets.RightShoulder], 5e-3);
        Near(new Point2D(0.7, 0.5 + 0.5 * Math.Sqrt(1 - 0.4 * 0.4)), sockets[ModuleSockets.LeftHip], 5e-3);
    }

    [Fact]
    public void The_face_is_the_largest_ellipse_inside_the_upper_two_thirds_or_the_whole_head()
    {
        var circle = ModuleShapes.Ellipse();

        var whole = ModuleSockets.FaceEllipse(circle, upperBand: false);
        var band = ModuleSockets.FaceEllipse(circle, upperBand: true);

        Near(new Point2D(0.5, 0.5), whole.Center, 0.01);
        Assert.InRange(whole.RadiusX, 0.48, 0.5);
        Assert.Equal(0.5, band.Center.X, 2);
        Assert.True(band.Center.Y + band.RadiusY <= 2.0 / 3 + 1e-6);
        Assert.True(band.RadiusY < whole.RadiusY);
        Near(band.Center, ModuleSockets.Derive(circle)[ModuleSockets.Face]);
    }

    [Fact]
    public void A_socket_set_by_hand_wins_and_the_rest_are_still_derived()
    {
        var shape = ModuleShapes.Triangle() with
        {
            Sockets = new SortedDictionary<string, Point2D>(StringComparer.Ordinal) { [ModuleSockets.LeftShoulder] = new(0.9, 0.6) }
        };

        var sockets = ModuleSockets.Derive(shape);

        Assert.Equal(new Point2D(0.9, 0.6), sockets[ModuleSockets.LeftShoulder]);
        Near(new Point2D(0.425, 0.15), sockets[ModuleSockets.RightShoulder]);
        Assert.True(ModuleSockets.IsHandSet(shape, ModuleSockets.LeftShoulder));
        Assert.False(ModuleSockets.IsHandSet(shape, ModuleSockets.RightShoulder));
    }

    [Fact]
    public void An_upside_down_triangles_neck_and_shoulders_are_on_its_wide_top()
    {
        var flipped = ModuleSockets.Flipped(ModuleShapes.Triangle(), x: false, y: true);
        var sockets = ModuleSockets.Derive(flipped);

        Near(new Point2D(0.5, 0), sockets[ModuleSockets.Neck]);
        Near(new Point2D(0.925, 0.15), sockets[ModuleSockets.LeftShoulder]);
        Near(new Point2D(0.075, 0.15), sockets[ModuleSockets.RightShoulder]);
        // Its hips are on the slopes down to the point.
        Near(new Point2D(0.7, 0.6), sockets[ModuleSockets.LeftHip]);
        Near(new Point2D(0.5, 1), ModuleSockets.Chin(flipped));
    }

    [Fact]
    public void A_hand_set_socket_stays_on_its_spot_of_the_outline_through_a_flip()
    {
        var shape = ModuleShapes.Ellipse() with
        {
            Sockets = new SortedDictionary<string, Point2D>(StringComparer.Ordinal) { [ModuleSockets.LeftShoulder] = new(0.9, 0.2), [ModuleSockets.Neck] = new(0.6, 0.1) }
        };

        var mirrored = ModuleSockets.Derive(ModuleSockets.Flipped(shape, x: true, y: false));
        var upsideDown = ModuleSockets.Derive(ModuleSockets.Flipped(shape, x: false, y: true));

        // Mirrored, the spot that was the left shoulder is now on the right side, so it's the right shoulder.
        Near(new Point2D(0.1, 0.2), mirrored[ModuleSockets.RightShoulder]);
        Near(new Point2D(0.4, 0.1), mirrored[ModuleSockets.Neck]);
        Near(new Point2D(0.9, 0.8), upsideDown[ModuleSockets.LeftShoulder]);
        Near(new Point2D(0.6, 0.9), upsideDown[ModuleSockets.Neck]);
    }

    [Fact]
    public void A_flip_keeps_the_outline_running_the_same_way_round_and_comes_back_the_same()
    {
        var triangle = ModuleShapes.Triangle();
        double Area(IReadOnlyList<ShapeAnchor> ring) =>
            ring.Select((a, i) => a.Point.X * ring[(i + 1) % ring.Count].Point.Y - ring[(i + 1) % ring.Count].Point.X * a.Point.Y).Sum() / 2;

        var flipped = ModuleSockets.Flipped(triangle, x: true, y: false);

        Assert.Equal(Math.Sign(Area(triangle.Anchors)), Math.Sign(Area(flipped.Anchors)));
        Assert.Same(flipped, ModuleSockets.Flipped(triangle, x: true, y: false));
        Assert.Same(triangle, ModuleSockets.Flipped(triangle, x: false, y: false));
    }

    [Fact]
    public void A_turned_frame_turns_its_sockets_with_the_outline()
    {
        var frame = new ModuleFrame(new Point2D(1, 2), 0.4, 0.2, 90);

        // The top centre of a box turned a quarter clockwise is on its right.
        Near(new Point2D(1.1, 2), frame.ToFigure(new Point2D(0.5, 0)), 1e-9);
        Near(new Point2D(1, 2.2), frame.ToFigure(new Point2D(1, 0.5)), 1e-9);
        Near(new Point2D(0.3, 0.7), frame.ToUnit(frame.ToFigure(new Point2D(0.3, 0.7))), 1e-9);
        var pinned = frame.Pinned(new Point2D(0.5, 1), new Point2D(5, 5));
        Near(new Point2D(5, 5), pinned.ToFigure(new Point2D(0.5, 1)), 1e-9);
    }

    [Fact]
    public void A_degenerate_shape_still_gives_every_socket()
    {
        var line = new ModuleShape([AnchorRing.Corner(new Point2D(0, 0)), AnchorRing.Corner(new Point2D(1, 1))]);

        var sockets = ModuleSockets.Derive(line);

        Assert.Equal(ModuleSockets.All.Count, sockets.Count);
        Assert.All(sockets.Values, p => Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y)));
    }

    // ---------------------------------------------------------------- normalising hand-edited bodies

    [Fact]
    public void A_valid_body_normalises_to_itself()
    {
        var body = new ModularBody(new ShapeModule(ModuleShapes.Ellipse(), 0.3, 0.3), null, new LimbModule(LimbStyle.Tube, 0.3, 0.05), null);

        Assert.Same(body, body.Normalized());
    }

    [Fact]
    public void Out_of_range_numbers_are_clamped_and_broken_outlines_replaced()
    {
        var broken = new ModuleShape([AnchorRing.Corner(new Point2D(0, 0)), AnchorRing.Corner(new Point2D(double.NaN, 1)), AnchorRing.Corner(new Point2D(1, 1))]);
        var body = new ModularBody(
            new ShapeModule(broken, double.NaN, 50, Rotation: 370, Offset: new Point2D(9, double.PositiveInfinity)),
            new ShapeModule(new ModuleShape([AnchorRing.Corner(new Point2D(0, 0)), AnchorRing.Corner(new Point2D(1, 1))]), -1, 0.4),
            new LimbModule((LimbStyle)42, 99, -3, Taper: 7, Bend: 0, End: broken, EndSize: double.NaN),
            new LimbModule(LimbStyle.Pieces, double.NegativeInfinity, 0.05, Upper: broken, Sides: (LimbSides)9));

        var fixedBody = body.Normalized();

        var head = fixedBody.Head!;
        Assert.Equal(4, head.Shape.Anchors.Count); // the plain ellipse
        Assert.Equal(0.3, head.Width);
        Assert.Equal(ModularBodies.MaxModuleSize, head.Height);
        Assert.Equal(10, head.Rotation!.Value, 9);
        Assert.Null(head.Offset);
        Assert.Equal(ModularBodies.MinModuleSize, fixedBody.Torso!.Width);
        Assert.Equal(4, fixedBody.Torso.Shape.Anchors.Count);
        var arms = fixedBody.Arms!;
        Assert.Equal(LimbStyle.Tube, arms.Style);
        Assert.Equal(ModularBodies.MaxLimbLength, arms.Length);
        Assert.Equal(ModularBodies.MinThickness, arms.Thickness);
        Assert.Equal(ModularBodies.MaxTaper, arms.Taper);
        Assert.Equal(ModularBodies.MinBend, arms.Bend);
        Assert.Null(arms.End);
        Assert.Equal(2 * ModularBodies.MinThickness, arms.EndSize!.Value, 9);
        Assert.Equal(0.35, fixedBody.Legs!.Length);
        Assert.Null(fixedBody.Legs.Upper);
        Assert.Null(fixedBody.Legs.Sides);

        var figure = ModularRig.Build(body);
        Assert.True(double.IsFinite(figure.Extent.Width) && double.IsFinite(figure.Extent.Height));
    }

    [Fact]
    public void Hand_set_sockets_outside_the_box_or_unknown_are_tidied()
    {
        var shape = ModuleShapes.Ellipse() with
        {
            Sockets = new SortedDictionary<string, Point2D>(StringComparer.Ordinal)
            {
                [ModuleSockets.Neck] = new(0.5, -2),
                ["tail"] = new(0.5, 0.5),
                [ModuleSockets.Face] = new(double.NaN, 0.5),
            }
        };

        var tidy = new ShapeModule(shape, 0.3, 0.3).Normalized().Shape.Sockets!;

        Assert.Equal([ModuleSockets.Neck], tidy.Keys);
        Assert.Equal(new Point2D(0.5, 0), tidy[ModuleSockets.Neck]);
    }
}
