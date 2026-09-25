using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Rendering;

/// <summary>
/// Draws a panel's free elements - shapes, text and pictures - in page space
/// (millimetres), and answers "is this point on it" for the editor. No data ownership, no
/// mutable state.
/// </summary>
public static class ElementRenderer
{
    /// <summary>Space between a text box's edge and its text, as a fraction of the letter size (boxed text only - bare text runs to its bounds).</summary>
    public const double BoxPaddingFraction = 0.45;

    /// <summary>A sound effect's letter outline, as a fraction of the letter size.</summary>
    public const float OutlineFraction = 0.16f;

    /// <param name="pictures">The comic's pictures by file name (<see cref="PictureElement.ArtFileName"/>); a picture missing from them draws as a placeholder.</param>
    public static void Draw(SKCanvas canvas, PanelElement element, bool drawText = true, IReadOnlyDictionary<string, ArtFile>? pictures = null)
    {
        switch (element)
        {
            case ShapeElement shape:
                DrawShape(canvas, shape);
                break;
            case TextElement text:
                DrawText(canvas, text, drawText);
                break;
            case PictureElement picture:
                PictureRenderer.Draw(canvas, pictures?.GetValueOrDefault(picture.ArtFileName), picture.Bounds, cover: false);
                break;
        }
    }

    // ---------------------------------------------------------------- shapes

    /// <summary>A shape's path: a closed ring, or an open line from the first anchor to the last (a single anchor is a dot).</summary>
    public static SKPath ShapePath(ShapeElement shape)
    {
        var anchors = shape.Anchors;
        if (anchors.Count == 0)
            return new SKPath();
        if (shape.Closed && anchors.Count > 2)
            return AnchorRingPath.ToSkPath(anchors);

        using var builder = new SKPathBuilder();
        builder.MoveTo(AnchorRingPath.ToSk(anchors[0].Point));
        if (anchors.Count == 1)
            builder.LineTo(AnchorRingPath.ToSk(anchors[0].Point));
        for (var i = 0; i + 1 < anchors.Count; i++)
            builder.CubicTo(AnchorRingPath.ToSk(anchors[i].OutHandle), AnchorRingPath.ToSk(anchors[i + 1].InHandle), AnchorRingPath.ToSk(anchors[i + 1].Point));
        return builder.Detach();
    }

    public static void DrawShape(SKCanvas canvas, ShapeElement shape)
    {
        using var path = ShapePath(shape);
        if (shape.Closed && shape.Style.Fill is { } fill)
        {
            using var fillPaint = new SKPaint { Color = FigureGeometry.ToSk(fill), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(path, fillPaint);
        }
        if (shape.Style.Stroke is { } stroke && shape.Style.StrokeWidthMm > 0)
        {
            using var strokePaint = StrokePaint(shape, (float)shape.Style.StrokeWidthMm);
            strokePaint.Color = FigureGeometry.ToSk(stroke);
            using var dash = LinePatterns.Apply(strokePaint, shape.Style.Dash);
            canvas.DrawPath(path, strokePaint);
        }
    }

    /// <summary>Lines and freehand strokes end round, like ink; closed shapes keep crisp corners.</summary>
    private static SKPaint StrokePaint(ShapeElement shape, float width) => new()
    {
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        IsAntialias = true,
        StrokeCap = shape.Closed ? SKStrokeCap.Butt : SKStrokeCap.Round,
        StrokeJoin = shape.Closed ? SKStrokeJoin.Miter : SKStrokeJoin.Round
    };

    // ---------------------------------------------------------------- text

    /// <summary>Space between the box's edge and the text: some for a boxed caption, none for bare text.</summary>
    public static double Padding(TextElement text) =>
        text.Style.BoxFill is null && text.Style.BoxStroke is null ? 0 : text.Style.FontSizeMm * BoxPaddingFraction;

    /// <summary>Where the lettering is laid out (the bounds less padding), so an editor can put a text box exactly where it goes.</summary>
    public static Rect2D TextArea(TextElement text)
    {
        var b = text.Bounds;
        var pad = Math.Min(Padding(text), Math.Min(b.Width, b.Height) / 2);
        return new Rect2D(b.Left + pad, b.Top + pad, b.Width - 2 * pad, b.Height - 2 * pad);
    }

    /// <summary>How tall the element's bounds must be to fit its text, at its width and letter size, without shrinking.</summary>
    public static double NeededHeight(TextElement text)
    {
        var area = TextArea(text);
        using var font = Lettering.Font((float)text.Style.FontSizeMm, text.Style.Bold, text.Style.Italic);
        using var paint = new SKPaint { IsAntialias = true };
        var lines = Lettering.Wrap(text.Text, font, paint, (float)Math.Max(area.Width, 0.1));
        return Math.Max(1, lines.Count) * font.Spacing + 2 * Padding(text);
    }

    public static void DrawText(SKCanvas canvas, TextElement text, bool drawText = true)
    {
        var bounds = ToSk(text.Bounds);
        if (text.Style.BoxFill is { } boxFill)
        {
            using var fill = new SKPaint { Color = FigureGeometry.ToSk(boxFill), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(bounds, fill);
        }
        if (text.Style.BoxStroke is { } boxStroke && text.Style.BoxStrokeWidthMm > 0)
        {
            using var stroke = new SKPaint { Color = FigureGeometry.ToSk(boxStroke), Style = SKPaintStyle.Stroke, StrokeWidth = (float)text.Style.BoxStrokeWidthMm, IsAntialias = true };
            using var dash = LinePatterns.Apply(stroke, text.Style.BoxDash);
            canvas.DrawRect(bounds, stroke);
        }

        if (!drawText || string.IsNullOrWhiteSpace(text.Text))
            return;

        var area = TextArea(text);
        if (area.Width <= 0 || area.Height <= 0)
            return;

        var fontSize = (float)text.Style.FontSizeMm;
        using var font = Lettering.Font(fontSize, text.Style.Bold, text.Style.Italic);
        // Hollow letters (no fill) still need a paint to measure with; they just aren't filled.
        using var paint = new SKPaint { Color = text.Style.Color is { } letters ? FigureGeometry.ToSk(letters) : SKColors.Transparent, IsAntialias = true };
        var maxWidth = (float)area.Width;
        var lines = Lettering.Wrap(text.Text, font, paint, maxWidth);

        // Shrink to fit rather than spill out of the box, the way bubble lettering does.
        var totalHeight = font.Spacing * lines.Count;
        if (totalHeight > area.Height && lines.Count > 0)
        {
            font.Size = Math.Max(fontSize * (float)area.Height / totalHeight, fontSize / 3);
            lines = Lettering.Wrap(text.Text, font, paint, maxWidth);
            totalHeight = font.Spacing * lines.Count;
        }

        var (x, align) = text.Style.Align switch
        {
            TextAlign.Left => ((float)area.Left, SKTextAlign.Left),
            TextAlign.Right => ((float)area.Right, SKTextAlign.Right),
            _ => ((float)area.MidX, SKTextAlign.Center)
        };
        var startY = (float)area.MidY - totalHeight / 2 - font.Metrics.Ascent;

        using var outline = text.Style.Outline is { } outlineColor
            ? new SKPaint
            {
                Color = FigureGeometry.ToSk(outlineColor),
                Style = SKPaintStyle.Stroke,
                // In proportion to the letters unless a weight was picked; shrunk along with them when the text shrinks to fit.
                StrokeWidth = (float)(text.Style.OutlineWidthMm ?? fontSize * OutlineFraction) * font.Size / fontSize,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            }
            : null;
        for (var i = 0; i < lines.Count; i++)
        {
            var y = startY + font.Spacing * i;
            if (outline != null)
                canvas.DrawText(lines[i], x, y, align, font, outline);
            if (text.Style.Color is not null)
                canvas.DrawText(lines[i], x, y, align, font, paint);
        }
    }

    // ---------------------------------------------------------------- hit testing

    /// <summary>
    /// Whether <paramref name="point"/> (page mm) is on the element, within
    /// <paramref name="tolerance"/> (mm) of its outline: a filled shape anywhere inside, an
    /// unfilled one or a line only along its stroke (you can see - and click - through it),
    /// text and pictures anywhere in their box.
    /// </summary>
    public static bool Hits(PanelElement element, Point2D point, double tolerance)
    {
        switch (element)
        {
            case ShapeElement shape:
                using (var path = ShapePath(shape))
                {
                    if (shape.Closed && shape.Style.Fill is not null && path.Contains((float)point.X, (float)point.Y))
                        return true;
                    var width = (shape.Style.Stroke is null ? 0 : shape.Style.StrokeWidthMm) + 2 * tolerance;
                    using var paint = StrokePaint(shape, (float)width);
                    paint.StrokeCap = SKStrokeCap.Round;
                    paint.StrokeJoin = SKStrokeJoin.Round;
                    using var band = paint.GetFillPath(path);
                    return band.Contains((float)point.X, (float)point.Y);
                }
            case TextElement or PictureElement:
                var b = PanelElements.Bounds(element);
                return point.X >= b.Left - tolerance && point.X <= b.Right + tolerance && point.Y >= b.Top - tolerance && point.Y <= b.Bottom + tolerance;
            default:
                return false;
        }
    }

    private static SKRect ToSk(Rect2D r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
}

/// <summary>Word's line "Dashes" as Skia dash patterns, scaled to the line's thickness so a thick dotted line has fat, round dots.</summary>
public static class LinePatterns
{
    /// <summary>The on/off lengths of <paramref name="dash"/> for a line <paramref name="width"/> thick, or null for a solid line.</summary>
    public static float[]? Intervals(LineDash dash, float width) => dash switch
    {
        // A zero-length "on" with round caps draws a dot the line's width across.
        LineDash.RoundDot => [0.001f, 2 * width],
        LineDash.SquareDot => [width, width],
        LineDash.Dash => [4 * width, 3 * width],
        LineDash.DashDot => [4 * width, 3 * width, width, 3 * width],
        LineDash.LongDash => [8 * width, 3 * width],
        LineDash.LongDashDot => [8 * width, 3 * width, width, 3 * width],
        _ => null
    };

    /// <summary>Gives <paramref name="paint"/> the dash pattern - with round caps for round dots, flat ones for everything else so round ends don't eat into the gaps, as in Word; the returned effect must outlive the drawing - dispose it after.</summary>
    public static SKPathEffect? Apply(SKPaint paint, LineDash dash)
    {
        if (Intervals(dash, Math.Max(paint.StrokeWidth, 0.01f)) is not { } intervals)
            return null;
        paint.StrokeCap = dash == LineDash.RoundDot ? SKStrokeCap.Round : SKStrokeCap.Butt;
        var effect = SKPathEffect.CreateDash(intervals, 0);
        paint.PathEffect = effect;
        return effect;
    }
}
