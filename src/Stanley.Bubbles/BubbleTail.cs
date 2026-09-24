using SkiaSharp;

namespace Stanley.Bubbles;

public enum TailKind
{
    /// <summary>Plain triangle from the outline to the target (Speech).</summary>
    SmoothTriangle,

    /// <summary>Triangle with a zigzag kink partway to the target (Shout).</summary>
    JaggedTriangle,

    /// <summary>Same shape as <see cref="SmoothTriangle"/>; the dashing is a stroke-time style, not a path difference (Whisper).</summary>
    DashedTriangle
}

/// <summary>
/// A single tail: where it leaves the outline (<see cref="AttachmentT"/>, a 0-1
/// fraction along the ring) and where it points (<see cref="Target"/>, a free
/// point in the bubble's coordinate space). A <see cref="SpeechBubble"/> can hold
/// any number of these; each is generated and unioned independently.
/// </summary>
public sealed class BubbleTail
{
    public float AttachmentT { get; set; }
    public SKPoint Target { get; set; }
    public TailKind Kind { get; set; }

    public BubbleTail(float attachmentT, SKPoint target, TailKind kind)
    {
        AttachmentT = attachmentT;
        Target = target;
        Kind = kind;
    }

    /// <summary>Builds this tail's standalone polygon, to be unioned with the bubble outline.</summary>
    public SKPath GeneratePath(BubbleOutline outline, float baseHalfWidth = 16f)
    {
        // The widened spread (see SpreadFor) can absorb a concave stretch of a jagged
        // outline (e.g. a Shout tooth's inner notch) that no jag orientation can route
        // around without the tail crossing itself; retry at UniformSpread's narrower,
        // rarely-concave base. Even that can occasionally still cross itself (an acute
        // angle between the jag and the outline's own local direction), so the last
        // resort drops the jag/absorbed-anchor detail entirely for a plain 3-point
        // triangle - trivially simple, since 3 points can't self-intersect.
        var points =
            TryBuildSimplePolygon(outline, SpreadFor(outline, Kind, baseHalfWidth), baseHalfWidth) ??
            TryBuildSimplePolygon(outline, UniformSpread(outline), baseHalfWidth) ??
            BuildPlainTriangle(outline, UniformSpread(outline));

        using var builder = new SKPathBuilder();
        builder.MoveTo(points[0]);
        for (var i = 1; i < points.Count; i++)
            builder.LineTo(points[i]);
        builder.Close();
        return builder.Detach();
    }

    /// <summary>The polygon at this spread, preferring whichever jag orientation (for <see cref="TailKind.JaggedTriangle"/>) keeps it simple; null if neither does.</summary>
    private List<SKPoint>? TryBuildSimplePolygon(BubbleOutline outline, float spread, float baseHalfWidth)
    {
        var unflipped = BuildPolygon(outline, spread, baseHalfWidth, jagFlipped: false);
        if (!HasSelfIntersection(unflipped))
            return unflipped;
        if (Kind != TailKind.JaggedTriangle)
            return null;

        var flipped = BuildPolygon(outline, spread, baseHalfWidth, jagFlipped: true);
        return HasSelfIntersection(flipped) ? null : flipped;
    }

    /// <summary>The guaranteed-simple last resort: no jag, no absorbed anchors, just the three points a triangle needs.</summary>
    private List<SKPoint> BuildPlainTriangle(BubbleOutline outline, float spread) =>
        [outline.PointAt(AttachmentT - spread), Target, outline.PointAt(AttachmentT + spread)];

    private List<SKPoint> BuildPolygon(BubbleOutline outline, float spread, float baseHalfWidth, bool jagFlipped)
    {
        var left = outline.PointAt(AttachmentT - spread);
        var right = outline.PointAt(AttachmentT + spread);
        var absorbed = outline.AnchorsBetween(AttachmentT - spread, AttachmentT + spread);

        var points = new List<SKPoint>(5 + absorbed.Count) { left };
        if (Kind == TailKind.JaggedTriangle)
        {
            var mid = outline.PointAt(AttachmentT);
            var jag = PerpOffset(Sub(Target, mid), baseHalfWidth * 0.6f);
            if (jagFlipped)
                jag = new SKPoint(-jag.X, -jag.Y);
            var kink = Lerp(mid, Target, 0.45f);

            points.Add(Add(kink, jag));
            points.Add(Target);
            points.Add(Sub(kink, jag));
        }
        else
        {
            points.Add(Target);
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
    private static float SpreadFor(BubbleOutline outline, TailKind kind, float baseHalfWidth)
    {
        var uniform = UniformSpread(outline);
        if (kind != TailKind.JaggedTriangle)
            return uniform;

        var perimeter = EstimatePerimeter(outline);
        if (perimeter < 0.0001f)
            return uniform;

        var arcSpread = MathF.Min(baseHalfWidth * 2f / perimeter, 0.2f);
        return MathF.Max(uniform, arcSpread);
    }

    /// <summary>A spread narrow enough to never straddle a whole outline anchor, so the base is a single edge rather than a multi-anchor walk.</summary>
    private static float UniformSpread(BubbleOutline outline) => 1f / (outline.Anchors.Count * 6f);

    private static float EstimatePerimeter(BubbleOutline outline, int samples = 64)
    {
        var total = 0f;
        var prev = outline.PointAt(0f);
        for (var i = 1; i <= samples; i++)
        {
            var p = outline.PointAt((float)i / samples);
            total += Length(Sub(p, prev));
            prev = p;
        }
        return total;
    }

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
