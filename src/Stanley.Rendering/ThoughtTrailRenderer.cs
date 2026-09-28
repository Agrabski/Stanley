using SkiaSharp;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>
/// Turns a <see cref="ThoughtTrail"/> into the small circles, shrinking towards the thinker,
/// that lead out of a thought cloud - the cloud's equivalent of a speech bubble's tail
/// (<see cref="TailPath"/>), but drawn as its own dots rather than unioned into the outline.
/// </summary>
public static class ThoughtTrailRenderer
{
    /// <summary>How many dots the trail draws - a classic thought-cloud trail, not a dotted line.</summary>
    public const int DotCount = 3;

    /// <summary>Each dot's radius as a fraction of the cloud's own diagonal, biggest nearest the cloud, smallest at the tip.</summary>
    private static readonly double[] RadiusFractions = [0.03, 0.02, 0.012];

    /// <summary>The dots' centres and radii, in the panel's own (page) space - pure geometry, so it's exactly what <see cref="Draw"/> paints and what a hit test or handle can check.</summary>
    public static IReadOnlyList<(Point2D Center, double Radius)> Dots(ThoughtTrail trail, PanelShape shape)
    {
        var basePoint = AnchorRing.PointAt(shape.Anchors, trail.AttachmentT);
        var scale = Diagonal(AnchorRing.BoundingBox(shape.Anchors));
        var dots = new List<(Point2D Center, double Radius)>(RadiusFractions.Length);
        for (var i = 0; i < RadiusFractions.Length; i++)
        {
            var t = (i + 1.0) / RadiusFractions.Length;
            dots.Add((Lerp(basePoint, trail.Target, t), scale * RadiusFractions[i]));
        }
        return dots;
    }

    public static void Draw(SKCanvas canvas, ThoughtTrail trail, PanelShape shape, SKColor? fillColor = null, SKColor? strokeColor = null, float strokeWidth = 0.5f)
    {
        using var fill = new SKPaint { Color = fillColor ?? SKColors.White, IsAntialias = true };
        using var stroke = new SKPaint { Color = strokeColor ?? SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth, IsAntialias = true };
        foreach (var (center, radius) in Dots(trail, shape))
        {
            var p = AnchorRingPath.ToSk(center);
            canvas.DrawCircle(p, (float)radius, fill);
            canvas.DrawCircle(p, (float)radius, stroke);
        }
    }

    private static Point2D Lerp(Point2D a, Point2D b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double Diagonal(Rect2D r) => Math.Sqrt(r.Width * r.Width + r.Height * r.Height);
}
