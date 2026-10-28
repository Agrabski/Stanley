using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>
/// Which module of a body built from shapes (docs/shape-characters.md) drew what, for the
/// character editor's stage: the module under a point, the outline of one module (its
/// selection highlight and handles), and where a missing part would go back on (its faint +).
/// All in figure space. A head or torso's own outline carries its part
/// (<see cref="FigureShape.Part"/>); a limb's tubes and round ends are known by the layer
/// they're painted in - arms in the arm layers, legs in every other one.
/// </summary>
public static class FigureModules
{
    /// <summary>The module painted topmost at <paramref name="figurePoint"/>, or null (off the body).</summary>
    public static ModulePart? At(BodyFigure figure, Point2D figurePoint)
    {
        var (x, y) = ((float)figurePoint.X, (float)figurePoint.Y);
        for (var l = figure.Layers.Count - 1; l >= 0; l--)
        {
            var layer = figure.Layers[l];
            var pieces = Pieces(layer).ToList();
            for (var i = pieces.Count - 1; i >= 0; i--)
            {
                using var path = pieces[i].Path();
                if (path.Contains(x, y))
                    return pieces[i].Part;
            }
        }
        return null;
    }

    /// <summary>Everything <paramref name="part"/> paints, as one figure-space outline (empty if it paints nothing). The caller disposes it.</summary>
    public static SKPath OutlineOf(BodyFigure figure, ModulePart part)
    {
        var all = FigureGeometry.Empty();
        foreach (var piece in figure.Layers.SelectMany(Pieces).Where(p => p.Part == part))
            all = FigureGeometry.Union(all, piece.Path());
        return all;
    }

    /// <summary><see cref="OutlineOf(BodyFigure, ModulePart)"/> where <paramref name="placement"/> stands the figure on the page. The caller disposes it.</summary>
    public static SKPath OutlineOf(BodyFigure figure, ModulePart part, Stanley.ProjectModel.Issues.CharacterPlacement placement)
    {
        using var outline = OutlineOf(figure, part);
        return FigureGeometry.Transformed(outline, FigureRenderer.ToPage(placement));
    }

    /// <summary>The box around what <paramref name="part"/> paints, in figure space, or null if it paints nothing.</summary>
    public static Rect2D? BoundsOf(BodyFigure figure, ModulePart part)
    {
        using var outline = OutlineOf(figure, part);
        if (outline.IsEmpty)
            return null;
        var b = outline.TightBounds;
        return Rect2D.FromEdges(b.Left, b.Top, b.Right, b.Bottom);
    }

    /// <summary>
    /// Where each missing part of <paramref name="body"/> would attach on <paramref name="figure"/>
    /// (its build): a head above the neck, a torso under the head, an arm at each empty
    /// shoulder, a leg at each empty hip - one + each, a click to put the part back. A limb on
    /// one side only gets its other side's.
    /// </summary>
    public static IReadOnlyList<ModuleGhost> Ghosts(ModularBody body, BodyFigure figure)
    {
        var ghosts = new List<ModuleGhost>();
        Point2D Joint(HumanoidBone bone) => figure.Layout.Bones.First(b => b.Bone == bone).Position;
        if (body.Head is null)
        {
            var neck = Joint(HumanoidBone.Head);
            ghosts.Add(new(ModulePart.Head, null, new Point2D(neck.X, neck.Y - 0.08)));
        }
        if (body.Torso is null && body.Head is not null)
            ghosts.Add(new(ModulePart.Torso, null, Joint(HumanoidBone.Hips)));
        void Limb(LimbModule? limb, ModulePart part, HumanoidBone left, HumanoidBone right, double outwards, double drop)
        {
            foreach (var (side, bone) in new[] { (LimbSide.Left, left), (LimbSide.Right, right) })
            {
                if (limb is not null && limb.Has(side))
                    continue;
                var at = Joint(bone);
                var outward = figure.Angle == ViewAngle.Profile ? 0 : side == LimbSide.Left ? outwards : -outwards;
                ghosts.Add(new(part, limb is null ? null : side, new Point2D(at.X + outward, at.Y + drop)));
            }
        }
        // Arms just outside the shoulders; legs just under the hips, on the body's bottom edge.
        Limb(body.Arms, ModulePart.Arms, HumanoidBone.LeftUpperArm, HumanoidBone.RightUpperArm, 0.04, 0.02);
        Limb(body.Legs, ModulePart.Legs, HumanoidBone.LeftUpperLeg, HumanoidBone.RightUpperLeg, 0, 0.025);
        return ghosts;
    }

    /// <summary>One piece a layer paints, and the module it belongs to.</summary>
    private readonly record struct Piece(ModulePart Part, Func<SKPath> Path);

    private static IEnumerable<Piece> Pieces(FigureLayer layer)
    {
        // A layer's tubes and round ends belong to the limb whose layer it is.
        var limb = layer.Kind is FigureLayerKind.LeftArm or FigureLayerKind.RightArm or FigureLayerKind.FarArm or FigureLayerKind.NearArm
            ? ModulePart.Arms
            : ModulePart.Legs;
        foreach (var capsule in layer.Capsules)
            yield return new(limb, () => FigureGeometry.Capsule(capsule));
        foreach (var ellipse in layer.Ellipses)
            yield return new(limb, () => FigureGeometry.Oval(ellipse));
        foreach (var shape in layer.Shapes ?? [])
        {
            if (shape.Part is { } part && shape.Anchors.Count >= 2)
                yield return new(part, () => AnchorRingPath.ToSkPath(shape.Anchors));
        }
    }
}

/// <summary>Where a missing part would go back on (figure space): the whole part, or one side of a pair (<paramref name="Side"/>) that has only the other.</summary>
public sealed record ModuleGhost(ModulePart Part, LimbSide? Side, Point2D At);
