using SkiaSharp;

namespace Stanley.Bubbles;

public enum BubbleStylePreset
{
    Speech,
    Shout,
    Whisper
}

/// <summary>
/// Each preset is just a pure <c>bounds -&gt; anchors</c> generator plus a default tail
/// kind and stroke style. Presets are the "ease of use" default path; the underlying
/// <see cref="BubbleOutline"/> anchor model is the escape hatch for hand-editing.
/// </summary>
public static class BubbleStylePresets
{
    public static List<BubbleAnchor> GenerateOutline(BubbleStylePreset preset, SKRect bounds) =>
        preset switch
        {
            BubbleStylePreset.Speech => Oval(bounds),
            BubbleStylePreset.Whisper => Oval(bounds),
            BubbleStylePreset.Shout => Zigzag(bounds),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
        };

    public static TailKind TailKindFor(BubbleStylePreset preset) =>
        preset switch
        {
            BubbleStylePreset.Speech => TailKind.SmoothTriangle,
            BubbleStylePreset.Whisper => TailKind.DashedTriangle,
            BubbleStylePreset.Shout => TailKind.JaggedTriangle,
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
        };

    public static bool UsesDashedStroke(BubbleStylePreset preset) => preset == BubbleStylePreset.Whisper;

    private static List<BubbleAnchor> Oval(SKRect b)
    {
        // Standard 4-point cubic-bezier ellipse approximation (kappa ~= 0.5523).
        const float kappa = 0.5522848f;
        var rx = b.Width / 2f;
        var ry = b.Height / 2f;
        var cx = b.MidX;
        var cy = b.MidY;
        var ox = rx * kappa;
        var oy = ry * kappa;

        var top = new SKPoint(cx, cy - ry);
        var right = new SKPoint(cx + rx, cy);
        var bottom = new SKPoint(cx, cy + ry);
        var left = new SKPoint(cx - rx, cy);

        return new List<BubbleAnchor>
        {
            new(top, new SKPoint(top.X - ox, top.Y), new SKPoint(top.X + ox, top.Y)),
            new(right, new SKPoint(right.X, right.Y - oy), new SKPoint(right.X, right.Y + oy)),
            new(bottom, new SKPoint(bottom.X + ox, bottom.Y), new SKPoint(bottom.X - ox, bottom.Y)),
            new(left, new SKPoint(left.X, left.Y + oy), new SKPoint(left.X, left.Y - oy))
        };
    }

    private static List<BubbleAnchor> Zigzag(SKRect b)
    {
        const int spikes = 10;
        var cx = b.MidX;
        var cy = b.MidY;
        var rx = b.Width / 2f;
        var ry = b.Height / 2f;

        var anchors = new List<BubbleAnchor>(spikes * 2);
        for (var i = 0; i < spikes * 2; i++)
        {
            var angle = MathF.PI * 2 * i / (spikes * 2);
            var r = i % 2 == 0 ? 1f : 0.72f; // alternate outer/inner radius for the star shape
            var p = new SKPoint(cx + MathF.Cos(angle) * rx * r, cy + MathF.Sin(angle) * ry * r);
            // Handles coincide with the point itself: a straight edge, no curve.
            anchors.Add(new BubbleAnchor(p, p, p, AnchorHandleType.Corner));
        }
        return anchors;
    }
}
