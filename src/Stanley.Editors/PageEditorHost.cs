using Stanley.EditorFramework;

namespace Stanley.Editors;

public static class PageEditorHost
{
    /// <summary>
    /// A fresh editing session for <paramref name="project"/>: one undo history (clean,
    /// i.e. "saved") shared by every page, the page navigator docked on the left, and the
    /// first page's editor in the editor area. Picking a page in the navigator swaps which
    /// page editor is shown; the ribbon follows the shown page editor, never the navigator.
    /// </summary>
    public static (EditorWorkspace Workspace, PageNavigatorViewModel Navigator) CreateWorkspace(ComicProject project)
    {
        var history = new EditorHistory();
        var navigator = new PageNavigatorViewModel(history, project.Pages, project.PageNumbering);
        var workspace = new EditorWorkspace(history, [navigator.CurrentPage.Editor], [navigator]);
        navigator.CurrentPageChanged += page => workspace.SwitchTo(page.Editor);
        return (workspace, navigator);
    }
}
