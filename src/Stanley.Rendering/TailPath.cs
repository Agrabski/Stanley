using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>Builds one tail's standalone polygon, to be unioned with its bubble's outline (<see cref="BubbleRenderer"/>).</summary>
public static class TailPath
{
    public static SKPath Generate(BubbleTail tail, BubbleShape shape, float baseHalfWidth = 16f)
    {
        if (tail.Kind == TailKind.ThoughtDots)
            return ThoughtDotsPath(tail, shape);

        // The widened spread (see SpreadFor) can absorb a concave stretch of a jagged
        // outline (e.g. a Shout tooth's inner notch) that no jag orientation can route
        // around without the tail crossing itself; retry at UniformSpread's narrower,
        // rarely-concave base. Even that can occasionally still cross itself (an acute
        // angle between the jag and the outline's own local direction), so the last
        // resort drops the jag/absorbed-anchor detail entirely for a plain 3-point
        // triangle - trivially simple, since 3 points can't self-intersect.
        var points =
            TryBuildSimplePolygon(tail, shape, SpreadFor(shape, tail.Kind, baseHalfWidth), baseHalfWidth) ??
            TryBuildSimplePolygon(tail, shape, UniformSpread(shape), baseHalfWidth) ??
            BuildPlainTriangle(tail, shape, UniformSpread(shape));

        using var builder = new SKPathBuilder();
        builder.MoveTo(points[0]);
        for (var i = 1; i < points.Count; i++)
            builder.LineTo(points[i]);
        builder.Close();
        return builder.Detach();
    }

    /// <summary>Radius of each thought dot as a fraction of the bubble's diagonal, biggest nearest the bubble (same look as <see cref="ThoughtTrailRenderer"/>, a little larger since a bubble is smaller than a panel).</summary>
    private static readonly double[] DotRadiusFractions = [0.06, 0.04, 0.025];

    /// <summary>Three separate circles, shrinking from the outline towards the target; unioned with the outline they stay detached dots.</summary>
    private static SKPath ThoughtDotsPath(BubbleTail tail, BubbleShape shape)
    {
        var basePoint = AnchorRing.PointAt(shape.Anchors, tail.AttachmentT);
        var box = AnchorRing.BoundingBox(shape.Anchors);
        var scale = Math.Sqrt(box.Width * box.Width + box.Height * box.Height);
        using var builder = new SKPathBuilder();
        for (var i = 0; i < DotRadiusFractions.Length; i++)
        {
            var t = (i + 1.0) / DotRadiusFractions.Length;
            var x = basePoint.X + (tail.Target.X - basePoint.X) * t;
            var y = basePoint.Y + (tail.Target.Y - basePoint.Y) * t;
            builder.AddCircle((float)x, (float)y, (float)(scale * DotRadiusFractions[i]));
        }
        return builder.Detach();
    }

    /// <summary>The polygon at this spread, preferring whichever jag orientation (for <see cref="TailKind.JaggedTriangle"/>) keeps it simple; null if neither does.</summary>
    private static List<SKPoint>? TryBuildSimplePolygon(BubbleTail tail, BubbleShape shape, double spread, float baseHalfWidth)
    {
        var unflipped = BuildPolygon(tail, shape, spread, baseHalfWidth, jagFlipped: false);
        if (!HasSelfIntersection(unflipped))
            return unflipped;
        if (tail.Kind != TailKind.JaggedTriangle)
            return null;

        var flipped = BuildPolygon(tail, shape, spread, baseHalfWidth, jagFlipped: true);
        return HasSelfIntersection(flipped) ? null : flipped;
    }

    /// <summary>The guaranteed-simple last resort: no jag, no absorbed anchors, just the three points a triangle needs.</summary>
    private static List<SKPoint> BuildPlainTriangle(BubbleTail tail, BubbleShape shape, double spread) =>
    [
        AnchorRingPath.ToSk(AnchorRing.PointAt(shape.Anchors, tail.AttachmentT - spread)),
        AnchorRingPath.ToSk(tail.Target),
        AnchorRingPath.ToSk(AnchorRing.PointAt(shape.Anchors, tail.AttachmentT + spread))
    ];

    private static List<SKPoint> BuildPolygon(BubbleTail tail, BubbleShape shape, double spread, float baseHalfWidth, bool jagFlipped)
    {
        var left = AnchorRingPath.ToSk(AnchorRing.PointAt(shape.Anchors, tail.AttachmentT - spread));
        var right = AnchorRingPath.ToSk(AnchorRing.PointAt(shape.Anchors, tail.AttachmentT + spread));
        var target = AnchorRingPath.ToSk(tail.Target);
        var absorbed = AnchorRing.AnchorsBetween(shape.Anchors, tail.AttachmentT - spread, tail.AttachmentT + spread)
            .ConvertAll(AnchorRingPath.ToSk);

        var points = new List<SKPoint>(5 + absorbed.Count) { left };
        if (tail.Kind == TailKind.JaggedTriangle)
        {
            var mid = AnchorRingPath.ToSk(AnchorRing.PointAt(shape.Anchors, tail.AttachmentT));
            var jag = PerpOffset(Sub(target, mid), baseHalfWidth * 0.6f);
            if (jagFlipped)
                jag = new SKPoint(-jag.X, -jag.Y);
            var kink = Lerp(mid, target, 0.45f);

            points.Add(Add(kink, jag));
            points.Add(target);
            points.Add(Sub(kink, jag));
        }
        else
        {
            points.Add(target);
        }
        points.Add(right);

        // The base (left..right) may straddle one or more outline vertices (e.g. a
        // Shout tooth) rather than sharing a single edge with the outline. Walk back
        // through them instead of chording straight from right to left, so that stretch
        // of the outline becomes part of the tail's own polygon and gets absorbed into
        // the union cleanly, rather than left as a sliver of the outline for the boolean
        // union to reconcile against a straight chord across it.
        for (var i = absorbed.Count - 1; i >= 0; i--)
            points.Add(absorbed[i]);

        return points;
    }

    private static bool HasSelfIntersection(List<SKPoint> polygon)
    {
        var n = polygon.Count;
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1))
                    continue;

                if (SegmentsIntersect(polygon[i], polygon[(i + 1) % n], polygon[j], polygon[(j + 1) % n]))
                    return true;
            }
        }
        return false;
    }

    private static bool SegmentsIntersect(SKPoint p1, SKPoint p2, SKPoint p3, SKPoint p4)
    {
        var d1 = Cross(Sub(p4, p3), Sub(p1, p3));
        var d2 = Cross(Sub(p4, p3), Sub(p2, p3));
        var d3 = Cross(Sub(p2, p1), Sub(p3, p1));
        var d4 = Cross(Sub(p2, p1), Sub(p4, p1));
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    /// <summary>
    /// How far, as a fraction of the outline's parametric length, the tail's two base
    /// points straddle the attachment point. A jagged outline is made of many short
    /// straight edges, so this fraction alone collapses to a sliver pinched inside a
    /// single zigzag tooth instead of smoothly absorbing it into the tail; widen it to
    /// an absolute arc length there so the tail's base can blend across the jaggedness.
    /// </summary>
    private static double SpreadFor(BubbleShape shape, TailKind kind, float baseHalfWidth)
    {
        var uniform = UniformSpread(shape);
        if (kind != TailKind.JaggedTriangle)
            return uniform;

        var perimeter = EstimatePerimeter(shape);
        if (perimeter < 0.0001)
            return uniform;

        var arcSpread = Math.Min(baseHalfWidth * 2.0 / perimeter, 0.2);
        return Math.Max(uniform, arcSpread);
    }

    /// <summary>A spread narrow enough to never straddle a whole outline anchor, so the base is a single edge rather than a multi-anchor walk.</summary>
    private static double UniformSpread(BubbleShape shape) => 1.0 / (shape.Anchors.Count * 6.0);

    private static double EstimatePerimeter(BubbleShape shape, int samples = 64)
    {
        var total = 0.0;
        var prev = AnchorRing.PointAt(shape.Anchors, 0);
        for (var i = 1; i <= samples; i++)
        {
            var p = AnchorRing.PointAt(shape.Anchors, (double)i / samples);
            total += Length(new Point2D(p.X - prev.X, p.Y - prev.Y));
            prev = p;
        }
        return total;
    }

    private static double Length(Point2D v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);

    private static SKPoint Lerp(SKPoint a, SKPoint b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    private static SKPoint Sub(SKPoint a, SKPoint b) => new(a.X - b.X, a.Y - b.Y);
    private static SKPoint Add(SKPoint a, SKPoint b) => new(a.X + b.X, a.Y + b.Y);
    private static float Cross(SKPoint a, SKPoint b) => a.X * b.Y - a.Y * b.X;
    private static float Length(SKPoint v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y);

    private static SKPoint PerpOffset(SKPoint direction, float length)
    {
        var len = Length(direction);
        if (len < 0.0001f)
            return new SKPoint(0, 0);
        return new SKPoint(-direction.Y / len * length, direction.X / len * length);
    }
}
