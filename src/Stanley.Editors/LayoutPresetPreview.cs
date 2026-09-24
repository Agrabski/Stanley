using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Stanley.Editing;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Editors;

/// <summary>A thumbnail of a <see cref="PanelLayoutPreset"/> on an A-series page, so the layout picker shows each grid instead of just naming it.</summary>
public sealed class LayoutPresetPreview : Control
{
    private static readonly Rect2D ThumbnailPage = new(0, 0, 210, 297);
    private static readonly PanelGrid ThumbnailGrid = new(14, 8);

    public static readonly StyledProperty<PanelLayoutPreset?> PresetProperty =
        AvaloniaProperty.Register<LayoutPresetPreview, PanelLayoutPreset?>(nameof(Preset));

    static LayoutPresetPreview()
    {
        AffectsRender<LayoutPresetPreview>(PresetProperty);
    }

    public PanelLayoutPreset? Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(42, 42 * ThumbnailPage.Height / ThumbnailPage.Width);

    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Bounds.Width / ThumbnailPage.Width, Bounds.Height / ThumbnailPage.Height);
        Rect ToRect(Rect2D r) => new(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale);

        context.DrawRectangle(Brushes.White, new Pen(Brushes.Gray, 1), ToRect(ThumbnailPage));
        if (Preset is null)
            return;

        var layout = PanelLayoutEditing.GridLayout(ThumbnailPage, ThumbnailGrid, Preset.ColumnsPerRow);
        if (!layout.IsValid)
            return;

        var fill = new SolidColorBrush(Color.FromRgb(0xE8, 0xEE, 0xF8));
        var pen = new Pen(Brushes.Black, 1.2);
        foreach (var rect in layout.Value)
            context.DrawRectangle(fill, pen, ToRect(rect));
    }
}
