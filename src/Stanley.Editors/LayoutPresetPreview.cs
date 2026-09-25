using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Stanley.Editing;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Editors;

/// <summary>
/// A thumbnail of a <see cref="PanelLayoutPreset"/> on a page, so a layout or template
/// picker shows each grid instead of just naming it: an A-series page with exaggerated
/// spacing unless <see cref="PageSize"/> and <see cref="Grid"/> give the real ones (a
/// strip, a webcomic page). The page is centred in the control, whatever its shape.
/// </summary>
public sealed class LayoutPresetPreview : Control
{
    private static readonly PageSize ThumbnailPage = new(210, 297);
    private static readonly PanelGrid ThumbnailGrid = new(14, 8);

    public static readonly StyledProperty<PanelLayoutPreset?> PresetProperty =
        AvaloniaProperty.Register<LayoutPresetPreview, PanelLayoutPreset?>(nameof(Preset));

    public static readonly StyledProperty<PageSize?> PageSizeProperty =
        AvaloniaProperty.Register<LayoutPresetPreview, PageSize?>(nameof(PageSize));

    public static readonly StyledProperty<PanelGrid?> GridProperty =
        AvaloniaProperty.Register<LayoutPresetPreview, PanelGrid?>(nameof(Grid));

    static LayoutPresetPreview()
    {
        AffectsRender<LayoutPresetPreview>(PresetProperty, PageSizeProperty, GridProperty);
        AffectsMeasure<LayoutPresetPreview>(PageSizeProperty);
    }

    public PanelLayoutPreset? Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    /// <summary>The page the layout is shown on; an A4 page if null.</summary>
    public PageSize? PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    /// <summary>The margin and gutter to show; wider than real ones (so they read at thumbnail size) if null.</summary>
    public PanelGrid? Grid
    {
        get => GetValue(GridProperty);
        set => SetValue(GridProperty, value);
    }

    private Rect2D Page => PageSize is { } size ? new Rect2D(0, 0, size.WidthMm, size.HeightMm) : new Rect2D(0, 0, ThumbnailPage.WidthMm, ThumbnailPage.HeightMm);

    protected override Size MeasureOverride(Size availableSize) => new(42, 42 * Page.Height / Page.Width);

    public override void Render(DrawingContext context)
    {
        var page = Page;
        var scale = Math.Min(Bounds.Width / page.Width, Bounds.Height / page.Height);
        var left = (Bounds.Width - page.Width * scale) / 2;
        var top = (Bounds.Height - page.Height * scale) / 2;
        Rect ToRect(Rect2D r) => new(left + r.X * scale, top + r.Y * scale, r.Width * scale, r.Height * scale);

        context.DrawRectangle(Brushes.White, new Pen(Brushes.Gray, 1), ToRect(page));
        if (Preset is null)
            return;

        var layout = PanelLayoutEditing.GridLayout(page, Grid ?? ThumbnailGrid, Preset.ColumnsPerRow);
        if (!layout.IsValid)
            return;

        var fill = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF8));
        var pen = new Pen(Brushes.Black, 1.2);
        foreach (var rect in layout.Value)
            context.DrawRectangle(fill, pen, ToRect(rect));
    }
}
