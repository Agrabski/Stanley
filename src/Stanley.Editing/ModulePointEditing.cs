using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// Edit Points and Edit sockets on a head or torso of a body built from shapes
/// (docs/shape-characters.md §9.2): the part's outline is handed to <see cref="ShapePointEditing"/>
/// as a drawn shape in figure space - flipped, sized, turned and sat where the rig puts it
/// standing at rest (<see cref="ModularRig.FrameOf"/>) - so every gesture is the page's, and
/// what comes back is written into the shape's unit box again. Sockets are shown and dragged
/// the same way, in figure space, and stored in the unit box by <see cref="ModuleEditing.SetSocket"/>.
/// </summary>
/// <remarks>
/// <para>An outline pulled out of its unit box is fitted back into (0,0)-(1,1): the box grows
/// to take it in, Width and Height grow with it, and the hand-placed sockets move with the
/// outline, so the box keeps meaning "the shape's size".</para>
/// <para>Where it ends up: a head sitting on a torso stays exactly where the points were put,
/// its nudge (<see cref="ShapeModule.Offset"/>) taking up whatever its re-derived chin would
/// have moved it by. The part the rest hangs from - the torso, or a head with no torso - is
/// sat on its legs again by the rig, the way a resize does: its nudge would pull it off the
/// legs, which hang from where it would be without one. Either way the refit itself never
/// shows.</para>
/// <para>Left and right are the character's own (left = the box's +x side, the viewer's right
/// from the front); a mirrored part's sockets are shown under the names its flip gives them
/// and stored under the names its own unit box has.</para>
/// </remarks>
public static class ModulePointEditing
{
    private static readonly ElementId OutlineId = ElementId.New();
    private static readonly ShapeStyle OutlineStyle = new(null, null, 0);

    /// <summary>The unit box (0,0)-(1,1) an outline is kept inside.</summary>
    public static Rect2D UnitBox { get; } = new(0, 0, 1, 1);

    /// <summary>The head's or torso's outline as a closed drawn shape in figure space, as the rig places it standing at rest from <paramref name="angle"/>; null without that part.</summary>
    public static ShapeElement? Outline(ModularBody body, ModulePart part, ViewAngle angle = ViewAngle.Front) =>
        ModuleEditing.ShapeOf(body, part) is { } module && ModularRig.FrameOf(body, part, angle) is { } frame ? Outline(module, frame) : null;

    /// <summary><paramref name="module"/>'s outline (flipped) placed through <paramref name="frame"/>.</summary>
    public static ShapeElement Outline(ShapeModule module, ModuleFrame frame) =>
        new(OutlineId, ElementLayer.Foreground, frame.Place(Flipped(module).Anchors), Closed: true, OutlineStyle);

    /// <summary>
    /// The head's or torso's outline replaced by <paramref name="edited"/>: figure-space anchors
    /// in the order <see cref="Outline(ModularBody, ModulePart, ViewAngle)"/> gives them, placed
    /// through <paramref name="frame"/> (the frame the outline was handed out in - for a drag,
    /// the one from its press). Fitted back into the unit box if it left it, sat as the remarks
    /// say. The shape is the character's own from then on (its library source goes).
    /// </summary>
    public static EditResult<ModularBody> SetOutline(ModularBody body, ModulePart part, ViewAngle angle, ModuleFrame frame, IReadOnlyList<ShapeAnchor> edited)
    {
        if (ModuleEditing.ShapeOf(body, part) is not { } module)
            return EditResult<ModularBody>.Failure($"This character has no {part.Noun()}.");
        var (fx, fy) = (module.FlipX == true, module.FlipY == true);
        // Back into the unit box. A point that didn't move keeps the very number it had (the
        // trip through the frame and back would change its last digits), so an edit touches
        // only what it changed in character.json.
        var known = new Dictionary<Point2D, Point2D>();
        foreach (var a in module.Shape.Anchors)
            foreach (var stored in new[] { a.Point, a.InHandle, a.OutHandle })
                known.TryAdd(frame.ToFigure(Flip(stored, fx, fy)), stored);
        Point2D Back(Point2D p) => known.TryGetValue(p, out var stored) ? stored : Flip(frame.ToUnit(p), fx, fy);
        var unit = edited.Select(a => a with { Point = Back(a.Point), InHandle = Back(a.InHandle), OutHandle = Back(a.OutHandle) }).ToList();
        if (fx != fy)
        {
            // A single flip runs the ring the other way round (ModuleSockets.Flipped): run it back.
            unit.Reverse();
            unit = unit.Select(a => a with { InHandle = a.OutHandle, OutHandle = a.InHandle }).ToList();
        }
        var moved = ModuleEditing.SetAnchors(body, part, unit);
        if (!moved.IsValid)
            return moved;

        // Where the box's middle has to end up: where the points were put (a head on a torso),
        // or where the rig sits the outline as edited (the part the rest hangs from).
        const double slack = 1e-9;
        var bounds = AnchorRing.CurveBounds(unit);
        var box = Rect2D.FromEdges(bounds.Left < -slack ? bounds.Left : 0, bounds.Top < -slack ? bounds.Top : 0,
            bounds.Right > 1 + slack ? bounds.Right : 1, bounds.Bottom > 1 + slack ? bounds.Bottom : 1);
        var middle = Flip(new Point2D(box.MidX, box.MidY), fx, fy);
        var sitsOnTorso = part == ModulePart.Head && body.Torso is not null;
        var target = (sitsOnTorso ? frame : ModularRig.FrameOf(moved.Value, part, angle) ?? frame).ToFigure(middle);

        var result = moved.Value;
        if (box != UnitBox)
        {
            Point2D Fit(Point2D p) => new((p.X - box.Left) / box.Width, (p.Y - box.Top) / box.Height);
            var fitted = ModuleEditing.SetAnchors(result, part, unit.Select(a => a with { Point = Fit(a.Point), InHandle = Fit(a.InHandle), OutHandle = Fit(a.OutHandle) }).ToList());
            if (!fitted.IsValid)
                return fitted;
            var resized = ModuleEditing.Resize(fitted.Value, part, Math.Abs(frame.Width) * box.Width, Math.Abs(frame.Height) * box.Height);
            if (!resized.IsValid)
                return resized;
            result = resized.Value;
            foreach (var (name, at) in module.Shape.Sockets ?? [])
            {
                if (double.IsFinite(at.X) && double.IsFinite(at.Y) && ModuleEditing.SetSocket(result, part, name, Fit(at)) is { IsValid: true } placed)
                    result = placed.Value;
            }
        }

        // The nudge takes up the difference (a few rounds: the ground can follow a part that's the lowest thing drawn).
        for (var round = 0; round < 4; round++)
        {
            if (ModularRig.FrameOf(result, part, angle) is not { } now)
                break;
            var (dx, dy) = (target.X - now.Center.X, target.Y - now.Center.Y);
            if (Math.Abs(dx) < 1e-12 && Math.Abs(dy) < 1e-12)
                break;
            if (ModuleEditing.Nudge(result, part, dx, dy) is not { IsValid: true } nudged)
                break;
            result = nudged.Value;
        }
        return EditResult<ModularBody>.Success(result);
    }

    // ---------------------------------------------------------------- sockets

    /// <summary>
    /// The head's or torso's sockets (<see cref="ModuleSockets.All"/>) where they are now, in
    /// figure space standing at rest from <paramref name="angle"/>, named for the character's own
    /// sides; empty without that part. The face is the centre of the face the part actually
    /// carries (a head's is fitted to the whole head).
    /// </summary>
    public static IReadOnlyList<ModuleSocketSpot> Sockets(ModularBody body, ModulePart part, ViewAngle angle = ViewAngle.Front)
    {
        if (ModuleEditing.ShapeOf(body, part) is not { } module || ModularRig.FrameOf(body, part, angle) is not { } frame)
            return [];
        var shape = Flipped(module);
        var derived = ModuleSockets.Derive(shape);
        return ModuleSockets.All.Select(name =>
        {
            var at = name == ModuleSockets.Face ? ModuleSockets.FaceEllipse(shape, upperBand: part != ModulePart.Head).Center : derived[name];
            return new ModuleSocketSpot(name, frame.ToFigure(at), ModuleSockets.IsHandSet(shape, name));
        }).ToList();
    }

    /// <summary>
    /// Socket <paramref name="name"/> (as <see cref="Sockets"/> names it) placed by hand at
    /// <paramref name="figure"/>, figure space through <paramref name="frame"/> (for a drag, the
    /// frame from its press). It stays there whatever the outline does, until reset.
    /// </summary>
    public static EditResult<ModularBody> MoveSocket(ModularBody body, ModulePart part, ModuleFrame frame, string name, Point2D figure)
    {
        if (ModuleEditing.ShapeOf(body, part) is not { } module)
            return EditResult<ModularBody>.Failure($"This character has no {part.Noun()}.");
        var (fx, fy) = (module.FlipX == true, module.FlipY == true);
        return ModuleEditing.SetSocket(body, part, fx ? Mirrored(name) : name, Flip(frame.ToUnit(figure), fx, fy));
    }

    /// <summary>Whether any of the head's or torso's sockets were placed by hand.</summary>
    public static bool HasHandSetSockets(ModularBody body, ModulePart part) =>
        ModuleEditing.ShapeOf(body, part)?.Shape is { } shape && ModuleSockets.All.Any(name => ModuleSockets.IsHandSet(shape, name));

    /// <summary>Every hand-placed socket of the head or torso goes back to the one derived from its outline.</summary>
    public static EditResult<ModularBody> ResetSockets(ModularBody body, ModulePart part)
    {
        if (ModuleEditing.ShapeOf(body, part) is not { } module)
            return EditResult<ModularBody>.Failure($"This character has no {part.Noun()}.");
        if (!HasHandSetSockets(body, part))
            return EditResult<ModularBody>.Failure($"None of the {part.Noun()}'s sockets were placed by hand.");
        var result = EditResult<ModularBody>.Success(body);
        foreach (var name in module.Shape.Sockets!.Keys.ToList())
            result = ModuleEditing.SetSocket(result.Value, part, name, null);
        return result;
    }

    // ---------------------------------------------------------------- helpers

    private static ModuleShape Flipped(ShapeModule module) => ModuleSockets.Flipped(module.Shape, module.FlipX == true, module.FlipY == true);

    /// <summary>Mirrored within the unit box, exactly as <see cref="ModuleSockets.Flipped"/> does it (so a placed point is bit for bit the rig's).</summary>
    private static Point2D Flip(Point2D p, bool x, bool y) => new(x ? 1 - p.X : p.X, y ? 1 - p.Y : p.Y);

    private static string Mirrored(string socket) => socket switch
    {
        ModuleSockets.LeftShoulder => ModuleSockets.RightShoulder,
        ModuleSockets.RightShoulder => ModuleSockets.LeftShoulder,
        ModuleSockets.LeftHip => ModuleSockets.RightHip,
        ModuleSockets.RightHip => ModuleSockets.LeftHip,
        _ => socket
    };
}

/// <summary>One of a head's or torso's sockets as shown on the stage: its name (<see cref="ModuleSockets"/>), where it is in figure space, and whether it was placed by hand.</summary>
public sealed record ModuleSocketSpot(string Name, Point2D At, bool HandSet);
