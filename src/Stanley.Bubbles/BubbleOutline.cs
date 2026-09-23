using SkiaSharp;

namespace Stanley.Bubbles;

/// <summary>
/// An arbitrary closed bezier shape: an ordered ring of anchors, each edge a cubic
/// bezier segment from one anchor's <see cref="BubbleAnchor.OutHandle"/> to the
/// next anchor's <see cref="BubbleAnchor.InHandle"/>.
/// </summary>
public sealed class BubbleOutline
{
    public List<BubbleAnchor> Anchors { get; }

    public BubbleOutline(IEnumerable<BubbleAnchor> anchors)
    {
        Anchors = anchors.ToList();
        if (Anchors.Count < 3)
            throw new ArgumentException("An outline needs at least 3 anchors.", nameof(anchors));
    }

    public SKPath ToPath()
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(Anchors[0].Point);
        for (var i = 0; i < Anchors.Count; i++)
        {
            var current = Anchors[i];
            var next = Anchors[(i + 1) % Anchors.Count];
            builder.CubicTo(current.OutHandle, next.InHandle, next.Point);
        }
        builder.Close();
        return builder.Detach();
    }

    /// <summary>Affine-maps every anchor point and handle from the old bounds to the new one.</summary>
    public void Rescale(SKRect from, SKRect to)
    {
        if (from.Width == 0 || from.Height == 0)
            return;

        var sx = to.Width / from.Width;
        var sy = to.Height / from.Height;

        SKPoint Map(SKPoint p) => new(
            to.Left + (p.X - from.Left) * sx,
            to.Top + (p.Y - from.Top) * sy);

        foreach (var anchor in Anchors)
        {
            anchor.Point = Map(anchor.Point);
            anchor.InHandle = Map(anchor.InHandle);
            anchor.OutHandle = Map(anchor.OutHandle);
        }
    }

    /// <summary>Evaluates a point on the outline for t in [0, 1), wrapping around the ring.</summary>
    public SKPoint PointAt(float t)
    {
        var n = Anchors.Count;
        var scaled = Wrap01(t) * n;
        var segment = (int)MathF.Floor(scaled) % n;
        var local = scaled - MathF.Floor(scaled);
        var p0 = Anchors[segment];
        var p1 = Anchors[(segment + 1) % n];
        return CubicPoint(p0.Point, p0.OutHandle, p1.InHandle, p1.Point, local);
    }

    /// <summary>
    /// The outline's own anchor points whose parametric position lies strictly between
    /// <paramref name="fromT"/> and <paramref name="toT"/>, walking forward (wrapping if
    /// <paramref name="toT"/> is "before" <paramref name="fromT"/>), ordered along that
    /// walk. Lets a tail whose base spans one or more outline vertices (e.g. a Shout
    /// preset's zigzag teeth) absorb that geometry into its own polygon instead of
    /// chording straight across it and leaving a sliver for the boolean union to
    /// reconcile on its own.
    /// </summary>
    public List<SKPoint> AnchorsBetween(float fromT, float toT)
    {
        var n = Anchors.Count;
        var from = Wrap01(fromT) * n;
        var to = Wrap01(toT) * n;
        if (to <= from)
            to += n;

        var found = new List<(float Pos, SKPoint Point)>();
        for (var i = 0; i < n; i++)
        {
            var pos = i <= from ? i + n : i;
            if (pos < to)
                found.Add((pos, Anchors[i].Point));
        }
        found.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        return found.ConvertAll(f => f.Point);
    }

    /// <summary>Brute-force nearest-point search: the t whose <see cref="PointAt"/> is closest to <paramref name="target"/>.</summary>
    public float NearestT(SKPoint target, int samples = 200)
    {
        var bestT = 0f;
        var bestDistSq = float.MaxValue;
        for (var i = 0; i < samples; i++)
        {
            var t = (float)i / samples;
            var p = PointAt(t);
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

    private static float Wrap01(float t)
    {
        t %= 1f;
        return t < 0 ? t + 1f : t;
    }

    private static SKPoint CubicPoint(SKPoint p0, SKPoint c0, SKPoint c1, SKPoint p1, float t)
    {
        var mt = 1 - t;
        var a = mt * mt * mt;
        var b = 3 * mt * mt * t;
        var c = 3 * mt * t * t;
        var d = t * t * t;
        return new SKPoint(
            a * p0.X + b * c0.X + c * c1.X + d * p1.X,
            a * p0.Y + b * c0.Y + c * c1.Y + d * p1.Y);
    }
}
