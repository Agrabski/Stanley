using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Stanley.EditorFramework;

/// <summary>
/// One open project's editing session: its shared <see cref="EditorHistory"/>, its pane
/// layout, and which editor pane is currently active. The window binds its ribbon to
/// <see cref="ActiveEditor"/>, so the ribbon always shows the tools of the pane being
/// worked in - one ribbon above all panes rather than a toolbar duplicated inside each.
/// </summary>
public sealed class EditorWorkspace : ObservableObject
{
    private IEditorPane? _activeEditor;

    public EditorWorkspace(EditorHistory history, params IEditorPane[] panes)
    {
        History = history;
        (Factory, Layout) = EditorDockHost.CreateLayout(panes);
        _activeEditor = panes.FirstOrDefault();

        Factory.ActiveDockableChanged += (_, e) => Follow(e.Dockable);
        Factory.FocusedDockableChanged += (_, e) => Follow(e.Dockable);
        Factory.DockableClosed += (_, e) =>
        {
            if (ReferenceEquals(e.Dockable, ActiveEditor))
                ActiveEditor = OpenEditors().FirstOrDefault();
        };
    }

    public EditorHistory History { get; }

    public IFactory Factory { get; }

    public IRootDock Layout { get; }

    /// <summary>The editor pane the user is working in, or null when none is open. Only editor panes count: activating a non-editor dockable (a future inspector, say) leaves the ribbon on the last editor.</summary>
    public IEditorPane? ActiveEditor
    {
        get => _activeEditor;
        private set => SetProperty(ref _activeEditor, value);
    }

    /// <summary>Makes <paramref name="editor"/> the active pane, in the dock layout as well as here.</summary>
    public void Activate(IEditorPane editor)
    {
        Factory.SetActiveDockable(editor);
        ActiveEditor = editor;
    }

    private void Follow(IDockable? dockable)
    {
        if (dockable is IEditorPane editor)
            ActiveEditor = editor;
    }

    private IEnumerable<IEditorPane> OpenEditors()
    {
        var pending = new Stack<IDockable>([Layout]);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next is IEditorPane editor)
                yield return editor;
            if (next is IDock { VisibleDockables: { } children })
            {
                foreach (var child in children)
                    pending.Push(child);
            }
        }
    }
}
