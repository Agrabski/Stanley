using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Stanley.ProjectModel.Geometry;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>
/// A live miniature of a page: the same <see cref="PageRenderer"/> drawing as the editor
/// and export, scaled to fit, redrawn whenever the page's content changes (so it follows
/// edits, drags and undo as they happen). Width comes from the layout; height follows
/// the page's aspect ratio.
/// </summary>
public sealed class PageThumbnail : Control
{
    public static readonly StyledProperty<PageEditorViewModel?> PageProperty =
        AvaloniaProperty.Register<PageThumbnail, PageEditorViewModel?>(nameof(Page));

    private PageEditorViewModel? _subscribed;

    public PageEditorViewModel? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
        {
            Subscribe(VisualRoot != null ? Page : null);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(Page);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Subscribe(null); // don't let a scrolled-away or removed thumbnail keep the page alive
    }

    private void Subscribe(PageEditorViewModel? page)
    {
        if (_subscribed != null)
            _subscribed.PropertyChanged -= OnPagePropertyChanged;
        _subscribed = page;
        if (_subscribed != null)
            _subscribed.PropertyChanged += OnPagePropertyChanged;
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PageEditorViewModel.Working) or nameof(PageEditorViewModel.Folio) or nameof(PageEditorViewModel.CharacterSnapshot))
            InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var page = Page?.PageBounds ?? new Rect2D(0, 0, 210, 297);
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 120;
        return new Size(width, width * page.Height / page.Width);
    }

    public override void Render(DrawingContext context)
    {
        if (Page is not { } page)
            return;
        context.Custom(new ThumbnailDrawOperation(new Rect(Bounds.Size), page.PageBounds, page.Working, page.Folio, page.CharacterSnapshot));
    }

    private sealed class ThumbnailDrawOperation(Rect bounds, Rect2D pageBounds, PageDocument document, PageFolio? folio,
        IReadOnlyDictionary<ProjectModel.Ids.CharacterId, ProjectModel.Characters.CharacterDefinition> characters) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public void Dispose() { }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                return;

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var scale = (float)Math.Min(bounds.Width / pageBounds.Width, bounds.Height / pageBounds.Height);
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
            canvas.Scale(scale);
            canvas.Translate(-(float)pageBounds.Left, -(float)pageBounds.Top);
            PageRenderer.Draw(canvas, pageBounds, document.PanelOrder
                .Where(document.Panels.ContainsKey)
                .Select(id => document.Panels[id])
                .ToList(), folio, characters);
            canvas.Restore();
        }
    }
}
