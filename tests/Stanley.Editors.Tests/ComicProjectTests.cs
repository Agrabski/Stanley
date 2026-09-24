using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Storage;

namespace Stanley.Editors.Tests;

public sealed class ComicProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-comic-tests-" + Guid.NewGuid().ToString("N"));

    public ComicProjectTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static PageNavigatorViewModel NavigatorFor(ComicProject project) => new(new EditorHistory(), project.Pages);

    [Fact]
    public void CreateNew_IsAnUntitledA4PageFilledWithTheChosenLayout()
    {
        var project = ComicProject.CreateNew(MetricPaperSize.A4, PanelLayoutPresets.All.First(p => p.ColumnsPerRow.Sum() == 6));

        Assert.True(project.IsUntitled);
        Assert.Equal(new Rect2D(0, 0, 210, 297), project.PageBounds);
        Assert.Equal(6, Assert.Single(project.Pages).Document.PanelOrder.Count);
    }

    [Fact]
    public void SaveAs_ThenOpen_RoundTripsEveryPageInOrderWithPanelsAndBubbles()
    {
        var project = ComicProject.CreateNew();
        var navigator = NavigatorFor(project);
        var first = navigator.CurrentPage.Editor;
        var panelId = first.Working.PanelOrder[0];
        first.SplitPanel(panelId, BoundaryOrientation.Vertical, 0.5);
        first.SetBubbleText(panelId, first.CreateBubble(panelId, new Point2D(40, 40)), "Hello!");
        var second = navigator.AddPageAfter(navigator.CurrentPage);
        second.Editor.ApplyLayoutPreset(PanelLayoutPresets.All.First(p => p.ColumnsPerRow.Sum() == 4));

        var folder = Path.Combine(_root, "My Comic");
        var saved = project.SaveAs(folder, navigator.Snapshot());

        Assert.Equal(folder, saved);
        Assert.Equal("My Comic", project.Title); // an untitled comic takes its folder's name
        var reopened = ComicProject.Open(folder);
        Assert.Equal("My Comic", reopened.Title);
        Assert.Equal(navigator.Pages.Select(p => p.Id), reopened.Pages.Select(p => p.Id));
        Assert.Equal("Hello!", Assert.Single(reopened.Pages[0].Document.Panels[panelId].Bubbles).Text);
        Assert.Equal(4, reopened.Pages[1].Document.PanelOrder.Count);
    }

    [Fact]
    public void Save_AfterDeletingAPanelAndAPage_RemovesTheirFiles()
    {
        var project = ComicProject.CreateNew(MetricPaperSize.A4, PanelLayoutPresets.All.First(p => p.ColumnsPerRow.Sum() == 4));
        var navigator = NavigatorFor(project);
        var extra = navigator.AddPageAfter(navigator.CurrentPage);
        var folder = Path.Combine(_root, "comic");
        project.SaveAs(folder, navigator.Snapshot());
        var editor = navigator.Pages[0].Editor;
        var victim = editor.Working.PanelOrder[3];
        Assert.Single(Directory.EnumerateFiles(folder, $"{victim.Value}.json", SearchOption.AllDirectories));
        Assert.Single(Directory.EnumerateDirectories(folder, $"{extra.Id.Value}-*", SearchOption.AllDirectories));

        editor.DeletePanel(victim);
        navigator.DeletePage(extra);
        project.Save(navigator.Snapshot());

        Assert.Empty(Directory.EnumerateFiles(folder, $"{victim.Value}.json", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateDirectories(folder, $"{extra.Id.Value}-*", SearchOption.AllDirectories));
        var reopened = ComicProject.Open(folder);
        Assert.Equal(3, Assert.Single(reopened.Pages).Document.PanelOrder.Count);
    }

    [Fact]
    public void SaveAs_IntoANonEmptyFolder_UsesANewSubfolderNamedAfterTheTitle()
    {
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "someone else's file");
        var project = ComicProject.CreateNew();
        project.Title = "Space Cats";

        var saved = project.SaveAs(_root, NavigatorFor(project).Snapshot());

        Assert.Equal(Path.Combine(_root, "Space Cats"), saved);
        Assert.True(ProjectRepository.IsInitialized(saved));
        Assert.False(ProjectRepository.IsInitialized(_root));
    }

    [Fact]
    public void SaveAs_FromASavedComic_CopiesTheWholeProject()
    {
        var project = ComicProject.CreateNew();
        var pages = NavigatorFor(project).Snapshot();
        var first = project.SaveAs(Path.Combine(_root, "a"), pages);
        File.WriteAllText(Path.Combine(first, "characters", "keep-me.txt"), "extra");

        var second = project.SaveAs(Path.Combine(_root, "b"), pages);

        Assert.True(File.Exists(Path.Combine(second, "characters", "keep-me.txt")));
        Assert.Equal(second, project.Location);
    }

    [Fact]
    public void Open_AProjectWithNoPagesYet_StartsOnABlankPage()
    {
        var folder = Path.Combine(_root, "fresh");
        ProjectRepository.Initialize(folder, "Fresh", new PageTrim(MetricPaperSizes.Size(MetricPaperSize.A5), 3));

        var project = ComicProject.Open(folder);

        Assert.Equal("Fresh", project.Title);
        Assert.Equal(148, project.PageBounds.Width, 6);
        Assert.Single(Assert.Single(project.Pages).Document.PanelOrder);

        project.Save(NavigatorFor(project).Snapshot());
        Assert.Single(ComicProject.Open(folder).Pages);
    }

    [Fact]
    public void Open_AFolderThatIsntAProject_Throws()
    {
        Assert.Throws<InvalidDataException>(() => ComicProject.Open(_root));
    }

    [Fact]
    public void Export_WritesAMultiPagePdfAndAPrintResolutionPng()
    {
        var project = ComicProject.CreateNew();
        var navigator = NavigatorFor(project);
        navigator.AddPageAfter(navigator.CurrentPage);
        var pdf = Path.Combine(_root, "comic.pdf");
        var png = Path.Combine(_root, "page.png");

        ComicProject.ExportPdf(pdf, navigator.Pages.Select(p => (p.Editor.PageBounds, p.Editor.Committed, p.Editor.Folio)));
        ComicProject.ExportPng(png, project.PageBounds, project.Pages[0].Document, dpi: 100);

        var pdfText = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf));
        Assert.StartsWith("%PDF", pdfText);
        Assert.Contains("/Count 2", pdfText);
        using var image = SkiaSharp.SKBitmap.Decode(png);
        Assert.Equal((int)Math.Round(210 * 100 / 25.4), image.Width);
        Assert.Equal((int)Math.Round(297 * 100 / 25.4), image.Height);
    }

    [Fact]
    public void PageNumbering_IsSavedWithTheIssueAndReopened()
    {
        var project = ComicProject.CreateNew();
        var numbering = new ProjectModel.Issues.PageNumbering(ProjectModel.Issues.PageNumberPosition.TopOuter, StartAt: 3);
        var navigator = new PageNavigatorViewModel(new EditorHistory(), project.Pages, numbering);
        var folder = Path.Combine(_root, "numbered");

        project.SaveAs(folder, navigator.Snapshot(), navigator.PageNumbering);

        Assert.Equal(numbering, ComicProject.Open(folder).PageNumbering);
    }
}
