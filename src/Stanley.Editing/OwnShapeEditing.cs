using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Editing;

/// <summary>
/// Shapes of the user's own on a character built from shapes (docs/shape-characters.md §9.3):
/// an outline imported from an SVG, drawn on the stage or picked from *My shapes* put on a
/// head, a torso or a limb's hand, foot or piece, and the outline a part is kept in My Assets
/// with. Pure functions over a <see cref="ModularBody"/>, built on <see cref="ModuleEditing"/>;
/// the character editor makes each one undo step.
/// </summary>
/// <remarks>
/// Outlines come in as a <see cref="FittedOutline"/>: filling the unit box, with the
/// proportions they were drawn in (<see cref="FittedOutline.Aspect"/>). A head or torso takes
/// those proportions, keeping its size (as much area as before) and colour; a hand or foot
/// keeps its own proportions in its box, as the library's do (§14.4); a piece fills its box.
/// </remarks>
public static class OwnShapeEditing
{
    /// <summary>How close (figure units) a drawn head or torso is put to where it was drawn before the rig's settling stops.</summary>
    private const double PlaceTolerance = 1e-4;

    /// <summary>
    /// Gives the head or torso <paramref name="outline"/>: it keeps as much area as it had and
    /// its colour and place, and takes the outline's proportions. <paramref name="asDrawn"/>
    /// (an import, a drawing) also takes off any turn or flip, so it looks as it was drawn; a
    /// pick from a gallery keeps them, as a library shape's does. The outline's
    /// <see cref="ModuleShape.Source"/> goes in with it.
    /// </summary>
    public static EditResult<ModularBody> SwapOwnShape(ModularBody body, ModulePart part, FittedOutline outline, bool asDrawn)
    {
        if (ModuleEditing.ShapeOf(body, part) is not { } module)
            return Fail(part.IsLimb() ? $"The {part.Noun()} have no outline of their own." : $"This character has no {part.Noun()}.");
        var aspect = outline.Aspect;
        if (!double.IsFinite(aspect) || aspect <= 0)
            return Fail("A shape needs some width and some height.");
        var size = Math.Sqrt(module.Width * module.Height);
        var result = ModuleEditing.SwapShape(body, part, outline.Shape)
            .Then(b => ModuleEditing.Resize(b, part, size * Math.Sqrt(aspect), size / Math.Sqrt(aspect)));
        return asDrawn ? result.Then(b => AsDrawn(b, part)) : result;
    }

    /// <summary>
    /// Gives the arms or legs <paramref name="outline"/> as their hand or foot (kept in its own
    /// proportions, hung from the wrist or ankle) or as one of their pieces (filling the
    /// piece's box), everything else staying. The outline's source goes in with it.
    /// </summary>
    public static EditResult<ModularBody> SwapOwnShape(ModularBody body, ModulePart part, LimbPiece piece, FittedOutline outline)
    {
        if (ModuleEditing.LimbOf(body, part) is null)
            return Fail(part.IsShape() ? $"Only arms and legs have that; a {part.Noun()} doesn't." : $"This character has no {part.Noun()}.");
        var shape = outline.Shape;
        if (piece == LimbPiece.End)
        {
            if (ModuleOutlines.Refit(shape, outline.Aspect, keepProportions: true) is not { } end)
                return Fail("A shape needs some width and some height.");
            shape = end.Shape with { Source = shape.Source, MyAssetsVersion = shape.MyAssetsVersion };
        }
        return ModuleEditing.SwapShape(body, part, piece, shape);
    }

    /// <summary>
    /// The head or torso given <paramref name="figureOutline"/>, an outline drawn on the stage
    /// in figure space (as the body stands at rest, seen from <paramref name="angle"/>): the
    /// part takes its shape and size, loses any turn or flip, and is nudged off its socket so
    /// it sits where it was drawn. For arms or legs it's their hand or foot, as big as drawn.
    /// </summary>
    public static EditResult<ModularBody> PlaceDrawnShape(ModularBody body, ModulePart part, IReadOnlyList<ShapeAnchor> figureOutline, ViewAngle angle = ViewAngle.Front)
    {
        if (figureOutline.Count < 3)
            return Fail("Place at least three points to make a shape.");
        if (part.IsLimb())
        {
            if (ModuleOutlines.Fit(figureOutline, keepProportions: true) is not { } end)
                return Fail("Draw the shape a little bigger.");
            var size = Math.Max(end.Bounds.Width, end.Bounds.Height);
            return ModuleEditing.SwapShape(body, part, LimbPiece.End, end.Shape)
                .Then(b => ModuleEditing.SetEndSize(b, part, size));
        }
        if (ModuleOutlines.Fit(figureOutline) is not { } fitted)
            return Fail("Draw the shape a little bigger.");
        var drawn = fitted.Bounds;
        var result = ModuleEditing.SwapShape(body, part, fitted.Shape)
            .Then(b => ModuleEditing.Resize(b, part, drawn.Width, drawn.Height))
            .Then(b => AsDrawn(b, part));
        if (!result.IsValid)
            return result;

        // Where the rig puts it depends on its new size (a head sits on its chin), and a torso
        // moved low enough can move the ground; a few rounds settle it where it was drawn.
        var placed = result.Value!;
        var target = new Point2D(drawn.X + drawn.Width / 2, drawn.Y + drawn.Height / 2);
        for (var round = 0; round < 4; round++)
        {
            if (CentreOf(placed, part, angle) is not { } centre)
                break;
            var (dx, dy) = (target.X - centre.X, target.Y - centre.Y);
            if (Math.Abs(dx) < PlaceTolerance && Math.Abs(dy) < PlaceTolerance)
                break;
            var nudged = ModuleEditing.Nudge(placed, part, dx, dy);
            if (!nudged.IsValid)
                break;
            placed = nudged.Value!;
        }
        return EditResult<ModularBody>.Success(placed);
    }

    /// <summary>
    /// The outline of the head's or torso's shape (<paramref name="piece"/> null), or a limb's
    /// hand, foot or piece, as it really looks - unflipped and unturned, in its own proportions
    /// (its long side 1, centred, standing at the top: the box <see cref="ProjectModel.Objects.ShapeAsset"/>
    /// keeps it in) - with its hand-placed sockets. Null where the part has no outline of its own
    /// (a round hand, a plain capsule piece).
    /// </summary>
    public static ModuleShape? OutlineToKeep(ModularBody body, ModulePart part, LimbPiece? piece = null)
    {
        FittedOutline? fitted = null;
        if (ModuleEditing.ShapeOf(body, part) is { } module)
            fitted = ModuleOutlines.Refit(module.Shape, module.Width / module.Height, keepProportions: true);
        else if (ModuleEditing.LimbOf(body, part) is { } limb && ModuleEditing.ShapeOf(limb, piece ?? LimbPiece.End) is { } shape)
        {
            fitted = (piece ?? LimbPiece.End) == LimbPiece.End
                ? ModuleOutlines.Fit(shape.Anchors, keepProportions: true, shape.Sockets)
                : ModuleOutlines.Refit(shape, limb.Thickness / Math.Max(1e-6, PieceLength(limb, piece!.Value)), keepProportions: true);
        }
        return fitted?.Shape;
    }

    /// <summary>
    /// Marks the part's shape as an unmodified copy of a kept one (<paramref name="source"/>,
    /// with the My Assets fingerprint it matches) without changing its outline - what keeping
    /// it in My Assets records in the comic.
    /// </summary>
    public static EditResult<ModularBody> Link(ModularBody body, ModulePart part, LimbPiece? piece, string source, string version)
    {
        ModuleShape Linked(ModuleShape s) => s with { Source = source, MyAssetsVersion = version };
        if (ModuleEditing.ShapeOf(body, part) is { } module)
            return ModuleEditing.SwapShape(body, part, Linked(module.Shape));
        if (ModuleEditing.LimbOf(body, part) is { } limb && ModuleEditing.ShapeOf(limb, piece ?? LimbPiece.End) is { } shape)
            return ModuleEditing.SwapShape(body, part, piece ?? LimbPiece.End, Linked(shape));
        return Fail($"The {part.Noun()} have no shape of their own to keep.");
    }

    /// <summary>The centre of the head's or torso's outline where the rig puts it at rest, figure units; null if it isn't drawn.</summary>
    public static Point2D? CentreOf(ModularBody body, ModulePart part, ViewAngle angle = ViewAngle.Front)
    {
        var figure = ModularRig.Build(body, angle);
        var outline = figure.Layers.SelectMany(l => l.Shapes ?? []).FirstOrDefault(s => s.Part == part && part.IsShape());
        if (outline is null)
            return null;
        var b = AnchorRing.CurveBounds(outline.Anchors);
        return new Point2D(b.X + b.Width / 2, b.Y + b.Height / 2);
    }

    private static EditResult<ModularBody> AsDrawn(ModularBody body, ModulePart part)
    {
        var result = ModuleEditing.SetRotation(body, part, 0);
        if (result.IsValid && ModuleEditing.ShapeOf(result.Value!, part)?.FlipX == true)
            result = ModuleEditing.FlipHorizontally(result.Value!, part);
        if (result.IsValid && ModuleEditing.ShapeOf(result.Value!, part)?.FlipY == true)
            result = ModuleEditing.FlipVertically(result.Value!, part);
        return result;
    }

    private static double PieceLength(LimbModule limb, LimbPiece piece)
    {
        var bend = limb.Bend ?? 0.5;
        return limb.Length * (piece == LimbPiece.Upper ? bend : 1 - bend);
    }

    private static EditResult<ModularBody> Then(this EditResult<ModularBody> result, Func<ModularBody, EditResult<ModularBody>> next) =>
        result.IsValid ? next(result.Value!) : result;

    private static EditResult<ModularBody> Fail(string error) => EditResult<ModularBody>.Failure(error);
}
