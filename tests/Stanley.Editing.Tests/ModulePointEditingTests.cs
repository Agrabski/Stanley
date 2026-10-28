using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Xunit;

namespace Stanley.Editing.Tests;

/// <summary>Edit Points and Edit sockets on a head or torso (docs/shape-characters.md §9.2): <see cref="ModulePointEditing"/>.</summary>
public sealed class ModulePointEditingTests
{
    private static ModularBody Body(ShapeModule? head = null, ShapeModule? torso = null, bool noTorso = false) => new(
        Head: head ?? new ShapeModule(ModuleShapes.Ellipse() with { Source = "library:shapes/head/organic/egg" }, 0.3, 0.28),
        Torso: noTorso ? null : torso ?? new ShapeModule(ModuleShapes.RoundedRectangle(0.2) with { Source = "library:shapes/torso/geometric/box" }, 0.32, 0.34),
        Arms: new LimbModule(LimbStyle.Tube, 0.26, 0.06),
        Legs: new LimbModule(LimbStyle.Tube, 0.3, 0.08));

    private static ModularBody Ok(Stanley.Editing.Abstractions.EditResult<ModularBody> result)
    {
        Assert.True(result.IsValid, result.Error);
        return result.Value;
    }

    private static void Near(Point2D expected, Point2D actual, double tolerance = 1e-9)
    {
        Assert.True(Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance, $"expected {expected}, got {actual}");
    }

    private static void SameOutline(IReadOnlyList<ShapeAnchor> expected, IReadOnlyList<ShapeAnchor> actual, double tolerance = 1e-9)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Near(expected[i].Point, actual[i].Point, tolerance);
            Near(expected[i].InHandle, actual[i].InHandle, tolerance);
            Near(expected[i].OutHandle, actual[i].OutHandle, tolerance);
            Assert.Equal(expected[i].HandleKind, actual[i].HandleKind);
        }
    }

    /// <summary>The outline as the rig draws it standing at rest - what the stage shows.</summary>
    private static IReadOnlyList<ShapeAnchor> Drawn(ModularBody body, ModulePart part, ViewAngle angle = ViewAngle.Front) =>
        ModularRig.Build(body, angle).Layers.SelectMany(l => l.Shapes ?? []).Single(s => s.Part == part && s.Anchors.Count >= 3).Anchors;

    public static TheoryData<string> Parts => new() { "head", "torso", "lone head", "turned head", "mirrored torso", "upside-down head" };

    private static (ModularBody Body, ModulePart Part) Case(string name) => name switch
    {
        "head" => (Body(), ModulePart.Head),
        "torso" => (Body(), ModulePart.Torso),
        "lone head" => (Body(noTorso: true), ModulePart.Head),
        "turned head" => (Body(head: new ShapeModule(ModuleShapes.RoundedRectangle(0.3), 0.3, 0.26, Rotation: 25, FlipX: true)), ModulePart.Head),
        "mirrored torso" => (Body(torso: new ShapeModule(ModuleShapes.Triangle(), 0.4, 0.35, Rotation: -10, FlipX: true, Offset: new Point2D(0.02, 0))), ModulePart.Torso),
        _ => (Body(head: new ShapeModule(ModuleShapes.Triangle(), 0.3, 0.3, FlipY: true)), ModulePart.Head),
    };

    [Theory]
    [MemberData(nameof(Parts))]
    public void The_outline_is_where_the_rig_draws_it(string name)
    {
        var (body, part) = Case(name);
        foreach (var angle in new[] { ViewAngle.Front, ViewAngle.Profile })
            SameOutline(Drawn(body, part, angle), ModulePointEditing.Outline(body, part, angle)!.Anchors);
    }

    [Theory]
    [MemberData(nameof(Parts))]
    public void Handing_the_outline_back_unchanged_changes_nothing_but_the_library_source(string name)
    {
        var (body, part) = Case(name);
        var frame = ModularRig.FrameOf(body, part)!.Value;
        var before = ModuleEditing.ShapeOf(body, part)!;

        var after = ModuleEditing.ShapeOf(Ok(ModulePointEditing.SetOutline(body, part, ViewAngle.Front, frame, ModulePointEditing.Outline(body, part)!.Anchors)), part)!;

        Assert.Equal(before.Shape.Anchors, after.Shape.Anchors); // the very same numbers, so a save diffs nothing
        Assert.Equal((before.Width, before.Height, before.Offset), (after.Width, after.Height, after.Offset));
        Assert.Null(after.Shape.Source);
    }

    [Theory]
    [MemberData(nameof(Parts))]
    public void A_point_pulled_out_of_the_box_refits_it_and_the_outline_stays_where_it_was_put(string name)
    {
        var (body, part) = Case(name);
        var frame = ModularRig.FrameOf(body, part)!.Value;
        var outline = ModulePointEditing.Outline(body, part)!;
        // The first point, pulled well out: up and to the side.
        var p = outline.Anchors[0].Point;
        var moved = ShapePointEditing.MovePoint(outline, 0, new Point2D(p.X + 0.08, p.Y - 0.1)).Value;

        var edited = Ok(ModulePointEditing.SetOutline(body, part, ViewAngle.Front, frame, moved.Anchors));

        var module = ModuleEditing.ShapeOf(edited, part)!;
        var bounds = AnchorRing.CurveBounds(module.Shape.Anchors);
        Assert.True(bounds.Left >= -1e-9 && bounds.Top >= -1e-9 && bounds.Right <= 1 + 1e-9 && bounds.Bottom <= 1 + 1e-9, $"left the box: {bounds}");
        var old = ModuleEditing.ShapeOf(body, part)!;
        Assert.True(module.Width * module.Height > old.Width * old.Height, "the box grew to take the point in");
        // A head on a torso is exactly where its points were put. The part the rest hangs from
        // keeps its legs: it may sit differently on them, but its own shape is what was drawn.
        var now = ModulePointEditing.Outline(edited, part)!.Anchors;
        if (part == ModulePart.Head && body.Torso is not null)
            SameOutline(moved.Anchors, now);
        else
        {
            var (dx, dy) = (now[1].Point.X - moved.Anchors[1].Point.X, now[1].Point.Y - moved.Anchors[1].Point.Y);
            SameOutline(moved.Anchors.Select(a => a with
            {
                Point = new(a.Point.X + dx, a.Point.Y + dy), InHandle = new(a.InHandle.X + dx, a.InHandle.Y + dy), OutHandle = new(a.OutHandle.X + dx, a.OutHandle.Y + dy)
            }).ToList(), now);
        }
    }

    [Fact]
    public void Pulling_a_torsos_top_up_keeps_it_on_its_legs_exactly()
    {
        var body = Body();
        var frame = ModularRig.FrameOf(body, ModulePart.Torso)!.Value;
        var outline = ModulePointEditing.Outline(body, ModulePart.Torso)!;
        var moved = outline;
        // Both top corners up: the hips don't move, so nothing else does.
        foreach (var i in new[] { 0, 1 })
            moved = ShapePointEditing.MovePoint(moved, i, new Point2D(moved.Anchors[i].Point.X, moved.Anchors[i].Point.Y - 0.05)).Value;

        var edited = Ok(ModulePointEditing.SetOutline(body, ModulePart.Torso, ViewAngle.Front, frame, moved.Anchors));

        SameOutline(moved.Anchors, ModulePointEditing.Outline(edited, ModulePart.Torso)!.Anchors);
        Assert.Null(edited.Torso!.Offset);
        Assert.True(edited.Torso.Height > body.Torso!.Height);
    }

    [Fact]
    public void An_added_point_keeps_the_outline()
    {
        var body = Body();
        var frame = ModularRig.FrameOf(body, ModulePart.Head)!.Value;
        var (withPoint, index) = ShapePointEditing.InsertPoint(ModulePointEditing.Outline(body, ModulePart.Head)!, 1, 0.5).Value;

        var edited = Ok(ModulePointEditing.SetOutline(body, ModulePart.Head, ViewAngle.Front, frame, withPoint.Anchors));

        Assert.Equal(5, edited.Head!.Shape.Anchors.Count);
        SameOutline(withPoint.Anchors, ModulePointEditing.Outline(edited, ModulePart.Head)!.Anchors);
        Assert.Equal(2, index);
    }

    [Fact]
    public void A_mirrored_torsos_sockets_are_named_for_the_characters_own_sides_and_stored_for_its_box()
    {
        var torso = new ShapeModule(ModuleShapes.Triangle(), 0.4, 0.35, FlipX: true, FlipY: true);
        var body = Body(torso: torso);
        var frame = ModularRig.FrameOf(body, ModulePart.Torso)!.Value;
        var shown = ModulePointEditing.Sockets(body, ModulePart.Torso);
        Assert.Equal(ModuleSockets.All, shown.Select(s => s.Name));
        Assert.All(shown, s => Assert.False(s.HandSet));
        // The character's own left shoulder is on the viewer's right from the front: +x.
        Assert.True(shown.Single(s => s.Name == ModuleSockets.LeftShoulder).At.X > shown.Single(s => s.Name == ModuleSockets.RightShoulder).At.X);

        var to = new Point2D(frame.Center.X + 0.15, frame.Center.Y - 0.1);
        var edited = Ok(ModulePointEditing.MoveSocket(body, ModulePart.Torso, frame, ModuleSockets.LeftShoulder, to));

        // Stored in the unflipped box, under the name it has there.
        Assert.True(edited.Torso!.Shape.Sockets!.ContainsKey(ModuleSockets.RightShoulder));
        var spot = ModulePointEditing.Sockets(edited, ModulePart.Torso).Single(s => s.Name == ModuleSockets.LeftShoulder);
        Assert.True(spot.HandSet);
        Near(to, spot.At, 1e-6); // the torso sits on its legs, which a shoulder doesn't move
        Assert.True(ModulePointEditing.HasHandSetSockets(edited, ModulePart.Torso));

        var reset = Ok(ModulePointEditing.ResetSockets(edited, ModulePart.Torso));
        Assert.Null(reset.Torso!.Shape.Sockets);
        Assert.False(ModulePointEditing.HasHandSetSockets(reset, ModulePart.Torso));
        Assert.False(ModulePointEditing.ResetSockets(reset, ModulePart.Torso).IsValid);
    }

    [Fact]
    public void Hand_set_sockets_move_with_a_refitted_outline()
    {
        var body = Ok(ModuleEditing.SetSocket(Body(), ModulePart.Torso, ModuleSockets.Neck, new Point2D(0.5, 0.1)));
        var before = ModulePointEditing.Sockets(body, ModulePart.Torso).Single(s => s.Name == ModuleSockets.Neck).At;
        var frame = ModularRig.FrameOf(body, ModulePart.Torso)!.Value;
        var outline = ModulePointEditing.Outline(body, ModulePart.Torso)!;
        // The right side pulled out (a belly): the box widens.
        var moved = ShapePointEditing.MovePoint(outline, 3, new Point2D(outline.Anchors[3].Point.X + 0.05, outline.Anchors[3].Point.Y)).Value;

        var edited = Ok(ModulePointEditing.SetOutline(body, ModulePart.Torso, ViewAngle.Front, frame, moved.Anchors));

        Assert.NotEqual(0.5, edited.Torso!.Shape.Sockets![ModuleSockets.Neck].X);
        var torsoShift = ModulePointEditing.Outline(edited, ModulePart.Torso)!.Anchors[0].Point;
        var (dx, dy) = (torsoShift.X - moved.Anchors[0].Point.X, torsoShift.Y - moved.Anchors[0].Point.Y);
        Near(new Point2D(before.X + dx, before.Y + dy), ModulePointEditing.Sockets(edited, ModulePart.Torso).Single(s => s.Name == ModuleSockets.Neck).At);
    }
}
