using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>Turns a <see cref="Bubble"/> into pixels: outline unioned with every tail's own polygon, then fill/stroke/text. No data ownership, no mutable state.</summary>
public static class BubbleRenderer
{
    /// <param name="tailBaseHalfWidth">In the same units as the bubble's coordinates - the default suits pixel-space bubbles; a page in millimetres wants a few units.</param>
    public static SKPath BuildRenderPath(Bubble bubble, float tailBaseHalfWidth = 16f)
    {
        var current = AnchorRingPath.ToSkPath(bubble.Shape.Anchors);
        foreach (var tail in bubble.Tails)
        {
            using var tailPath = TailPath.Generate(tail, bubble.Shape, tailBaseHalfWidth);
            var next = current.Op(tailPath, SKPathOp.Union);
            current.Dispose();
            current = next;
        }
        return current;
    }

    /// <summary>Stroke width, font size and tail width are all in the bubble's own coordinate units, so a millimetre-space page passes millimetre values and lets the canvas transform handle zoom.</summary>
    public static void Draw(
        SKCanvas canvas,
        Bubble bubble,
        SKColor fillColor,
        SKColor strokeColor,
        float strokeWidth = 3f,
        float fontSize = 18f,
        float tailBaseHalfWidth = 16f)
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
            strokePaint.PathEffect = SKPathEffect.CreateDash([strokeWidth * 3.3f, strokeWidth * 2.7f], 0);

        using var path = BuildRenderPath(bubble, tailBaseHalfWidth);
        canvas.DrawPath(path, fillPaint);
        canvas.DrawPath(path, strokePaint);

        if (!string.IsNullOrWhiteSpace(bubble.Text))
            BubbleTextRenderer.Draw(canvas, bubble, fontSize);
    }
}
