using SkiaSharp;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Rendering;

/// <summary>
/// Draws a page's artwork - white paper, panels, their bubbles - in page space
/// (millimetres). The editor canvas draws through this under its zoom transform, and
/// export draws through it under a points-per-mm (PDF) or pixels-per-mm (PNG) scale, so
/// what you see on screen is exactly what gets exported.
/// </summary>
public static class PageRenderer
{
    /// <summary>Lettering size: ~10pt, the usual comic dialogue size at print.</summary>
    public const float FontSizeMm = 3.5f;
    public const float BubbleStrokeMm = 0.35f;
    public const float PanelBorderMm = 0.7f;
    public const float TailBaseHalfWidthMm = 2.5f;

    public static void Draw(SKCanvas canvas, Rect2D pageBounds, IEnumerable<Panel> panelsInOrder)
    {
        DrawPaper(canvas, pageBounds);
        DrawPanels(canvas, panelsInOrder);
    }

    public static void DrawPaper(SKCanvas canvas, Rect2D pageBounds)
    {
        using var paper = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(ToSk(pageBounds), paper);
    }

    /// <summary>Panels in z-order, each with its bubbles clipped to it and its border on top.</summary>
    public static void DrawPanels(SKCanvas canvas, IEnumerable<Panel> panelsInOrder)
    {
        using var border = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = PanelBorderMm, IsAntialias = true, StrokeJoin = SKStrokeJoin.Miter };
        using var panelFill = new SKPaint { Color = SKColors.White };
        foreach (var panel in panelsInOrder)
        {
            using var path = PanelRenderer.ToSkPath(panel.Shape);
            canvas.DrawPath(path, panelFill);

            // A bubble belongs to its panel: clip it there, like ink that can't leave the frame.
            canvas.Save();
            canvas.ClipPath(path, antialias: true);
            foreach (var bubble in panel.Bubbles)
                BubbleRenderer.Draw(canvas, bubble, SKColors.White, SKColors.Black, BubbleStrokeMm, FontSizeMm, TailBaseHalfWidthMm);
            canvas.Restore();

            canvas.DrawPath(path, border);
        }
    }

    /// <summary>Writes the page as a one-page vector PDF at its real trim size.</summary>
    public static void ExportPdf(Stream output, Rect2D pageBounds, IEnumerable<Panel> panelsInOrder)
    {
        const float pointsPerMm = 72f / 25.4f;
        using var document = SKDocument.CreatePdf(output)
            ?? throw new InvalidOperationException("PDF export isn't available on this platform.");
        var canvas = document.BeginPage((float)pageBounds.Width * pointsPerMm, (float)pageBounds.Height * pointsPerMm);
        canvas.Scale(pointsPerMm);
        canvas.Translate(-(float)pageBounds.Left, -(float)pageBounds.Top);
        Draw(canvas, pageBounds, panelsInOrder);
        document.EndPage();
        document.Close();
    }

    /// <summary>Writes the page as a PNG at <paramref name="dpi"/> (300 = print quality).</summary>
    public static void ExportPng(Stream output, Rect2D pageBounds, IEnumerable<Panel> panelsInOrder, int dpi = 300)
    {
        var pixelsPerMm = dpi / 25.4f;
        var width = (int)Math.Round(pageBounds.Width * pixelsPerMm);
        var height = (int)Math.Round(pageBounds.Height * pixelsPerMm);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Couldn't allocate an image that large.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale(pixelsPerMm);
        canvas.Translate(-(float)pageBounds.Left, -(float)pageBounds.Top);
        Draw(canvas, pageBounds, panelsInOrder);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(output);
    }

    private static SKRect ToSk(Rect2D r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
}
