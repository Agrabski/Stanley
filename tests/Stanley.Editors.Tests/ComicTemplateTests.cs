using SkiaSharp;
using Stanley.Editing;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Editors.Tests;

/// <summary>File › New's comic strip and webcomic templates, as the comic they make.</summary>
public sealed class ComicTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-template-tests-" + Guid.NewGuid().ToString("N"));

    public ComicTemplateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ComicTemplate Template(string name) => ComicTemplates.All.Single(t => t.Name == name);

    [Fact]
    public void CreateNew_FromATemplate_HasItsPageSizePanelsAndSpacing()
    {
        var daily = Template("Daily strip");

        var project = ComicProject.CreateNew(daily);

        Assert.True(project.IsUntitled);
        Assert.Equal(new Rect2D(0, 0, 330, 105), project.PageBounds);
        Assert.Equal(0, project.Trim.BleedMm);
        Assert.Equal(new PanelGrid(5, 4), project.Grid);
        var page = Assert.Single(project.Pages).Document;
        Assert.Equal(4, page.PanelOrder.Count);
        var tops = page.Panels.Values.Select(p => AnchorRing.BoundingBox(p.Shape.Anchors).Top).Distinct();
        Assert.Equal(5, Assert.Single(tops), 6); // one row, at the template's margin
    }

    [Fact]
    public void NewPages_StartWithTheTemplatesPanelsAndSpacing()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew(Template("Four-panel strip")));
        var navigator = session.Navigator;
        Assert.Equal(new PanelGrid(5, 4), navigator.CurrentPage.Editor.Grid);

        var next = navigator.AddPageAfter(navigator.CurrentPage);

        Assert.Equal(4, next.Editor.Working.PanelOrder.Count);
        Assert.Equal(new PanelGrid(5, 4), next.Editor.Grid);
    }

    [Fact]
    public void AComicBook_StillStartsNewPagesWithOnePanel()
    {
        var project = ComicProject.CreateNew(MetricPaperSize.A4, PanelLayoutPresets.All.First(p => p.ColumnsPerRow.Sum() == 6));
        var navigator = PageEditorHost.CreateWorkspace(project).Navigator;

        var next = navigator.AddPageAfter(navigator.CurrentPage);

        Assert.Null(project.Format);
        Assert.Single(next.Editor.Working.PanelOrder);
        Assert.Equal(PanelGrid.Default, next.Editor.Grid);
    }

    [Fact]
    public void TheFormat_IsSavedWithTheComic_AndReopened()
    {
        var project = ComicProject.CreateNew(Template("Vertical scroll"));
        var navigator = PageEditorHost.CreateWorkspace(project).Navigator;

        var folder = project.SaveAs(Path.Combine(_root, "Webcomic"), navigator.Snapshot());
        var reopened = ComicProject.Open(folder);

        Assert.Equal(800, reopened.ExportWidthPx);
        Assert.Equal(new PanelGrid(10, 20), reopened.Grid);
        Assert.Equal([1, 1], reopened.NewPageLayout!.ColumnsPerRow);
        Assert.Equal(new PanelGrid(10, 20), PageEditorHost.CreateWorkspace(reopened).Navigator.CurrentPage.Editor.Grid);
    }

    [Fact]
    public void ExportPng_AtTheWebcomicsWidth_IsExactlyThatManyPixels()
    {
        var project = ComicProject.CreateNew(Template("Square post"));
        var page = project.Pages[0];
        var path = Path.Combine(_root, "post.png");

        ComicProject.ExportPng(path, page.Bounds, page.Document, widthPx: project.ExportWidthPx);

        using var image = SKBitmap.Decode(path);
        Assert.Equal((1080, 1080), (image.Width, image.Height));
    }
}
