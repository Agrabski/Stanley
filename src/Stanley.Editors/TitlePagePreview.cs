using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>
/// A miniature of a title page design on the comic's own page size and spacing - drawn
/// by <see cref="Stanley.Rendering.PageRenderer"/> from what <see cref="TitlePages.Compose"/>
/// would insert, so the Insert › Title page gallery shows exactly what a click gives.
/// </summary>
public sealed class TitlePagePreview : Control
{
    // What the fields show with no comic to take them from.
    private static readonly TextFields SampleFields = new("Comic Title", "1");
    private static readonly IReadOnlyDictionary<CharacterId, CharacterDefinition> NoCharacters = new Dictionary<CharacterId, CharacterDefinition>();
    private static readonly IReadOnlyDictionary<CharacterId, CharacterRevisionId> NoLooks = new Dictionary<CharacterId, CharacterRevisionId>();
    private static readonly IReadOnlyDictionary<string, ArtFile> NoPictures = new Dictionary<string, ArtFile>();

    public static readonly StyledProperty<TitlePageDesign> DesignProperty =
        AvaloniaProperty.Register<TitlePagePreview, TitlePageDesign>(nameof(Design));

    /// <summary>The page editor whose page size and spacing to show the design on; an A4 page if null.</summary>
    public static readonly StyledProperty<PageEditorViewModel?> PageProperty =
        AvaloniaProperty.Register<TitlePagePreview, PageEditorViewModel?>(nameof(Page));

    private PageDocument? _document;
    private Rect2D _pageBounds = new(0, 0, 210, 297);

    static TitlePagePreview()
    {
        AffectsRender<TitlePagePreview>(DesignProperty, PageProperty);
        AffectsMeasure<TitlePagePreview>(PageProperty);
    }

    public TitlePageDesign Design
    {
        get => GetValue(DesignProperty);
        set => SetValue(DesignProperty, value);
    }

    public PageEditorViewModel? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DesignProperty || change.Property == PageProperty)
            _document = null; // composed again (fresh ids and all) on the next render
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var page = Page?.PageBounds ?? _pageBounds;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 80;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : width * page.Height / page.Width;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        if (_document is null)
        {
            _pageBounds = Page?.PageBounds ?? _pageBounds;
            var panels = TitlePages.Compose(Design, _pageBounds, Page?.Grid ?? PanelGrid.Default, TitlePages.DefaultWords);
            _document = new PageDocument(panels.Select(p => p.Id).ToList(), panels.ToDictionary(p => p.Id), TitlePage: TitlePageScope.Comic);
        }

        // Centred, whatever the page's shape, with a hairline edge so white paper shows on a white menu.
        var scale = Math.Min(Bounds.Width / _pageBounds.Width, Bounds.Height / _pageBounds.Height);
        var size = new Size(_pageBounds.Width * scale, _pageBounds.Height * scale);
        var frame = new Rect(new Point((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2), size);
        context.Custom(new PageThumbnail.ThumbnailDrawOperation(frame, _pageBounds, _document, null, NoCharacters, NoLooks, NoPictures, Page?.Fields ?? SampleFields));
        context.DrawRectangle(null, new Pen(Brushes.Gray, 1), frame);
    }
}
