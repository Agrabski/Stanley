using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// Keeps a body built from shapes buildable whatever a hand-edited <c>character.json</c>
/// says (docs/shape-characters.md §4): sizes, lengths and the rest pulled into range, and an
/// outline that can't be drawn (fewer than three anchors, a number that isn't finite, no
/// area) swapped for a plain one. <see cref="ModularRig"/> builds from the normalised body,
/// as <see cref="BodyRig"/> does from <see cref="BodyShape.Normalized"/>.
/// </summary>
public static class ModularBodies
{
    /// <summary>The smallest and largest a head or torso may be drawn, either way (figure units).</summary>
    public const double MinModuleSize = 0.02;
    public const double MaxModuleSize = 2.0;

    /// <summary>How far a module may be nudged off its socket, either way (figure units).</summary>
    public const double MaxOffset = 1.0;

    /// <summary>A limb's length, root to end (figure units).</summary>
    public const double MinLimbLength = 0.02;
    public const double MaxLimbLength = 1.5;

    /// <summary>A limb's thickness at its root (figure units).</summary>
    public const double MinThickness = 0.005;
    public const double MaxThickness = 0.5;

    public const double MaxTaper = 0.9;
    public const double MinBend = 0.2;
    public const double MaxBend = 0.8;

    /// <summary>A hand or foot shape's height (figure units).</summary>
    public const double MinEndSize = 0.005;
    public const double MaxEndSize = 0.6;

    /// <summary>
    /// The body with every number in range and every outline drawable. A part whose outline
    /// is broken keeps its size, turn and colour on a plain ellipse (a head or torso) or its
    /// limb style's own shape (an end or a piece); the same instance comes back when nothing
    /// needed fixing.
    /// </summary>
    public static ModularBody Normalized(this ModularBody body)
    {
        var fixedBody = body with
        {
            Head = body.Head?.Normalized(),
            Torso = body.Torso?.Normalized(),
            Arms = body.Arms?.Normalized(),
            Legs = body.Legs?.Normalized(),
        };
        return fixedBody == body ? body : fixedBody;
    }

    /// <summary>A head or torso with its size, turn and nudge in range and a drawable outline.</summary>
    public static ShapeModule Normalized(this ShapeModule module)
    {
        var shape = module.Shape is { } s && Usable(s) is { } usable ? usable : ModuleShapes.Ellipse();
        var rotation = module.Rotation is { } r ? (double.IsFinite(r) ? Math.IEEERemainder(r, 360) : null) : (double?)null;
        var offset = module.Offset is { } o
            ? (double.IsFinite(o.X) && double.IsFinite(o.Y) ? new Point2D(Math.Clamp(o.X, -MaxOffset, MaxOffset), Math.Clamp(o.Y, -MaxOffset, MaxOffset)) : null)
            : (Point2D?)null;
        var fixedModule = module with
        {
            Shape = shape,
            Width = Clamp(module.Width, MinModuleSize, MaxModuleSize, 0.3),
            Height = Clamp(module.Height, MinModuleSize, MaxModuleSize, 0.3),
            Rotation = rotation,
            Offset = offset,
        };
        return fixedModule == module ? module : fixedModule;
    }

    /// <summary>A limb with its length, thickness, taper, bend and end size in range, and any broken end or piece outline dropped for the style's own.</summary>
    public static LimbModule Normalized(this LimbModule limb)
    {
        var fixedLimb = limb with
        {
            Style = Enum.IsDefined(limb.Style) ? limb.Style : LimbStyle.Tube,
            Length = Clamp(limb.Length, MinLimbLength, MaxLimbLength, 0.35),
            Thickness = Clamp(limb.Thickness, MinThickness, MaxThickness, 0.05),
            Taper = limb.Taper is { } t ? Clamp(t, 0, MaxTaper, 0.5) : null,
            Bend = limb.Bend is { } b ? Clamp(b, MinBend, MaxBend, 0.5) : null,
            EndSize = limb.EndSize is { } e ? Clamp(e, MinEndSize, MaxEndSize, 2 * Clamp(limb.Thickness, MinThickness, MaxThickness, 0.05)) : null,
            End = limb.End is { } end ? Usable(end) : null,
            Upper = limb.Upper is { } upper ? Usable(upper) : null,
            Lower = limb.Lower is { } lower ? Usable(lower) : null,
            Sides = limb.Sides is { } sides && !Enum.IsDefined(sides) ? null : limb.Sides,
        };
        return fixedLimb == limb ? limb : fixedLimb;
    }

    /// <summary>The shape, if its outline can be drawn: its hand-set sockets cut down to the known, finite ones and kept to the unit box. Null for a broken outline.</summary>
    private static ModuleShape? Usable(ModuleShape shape)
    {
        if (!ModuleSockets.IsUsable(shape.Anchors))
            return null;
        var bounds = AnchorRing.CurveBounds(shape.Anchors);
        if (!(bounds.Width > 1e-9 && bounds.Height > 1e-9))
            return null;
        if (shape.Sockets is not { } sockets)
            return shape;
        var kept = new SortedDictionary<string, Point2D>(StringComparer.Ordinal);
        foreach (var (name, p) in sockets)
            if (ModuleSockets.All.Contains(name) && double.IsFinite(p.X) && double.IsFinite(p.Y))
                kept[name] = new Point2D(Math.Clamp(p.X, 0, 1), Math.Clamp(p.Y, 0, 1));
        var same = kept.Count == sockets.Count && kept.All(k => sockets.TryGetValue(k.Key, out var p) && p == k.Value);
        return same ? shape : shape with { Sockets = kept.Count == 0 ? null : kept };
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
