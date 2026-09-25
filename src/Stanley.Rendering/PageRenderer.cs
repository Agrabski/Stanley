using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Rendering;

/// <summary>A page number as printed: its text, where it goes, and which side of the spread the page is on (so "outer corner" knows which corner).</summary>
public sealed record PageFolio(string Text, PageNumberPosition Position, bool IsRightHandPage);

/// <summary>
/// Draws a page's artwork - white paper, panels, their characters and bubbles - in page space
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
    public const float CharacterStrokeMm = 0.45f;

    /// <summary>Page numbers: ~9pt, centred this far in from the trim edge (inside a 10mm margin, clear of a 3mm bleed) and aligned with the default margin at the sides.</summary>
    public const float FolioFontSizeMm = 3.2f;
    public const float FolioEdgeDistanceMm = 5.5f;
    public const float FolioSideDistanceMm = 10f;

    /// <param name="characters">The project's characters, to draw the panels' character instances with; an instance whose character isn't in here draws as a placeholder.</param>
    /// <param name="issueLooks">The issue's look per character (<see cref="Issue.CharacterRevisions"/>), for instances without a look of their own.</param>
    public static void Draw(SKCanvas canvas, Rect2D pageBounds, IEnumerable<Panel> panelsInOrder, PageFolio? folio = null,
        IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters = null, IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null)
    {
        DrawPaper(canvas, pageBounds);
        DrawPanels(canvas, panelsInOrder, characters, issueLooks);
        if (folio != null)
            DrawFolio(canvas, pageBounds, folio);
    }

    /// <summary>Prints the page number in the bottom or top margin, on top of everything else so it's never lost under art.</summary>
    public static void DrawFolio(SKCanvas canvas, Rect2D pageBounds, PageFolio folio)
    {
        if (folio.Position == PageNumberPosition.None || string.IsNullOrEmpty(folio.Text))
            return;

        var outerRight = folio.IsRightHandPage;
        var (x, align) = folio.Position switch
        {
            PageNumberPosition.BottomCenter => ((float)pageBounds.MidX, SKTextAlign.Center),
            _ when outerRight => ((float)pageBounds.Right - FolioSideDistanceMm, SKTextAlign.Right),
            _ => ((float)pageBounds.Left + FolioSideDistanceMm, SKTextAlign.Left)
        };
        var centerY = folio.Position == PageNumberPosition.TopOuter
            ? (float)pageBounds.Top + FolioEdgeDistanceMm
            : (float)pageBounds.Bottom - FolioEdgeDistanceMm;

        using var font = new SKFont(SKTypeface.Default, FolioFontSizeMm) { LinearMetrics = true, Subpixel = true };
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        // Centre the digits' height on the line, not the baseline.
        var baseline = centerY - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
        canvas.DrawText(folio.Text, x, baseline, align, font, paint);
    }

    public static void DrawPaper(SKCanvas canvas, Rect2D pageBounds)
    {
        using var paper = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(ToSk(pageBounds), paper);
    }

    /// <summary>Panels in z-order, each with its characters and then its bubbles clipped to it, and its border on top.</summary>
    public static void DrawPanels(SKCanvas canvas, IEnumerable<Panel> panelsInOrder, IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters = null,
        IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null)
    {
        using var border = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = PanelBorderMm, IsAntialias = true, StrokeJoin = SKStrokeJoin.Miter };
        using var panelFill = new SKPaint { Color = SKColors.White };
        foreach (var panel in panelsInOrder)
        {
            using var path = PanelRenderer.ToSkPath(panel.Shape);
            canvas.DrawPath(path, panelFill);

            // Characters and bubbles belong to their panel: clip them there, like ink that can't leave the frame.
            canvas.Save();
            canvas.ClipPath(path, antialias: true);
            foreach (var instance in panel.CharacterInstances)
                CharacterRenderers.DrawInstance(canvas, instance, characters, CharacterStrokeMm, issueLooks);
            foreach (var bubble in panel.Bubbles)
                BubbleRenderer.Draw(canvas, bubble, SKColors.White, SKColors.Black, BubbleStrokeMm, FontSizeMm, TailBaseHalfWidthMm);
            canvas.Restore();

            canvas.DrawPath(path, border);
        }
    }

    /// <summary>Writes the page as a one-page vector PDF at its real trim size.</summary>
    public static void ExportPdf(Stream output, Rect2D pageBounds, IEnumerable<Panel> panelsInOrder, PageFolio? folio = null,
        IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters = null, IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null) =>
        ExportPdf(output, [(pageBounds, panelsInOrder, folio)], characters, issueLooks);

    /// <summary>Writes every page, in order, into one vector PDF, each at its real trim size.</summary>
    public static void ExportPdf(Stream output, IEnumerable<(Rect2D Bounds, IEnumerable<Panel> PanelsInOrder, PageFolio? Folio)> pages,
        IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters = null, IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null)
    {
        const float pointsPerMm = 72f / 25.4f;
        using var document = SKDocument.CreatePdf(output)
            ?? throw new InvalidOperationException("PDF export isn't available on this platform.");
        foreach (var (pageBounds, panels, folio) in pages)
        {
            var canvas = document.BeginPage((float)pageBounds.Width * pointsPerMm, (float)pageBounds.Height * pointsPerMm);
            canvas.Scale(pointsPerMm);
            canvas.Translate(-(float)pageBounds.Left, -(float)pageBounds.Top);
            Draw(canvas, pageBounds, panels, folio, characters, issueLooks);
            document.EndPage();
        }
        document.Close();
    }

    /// <summary>Writes the page as a PNG at <paramref name="dpi"/> (300 = print quality).</summary>
    public static void ExportPng(Stream output, Rect2D pageBounds, IEnumerable<Panel> panelsInOrder, int dpi = 300, PageFolio? folio = null,
        IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters = null, IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null)
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
        Draw(canvas, pageBounds, panelsInOrder, folio, characters, issueLooks);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(output);
    }

    private static SKRect ToSk(Rect2D r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
}
