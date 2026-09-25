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
        : this(history, panes, [])
    {
    }

    /// <param name="leftTools">Side panes (a navigator, an inspector) docked left of the editors. They're not editors, so activating one never changes <see cref="ActiveEditor"/> - the ribbon stays on the editor being worked in.</param>
    public EditorWorkspace(EditorHistory history, IReadOnlyList<IEditorPane> panes, IReadOnlyList<IDockable> leftTools)
    {
        History = history;
        (Factory, Layout) = EditorDockHost.CreateLayout(panes, leftTools);
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

    /// <summary>
    /// Shows <paramref name="editor"/> as a tab alongside whatever's already open - adding
    /// it if it isn't there yet - and makes it active. Unlike <see cref="Replace"/>, nothing
    /// else open is closed: this is how opening a character works, so it never throws away
    /// the page (or another character) the user was already looking at.
    /// </summary>
    public void Show(IEditorPane editor)
    {
        if (FindEditorsDock() is not { } dock)
            return;

        if (dock.VisibleDockables is null || !dock.VisibleDockables.Contains(editor))
            Factory.AddDockable(dock, editor);

        dock.ActiveDockable = editor;
        Factory.SetActiveDockable(editor);
        ActiveEditor = editor;
    }

    /// <summary>
    /// Shows <paramref name="editor"/> as the active tab - adding it if it isn't open yet -
    /// and closes <paramref name="closing"/> if it was open. Used where exactly one tab of a
    /// kind makes sense: the page navigator swaps the current page's tab for the newly
    /// picked page's, and a character's "back to the page" closes just that character's tab.
    /// Anything else open (another character's tab) is left alone.
    /// </summary>
    public void Replace(IEditorPane? closing, IEditorPane editor)
    {
        if (FindEditorsDock() is not { } dock)
            return;

        var shown = dock.VisibleDockables?.ToList() ?? [];
        if (!shown.Contains(editor))
            Factory.AddDockable(dock, editor);
        if (closing != null && !ReferenceEquals(closing, editor) && shown.Contains(closing))
            Factory.RemoveDockable(closing, collapse: false);

        dock.ActiveDockable = editor;
        Factory.SetActiveDockable(editor);
        ActiveEditor = editor;
    }

    /// <summary>
    /// Closes <paramref name="editor"/>'s tab if it's open, even if it isn't the active one
    /// (e.g. deleting a character whose tab is open in the background). If it was active,
    /// falls back to whatever tab is still open.
    /// </summary>
    public void Close(IEditorPane editor)
    {
        if (FindEditorsDock() is not { } dock || dock.VisibleDockables is not { } shown || !shown.Contains(editor))
            return;

        var wasActive = ReferenceEquals(editor, ActiveEditor);
        Factory.RemoveDockable(editor, collapse: false);
        if (!wasActive)
            return;

        var next = dock.VisibleDockables?.OfType<IEditorPane>().FirstOrDefault();
        if (next != null)
        {
            dock.ActiveDockable = next;
            Factory.SetActiveDockable(next);
        }
        ActiveEditor = next;
    }

    private IDock? FindEditorsDock()
    {
        var pending = new Stack<IDockable>([Layout]);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next is IDock dock)
            {
                if (dock.Id == EditorDockHost.EditorsDockId)
                    return dock;
                foreach (var child in dock.VisibleDockables ?? [])
                    pending.Push(child);
            }
        }
        return null;
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
