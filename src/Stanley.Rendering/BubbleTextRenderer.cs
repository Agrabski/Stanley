using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Rendering;

/// <summary>
/// Draws a bubble's text in its font, word-wrapped and lined up (centred unless the bubble
/// says otherwise) within its shape's bounding box.
/// SkiaSharp-only measurement/layout (no Avalonia text stack available here) - a plain
/// greedy word wrap, not full text shaping; good enough for short dialogue lines.
/// </summary>
public static class BubbleTextRenderer
{
    private const float HorizontalPaddingFraction = 0.18f;
    private const float VerticalPaddingFraction = 0.22f;

    /// <summary>The box inside the bubble the text is laid out in (its bounding box minus padding), so an editor can put a text box exactly where the lettering goes.</summary>
    public static Rect2D TextArea(Bubble bubble)
    {
        var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var dx = bounds.Width * HorizontalPaddingFraction;
        var dy = bounds.Height * VerticalPaddingFraction;
        return new Rect2D(bounds.Left + dx, bounds.Top + dy, bounds.Width - 2 * dx, bounds.Height - 2 * dy);
    }

    /// <summary>Draws the text at the bubble's own size, or <paramref name="fontSize"/> when it has none (in the canvas's units).</summary>
    public static void Draw(SKCanvas canvas, Bubble bubble, float fontSize = 18f, SKColor? color = null)
    {
        fontSize = bubble.FontSizeMm is { } own ? (float)own : fontSize;
        var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var skBounds = SKRect.Create(
            (float)bounds.Left, (float)bounds.Top, (float)bounds.Width, (float)bounds.Height);

        var maxWidth = skBounds.Width * (1 - 2 * HorizontalPaddingFraction);
        var maxHeight = skBounds.Height * (1 - 2 * VerticalPaddingFraction);
        if (maxWidth <= 0 || maxHeight <= 0)
            return;

        using var font = Lettering.Font(fontSize, bubble.Bold, bubble.Italic, bubble.FontFamily);
        using var paint = new SKPaint { Color = color ?? SKColors.Black, IsAntialias = true };

        var lines = WrapLines(bubble.Text, font, paint, maxWidth);
        var lineHeight = font.Spacing;
        var totalHeight = lineHeight * lines.Count;

        // Shrink to fit rather than overflow the bubble, so a long line stays readable
        // instead of spilling past the outline.
        if (totalHeight > maxHeight && lines.Count > 0)
        {
            var scale = maxHeight / totalHeight;
            font.Size = Math.Max(fontSize * scale, fontSize / 3);
            lines = WrapLines(bubble.Text, font, paint, maxWidth);
            lineHeight = font.Spacing;
            totalHeight = lineHeight * lines.Count;
        }

        var (x, align) = bubble.Align switch
        {
            TextAlign.Left => (skBounds.MidX - maxWidth / 2, SKTextAlign.Left),
            TextAlign.Right => (skBounds.MidX + maxWidth / 2, SKTextAlign.Right),
            _ => (skBounds.MidX, SKTextAlign.Center)
        };
        var startY = skBounds.MidY - totalHeight / 2 + font.Metrics.Ascent * -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var y = startY + lineHeight * i;
            canvas.DrawText(lines[i], x, y, align, font, paint);
        }
    }

    private static List<string> WrapLines(string text, SKFont font, SKPaint paint, float maxWidth) => Lettering.Wrap(text, font, paint, maxWidth);
}
