using Stanley.ProjectModel;

namespace Stanley.Editing;

/// <summary>Which part of File › New a template is listed under.</summary>
public enum ComicTemplateKind
{
    /// <summary>Printed comic strips: a newspaper daily, a Sunday strip, a four-panel gag.</summary>
    Strip,

    /// <summary>Comics read on screen, whose pages are exported as pictures a set number of pixels wide.</summary>
    Webcomic
}

/// <summary>
/// A starting point for a comic that isn't a comic book page, like Word's templates: the
/// page size, the panels every page starts with and the spacing between them, and for a
/// webcomic the width its pages are exported at, in pixels. Everything stays adjustable
/// afterwards; a template only picks the first values. Sizes are metric like everything
/// else, chosen so a webcomic's pixel size comes out exact (a 200 mm page at 800 px).
/// </summary>
/// <param name="PanelsPerRow">Every new page's panels: rows top to bottom, each split into this many equal panels.</param>
/// <param name="ExportWidthPx">For a webcomic, the width a page is exported at, in pixels; null for print.</param>
public sealed record ComicTemplate(
    string Name,
    string Description,
    ComicTemplateKind Kind,
    PageTrim Trim,
    PanelGrid Grid,
    IReadOnlyList<int> PanelsPerRow,
    int? ExportWidthPx = null)
{
    /// <summary>The name as typed on a command line: "daily-strip", "vertical-scroll".</summary>
    public string Key => Name.ToLowerInvariant().Replace(' ', '-');

    /// <summary>The panels every page starts with, as a layout the page editor can tile with.</summary>
    public PanelLayoutPreset Layout => new(Name, PanelsPerRow);

    /// <summary>What a comic made from this template records about itself (<see cref="SeriesManifest.Format"/>).</summary>
    public ComicFormat Format => new(Grid.MarginMm, Grid.GutterMm, PanelsPerRow, ExportWidthPx);

    /// <summary>An exported page's height in pixels, in proportion to <see cref="ExportWidthPx"/>; null for print.</summary>
    public int? ExportHeightPx => ExportWidthPx is { } width ? ComicTemplates.ExportHeightPx(width, Trim.Size) : null;
}

/// <summary>File › New's templates for comic strips and webcomics (comic book pages are the paper sizes and layouts).</summary>
public static class ComicTemplates
{
    // Strips are printed inside a newspaper or book page rather than trimmed, so no bleed.
    private static readonly PanelGrid StripGrid = new(5, 4);

    public static IReadOnlyList<ComicTemplate> All { get; } =
    [
        new("Daily strip", "Four panels in a row, like a newspaper's weekday strip.",
            ComicTemplateKind.Strip, Trim(330, 105), StripGrid, [4]),
        new("Sunday strip", "Three tiers, with room for a title panel and a longer gag.",
            ComicTemplateKind.Strip, Trim(330, 225), StripGrid, [2, 3, 3]),
        new("Four-panel strip", "Four panels stacked and read downwards, like a Japanese 4-koma.",
            ComicTemplateKind.Strip, Trim(90, 262), StripGrid, [1, 1, 1, 1]),
        new("Vertical scroll", "Tall pages read by scrolling down, exported at 800 × 1280 px - the size WEBTOON Canvas takes.",
            ComicTemplateKind.Webcomic, Trim(200, 320), new PanelGrid(10, 20), [1, 1], 800),
        new("Web strip", "Three panels in a row, exported at 1200 × 400 px - a classic webcomic strip.",
            ComicTemplateKind.Webcomic, Trim(300, 100), StripGrid, [3], 1200),
        new("Square post", "Four panels on a square, exported at 1080 × 1080 px - for Instagram and the like.",
            ComicTemplateKind.Webcomic, Trim(200, 200), new PanelGrid(8, 5), [2, 2], 1080),
        new("Portrait post", "A tall post, exported at 1080 × 1350 px - a wide panel over two.",
            ComicTemplateKind.Webcomic, Trim(200, 250), new PanelGrid(8, 5), [1, 2], 1080),
    ];

    public static IEnumerable<ComicTemplate> OfKind(ComicTemplateKind kind) => All.Where(t => t.Kind == kind);

    /// <summary>The template whose page is <paramref name="size"/>, if any - to name a comic's format.</summary>
    public static ComicTemplate? Matching(PageSize size) => All.FirstOrDefault(t => t.Trim.Size == size);

    /// <summary>A page's height in pixels when it's exported <paramref name="widthPx"/> wide.</summary>
    public static int ExportHeightPx(int widthPx, PageSize size) => (int)Math.Round(widthPx * size.HeightMm / size.WidthMm);

    private static PageTrim Trim(double widthMm, double heightMm) => new(new PageSize(widthMm, heightMm), BleedMm: 0);
}
