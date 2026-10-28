using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Characters;

/// <summary>An outline fitted into a module's unit box, and the box it was fitted from (in the outline's own units).</summary>
/// <param name="Bounds">The outline's curve bounds before fitting: its size as drawn (figure units for one drawn on the stage, the file's units for an imported one).</param>
public sealed record FittedOutline(ModuleShape Shape, Rect2D Bounds)
{
    /// <summary>The outline's width over its height as drawn - what a head's or torso's Width and Height take back, since the unit box loses it.</summary>
    public double Aspect => Bounds.Width / Bounds.Height;
}

/// <summary>
/// Outlines of the user's own - drawn on the stage, imported from an SVG, kept in My Assets
/// (docs/shape-characters.md §9.3) - turned into a <see cref="ModuleShape"/>'s unit box, by the
/// box conventions the rig honours (§14.4): a head, a torso or a limb piece fills the box (the
/// module's size gives the proportions back); a hand or foot keeps its own, its long side 1,
/// centred across, its top at 0.
/// </summary>
public static class ModuleOutlines
{
    /// <summary>Coordinates in the unit box are rounded to this, so files stay readable and a re-save is byte-identical.</summary>
    private const int Decimals = 5;

    /// <summary>
    /// <paramref name="outline"/> (a closed ring in any units, y down) fitted into the unit box:
    /// stretched to fill it, or - <paramref name="keepProportions"/> - its long side 1, centred
    /// across and standing at the top. The ring is turned to run clockwise on screen, like the
    /// library's shapes. <paramref name="sockets"/> (hand-placed, in the outline's units) go
    /// along. Null if the outline has fewer than three points, or no width or height.
    /// </summary>
    public static FittedOutline? Fit(IReadOnlyList<ShapeAnchor> outline, bool keepProportions = false, IReadOnlyDictionary<string, Point2D>? sockets = null)
    {
        if (outline.Count < 3 || outline.Any(a => !Finite(a.Point) || !Finite(a.InHandle) || !Finite(a.OutHandle)))
            return null;
        var bounds = AnchorRing.CurveBounds(outline);
        if (!(bounds.Width > 1e-9) || !(bounds.Height > 1e-9))
            return null;
        var target = new Rect2D(0, 0, 1, 1);
        if (keepProportions)
        {
            var scale = 1 / Math.Max(bounds.Width, bounds.Height);
            target = new Rect2D(0.5 - bounds.Width * scale / 2, 0, bounds.Width * scale, bounds.Height * scale);
        }
        var sx = target.Width / bounds.Width;
        var sy = target.Height / bounds.Height;
        Point2D Map(Point2D p) => new(Math.Round(target.Left + (p.X - bounds.Left) * sx, Decimals), Math.Round(target.Top + (p.Y - bounds.Top) * sy, Decimals));

        var anchors = Clockwise(outline).Select(a => a with { Point = Map(a.Point), InHandle = Map(a.InHandle), OutHandle = Map(a.OutHandle) }).ToList();
        SortedDictionary<string, Point2D>? fitted = null;
        if (sockets is { Count: > 0 })
        {
            fitted = new SortedDictionary<string, Point2D>(StringComparer.Ordinal);
            foreach (var (name, p) in sockets)
                fitted[name] = Map(p);
        }
        return new FittedOutline(new ModuleShape(anchors, fitted), bounds);
    }

    /// <summary>
    /// A shape in a module's unit box (filling it, <paramref name="aspect"/> its width over its
    /// height) as it really looks, refitted by <see cref="Fit"/>: how a part's outline is put
    /// into another kind of box (a head's into a hand's) or kept in My Assets.
    /// </summary>
    public static FittedOutline? Refit(ModuleShape shape, double aspect, bool keepProportions)
    {
        if (!double.IsFinite(aspect) || aspect <= 0)
            return null;
        var real = new Rect2D(0, 0, aspect, 1);
        var anchors = AnchorRing.Rescale(shape.Anchors, AnchorRing.CurveBounds(shape.Anchors), real);
        var sockets = shape.Sockets?.ToDictionary(s => s.Key, s => new Point2D(s.Value.X * aspect, s.Value.Y));
        return Fit(anchors, keepProportions, sockets);
    }

    /// <summary>Twice the area the ring encloses, positive when it runs clockwise on screen (y down). Each edge is sampled, so bulging curves count.</summary>
    public static double SignedArea(IReadOnlyList<ShapeAnchor> ring)
    {
        const int samples = 8;
        var sum = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            var previous = a.Point;
            for (var s = 1; s <= samples; s++)
            {
                var p = Cubic(a.Point, a.OutHandle, b.InHandle, b.Point, (double)s / samples);
                sum += previous.X * p.Y - p.X * previous.Y;
                previous = p;
            }
        }
        return sum;
    }

    /// <summary><paramref name="ring"/> running clockwise on screen: as it is, or reversed (each point's handles swapped so the curves stay the same).</summary>
    public static IReadOnlyList<ShapeAnchor> Clockwise(IReadOnlyList<ShapeAnchor> ring)
    {
        if (SignedArea(ring) >= 0)
            return ring;
        return ring.Reverse().Select(a => a with { InHandle = a.OutHandle, OutHandle = a.InHandle }).ToList();
    }

    private static Point2D Cubic(Point2D p0, Point2D c0, Point2D c1, Point2D p1, double t)
    {
        var u = 1 - t;
        return new Point2D(
            u * u * u * p0.X + 3 * u * u * t * c0.X + 3 * u * t * t * c1.X + t * t * t * p1.X,
            u * u * u * p0.Y + 3 * u * u * t * c0.Y + 3 * u * t * t * c1.Y + t * t * t * p1.Y);
    }

    private static bool Finite(Point2D p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
}
