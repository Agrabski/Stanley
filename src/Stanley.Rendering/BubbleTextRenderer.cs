using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>
/// Draws a bubble's text, word-wrapped and centered within its shape's bounding box.
/// SkiaSharp-only measurement/layout (no Avalonia text stack available here) - a plain
/// greedy word wrap, not full text shaping; good enough for short dialogue lines.
/// </summary>
public static class BubbleTextRenderer
{
    private const float HorizontalPaddingFraction = 0.18f;
    private const float VerticalPaddingFraction = 0.22f;

    public static void Draw(SKCanvas canvas, Bubble bubble, float fontSize = 18f, SKColor? color = null)
    {
        var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var skBounds = SKRect.Create(
            (float)bounds.Left, (float)bounds.Top, (float)bounds.Width, (float)bounds.Height);

        var maxWidth = skBounds.Width * (1 - 2 * HorizontalPaddingFraction);
        var maxHeight = skBounds.Height * (1 - 2 * VerticalPaddingFraction);
        if (maxWidth <= 0 || maxHeight <= 0)
            return;

        using var font = new SKFont(SKTypeface.Default, fontSize);
        using var paint = new SKPaint { Color = color ?? SKColors.Black, IsAntialias = true };

        var lines = WrapLines(bubble.Text, font, paint, maxWidth);
        var lineHeight = font.Spacing;
        var totalHeight = lineHeight * lines.Count;

        // Shrink to fit rather than overflow the bubble, so a long line stays readable
        // instead of spilling past the outline.
        if (totalHeight > maxHeight && lines.Count > 0)
        {
            var scale = maxHeight / totalHeight;
            font.Size = Math.Max(fontSize * scale, 6f);
            lines = WrapLines(bubble.Text, font, paint, maxWidth);
            lineHeight = font.Spacing;
            totalHeight = lineHeight * lines.Count;
        }

        var startY = skBounds.MidY - totalHeight / 2 + font.Metrics.Ascent * -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var y = startY + lineHeight * i;
            canvas.DrawText(lines[i], skBounds.MidX, y, SKTextAlign.Center, font, paint);
        }
    }

    private static List<string> WrapLines(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var current = words[0];
            for (var i = 1; i < words.Length; i++)
            {
                var candidate = current + " " + words[i];
                if (font.MeasureText(candidate, paint) <= maxWidth)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = words[i];
                }
            }
            lines.Add(current);
        }
        return lines;
    }
}
