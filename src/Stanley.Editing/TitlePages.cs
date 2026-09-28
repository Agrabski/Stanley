using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>The ready-made title page designs (Insert › Title page), like Word's cover pages.</summary>
public enum TitlePageDesign
{
    /// <summary>A sky over the whole page, the title across the top in big outlined letters, room for cover art below.</summary>
    Cover,

    /// <summary>A coloured band across the top holding the title, a big panel for art under it, the credits at the foot.</summary>
    Banner,

    /// <summary>Plain paper with the title centred over a short rule - a book's title page.</summary>
    Classic
}

/// <summary>The words a title page shows: the comic's title, a line under it (the issue, a chapter) and who made it - fields (<see cref="TextFields"/>) and all.</summary>
public sealed record TitlePageWords(string Title, string Subtitle, string Credits);

/// <summary>
/// Composes a title page: ordinary panels, text and shapes, laid out for the page's size,
/// so everything on it stays editable like any other page - retype the words in place,
/// restyle them, swap the background, add characters or a picture. Pure functions, like
/// <see cref="PanelLayoutEditing"/>.
///
/// The title and issue number are fields (<see cref="TextFields"/>): Stanley fills them in
/// from the comic, so they follow File › Info, and the words around them are the user's
/// to change ("Wydanie #{issue}"). The three texts carry fixed element ids
/// (<see cref="TitleId"/>, <see cref="SubtitleId"/>, <see cref="CreditsId"/>), so a page
/// redone in another design keeps whatever was typed into them (<see cref="WordsOn"/>).
/// </summary>
public static class TitlePages
{
    public static readonly ElementId TitleId = ElementId.FromValue("title");
    public static readonly ElementId SubtitleId = ElementId.FromValue("subtitle");
    public static readonly ElementId CreditsId = ElementId.FromValue("credits");

    public static readonly ColorValue Sky = ColorValue.FromHex("#4a90d9");
    public static readonly ColorValue PaleSky = ColorValue.FromHex("#d6eaf8");
    public static readonly ColorValue Navy = ColorValue.FromHex("#1b2a49");
    public static readonly ColorValue White = ColorValue.FromHex("#ffffff");

    // Line heights as fractions of the page's shorter side, so a design scales from a
    // four-panel strip to an A3 cover: the title gets two lines of this before it shrinks.
    private const double TitleLine = 0.14;
    private const double SubtitleLine = 0.06;
    private const double CreditsLine = 0.045;

    /// <summary>How much taller a line of text is than its letters (the default font's line spacing, rounded up).</summary>
    private const double LineSpacing = 1.25;

    public static IReadOnlyList<TitlePageDesign> All { get; } = Enum.GetValues<TitlePageDesign>();

    public static string Name(TitlePageDesign design) => design switch
    {
        TitlePageDesign.Cover => "Cover",
        TitlePageDesign.Banner => "Title band",
        TitlePageDesign.Classic => "Book title page",
        _ => throw new ArgumentOutOfRangeException(nameof(design), design, null)
    };

    public static string Description(TitlePageDesign design) => design switch
    {
        TitlePageDesign.Cover => "A sky over the whole page with the title across the top - add your cover art below it",
        TitlePageDesign.Banner => "The title in a coloured band, a big panel for art underneath and the credits at the foot",
        TitlePageDesign.Classic => "The title centred on plain paper over a short rule, like the first page of a book",
        _ => throw new ArgumentOutOfRangeException(nameof(design), design, null)
    };

    /// <summary>The words a new title page starts with: the comic's title and issue number as fields, and a credit line to fill in.</summary>
    public static TitlePageWords DefaultWords { get; } = new(TextFields.TitleField, $"Issue #{TextFields.IssueField}", "Story and art by Your Name");

    /// <summary>The words on an existing title page, found by their element ids; <paramref name="fallback"/>'s for any it no longer has.</summary>
    public static TitlePageWords WordsOn(IEnumerable<Panel> panels, TitlePageWords fallback)
    {
        var texts = panels.SelectMany(p => p.Elements).OfType<TextElement>().ToList();
        string Find(ElementId id, string otherwise) => texts.FirstOrDefault(t => t.Id == id)?.Text ?? otherwise;
        return new TitlePageWords(Find(TitleId, fallback.Title), Find(SubtitleId, fallback.Subtitle), Find(CreditsId, fallback.Credits));
    }

    /// <summary>
    /// A title page in <paramref name="design"/> for a page of <paramref name="page"/>'s size,
    /// in z-order: the words inside the page's live area (<paramref name="grid"/>'s margin),
    /// backgrounds running to the page edge.
    /// </summary>
    public static IReadOnlyList<Panel> Compose(TitlePageDesign design, Rect2D page, PanelGrid grid, TitlePageWords words)
    {
        var live = grid.LiveArea(page);
        var unit = Math.Min(page.Width, page.Height);
        var title = unit * TitleLine;
        var subtitle = unit * SubtitleLine;
        var credits = unit * CreditsLine;

        switch (design)
        {
            case TitlePageDesign.Cover:
                {
                    var titleBox = new Rect2D(live.Left, live.Top, live.Width, 2 * title);
                    var subtitleBox = Centred(live, titleBox.Bottom + 0.2 * subtitle, Math.Min(live.Width, 3.5 * title), 1.4 * subtitle);
                    return
                    [
                        Plain(page, new GradientBackground(Sky, PaleSky),
                        Text(TitleId, titleBox, words.Title, new TextStyle(Points(title), TextStylePresets.EffectYellow, Bold: true, Outline: TextStylePresets.Ink)),
                        Text(SubtitleId, subtitleBox, words.Subtitle, new TextStyle(Points(0.8 * subtitle), TextStylePresets.Ink, Bold: true, BoxFill: White, BoxStroke: TextStylePresets.Ink)),
                        Text(CreditsId, new Rect2D(live.Left, live.Bottom - credits, live.Width, credits), words.Credits, new TextStyle(Points(credits), TextStylePresets.Ink)))
                    ];
                }
            case TitlePageDesign.Banner:
                {
                    var band = Rect2D.FromEdges(page.Left, page.Top, page.Right, live.Top + 2 * title + subtitle + 0.25 * title);
                    var creditsBox = new Rect2D(live.Left, live.Bottom - credits, live.Width, credits);
                    var art = Rect2D.FromEdges(live.Left, band.Bottom + grid.GutterMm, live.Right, creditsBox.Top - grid.GutterMm);
                    return
                    [
                        Plain(band, new ColorBackground(Navy),
                        Text(TitleId, new Rect2D(live.Left, live.Top, live.Width, 2 * title), words.Title, new TextStyle(Points(title), White, Bold: true)),
                        Text(SubtitleId, new Rect2D(live.Left, live.Top + 2 * title, live.Width, subtitle), words.Subtitle, new TextStyle(Points(subtitle), TextStylePresets.EffectYellow, Bold: true))),
                    // The cover art's panel: bordered, empty, the size of what's left.
                    new Panel(PanelId.New(), PanelShapes.Rectangle(art.Height > 0 ? art : live), Background: null, CharacterInstances: [], Bubbles: []),
                    Plain(Rect2D.FromEdges(page.Left, creditsBox.Top, page.Right, page.Bottom), background: null,
                        Text(CreditsId, creditsBox, words.Credits, new TextStyle(Points(credits), TextStylePresets.Ink)))
                    ];
                }
            case TitlePageDesign.Classic:
                {
                    var titleBox = new Rect2D(live.Left, page.Top + 0.3 * page.Height - title, live.Width, 2 * title);
                    var ruleY = titleBox.Bottom + 0.4 * subtitle;
                    var ruleHalf = 0.15 * live.Width;
                    var rule = new ShapeElement(ElementId.New(), ElementLayer.Foreground,
                        [AnchorRing.Corner(new Point2D(page.MidX - ruleHalf, ruleY)), AnchorRing.Corner(new Point2D(page.MidX + ruleHalf, ruleY))],
                        Closed: false, new ShapeStyle(TextStylePresets.Ink, Fill: null, StrokeWidthMm: Math.Max(0.35, 0.0035 * unit)));
                    return
                    [
                        Plain(page, background: null,
                        Text(TitleId, titleBox, words.Title, new TextStyle(Points(title), TextStylePresets.Ink, Bold: true)),
                        rule,
                        Text(SubtitleId, new Rect2D(live.Left, ruleY + 0.4 * subtitle, live.Width, subtitle), words.Subtitle, new TextStyle(Points(subtitle), TextStylePresets.Ink, Italic: true)),
                        Text(CreditsId, new Rect2D(live.Left, page.Top + 0.8 * page.Height, live.Width, credits), words.Credits, new TextStyle(Points(credits), TextStylePresets.Ink)))
                    ];
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(design), design, null);
        }
    }

    /// <summary>A borderless panel: the design's ground, not a comic panel.</summary>
    private static Panel Plain(Rect2D bounds, PanelBackground? background, params PanelElement[] elements) =>
        new(PanelId.New(), PanelShapes.Rectangle(bounds), background, CharacterInstances: [], Bubbles: [], elements, Borderless: true);

    private static TextElement Text(ElementId id, Rect2D bounds, string text, TextStyle style) =>
        new(id, ElementLayer.Foreground, bounds, text, style);

    private static Rect2D Centred(Rect2D within, double top, double width, double height) =>
        new(within.MidX - width / 2, top, width, height);

    /// <summary>The letter size, in points and to the half point like Word's, that fills one line <paramref name="lineMm"/> tall.</summary>
    private static double Points(double lineMm) =>
        Math.Clamp(Math.Round(FontPoints.FromMm(lineMm / LineSpacing) * 2) / 2, TextEditing.MinFontSizePt, TextEditing.MaxFontSizePt);
}
