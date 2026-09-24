using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>Turns a <see cref="Bubble"/> into pixels: outline unioned with every tail's own polygon, then fill/stroke/text. No data ownership, no mutable state.</summary>
public static class BubbleRenderer
{
    public static SKPath BuildRenderPath(Bubble bubble)
    {
        var current = AnchorRingPath.ToSkPath(bubble.Shape.Anchors);
        foreach (var tail in bubble.Tails)
        {
            using var tailPath = TailPath.Generate(tail, bubble.Shape);
            var next = current.Op(tailPath, SKPathOp.Union);
            current.Dispose();
            current = next;
        }
        return current;
    }

    public static void Draw(SKCanvas canvas, Bubble bubble, SKColor fillColor, SKColor strokeColor, float strokeWidth = 3f)
    {
        using var fillPaint = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var strokePaint = new SKPaint
        {
            Color = strokeColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            IsAntialias = true
        };
        if (BubbleStylePresets.UsesDashedStroke(bubble.Style))
            strokePaint.PathEffect = SKPathEffect.CreateDash([10, 8], 0);

        using var path = BuildRenderPath(bubble);
        canvas.DrawPath(path, fillPaint);
        canvas.DrawPath(path, strokePaint);

        if (!string.IsNullOrWhiteSpace(bubble.Text))
            BubbleTextRenderer.Draw(canvas, bubble);
    }
}
