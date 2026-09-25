using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Tests;

/// <summary>The fields title pages and comic formats added: written only when set, so files from before read - and write back - unchanged.</summary>
public class TitlePageAndFormatJsonTests
{
    private static Panel APanel(bool borderless = false) =>
        new(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 100, 80)), null, [], [], Borderless: borderless);

    [Fact]
    public void A_panel_is_bordered_unless_it_says_otherwise()
    {
        var bordered = ProjectJson.Serialize(APanel());
        var borderless = ProjectJson.Serialize(APanel(borderless: true));

        Assert.DoesNotContain("borderless", bordered, StringComparison.Ordinal);
        Assert.Contains("\"borderless\": true", borderless, StringComparison.Ordinal);
        Assert.True(ProjectJson.Deserialize<Panel>(borderless).Borderless);
        Assert.False(ProjectJson.Deserialize<Panel>(bordered).Borderless);
    }

    [Fact]
    public void Only_the_title_page_is_marked()
    {
        var page = new Page(PageId.New(), "Page 2", null, [PanelId.New()]);

        var plain = ProjectJson.Serialize(page);
        var title = ProjectJson.Serialize(page with { TitlePage = true });

        Assert.DoesNotContain("titlePage", plain, StringComparison.Ordinal);
        Assert.Contains("\"titlePage\": true", title, StringComparison.Ordinal);
        Assert.True(ProjectJson.Deserialize<Page>(title).TitlePage);
        Assert.False(ProjectJson.Deserialize<Page>(plain).TitlePage);
    }

    [Fact]
    public void A_comic_book_manifest_has_no_format_and_a_strips_round_trips()
    {
        var trim = new PageTrim(new PageSize(330, 105), 0);
        var book = new SeriesManifest("Book", trim, []);
        var strip = book with { Format = new ComicFormat(5, 4, [4]) };
        var web = book with { Format = new ComicFormat(10, 20, [1, 1], ExportWidthPx: 800) };

        Assert.DoesNotContain("format", ProjectJson.Serialize(book), StringComparison.Ordinal);
        Assert.DoesNotContain("exportWidthPx", ProjectJson.Serialize(strip), StringComparison.Ordinal);
        Assert.Contains("\"panelsPerRow\": [\n      4\n    ]", ProjectJson.Serialize(strip), StringComparison.Ordinal);

        var readStrip = ProjectJson.Deserialize<SeriesManifest>(ProjectJson.Serialize(strip)).Format!;
        var readWeb = ProjectJson.Deserialize<SeriesManifest>(ProjectJson.Serialize(web)).Format!;
        Assert.Equal((5.0, 4.0, null), (readStrip.MarginMm, readStrip.GutterMm, readStrip.ExportWidthPx));
        Assert.Equal([4], readStrip.PanelsPerRow!);
        Assert.Equal([1, 1], readWeb.PanelsPerRow!);
        Assert.Equal(800, readWeb.ExportWidthPx);
        Assert.Null(ProjectJson.Deserialize<SeriesManifest>(ProjectJson.Serialize(book)).Format);
    }
}
