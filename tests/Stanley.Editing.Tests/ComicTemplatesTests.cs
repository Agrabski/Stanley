using Stanley.ProjectModel;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Editing.Tests;

public class ComicTemplatesTests
{
    [Fact]
    public void EveryTemplate_TilesItsPageWithItsPanels()
    {
        foreach (var template in ComicTemplates.All)
        {
            var page = new Rect2D(0, 0, template.Trim.Size.WidthMm, template.Trim.Size.HeightMm);
            var layout = PanelLayoutEditing.GridLayout(page, template.Grid, template.PanelsPerRow);

            Assert.True(layout.IsValid, $"{template.Name}: {layout.Error}");
            Assert.Equal(template.PanelsPerRow.Sum(), layout.Value.Count);
        }
    }

    [Fact]
    public void Templates_HaveDistinctNamesAndSizes_BothKindsOnOffer()
    {
        Assert.Equal(ComicTemplates.All.Count, ComicTemplates.All.Select(t => t.Name).Distinct().Count());
        Assert.Equal(ComicTemplates.All.Count, ComicTemplates.All.Select(t => t.Trim.Size).Distinct().Count());
        Assert.NotEmpty(ComicTemplates.OfKind(ComicTemplateKind.Strip));
        Assert.NotEmpty(ComicTemplates.OfKind(ComicTemplateKind.Webcomic));
    }

    [Fact]
    public void Webcomics_ExportAtTheirPixelSize_StripsAtPrintResolution()
    {
        Assert.All(ComicTemplates.OfKind(ComicTemplateKind.Webcomic), t => Assert.NotNull(t.ExportWidthPx));
        Assert.All(ComicTemplates.OfKind(ComicTemplateKind.Strip), t => Assert.Null(t.ExportWidthPx));

        var scroll = ComicTemplates.All.Single(t => t.Name == "Vertical scroll");
        Assert.Equal((800, 1280), (scroll.ExportWidthPx, scroll.ExportHeightPx));
        Assert.Equal(1350, ComicTemplates.All.Single(t => t.Name == "Portrait post").ExportHeightPx);
    }

    [Fact]
    public void NothingIsTrimmed_SoNoTemplateHasBleed() =>
        Assert.All(ComicTemplates.All, t => Assert.Equal(0, t.Trim.BleedMm));

    [Fact]
    public void Format_RecordsSpacingPanelsAndExportWidth()
    {
        var daily = ComicTemplates.All.Single(t => t.Name == "Daily strip");

        var format = daily.Format;

        Assert.Equal((5, 4, 4, (int?)null), (format.MarginMm, format.GutterMm, Assert.Single(format.PanelsPerRow!), format.ExportWidthPx));
        Assert.Same(daily, ComicTemplates.Matching(new PageSize(330, 105)));
        Assert.Null(ComicTemplates.Matching(MetricPaperSizes.Size(MetricPaperSize.A4)));
    }
}
