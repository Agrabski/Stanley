using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Characters;
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
    public void TheTitlePage_IsTheComics_SavedInTheProjectFolder_NotInTheIssue()
    {
        var project = ComicProject.CreateNew();
        var navigator = PageEditorHost.CreateWorkspace(project).Navigator;
        var title = navigator.InsertTitlePage(TitlePageDesign.Banner);
        Assert.Equal(TitlePageScope.Comic, title.Editor.Committed.TitlePage);
        Assert.Same(title, navigator.ComicTitlePage);

        var folder = project.SaveAs(Path.Combine(_root, "Comic"), navigator.Snapshot());

        Assert.True(File.Exists(Path.Combine(folder, "title-page", "page.json")));
        var reopened = ComicProject.Open(folder);
        var titlePage = Assert.IsType<ComicPage>(reopened.TitlePage);
        Assert.Equal(title.Id, titlePage.Id);
        Assert.Equal(title.Editor.Committed.Panels.Values.Count(p => p.Borderless), titlePage.Document.Panels.Values.Count(p => p.Borderless));
        Assert.DoesNotContain(reopened.Pages, p => p.Document.IsTitlePage); // the issue's own pages
        var shown = PageEditorHost.CreateWorkspace(reopened).Navigator;
        Assert.Equal([title.Id, reopened.Pages[0].Id], shown.Pages.Select(p => p.Id));
    }

    [Fact]
    public void EveryIssue_OpensWithTheComicsTitlePage_ShowingItsOwnIssueNumber()
    {
        var project = ComicProject.CreateNew();
        project.Title = "Moon Pie";
        var navigator = PageEditorHost.CreateWorkspace(project).Navigator;
        navigator.InsertTitlePage(TitlePageDesign.Cover);
        var folder = project.SaveAs(Path.Combine(_root, "Series"), navigator.Snapshot());
        var secondIssue = AddIssue(folder, "2");

        var issueTwo = PageEditorHost.CreateWorkspace(ComicProject.Open(folder, secondIssue)).Navigator;

        var titlePage = issueTwo.Pages[0];
        Assert.Same(issueTwo.ComicTitlePage, titlePage);
        Assert.Equal(new TextFields("Moon Pie", "2"), titlePage.Editor.Fields);
        Assert.Equal("Issue #2", titlePage.Editor.Fields!.Fill(Text(titlePage.Editor.Committed, TitlePages.SubtitleId).Text));
        Assert.Equal(2, issueTwo.Pages.Count);
    }

    [Fact]
    public void AnIssue_CanHaveItsOwnTitlePage_WithoutChangingTheComics()
    {
        var project = ComicProject.CreateNew();
        var session = PageEditorHost.CreateWorkspace(project);
        var navigator = session.Navigator;
        var comic = navigator.InsertTitlePage(TitlePageDesign.Cover);
        var editor = navigator.CurrentPage.Editor;

        editor.IsOwnTitlePage = true; // Insert › Title page › Only this issue

        var own = navigator.Pages[0];
        Assert.NotSame(comic, own);
        Assert.Equal(TitlePageScope.Issue, own.Editor.Committed.TitlePage);
        Assert.Same(own, navigator.OwnTitlePage);
        Assert.Same(own, navigator.CurrentPage);
        Assert.DoesNotContain(comic, navigator.Pages);
        Assert.Equal("Remove this issue's title page", own.Editor.RemoveTitlePageText);
        var (panel, index) = Find(own.Editor.Committed, TitlePages.CreditsId);
        own.Editor.SetElementText(panel, index, "Guest art by Sam");
        Assert.Equal("Story and art by Your Name", Text(comic.Editor.Committed, TitlePages.CreditsId).Text);

        var folder = project.SaveAs(Path.Combine(_root, "Own"), navigator.Snapshot());
        var reopened = ComicProject.Open(folder);
        Assert.Equal("Story and art by Your Name", Text(reopened.TitlePage!.Document, TitlePages.CreditsId).Text);
        Assert.Equal(TitlePageScope.Issue, reopened.Pages[0].Document.TitlePage);
        var shown = PageEditorHost.CreateWorkspace(reopened).Navigator;
        Assert.Equal("Guest art by Sam", Text(shown.Pages[0].Editor.Committed, TitlePages.CreditsId).Text);
        Assert.Equal(2, shown.Pages.Count); // the comic's is kept, not shown

        own.Editor.IsOwnTitlePage = false; // back to the comic's
        Assert.Same(comic, navigator.Pages[0]);
        Assert.Null(navigator.OwnTitlePage);
        session.Workspace.History.Undo();
        Assert.Same(own, navigator.Pages[0]);
    }

    [Fact]
    public void RemovingTheComicsTitlePage_DeletesItsFolder_AndAnIssuesOwnThenStands()
    {
        var project = ComicProject.CreateNew();
        var navigator = PageEditorHost.CreateWorkspace(project).Navigator;
        var comic = navigator.InsertTitlePage(TitlePageDesign.Classic);
        var folder = project.SaveAs(Path.Combine(_root, "Removed"), navigator.Snapshot());
        Assert.Equal("Remove the title page from every issue", comic.Editor.RemoveTitlePageText);

        navigator.RemoveTitlePage();
        project.Save(navigator.Snapshot());

        Assert.Null(navigator.ComicTitlePage);
        Assert.False(Directory.Exists(Path.Combine(folder, "title-page")));
        Assert.Null(ComicProject.Open(folder).TitlePage);
    }

    [Fact]
    public void DeletingAnIssuesOwnTitlePage_BringsTheComicsBack()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var navigator = session.Navigator;
        var comic = navigator.InsertTitlePage(TitlePageDesign.Cover);
        navigator.SetOwnTitlePage(true);
        var own = navigator.Pages[0];
        Assert.True(navigator.CanRemoveTitlePage);

        navigator.DeletePage(own);

        Assert.Same(comic, navigator.Pages[0]);
        Assert.Same(comic, navigator.CurrentPage);
        Assert.Same(comic, navigator.TitlePage);
        Assert.Equal(2, navigator.Pages.Count);
        navigator.InsertTitlePage(TitlePageDesign.Banner); // redoes the comic's, never a second one
        Assert.Same(comic, navigator.ComicTitlePage);
        Assert.Equal(2, navigator.Pages.Count);

        session.Workspace.History.Undo();
        session.Workspace.History.Undo();
        Assert.Same(own, navigator.Pages[0]);
        Assert.Same(comic, navigator.ComicTitlePage);
    }

    [Fact]
    public void ACharacterOnTheComicsTitlePage_CountsAsPlaced_WhileTheIssueShowsItsOwn()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var navigator = session.Navigator;
        var comic = navigator.InsertTitlePage(TitlePageDesign.Banner).Editor;
        var character = session.Characters.CreateCharacter();
        var panel = comic.Working.PanelOrder.First(id => !comic.Working.Panels[id].Borderless);
        Assert.Equal(0, comic.InsertCharacter(character.Id, panel));
        Assert.Equal(1, session.Characters.UsageCounter(character.Id));

        navigator.SetOwnTitlePage(true);
        var own = navigator.Pages[0].Editor;
        own.DeleteCharacter(own.Working.PanelOrder.Single(id => own.Working.Panels[id].CharacterInstances.Count > 0), 0);

        Assert.Equal(1, session.Characters.UsageCounter(character.Id)); // still on the comic's, which other issues show
    }

    [Fact]
    public void AnIssueNeverLosesItsLastPage_TheTitlePageAside()
    {
        var navigator = PageEditorHost.CreateWorkspace(ComicProject.CreateNew()).Navigator;
        var page = navigator.CurrentPage;
        var comic = navigator.InsertTitlePage(TitlePageDesign.Cover);

        Assert.False(navigator.DeletePageCommand.CanExecute(page));
        navigator.DeletePage(page);
        Assert.Equal([comic, page], navigator.Pages);

        navigator.SetOwnTitlePage(true);
        var own = navigator.Pages[0];
        navigator.DeletePage(page); // the issue's own title page is one of its pages
        Assert.Equal([own], navigator.Pages);

        // Taking it away would bring back the comic's in its place, leaving the issue no page.
        Assert.False(navigator.CanRemoveTitlePage);
        Assert.False(navigator.DeletePageCommand.CanExecute(own));
        own.Editor.IsOwnTitlePage = false;
        Assert.Equal([own], navigator.Pages);
    }

    [Fact]
    public void UntickingOnlyThisIssue_WithNoComicTitlePage_MakesItTheComics()
    {
        var project = ComicProject.CreateNew();
        var navigator = PageEditorHost.CreateWorkspace(project).Navigator;
        navigator.InsertTitlePage(TitlePageDesign.Cover);
        navigator.SetOwnTitlePage(true);
        var folder = project.SaveAs(Path.Combine(_root, "Promoted"), navigator.Snapshot());
        Directory.Delete(Path.Combine(folder, "title-page"), recursive: true); // an issue with a title page of its own in a comic without one

        var reopened = ComicProject.Open(folder);
        var shown = PageEditorHost.CreateWorkspace(reopened).Navigator;
        Assert.Null(shown.ComicTitlePage);
        var own = Assert.IsType<PageItem>(shown.OwnTitlePage);
        Assert.True(own.Editor.IsOwnTitlePage);

        own.Editor.IsOwnTitlePage = false;

        var comic = Assert.IsType<PageItem>(shown.ComicTitlePage);
        Assert.Same(comic, shown.Pages[0]);
        Assert.Null(shown.OwnTitlePage);
        reopened.Save(shown.Snapshot());
        Assert.True(File.Exists(Path.Combine(folder, "title-page", "page.json")));
        var again = ComicProject.Open(folder);
        Assert.NotNull(again.TitlePage);
        Assert.DoesNotContain(again.Pages, p => p.Document.IsTitlePage);
    }

    [Fact]
    public void APictureOnTheComicsTitlePage_IsKeptInTheTitlePageFolder()
    {
        var project = ComicProject.CreateNew();
        var session = PageEditorHost.CreateWorkspace(project);
        var navigator = session.Navigator;
        var title = navigator.InsertTitlePage(TitlePageDesign.Banner).Editor;
        var panel = title.Working.PanelOrder.First(id => !title.Working.Panels[id].Borderless);
        var picture = ArtFile.Svg("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\"/></svg>");
        Assert.True(title.ImportPicture(new PictureImportRequest(panel, AsBackground: true), "moon.svg", picture));
        var name = ((InlineBackground)title.Committed.Panels[panel].Background!).ArtFileName;

        var folder = project.SaveAs(Path.Combine(_root, "Pictured"), navigator.Snapshot(), pictures: title.PictureSnapshot);

        Assert.True(File.Exists(Path.Combine(folder, "title-page", "art", name)));
        var reopened = ComicProject.Open(folder);
        Assert.True(reopened.Pictures.ContainsKey(name));

        navigator.RemoveTitlePage();
        project.Save(navigator.Snapshot(), pictures: title.PictureSnapshot);
        Assert.False(Directory.Exists(Path.Combine(folder, "title-page")));
    }

    [Fact]
    public void TheComicsTitlePage_StaysFirst()
    {
        var navigator = PageEditorHost.CreateWorkspace(ComicProject.CreateNew()).Navigator;
        var comic = navigator.InsertTitlePage(TitlePageDesign.Cover);
        navigator.AddPageAfter(navigator.Pages[^1]);

        navigator.MovePage(0, 2);
        navigator.MovePage(2, 0);

        Assert.Same(comic, navigator.Pages[0]);
    }

    /// <summary>A second issue with one page, written straight into the project folder - the editor has no issue switcher yet.</summary>
    private static IssueId AddIssue(string folder, string number)
    {
        var repository = new Stanley.ProjectModel.Storage.ProjectRepository(folder);
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 190, 277)), null, [], []);
        var page = new Page(PageId.New(), "Page 1", null, [panel.Id]);
        var issue = new Issue(IssueId.New(), number, "", [page.Id], new SortedDictionary<CharacterId, CharacterRevisionId>());
        repository.SaveIssue(issue);
        repository.SavePage(issue.Id, page);
        repository.SavePanel(issue.Id, page.Id, panel);
        var manifest = repository.LoadManifest();
        repository.SaveManifest(manifest with { IssueIds = [.. manifest.IssueIds, issue.Id] });
        return issue.Id;
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
