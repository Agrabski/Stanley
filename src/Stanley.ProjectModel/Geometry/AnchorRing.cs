namespace Stanley.ProjectModel.Geometry;

/// <summary>
/// Pure bezier-ring math shared by every anchor-ring shape (<see cref="PanelShape"/>,
/// <see cref="Bubbles.BubbleShape"/>): an ordered ring of anchors, each edge a cubic
/// bezier segment from one anchor's <see cref="ShapeAnchor.OutHandle"/> to the next
/// anchor's <see cref="ShapeAnchor.InHandle"/>. Kept as free functions over
/// <c>IReadOnlyList&lt;ShapeAnchor&gt;</c> rather than methods on either shape type, so
/// panels and bubbles share the math without sharing a base type they'd otherwise have
/// no other reason to have in common.
/// </summary>
public static class AnchorRing
{
    /// <summary>Evaluates a point on the ring for t in [0, 1), wrapping around.</summary>
    public static Point2D PointAt(IReadOnlyList<ShapeAnchor> anchors, double t)
    {
        var n = anchors.Count;
        var scaled = Wrap01(t) * n;
        var segment = (int)Math.Floor(scaled) % n;
        var local = scaled - Math.Floor(scaled);
        var p0 = anchors[segment];
        var p1 = anchors[(segment + 1) % n];
        return CubicPoint(p0.Point, p0.OutHandle, p1.InHandle, p1.Point, local);
    }

    /// <summary>Brute-force nearest-point search: the t whose <see cref="PointAt"/> is closest to <paramref name="target"/>.</summary>
    public static double NearestT(IReadOnlyList<ShapeAnchor> anchors, Point2D target, int samples = 200)
    {
        var bestT = 0.0;
        var bestDistSq = double.MaxValue;
        for (var i = 0; i < samples; i++)
        {
            var t = (double)i / samples;
            var p = PointAt(anchors, t);
            var dx = p.X - target.X;
            var dy = p.Y - target.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestT = t;
            }
        }
        return bestT;
    }

    /// <summary>
    /// The ring's own anchor points whose parametric position lies strictly between
    /// <paramref name="fromT"/> and <paramref name="toT"/>, walking forward (wrapping if
    /// <paramref name="toT"/> is "before" <paramref name="fromT"/>), ordered along that walk.
    /// </summary>
    public static List<Point2D> AnchorsBetween(IReadOnlyList<ShapeAnchor> anchors, double fromT, double toT)
    {
        var n = anchors.Count;
        var from = Wrap01(fromT) * n;
        var to = Wrap01(toT) * n;
        if (to <= from)
            to += n;

        var found = new List<(double Pos, Point2D Point)>();
        for (var i = 0; i < n; i++)
        {
            var pos = i <= from ? i + n : i;
            if (pos < to)
                found.Add((pos, anchors[i].Point));
        }
        found.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        return found.ConvertAll(f => f.Point);
    }

    /// <summary>The smallest axis-aligned rectangle containing every anchor point (handles may extend beyond it).</summary>
    public static Rect2D BoundingBox(IReadOnlyList<ShapeAnchor> anchors)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var a in anchors)
        {
            minX = Math.Min(minX, a.Point.X);
            minY = Math.Min(minY, a.Point.Y);
            maxX = Math.Max(maxX, a.Point.X);
            maxY = Math.Max(maxY, a.Point.Y);
        }
        return Rect2D.FromEdges(minX, minY, maxX, maxY);
    }

    /// <summary>Affine-maps every anchor point and handle from <paramref name="from"/> to <paramref name="to"/>, returning a new ring.</summary>
    public static IReadOnlyList<ShapeAnchor> Rescale(IReadOnlyList<ShapeAnchor> anchors, Rect2D from, Rect2D to)
    {
        if (from.Width == 0 || from.Height == 0)
            return anchors;

        var sx = to.Width / from.Width;
        var sy = to.Height / from.Height;

        Point2D Map(Point2D p) => new(
            to.Left + (p.X - from.Left) * sx,
            to.Top + (p.Y - from.Top) * sy);

        return anchors
            .Select(a => a with { Point = Map(a.Point), InHandle = Map(a.InHandle), OutHandle = Map(a.OutHandle) })
            .ToList();
    }

    private static double Wrap01(double t)
    {
        t %= 1.0;
        return t < 0 ? t + 1.0 : t;
    }

    private static Point2D CubicPoint(Point2D p0, Point2D c0, Point2D c1, Point2D p1, double t)
    {
        var mt = 1 - t;
        var a = mt * mt * mt;
        var b = 3 * mt * mt * t;
        var c = 3 * mt * t * t;
        var d = t * t * t;
        return new Point2D(
            a * p0.X + b * c0.X + c * c1.X + d * p1.X,
            a * p0.Y + b * c0.Y + c * c1.Y + d * p1.Y);
    }
}
