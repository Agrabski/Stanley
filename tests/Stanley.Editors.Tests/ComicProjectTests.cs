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

    private static PageEditorViewModel EditorFor(ComicProject project) =>
        new(new EditorHistory(), project.PageBounds, project.Document);

    [Fact]
    public void CreateNew_IsAnUntitledA4PageFilledWithTheChosenLayout()
    {
        var project = ComicProject.CreateNew(MetricPaperSize.A4, PanelLayoutPresets.All.First(p => p.ColumnsPerRow.Sum() == 6));

        Assert.True(project.IsUntitled);
        Assert.Equal(new Rect2D(0, 0, 210, 297), project.PageBounds);
        Assert.Equal(6, project.Document.PanelOrder.Count);
    }

    [Fact]
    public void SaveAs_ThenOpen_RoundTripsPanelsAndBubbles()
    {
        var project = ComicProject.CreateNew();
        var editor = EditorFor(project);
        var panelId = editor.Working.PanelOrder[0];
        editor.SplitPanel(panelId, BoundaryOrientation.Vertical, 0.5);
        var index = editor.CreateBubble(panelId, new Point2D(40, 40));
        editor.SetBubbleText(panelId, index, "Hello!");

        var folder = Path.Combine(_root, "My Comic");
        var saved = project.SaveAs(folder, editor.Committed);

        Assert.Equal(folder, saved);
        Assert.Equal("My Comic", project.Title); // an untitled comic takes its folder's name
        var reopened = ComicProject.Open(folder);
        Assert.Equal("My Comic", reopened.Title);
        Assert.Equal(editor.Committed.PanelOrder, reopened.Document.PanelOrder);
        Assert.Equal("Hello!", Assert.Single(reopened.Document.Panels[panelId].Bubbles).Text);
    }

    [Fact]
    public void Save_AfterDeletingAPanel_RemovesItsFile()
    {
        var project = ComicProject.CreateNew(MetricPaperSize.A4, PanelLayoutPresets.All.First(p => p.ColumnsPerRow.Sum() == 4));
        var editor = EditorFor(project);
        var folder = Path.Combine(_root, "comic");
        project.SaveAs(folder, editor.Committed);
        var victim = editor.Working.PanelOrder[3];
        Assert.Single(Directory.EnumerateFiles(folder, $"{victim.Value}.json", SearchOption.AllDirectories));

        editor.DeletePanel(victim);
        project.Save(editor.Committed);

        Assert.Empty(Directory.EnumerateFiles(folder, $"{victim.Value}.json", SearchOption.AllDirectories));
        Assert.Equal(3, ComicProject.Open(folder).Document.PanelOrder.Count);
    }

    [Fact]
    public void SaveAs_IntoANonEmptyFolder_UsesANewSubfolderNamedAfterTheTitle()
    {
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "someone else's file");
        var project = ComicProject.CreateNew();
        project.Title = "Space Cats";

        var saved = project.SaveAs(_root, project.Document);

        Assert.Equal(Path.Combine(_root, "Space Cats"), saved);
        Assert.True(ProjectRepository.IsInitialized(saved));
        Assert.False(ProjectRepository.IsInitialized(_root));
    }

    [Fact]
    public void SaveAs_FromASavedComic_CopiesTheWholeProject()
    {
        var project = ComicProject.CreateNew();
        var first = project.SaveAs(Path.Combine(_root, "a"), project.Document);
        File.WriteAllText(Path.Combine(first, "characters", "keep-me.txt"), "extra");

        var second = project.SaveAs(Path.Combine(_root, "b"), project.Document);

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
        Assert.Single(project.Document.PanelOrder);

        project.Save(project.Document);
        Assert.Single(ComicProject.Open(folder).Document.PanelOrder);
    }

    [Fact]
    public void Open_AFolderThatIsntAProject_Throws()
    {
        Assert.Throws<InvalidDataException>(() => ComicProject.Open(_root));
    }

    [Fact]
    public void Export_WritesAPdfAndAPrintResolutionPng()
    {
        var project = ComicProject.CreateNew();
        var pdf = Path.Combine(_root, "page.pdf");
        var png = Path.Combine(_root, "page.png");

        project.ExportPdf(pdf, project.Document);
        project.ExportPng(png, project.Document, dpi: 100);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 4));
        using var image = SkiaSharp.SKBitmap.Decode(png);
        Assert.Equal((int)Math.Round(210 * 100 / 25.4), image.Width);
        Assert.Equal((int)Math.Round(297 * 100 / 25.4), image.Height);
    }
}
