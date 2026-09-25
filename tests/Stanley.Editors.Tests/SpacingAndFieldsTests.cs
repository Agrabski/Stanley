using Stanley.Editing;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>The comic-wide margin and gutter (Layout tab), and the fields texts can show ({title}, {issue}).</summary>
public sealed class SpacingAndFieldsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-spacing-tests-" + Guid.NewGuid().ToString("N"));

    public SpacingAndFieldsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static Rect2D OnlyPanel(PageItem page) => AnchorRing.BoundingBox(page.Editor.Working.Panels.Values.Single().Shape.Anchors);

    [Fact]
    public void A_margin_set_on_one_page_is_every_pages_and_their_panels_follow_it_except_on_a_locked_page()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var navigator = session.Navigator;
        var first = navigator.CurrentPage;
        var second = navigator.AddPageAfter(first);
        var locked = navigator.AddPageAfter(second);
        locked.Editor.IsLayoutLocked = true;

        first.Editor.MarginMm = 15; // what the Layout tab's Margin box does

        Assert.All(navigator.Pages, p => Assert.Equal(new PanelGrid(15, 4), p.Editor.Grid));
        Assert.Equal(Rect2D.FromEdges(15, 15, 195, 282), OnlyPanel(first));
        Assert.Equal(Rect2D.FromEdges(15, 15, 195, 282), OnlyPanel(second));
        Assert.Equal(Rect2D.FromEdges(10, 10, 200, 287), OnlyPanel(locked));

        session.Workspace.History.Undo(); // one step for the lot
        Assert.All(navigator.Pages, p => Assert.Equal(PanelGrid.Default, p.Editor.Grid));
        Assert.Equal(Rect2D.FromEdges(10, 10, 200, 287), OnlyPanel(first));
        Assert.Equal(Rect2D.FromEdges(10, 10, 200, 287), OnlyPanel(second));
    }

    [Fact]
    public void Editing_the_layout_on_another_page_snaps_to_the_new_margin()
    {
        var navigator = PageEditorHost.CreateWorkspace(ComicProject.CreateNew()).Navigator;
        navigator.CurrentPage.Editor.MarginMm = 20;
        var page = navigator.AddPageAfter(navigator.CurrentPage).Editor;
        var id = page.Working.PanelOrder[0];
        page.BeginResizePanel(id);

        // Drag the right edge to within snapping distance of the new margin line.
        page.UpdateResizePanel(id, Rect2D.FromEdges(20, 20, 188.5, 277), RectEdges.Right, snapTolerance: 2);
        page.EndGesture(commit: true);

        Assert.Equal(190, OnlyPanel(navigator.CurrentPage).Right, 6);
    }

    [Fact]
    public void A_comic_books_margin_is_saved_and_reopened_and_the_default_leaves_the_file_alone()
    {
        var project = ComicProject.CreateNew();
        var navigator = PageEditorHost.CreateWorkspace(project).Navigator;
        var plain = project.SaveAs(Path.Combine(_root, "Plain"), navigator.Snapshot());
        Assert.Null(ComicProject.Open(plain).Format);

        navigator.SetSpacing(new PanelGrid(12, 6));
        project.Grid = navigator.Spacing; // what the window keeps in step
        project.Save(navigator.Snapshot());

        var reopened = ComicProject.Open(plain);
        Assert.Equal(new PanelGrid(12, 6), reopened.Grid);
        Assert.Null(reopened.NewPageLayout);
        Assert.Null(reopened.ExportWidthPx);
        Assert.Equal(new PanelGrid(12, 6), PageEditorHost.CreateWorkspace(reopened).Navigator.CurrentPage.Editor.Grid);
    }

    [Fact]
    public void A_templates_spacing_change_keeps_the_rest_of_its_format()
    {
        var project = ComicProject.CreateNew(ComicTemplates.All.Single(t => t.Name == "Vertical scroll"));
        project.Grid = new PanelGrid(6, 12);

        var format = project.Format!;

        Assert.Equal((6, 12, 800), (format.MarginMm, format.GutterMm, format.ExportWidthPx));
        Assert.Equal([1, 1], format.PanelsPerRow!);
    }

    [Fact]
    public void The_issue_number_is_saved_with_the_issue_and_every_page_shows_it()
    {
        var project = ComicProject.CreateNew();
        project.Title = "Moon Pie";
        project.IssueNumber = "12";
        var navigator = PageEditorHost.CreateWorkspace(project).Navigator;

        Assert.All(navigator.Pages, p => Assert.Equal(new TextFields("Moon Pie", "12"), p.Editor.Fields));
        navigator.Fields = new TextFields("Moon Pie", "13");
        Assert.Equal("13", navigator.AddPageAfter(navigator.CurrentPage).Editor.Fields!.Issue);

        var folder = project.SaveAs(Path.Combine(_root, "Issue"), navigator.Snapshot());
        Assert.Equal("12", ComicProject.Open(folder).IssueNumber);
    }

    [Fact]
    public void Insert_field_appends_to_the_selected_text_when_nothing_is_being_typed()
    {
        var editor = PageEditorHost.CreateWorkspace(ComicProject.CreateNew()).Navigator.CurrentPage.Editor;
        var panelId = editor.Working.PanelOrder[0];
        var index = editor.CreateText(panelId, new Point2D(40, 40));
        editor.SetElementText(panelId, index, "Wydanie #");
        Assert.True(editor.InsertFieldCommand.CanExecute(null));

        editor.InsertFieldCommand.Execute(TextFieldChoice.All.Single(f => f.Token == TextFields.IssueField));

        Assert.Equal("Wydanie #{issue}", ((TextElement)editor.Working.Panels[panelId].Elements[index]).Text);
        editor.ClearSelection();
        Assert.False(editor.InsertFieldCommand.CanExecute(null));
    }

    [Fact]
    public void Insert_field_while_typing_is_left_to_the_text_editor()
    {
        var editor = PageEditorHost.CreateWorkspace(ComicProject.CreateNew()).Navigator.CurrentPage.Editor;
        var panelId = editor.Working.PanelOrder[0];
        var index = editor.CreateText(panelId, new Point2D(40, 40));
        editor.SetElementText(panelId, index, "Issue #");
        editor.FieldInsertRequested += request => request.Handled = true; // the open editor took it

        editor.InsertField(TextFields.IssueField);

        Assert.Equal("Issue #", ((TextElement)editor.Working.Panels[panelId].Elements[index]).Text);
    }
}
