using Stanley.EditorFramework;

namespace Stanley.Editors;

public static class PageEditorHost
{
    /// <summary>A fresh editing session for <paramref name="project"/>: its own undo history (clean, i.e. "saved"), and a page editor pane on the project's page.</summary>
    public static (EditorWorkspace Workspace, PageEditorViewModel Editor) CreateWorkspace(ComicProject project)
    {
        var history = new EditorHistory();
        var editor = new PageEditorViewModel(history, project.PageBounds, project.Document);
        return (new EditorWorkspace(history, editor), editor);
    }

    /// <summary>A blank, unsaved A4 comic - what the app opens with, like a blank Word document.</summary>
    public static (EditorWorkspace Workspace, PageEditorViewModel Editor) CreateDemoWorkspace() =>
        CreateWorkspace(ComicProject.CreateNew());
}
