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
        var spread = SpreadFor(outline, Kind, baseHalfWidth);
        var left = outline.PointAt(AttachmentT - spread);
        var right = outline.PointAt(AttachmentT + spread);
        var absorbed = outline.AnchorsBetween(AttachmentT - spread, AttachmentT + spread);

        using var builder = new SKPathBuilder();
        builder.MoveTo(left);
        if (Kind == TailKind.JaggedTriangle)
        {
            var mid = Lerp(left, right, 0.5f);
            var toward = Sub(Target, mid);
            var jag = PerpOffset(toward, baseHalfWidth * 0.6f);
            var kink = Lerp(mid, Target, 0.45f);

            // PerpOffset's rotation direction is fixed, but which side "left" and
            // "right" fall on flips with the outline's local winding (e.g. a convex
            // spike vs. a concave notch on a jagged outline). Orient the jag so it
            // bulges towards left's side on the way out and right's side on the way
            // back, otherwise the two legs swap sides and the tail crosses itself.
            if (Cross(toward, Sub(left, mid)) < 0 != Cross(toward, jag) < 0)
                jag = new SKPoint(-jag.X, -jag.Y);

            builder.LineTo(Add(kink, jag));
            builder.LineTo(Target);
            builder.LineTo(Sub(kink, jag));
        }
        else
        {
            builder.LineTo(Target);
        }
        builder.LineTo(right);

        // The base (left..right) may straddle one or more outline vertices (e.g. a
        // Shout tooth) rather than sharing a single edge with the outline. Walk back
        // through them instead of chording straight from right to left, so that stretch
        // of the outline becomes part of the tail's own polygon and gets absorbed into
        // the union cleanly, rather than left as a sliver of the outline for the boolean
        // union to reconcile against a straight chord across it.
        for (var i = absorbed.Count - 1; i >= 0; i--)
            builder.LineTo(absorbed[i]);

        builder.Close();
        return builder.Detach();
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
        var uniform = 1f / (outline.Anchors.Count * 6f);
        if (kind != TailKind.JaggedTriangle)
            return uniform;

        var perimeter = EstimatePerimeter(outline);
        if (perimeter < 0.0001f)
            return uniform;

        var arcSpread = MathF.Min(baseHalfWidth * 2f / perimeter, 0.2f);
        return MathF.Max(uniform, arcSpread);
    }

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
