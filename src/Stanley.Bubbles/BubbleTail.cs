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
        var spread = SpreadFor(outline);
        var left = outline.PointAt(AttachmentT - spread);
        var right = outline.PointAt(AttachmentT + spread);

        using var builder = new SKPathBuilder();
        if (Kind == TailKind.JaggedTriangle)
        {
            var mid = Lerp(left, right, 0.5f);
            var toward = Sub(Target, mid);
            var jag = PerpOffset(toward, baseHalfWidth * 0.6f);
            var kink = Lerp(mid, Target, 0.45f);

            builder.MoveTo(left);
            builder.LineTo(Add(kink, jag));
            builder.LineTo(Target);
            builder.LineTo(Sub(kink, jag));
            builder.LineTo(right);
        }
        else
        {
            builder.MoveTo(left);
            builder.LineTo(Target);
            builder.LineTo(right);
        }

        builder.Close();
        return builder.Detach();
    }

    /// <summary>How far, as a fraction of the outline's parametric length, the tail's two base points straddle the attachment point.</summary>
    private static float SpreadFor(BubbleOutline outline) => 1f / (outline.Anchors.Count * 6f);

    private static SKPoint Lerp(SKPoint a, SKPoint b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    private static SKPoint Sub(SKPoint a, SKPoint b) => new(a.X - b.X, a.Y - b.Y);
    private static SKPoint Add(SKPoint a, SKPoint b) => new(a.X + b.X, a.Y + b.Y);

    private static SKPoint PerpOffset(SKPoint direction, float length)
    {
        var len = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (len < 0.0001f)
            return new SKPoint(0, 0);
        return new SKPoint(-direction.Y / len * length, direction.X / len * length);
    }
}
