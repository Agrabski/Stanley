using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>Insert › Title page (like Word's cover pages), and the panel border it relies on.</summary>
public sealed class TitlePageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-title-page-tests-" + Guid.NewGuid().ToString("N"));

    public TitlePageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static TextElement Text(PageDocument document, ElementId id) =>
        document.Panels.Values.SelectMany(p => p.Elements).OfType<TextElement>().Single(t => t.Id == id);

    private static (PanelId Panel, int Index) Find(PageDocument document, ElementId id)
    {
        var panel = document.Panels.Values.Single(p => p.Elements.Any(e => e.Id == id));
        return (panel.Id, panel.Elements.ToList().FindIndex(e => e.Id == id));
    }

    [Fact]
    public void InsertTitlePage_PutsItAtTheFront_WithTheTitleAndIssueAsFields_AsOneUndoStep()
    {
        var project = ComicProject.CreateNew();
        project.Title = "Moon Pie";
        var session = PageEditorHost.CreateWorkspace(project);
        var navigator = session.Navigator;
        var first = navigator.CurrentPage;
        navigator.AddPageAfter(first);

        var title = navigator.InsertTitlePage(TitlePageDesign.Cover);

        Assert.Same(title, navigator.Pages[0]);
        Assert.Same(title, navigator.CurrentPage);
        Assert.Same(title, navigator.TitlePage);
        Assert.True(title.Editor.Committed.IsTitlePage);
        Assert.Equal("Title", title.Caption);
        Assert.Equal("2", first.Caption);
        Assert.Equal("{title}", Text(title.Editor.Committed, TitlePages.TitleId).Text);
        Assert.Equal("Issue #{issue}", Text(title.Editor.Committed, TitlePages.SubtitleId).Text);
        Assert.Equal(new TextFields("Moon Pie", "1"), title.Editor.Fields); // what the fields show
        Assert.Equal(first.Editor.PageBounds, title.Editor.PageBounds);

        session.Workspace.History.Undo();
        Assert.False(navigator.HasTitlePage);
        Assert.Equal(2, navigator.Pages.Count);
    }

    [Fact]
    public void PickingAnotherDesign_RedoesTheTitlePageInPlace_KeepingItsWords()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var navigator = session.Navigator;
        var title = navigator.InsertTitlePage(TitlePageDesign.Cover);
        var editor = title.Editor;
        var (panel, index) = Find(editor.Committed, TitlePages.CreditsId);
        editor.SetElementText(panel, index, "By Robin");
        navigator.CurrentPage = navigator.Pages[1];

        var again = navigator.InsertTitlePage(TitlePageDesign.Classic);

        Assert.Same(title, again);
        Assert.Same(title, navigator.CurrentPage); // shown, so the change isn't invisible
        Assert.Equal(2, navigator.Pages.Count);
        Assert.Equal("By Robin", Text(editor.Committed, TitlePages.CreditsId).Text);
        Assert.Single(editor.Committed.Panels.Values); // the classic design: one borderless page
        Assert.Null(editor.Committed.Panels.Values.Single().Background);

        session.Workspace.History.Undo();
        Assert.IsType<GradientBackground>(editor.Committed.Panels.Values.Single().Background);
        Assert.Equal("By Robin", Text(editor.Committed, TitlePages.CreditsId).Text);
    }

    [Fact]
    public void RemoveTitlePage_DeletesIt_ButNeverTheOnlyPage()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var navigator = session.Navigator;
        var editor = navigator.CurrentPage.Editor;
        Assert.False(editor.RemoveTitlePageCommand.CanExecute(null));

        editor.InsertTitlePageCommand.Execute(new TitlePageChoice(TitlePageDesign.Banner));
        Assert.True(editor.HasTitlePage);
        Assert.True(editor.RemoveTitlePageCommand.CanExecute(null));
        editor.RemoveTitlePageCommand.Execute(null);

        Assert.False(navigator.HasTitlePage);
        Assert.Same(editor, Assert.Single(navigator.Pages).Editor);
        Assert.False(editor.RemoveTitlePageCommand.CanExecute(null));
    }

    [Fact]
    public void ACopyOfTheTitlePage_IsAnOrdinaryPage()
    {
        var navigator = PageEditorHost.CreateWorkspace(ComicProject.CreateNew()).Navigator;
        var title = navigator.InsertTitlePage(TitlePageDesign.Cover);

        var copy = navigator.DuplicatePage(title);

        Assert.False(copy.Editor.Committed.IsTitlePage);
        Assert.Same(title, navigator.TitlePage);
    }

    [Fact]
    public void TheTitlePage_IsSavedAsSuch_AndReopensAsSuch()
    {
        var project = ComicProject.CreateNew();
        var navigator = PageEditorHost.CreateWorkspace(project).Navigator;
        var title = navigator.InsertTitlePage(TitlePageDesign.Banner);

        var folder = project.SaveAs(Path.Combine(_root, "Comic"), navigator.Snapshot());

        var reopened = ComicProject.Open(folder);
        Assert.True(reopened.Pages[0].Document.IsTitlePage);
        Assert.False(reopened.Pages[1].Document.IsTitlePage);
        Assert.Equal(title.Editor.Committed.Panels.Values.Count(p => p.Borderless), reopened.Pages[0].Document.Panels.Values.Count(p => p.Borderless));
        var issue = Assert.Single(Directory.GetDirectories(Path.Combine(folder, "issues")));
        Assert.Contains(Directory.GetDirectories(Path.Combine(issue, "pages")), d => d.EndsWith("-title-page", StringComparison.Ordinal));
    }

    [Fact]
    public void PanelBorder_TogglesAsOneUndoStep()
    {
        var history = new EditorHistory();
        var editor = new PageEditorViewModel(history, new Rect2D(0, 0, 210, 297), ComicProject.CreateNew().Pages[0].Document);
        var panelId = editor.Working.PanelOrder[0];
        editor.Select(panelId);
        Assert.True(editor.SelectedPanelHasBorder);

        editor.SelectedPanelHasBorder = false;

        Assert.True(editor.Committed.Panels[panelId].Borderless);
        Assert.False(editor.SelectedPanelHasBorder);
        history.Undo();
        Assert.False(editor.Committed.Panels[panelId].Borderless);
        Assert.True(editor.SelectedPanelHasBorder);
    }
}
