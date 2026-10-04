using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Rendering;

/// <summary>
/// Draws a bubble's text in its font, word-wrapped and lined up (centred unless the bubble
/// says otherwise) within its shape's bounding box, always at its own size - the size in the
/// font box is the size on the page, however long the text is (issue #66); it's the bubble
/// that grows to fit, never the letters that shrink.
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

    /// <summary>
    /// Draws the text at the bubble's own size (points, drawn in millimetres - the page's
    /// units), or <paramref name="fontSize"/> when it has none (in the canvas's units) - always
    /// at that size, never shrunk to fit. A bubble too small for its text is grown instead
    /// (<see cref="NeededScale"/>, <c>Stanley.Editing.BubbleEditing.GrowToFit</c>); one a user
    /// then resizes smaller by hand simply spills past the outline, the same trade every text
    /// box on the page makes for WYSIWYG sizing over auto-shrink.
    /// </summary>
    public static void Draw(SKCanvas canvas, Bubble bubble, float fontSize = 18f, SKColor? color = null)
    {
        fontSize = OwnFontSize(bubble, fontSize);
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

    /// <summary>
    /// How much <paramref name="bubble"/> would have to grow - scaled up around its centre,
    /// width and height alike - for its own text at its own lettering to fit without
    /// shrinking; 1 when it fits already. Uses <see cref="Draw"/>'s own wrap and padding, so a
    /// bubble scaled up by this much draws with room to spare, never less. Growing widens the
    /// text area too, which can let a line rewrap shorter, so this searches for the smallest
    /// scale at which that settles rather than growing height alone.
    /// <c>Stanley.Editing.BubbleEditing.GrowToFit</c> turns this into the actual resize.
    /// </summary>
    public static float NeededScale(Bubble bubble, float fontSize = 18f)
    {
        if (string.IsNullOrWhiteSpace(bubble.Text))
            return 1f;

        fontSize = OwnFontSize(bubble, fontSize);
        var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var width0 = (float)bounds.Width * (1 - 2 * HorizontalPaddingFraction);
        var height0 = (float)bounds.Height * (1 - 2 * VerticalPaddingFraction);
        if (width0 <= 0 || height0 <= 0)
            return 1f;

        using var font = Lettering.Font(fontSize, bubble.Bold, bubble.Italic, bubble.FontFamily);
        using var measurer = new Lettering.Measurer(font);
        using var paint = new SKPaint { IsAntialias = true };

        // Fits when the wrapped lines are no taller than the area - and none wider, since a
        // word longer than the line never breaks.
        bool FitsAt(float scale)
        {
            var lines = WrapLines(bubble.Text, font, paint, width0 * scale);
            return font.Spacing * lines.Count <= height0 * scale && lines.All(l => measurer.Width(l, paint) <= width0 * scale);
        }

        if (FitsAt(1f))
            return 1f;

        // Grow the upper bound until it fits, then bisect down to the smallest scale that does.
        var lo = 1f;
        var hi = 2f;
        while (!FitsAt(hi) && hi < 1e6f)
            hi *= 2f;
        for (var i = 0; i < 40; i++)
        {
            var mid = (lo + hi) / 2f;
            if (FitsAt(mid))
                hi = mid;
            else
                lo = mid;
        }
        return hi * FitSlack;
    }

    /// <summary>
    /// The bisection above lands exactly where the widest line just fits its area, so whether
    /// that line stays on one row or tips over to the next would come down to float noise - and
    /// to the last digit of two text engines' (the page's, the inline editor's) measurements.
    /// A hair more room (invisible) makes the bubble fit its text with margin instead.
    /// </summary>
    private const float FitSlack = 1.005f;

    /// <summary>The size <see cref="Draw"/> and <see cref="NeededScale"/> both letter at: the bubble's own (points, in millimetres), or <paramref name="fallback"/> when it has none.</summary>
    private static float OwnFontSize(Bubble bubble, float fallback) =>
        bubble.FontSizePt is { } own ? (float)FontPoints.ToMm(own) : fallback;

    private static List<string> WrapLines(string text, SKFont font, SKPaint paint, float maxWidth) => Lettering.Wrap(text, font, paint, maxWidth);
}
