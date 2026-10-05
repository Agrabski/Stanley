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
    /// The t where the ray from <paramref name="centre"/> through <paramref name="towards"/>
    /// crosses the ring: "the same direction from the middle" on another outline. A t on its
    /// own is only a fraction along one particular ring (an oval's four anchors count from the
    /// top, a Shout star's twenty from the right), so it means a different spot on a different
    /// shape; a direction means the same on any, which makes this what carries a point across a
    /// change of outline. Where the ray crosses more than once (a ring that bulges back past the
    /// centre's line of sight) the crossing nearest <paramref name="towards"/> wins; a ring it
    /// never crosses (the centre outside it) falls back to <see cref="NearestT"/>.
    /// </summary>
    public static double TowardsT(IReadOnlyList<ShapeAnchor> anchors, Point2D centre, Point2D towards, int samples = 400)
    {
        var aim = Math.Atan2(towards.Y - centre.Y, towards.X - centre.X);
        double Off(double t)
        {
            var p = PointAt(anchors, t);
            return WrapAngle(Math.Atan2(p.Y - centre.Y, p.X - centre.X) - aim);
        }
        double DistSq(double t)
        {
            var p = PointAt(anchors, t);
            return (p.X - towards.X) * (p.X - towards.X) + (p.Y - towards.Y) * (p.Y - towards.Y);
        }

        double? best = null;
        var prev = Off(0);
        for (var i = 1; i <= samples; i++)
        {
            double lo = (double)(i - 1) / samples, hi = (double)i / samples;
            var next = Off(hi);
            double? hit = null;
            if (prev == 0)
                hit = lo;
            // The offset changing sign is the ring crossing the ray - unless it jumps by about a
            // whole turn, which is the ring crossing the ray's opposite (+pi wrapping to -pi).
            else if (Math.Sign(prev) != Math.Sign(next) && next != 0 && Math.Abs(next - prev) < Math.PI)
            {
                var loOff = prev;
                for (var k = 0; k < 60; k++)
                {
                    var mid = (lo + hi) / 2;
                    var midOff = Off(mid);
                    if (Math.Sign(midOff) == Math.Sign(loOff))
                        (lo, loOff) = (mid, midOff);
                    else
                        hi = mid;
                }
                hit = (lo + hi) / 2;
            }
            if (hit is { } h && (best is not { } b || DistSq(h) < DistSq(b)))
                best = h;
            prev = next;
        }
        return best is { } found ? Wrap01(found) : NearestT(anchors, towards);
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

    /// <summary>
    /// The smallest axis-aligned rectangle containing the outline itself - every edge's curve,
    /// which a handle pulled out can bow past the anchor points (unlike
    /// <see cref="BoundingBox"/>, which only looks at the points). <paramref name="closed"/>
    /// includes the edge from the last anchor back to the first; an open line has none.
    /// </summary>
    public static Rect2D CurveBounds(IReadOnlyList<ShapeAnchor> anchors, bool closed = true)
    {
        if (anchors.Count == 0)
            return default;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Include(Point2D p)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        foreach (var a in anchors)
            Include(a.Point);
        var edges = closed ? anchors.Count : anchors.Count - 1;
        for (var i = 0; i < edges; i++)
        {
            var a = anchors[i];
            var b = anchors[(i + 1) % anchors.Count];
            // Where the curve turns back along x or y: the roots of its derivative, a quadratic per axis.
            foreach (var t in Turns(a.Point.X, a.OutHandle.X, b.InHandle.X, b.Point.X).Concat(Turns(a.Point.Y, a.OutHandle.Y, b.InHandle.Y, b.Point.Y)))
                Include(CubicPoint(a.Point, a.OutHandle, b.InHandle, b.Point, t));
        }
        return Rect2D.FromEdges(minX, minY, maxX, maxY);
    }

    /// <summary>The t in (0, 1) where one coordinate of a cubic bezier stops rising or falling.</summary>
    private static IEnumerable<double> Turns(double p0, double c0, double c1, double p1)
    {
        var a = -p0 + 3 * c0 - 3 * c1 + p1;
        var b = 2 * (p0 - 2 * c0 + c1);
        var c = c0 - p0;
        if (Math.Abs(a) < 1e-12)
        {
            if (Math.Abs(b) > 1e-12 && -c / b is > 0 and < 1 and var t)
                yield return t;
            yield break;
        }
        var discriminant = b * b - 4 * a * c;
        if (discriminant < 0)
            yield break;
        var root = Math.Sqrt(discriminant);
        foreach (var t in new[] { (-b + root) / (2 * a), (-b - root) / (2 * a) })
        {
            if (t is > 0 and < 1)
                yield return t;
        }
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

    /// <summary>A four-corner ring: straight edges, handles on their anchors.</summary>
    public static List<ShapeAnchor> Rectangle(Rect2D b) =>
    [
        Corner(new Point2D(b.Left, b.Top)),
        Corner(new Point2D(b.Right, b.Top)),
        Corner(new Point2D(b.Right, b.Bottom)),
        Corner(new Point2D(b.Left, b.Bottom))
    ];

    /// <summary>The ellipse filling <paramref name="b"/>: the standard 4-point cubic-bezier approximation (kappa ~= 0.5523), starting at the top and going clockwise.</summary>
    public static List<ShapeAnchor> Ellipse(Rect2D b)
    {
        const double kappa = 0.5522848;
        var rx = b.Width / 2;
        var ry = b.Height / 2;
        var cx = b.MidX;
        var cy = b.MidY;
        var ox = rx * kappa;
        var oy = ry * kappa;

        var top = new Point2D(cx, cy - ry);
        var right = new Point2D(cx + rx, cy);
        var bottom = new Point2D(cx, cy + ry);
        var left = new Point2D(cx - rx, cy);

        return
        [
            new ShapeAnchor(top, new Point2D(top.X - ox, top.Y), new Point2D(top.X + ox, top.Y), AnchorHandleKind.Smooth),
            new ShapeAnchor(right, new Point2D(right.X, right.Y - oy), new Point2D(right.X, right.Y + oy), AnchorHandleKind.Smooth),
            new ShapeAnchor(bottom, new Point2D(bottom.X + ox, bottom.Y), new Point2D(bottom.X - ox, bottom.Y), AnchorHandleKind.Smooth),
            new ShapeAnchor(left, new Point2D(left.X, left.Y + oy), new Point2D(left.X, left.Y - oy), AnchorHandleKind.Smooth)
        ];
    }

    /// <summary>An anchor with both handles on its point: the edges either side of it are straight there.</summary>
    public static ShapeAnchor Corner(Point2D p) => new(p, p, p, AnchorHandleKind.Corner);

    private static double Wrap01(double t)
    {
        t %= 1.0;
        return t < 0 ? t + 1.0 : t;
    }

    /// <summary>An angle brought into (-pi, pi].</summary>
    private static double WrapAngle(double a)
    {
        a %= 2 * Math.PI;
        return a > Math.PI ? a - 2 * Math.PI : a <= -Math.PI ? a + 2 * Math.PI : a;
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
