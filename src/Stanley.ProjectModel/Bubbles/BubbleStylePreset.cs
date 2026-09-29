using System.Text.Json.Serialization;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Bubbles;

[JsonConverter(typeof(CamelCaseEnumConverter<BubbleStylePreset>))]
public enum BubbleStylePreset
{
    Speech,
    Shout,
    Whisper
}

[JsonConverter(typeof(CamelCaseEnumConverter<TailKind>))]
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
/// Each preset is just a pure <c>bounds -&gt; anchors</c> generator plus a default tail
/// kind and stroke style - the "ease of use" default path; the underlying
/// <see cref="BubbleShape"/> anchor model is the escape hatch for hand-editing. Same
/// enum-plus-static-lookup shape as <see cref="MetricPaperSizes"/>.
/// </summary>
public static class BubbleStylePresets
{
    public static BubbleShape GenerateShape(BubbleStylePreset preset, Rect2D bounds) => new(
        preset switch
        {
            BubbleStylePreset.Speech => Oval(bounds),
            BubbleStylePreset.Whisper => Oval(bounds),
            BubbleStylePreset.Shout => Zigzag(bounds),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
        });

    public static TailKind TailKindFor(BubbleStylePreset preset) =>
        preset switch
        {
            BubbleStylePreset.Speech => TailKind.SmoothTriangle,
            BubbleStylePreset.Whisper => TailKind.DashedTriangle,
            BubbleStylePreset.Shout => TailKind.JaggedTriangle,
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
        };

    public static bool UsesDashedStroke(BubbleStylePreset preset) => preset == BubbleStylePreset.Whisper;

    private static List<ShapeAnchor> Oval(Rect2D b) => AnchorRing.Ellipse(b);

    private static List<ShapeAnchor> Zigzag(Rect2D b)
    {
        const int spikes = 10;
        var cx = b.MidX;
        var cy = b.MidY;
        var rx = b.Width / 2;
        var ry = b.Height / 2;

        var anchors = new List<ShapeAnchor>(spikes * 2);
        for (var i = 0; i < spikes * 2; i++)
        {
            var angle = Math.PI * 2 * i / (spikes * 2);
            var r = i % 2 == 0 ? 1.0 : 0.72; // alternate outer/inner radius for the star shape
            var p = new Point2D(cx + Math.Cos(angle) * rx * r, cy + Math.Sin(angle) * ry * r);
            // Handles coincide with the point itself: a straight edge, no curve.
            anchors.Add(new ShapeAnchor(p, p, p, AnchorHandleKind.Corner));
        }
        // No spike points straight up or down, so the star falls short of its box's top and
        // bottom; stretch it to reach them. Then, as for an oval, its bounding box is the box it
        // was made for, and switching Speech -> Shout -> Speech gives back the same bubble
        // rather than one a little shorter every round trip.
        return [.. AnchorRing.Rescale(anchors, AnchorRing.BoundingBox(anchors), b)];
    }
}
