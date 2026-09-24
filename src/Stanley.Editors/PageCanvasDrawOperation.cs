using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>An immutable snapshot of everything one frame draws - the draw operation runs on the render thread, so it must not read the live view model.</summary>
public sealed record PageCanvasScene(
    PageDocument Document,
    Rect2D PageBounds,
    PanelGrid Grid,
    double Zoom,
    Point2D Offset,
    PanelId? SelectedPanelId,
    int SelectedBubbleIndex,
    PanelId? HoverPanelId,
    GutterHit? HighlightGutter,
    IReadOnlyList<SnapGuide> Guides,
    Rect2D? RubberBand,
    bool RubberBandIsBubble,
    bool ShowMarginGuides = true,
    PageFolio? Folio = null,
    bool DarkChrome = false,
    IReadOnlyDictionary<CharacterId, CharacterDefinition>? Characters = null,
    int SelectedCharacterIndex = -1,
    Rect2D? SelectedCharacterBounds = null,
    IReadOnlyList<Point2D>? LimbHandles = null);

/// <summary>
/// Draws the page in two passes: the artwork in page space (millimetres, under the
/// zoom transform, so it scales like print), then editor chrome - selection, handles,
/// guides - in screen space, so handles stay a constant, grabbable size at any zoom.
/// </summary>
public sealed class PageCanvasDrawOperation : ICustomDrawOperation
{
    public const float FontSizeMm = PageRenderer.FontSizeMm;
    public const float TailBaseHalfWidthMm = PageRenderer.TailBaseHalfWidthMm;

    private static readonly SKColor Pasteboard = new(0xDD, 0xDF, 0xE3);

    /// <summary>The desk around the page in dark mode. The page itself stays white - it's paper, and the print is what's being judged.</summary>
    private static readonly SKColor DarkPasteboard = new(0x2B, 0x2D, 0x31);
    private static readonly SKColor Accent = new(0x25, 0x7A, 0xE8);
    private static readonly SKColor TailHandle = new(0xF5, 0x8A, 0x07);
    private static readonly SKColor PoseHandle = new(0x2E, 0x9E, 0x5B);
    private static readonly SKColor GuideColor = new(0xE0, 0x2F, 0x8C);
    private static readonly SKColor MarginColor = new(0x5B, 0xC0, 0xDE);

    private readonly Rect _bounds;
    private readonly PageCanvasScene _scene;

    public PageCanvasDrawOperation(Rect bounds, PageCanvasScene scene)
    {
        _bounds = bounds;
        _scene = scene;
    }

    public Rect Bounds => _bounds;

    public void Dispose() { }

    public bool Equals(ICustomDrawOperation? other) => false;

    public bool HitTest(Point p) => _bounds.Contains(p);

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature == null)
            return;

        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;

        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, (float)_bounds.Width, (float)_bounds.Height));
        canvas.DrawColor(_scene.DarkChrome ? DarkPasteboard : Pasteboard);

        canvas.Save();
        canvas.Translate((float)_scene.Offset.X, (float)_scene.Offset.Y);
        canvas.Scale((float)_scene.Zoom);
        DrawPage(canvas);
        canvas.Restore();

        DrawChrome(canvas);
        canvas.Restore();
    }

    // ---------------------------------------------------------------- page space (mm)

    private void DrawPage(SKCanvas canvas)
    {
        var page = ToSk(_scene.PageBounds);
        var px = 1f / (float)_scene.Zoom; // one screen pixel, in mm

        using (var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 60), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6 * px), IsAntialias = true })
            canvas.DrawRect(SKRect.Create(page.Left + 2 * px, page.Top + 3 * px, page.Width, page.Height), shadow);
        PageRenderer.DrawPaper(canvas, _scene.PageBounds);
        if (_scene.ShowMarginGuides)
        {
            // Margin guide: where the live area (and the snap grid) starts. Drawn under
            // the panels, so it only shows where the page isn't covered yet.
            using var margin = new SKPaint
            {
                Color = MarginColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = px,
                PathEffect = SKPathEffect.CreateDash([4 * px, 4 * px], 0),
                IsAntialias = true
            };
            canvas.DrawRect(ToSk(_scene.Grid.LiveArea(_scene.PageBounds)), margin);
        }

        PageRenderer.DrawPanels(canvas, _scene.Document.PanelOrder
            .Where(_scene.Document.Panels.ContainsKey)
            .Select(id => _scene.Document.Panels[id])
            .ToList(), _scene.Characters);
        if (_scene.Folio != null)
            PageRenderer.DrawFolio(canvas, _scene.PageBounds, _scene.Folio);
    }

    // ---------------------------------------------------------------- screen space (px)

    private void DrawChrome(SKCanvas canvas)
    {
        var doc = _scene.Document;

        if (_scene.HoverPanelId is { } hoverId && !hoverId.Equals(_scene.SelectedPanelId) && doc.Panels.TryGetValue(hoverId, out var hovered))
        {
            using var hover = Stroke(Accent.WithAlpha(110), 1.5f);
            canvas.DrawRect(Screen(AnchorRing.BoundingBox(hovered.Shape.Anchors)), hover);
        }

        if (_scene.HighlightGutter is { } gutter)
            DrawGutter(canvas, gutter);

        if (_scene.SelectedPanelId is { } selectedId && doc.Panels.TryGetValue(selectedId, out var selectedPanel))
        {
            var panelRect = Screen(AnchorRing.BoundingBox(selectedPanel.Shape.Anchors));
            var hasBubble = _scene.SelectedBubbleIndex >= 0 && _scene.SelectedBubbleIndex < selectedPanel.Bubbles.Count;
            var hasCharacter = !hasBubble && _scene.SelectedCharacterIndex >= 0
                && _scene.SelectedCharacterIndex < selectedPanel.CharacterInstances.Count && _scene.SelectedCharacterBounds is not null;

            using (var outline = Stroke(Accent.WithAlpha(hasBubble || hasCharacter ? (byte)120 : (byte)255), 2f))
                canvas.DrawRect(panelRect, outline);
            if (hasBubble)
            {
                DrawBubbleSelection(canvas, selectedPanel.Bubbles[_scene.SelectedBubbleIndex]);
            }
            else if (hasCharacter)
            {
                DrawCharacterSelection(canvas, selectedPanel.CharacterInstances[_scene.SelectedCharacterIndex], _scene.SelectedCharacterBounds!.Value);
            }
            else
            {
                foreach (var corner in Corners(panelRect))
                    DrawSquareHandle(canvas, corner, Accent);
            }
        }

        if (_scene.Guides.Count > 0)
        {
            using var guide = Stroke(GuideColor, 1f);
            var page = Screen(_scene.PageBounds);
            foreach (var g in _scene.Guides)
            {
                if (g.Orientation == BoundaryOrientation.Vertical)
                {
                    var x = ScreenX(g.Position);
                    canvas.DrawLine(x, page.Top - 12, x, page.Bottom + 12, guide);
                }
                else
                {
                    var y = ScreenY(g.Position);
                    canvas.DrawLine(page.Left - 12, y, page.Right + 12, y, guide);
                }
            }
        }

        if (_scene.RubberBand is { } band)
        {
            using var dashed = Stroke(Accent, 1.5f);
            dashed.PathEffect = SKPathEffect.CreateDash([6, 4], 0);
            var rect = Screen(band);
            if (_scene.RubberBandIsBubble)
                canvas.DrawOval(rect, dashed);
            else
                canvas.DrawRect(rect, dashed);
        }
    }

    private void DrawBubbleSelection(SKCanvas canvas, ProjectModel.Bubbles.Bubble bubble)
    {
        var rect = Screen(AnchorRing.BoundingBox(bubble.Shape.Anchors));
        using (var box = Stroke(Accent, 1f))
        {
            box.PathEffect = SKPathEffect.CreateDash([4, 3], 0);
            canvas.DrawRect(rect, box);
        }

        foreach (var corner in Corners(rect))
            DrawSquareHandle(canvas, corner, Accent);
        foreach (var mid in new[] { new SKPoint(rect.MidX, rect.Top), new SKPoint(rect.Right, rect.MidY), new SKPoint(rect.MidX, rect.Bottom), new SKPoint(rect.Left, rect.MidY) })
            DrawSquareHandle(canvas, mid, Accent, 3.5f);

        using var fill = new SKPaint { Color = TailHandle, IsAntialias = true };
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var ring = Stroke(TailHandle, 2f);
        using var outline = Stroke(SKColors.White, 1.5f);
        foreach (var tail in bubble.Tails)
        {
            var baseScreen = Screen(AnchorRing.PointAt(bubble.Shape.Anchors, tail.AttachmentT));
            canvas.DrawCircle(baseScreen, 4.5f, white);
            canvas.DrawCircle(baseScreen, 4.5f, ring);

            var tip = Screen(tail.Target);
            canvas.DrawCircle(tip, 6f, fill);
            canvas.DrawCircle(tip, 6f, outline);
        }
    }

    /// <summary>A dashed box around the figure, resize handles on its two top corners (it scales about its feet) and a marker on the ground point.</summary>
    private void DrawCharacterSelection(SKCanvas canvas, ProjectModel.Issues.CharacterInstance instance, Rect2D bounds)
    {
        var rect = Screen(bounds);
        using (var box = Stroke(Accent, 1f))
        {
            box.PathEffect = SKPathEffect.CreateDash([4, 3], 0);
            canvas.DrawRect(rect, box);
        }
        DrawSquareHandle(canvas, new SKPoint(rect.Left, rect.Top), Accent);
        DrawSquareHandle(canvas, new SKPoint(rect.Right, rect.Top), Accent);

        // Hands and feet: drag one to pose that limb.
        using (var limbFill = new SKPaint { Color = PoseHandle, IsAntialias = true })
        using (var limbRing = Stroke(SKColors.White, 1.5f))
        {
            foreach (var handle in _scene.LimbHandles ?? [])
            {
                var p = Screen(handle);
                canvas.DrawCircle(p, 5.5f, limbFill);
                canvas.DrawCircle(p, 5.5f, limbRing);
            }
        }

        var ground = Screen(instance.Placement.Ground);
        using var fill = new SKPaint { Color = TailHandle, IsAntialias = true };
        using var outline = Stroke(SKColors.White, 1.5f);
        using var diamond = new SKPathBuilder();
        diamond.MoveTo(ground.X, ground.Y - 5);
        diamond.LineTo(ground.X + 5, ground.Y);
        diamond.LineTo(ground.X, ground.Y + 5);
        diamond.LineTo(ground.X - 5, ground.Y);
        diamond.Close();
        using var path = diamond.Detach();
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, outline);
    }

    private void DrawGutter(SKCanvas canvas, GutterHit gutter)
    {
        var vertical = gutter.Drag.Orientation == BoundaryOrientation.Vertical;
        var gap = Math.Max(gutter.Drag.Gap, 0);
        var mm = vertical
            ? Rect2D.FromEdges(gutter.Position, gutter.SpanStart, gutter.Position + gap, gutter.SpanEnd)
            : Rect2D.FromEdges(gutter.SpanStart, gutter.Position, gutter.SpanEnd, gutter.Position + gap);
        var rect = Screen(mm);
        // Always at least a few pixels thick, so a zero-width boundary still shows.
        if (vertical && rect.Width < 4) rect.Inflate((4 - rect.Width) / 2, 0);
        if (!vertical && rect.Height < 4) rect.Inflate(0, (4 - rect.Height) / 2);

        using var fill = new SKPaint { Color = TailHandle.WithAlpha(150), IsAntialias = true };
        canvas.DrawRect(rect, fill);
    }

    private static void DrawSquareHandle(SKCanvas canvas, SKPoint center, SKColor color, float half = 4.5f)
    {
        var rect = new SKRect(center.X - half, center.Y - half, center.X + half, center.Y + half);
        using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var stroke = Stroke(color, 1.5f);
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, stroke);
    }

    private static SKPaint Stroke(SKColor color, float width) =>
        new() { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };

    private static SKPoint[] Corners(SKRect r) =>
        [new(r.Left, r.Top), new(r.Right, r.Top), new(r.Right, r.Bottom), new(r.Left, r.Bottom)];

    private float ScreenX(double x) => (float)(_scene.Offset.X + x * _scene.Zoom);
    private float ScreenY(double y) => (float)(_scene.Offset.Y + y * _scene.Zoom);
    private SKPoint Screen(Point2D p) => new(ScreenX(p.X), ScreenY(p.Y));
    private SKRect Screen(Rect2D r) => new(ScreenX(r.Left), ScreenY(r.Top), ScreenX(r.Right), ScreenY(r.Bottom));

    private static SKRect ToSk(Rect2D r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
}
