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
    private readonly IDock? _row;
    private readonly IDock? _rightDock;
    private IReadOnlyList<(IDockable Dockable, double Proportion)> _rowProportions = [];

    public EditorWorkspace(EditorHistory history, params IEditorPane[] panes)
        : this(history, panes, [])
    {
    }

    /// <param name="leftTools">Side panes (a navigator, an inspector) docked left of the editors. They're not editors, so activating one never changes <see cref="ActiveEditor"/> - the ribbon stays on the editor being worked in.</param>
    public EditorWorkspace(EditorHistory history, IReadOnlyList<IEditorPane> panes, IReadOnlyList<IDockable> leftTools)
        : this(history, panes, leftTools, [])
    {
    }

    /// <param name="rightTools">Side panes docked right of the editors (the Layers pane). Like the left ones they never change <see cref="ActiveEditor"/>; unlike them they can be shown and hidden (<see cref="SetRightToolsVisible"/>).</param>
    /// <param name="rightToolsVisible">Whether the right-hand panes start out showing.</param>
    public EditorWorkspace(EditorHistory history, IReadOnlyList<IEditorPane> panes, IReadOnlyList<IDockable> leftTools,
        IReadOnlyList<IDockable> rightTools, bool rightToolsVisible = true)
    {
        History = history;
        (Factory, Layout) = EditorDockHost.CreateLayout(panes, leftTools, rightTools);
        _activeEditor = panes.FirstOrDefault();
        _row = FindDock(EditorDockHost.MainRowId);
        _rightDock = FindDock(EditorDockHost.RightToolsDockId);

        Factory.ActiveDockableChanged += (_, e) => Follow(e.Dockable);
        Factory.FocusedDockableChanged += (_, e) => Follow(e.Dockable);
        Factory.DockableClosed += (_, e) =>
        {
            if (ReferenceEquals(e.Dockable, ActiveEditor))
                ActiveEditor = OpenEditors().FirstOrDefault();
        };

        if (!rightToolsVisible)
            SetRightToolsVisible(false);
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

    /// <summary>Whether the right-hand panes (the Layers pane) are showing. False too when the workspace has none.</summary>
    public bool RightToolsVisible => _row?.VisibleDockables is { } shown && _rightDock != null && shown.Contains(_rightDock);

    /// <summary>
    /// Shows or hides the right-hand panes; the editors reclaim the room when they go. Showing
    /// them again gives every column the width it had when they were hidden - without that,
    /// Dock re-normalises the proportions on each toggle and the pane creeps narrower every
    /// time. Does nothing for a workspace without right-hand panes.
    /// </summary>
    public void SetRightToolsVisible(bool visible)
    {
        if (_row is null || _rightDock is null || visible == RightToolsVisible)
            return;

        if (visible)
        {
            Factory.AddDockable(_row, Factory.CreateProportionalDockSplitter());
            Factory.AddDockable(_row, _rightDock);
            foreach (var (dockable, proportion) in _rowProportions)
            {
                dockable.Proportion = proportion;
                dockable.CollapsedProportion = proportion;
            }
        }
        else
        {
            _rowProportions = (_row.VisibleDockables ?? []).Where(d => d is not IProportionalDockSplitter).Select(d => (d, d.Proportion)).ToList();
            Factory.RemoveDockable(_rightDock, collapse: false);
            // Dock cleans up the splitter the right pane leaves behind; make sure it did.
            while (_row.VisibleDockables?.LastOrDefault() is IProportionalDockSplitter orphan)
                Factory.RemoveDockable(orphan, collapse: false);
            if (FindEditorsDock() is { ActiveDockable: { } active } editors)
                Factory.SetFocusedDockable(editors, active);
        }
        OnPropertyChanged(nameof(RightToolsVisible));
    }

    private IDock? FindEditorsDock() => FindDock(EditorDockHost.EditorsDockId);

    private IDock? FindDock(string id)
    {
        var pending = new Stack<IDockable>([Layout]);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next is IDock dock)
            {
                if (dock.Id == id)
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
