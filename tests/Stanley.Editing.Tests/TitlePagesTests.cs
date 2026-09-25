using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing.Tests;

public class TitlePagesTests
{
    private static readonly Rect2D A4 = new(0, 0, 210, 297);
    private static readonly TitlePageWords Words = new("The Big Heist", "Issue #3", "Story and art by Sam");

    public static TheoryData<TitlePageDesign> Designs => new(TitlePages.All);

    private static IEnumerable<TextElement> Texts(IEnumerable<Panel> panels) => panels.SelectMany(p => p.Elements).OfType<TextElement>();

    private static TextElement Text(IEnumerable<Panel> panels, Stanley.ProjectModel.Ids.ElementId id) => Assert.Single(Texts(panels), t => t.Id == id);

    [Theory]
    [MemberData(nameof(Designs))]
    public void Compose_PutsTheWordsOnThePage_BiggestForTheTitle(TitlePageDesign design)
    {
        var panels = TitlePages.Compose(design, A4, PanelGrid.Default, Words);

        var title = Text(panels, TitlePages.TitleId);
        var subtitle = Text(panels, TitlePages.SubtitleId);
        var credits = Text(panels, TitlePages.CreditsId);
        Assert.Equal(("The Big Heist", "Issue #3", "Story and art by Sam"), (title.Text, subtitle.Text, credits.Text));
        Assert.True(title.Style.FontSizePt > subtitle.Style.FontSizePt);
        Assert.True(subtitle.Style.FontSizePt > credits.Style.FontSizePt);
        Assert.All(panels.Select(p => p.Id), id => Assert.Single(panels, p => p.Id == id));
    }

    [Theory]
    [MemberData(nameof(Designs))]
    public void Compose_FitsAnyPageShape_TextInsideTheLiveAreaAndInsideItsPanel(TitlePageDesign design)
    {
        // A4, a wide daily strip and a tall four-panel strip.
        foreach (var (page, grid) in new[] { (A4, PanelGrid.Default), (new Rect2D(0, 0, 330, 105), new PanelGrid(5, 4)), (new Rect2D(0, 0, 90, 262), new PanelGrid(5, 4)) })
        {
            var live = grid.LiveArea(page);
            foreach (var panel in TitlePages.Compose(design, page, grid, Words))
            {
                var panelBounds = AnchorRing.BoundingBox(panel.Shape.Anchors);
                Assert.True(Inside(panelBounds, page), $"{design} panel {panelBounds} leaves the page {page}");
                foreach (var text in panel.Elements.OfType<TextElement>())
                {
                    Assert.True(Inside(text.Bounds, live), $"{design} text '{text.Text}' {text.Bounds} leaves the live area {live}");
                    Assert.True(Inside(text.Bounds, panelBounds), $"{design} text '{text.Text}' leaves its panel");
                    Assert.True(text.Style.FontSizePt % 0.5 == 0, "sizes are whole or half points, like Word's");
                }
            }
        }
    }

    [Fact]
    public void Designs_AreBorderlessGrounds_ExceptTheBannersArtPanel()
    {
        Assert.All(TitlePages.Compose(TitlePageDesign.Cover, A4, PanelGrid.Default, Words), p => Assert.True(p.Borderless));
        Assert.All(TitlePages.Compose(TitlePageDesign.Classic, A4, PanelGrid.Default, Words), p => Assert.True(p.Borderless));

        var banner = TitlePages.Compose(TitlePageDesign.Banner, A4, PanelGrid.Default, Words);
        var art = Assert.Single(banner, p => !p.Borderless);
        Assert.Empty(art.Elements);
        Assert.Equal(PanelGrid.Default.LiveArea(A4).Left, AnchorRing.BoundingBox(art.Shape.Anchors).Left, 6);
    }

    [Fact]
    public void Cover_RunsItsSkyToThePageEdge()
    {
        var ground = Assert.Single(TitlePages.Compose(TitlePageDesign.Cover, A4, PanelGrid.Default, Words));

        Assert.Equal(A4, AnchorRing.BoundingBox(ground.Shape.Anchors));
        Assert.IsType<GradientBackground>(ground.Background);
    }

    [Fact]
    public void WordsOn_ReadsTheWordsBack_AndFallsBackForOnesDeleted()
    {
        var panels = TitlePages.Compose(TitlePageDesign.Banner, A4, PanelGrid.Default, Words)
            .Select(p => p with { Elements = p.Elements.Where(e => e.Id != TitlePages.SubtitleId).ToList() })
            .ToList();

        var read = TitlePages.WordsOn(panels, TitlePages.DefaultWords);

        Assert.Equal(new TitlePageWords("The Big Heist", "Issue #{issue}", "Story and art by Sam"), read);
    }

    [Fact]
    public void DefaultWords_LetStanleyFillInTheTitleAndIssueNumber()
    {
        var words = TitlePages.DefaultWords;
        var fields = new TextFields("Moon Pie", "2");

        Assert.Equal(("{title}", "Issue #{issue}"), (words.Title, words.Subtitle));
        Assert.Equal(("Moon Pie", "Issue #2"), (fields.Fill(words.Title), fields.Fill(words.Subtitle)));
        Assert.Equal("Wydanie #2", fields.Fill(words.Subtitle.Replace("Issue", "Wydanie", StringComparison.Ordinal)));
    }

    private static bool Inside(Rect2D inner, Rect2D outer) =>
        inner.Left >= outer.Left - 1e-6 && inner.Top >= outer.Top - 1e-6 && inner.Right <= outer.Right + 1e-6 && inner.Bottom <= outer.Bottom + 1e-6;
}
