using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Stanley.EditorFramework;

/// <summary>
/// One shared undo/redo stack per open project, spanning every open editor pane - not
/// one private stack per pane. Each entry is a closure pair an <see cref="EditorViewModel{TDocument}"/>
/// hands over when it commits an edit; this class never needs to know what a
/// <c>BubbleDocument</c> or a page's panel set actually is.
/// </summary>
public sealed class EditorHistory : ObservableObject
{
    private readonly Stack<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();

    // The undo-stack top at the last save (null = empty stack). The document is unchanged
    // since then exactly when the current top is that same entry - so undoing back to the
    // saved state counts as clean again, the way Word's "saved" indicator behaves.
    private HistoryEntry? _savedTop;

    // While a Group is open, pushes collect here and become one entry when it closes.
    private List<HistoryEntry>? _group;

    public EditorHistory()
    {
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Whether anything has changed since the last <see cref="MarkSaved"/> (or since the history was created).</summary>
    public bool IsDirty => !ReferenceEquals(_undo.TryPeek(out var top) ? top : null, _savedTop);

    /// <summary>Records the current state as saved; <see cref="IsDirty"/> is false until the next edit, undo or redo moves away from it.</summary>
    public void MarkSaved()
    {
        _savedTop = _undo.TryPeek(out var top) ? top : null;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Raised after an undo or redo, with the <c>source</c> the entry was pushed with.</summary>
    public event Action<object?>? Restored;

    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }

    /// <summary>Records one committed edit. <paramref name="restoreBefore"/>/<paramref name="restoreAfter"/> must each fully restore the affected pane's state - they are the entire undo/redo implementation for this entry.</summary>
    /// <param name="source">Who made the edit (usually the editor pane). Reported by <see cref="Restored"/> so undoing an edit made elsewhere - on another page, say - can bring that place back into view first.</param>
    public void Push(string description, Action restoreBefore, Action restoreAfter, object? source = null)
    {
        var entry = new HistoryEntry(description, restoreBefore, restoreAfter, source);
        if (_group != null)
        {
            _group.Add(entry);
            return;
        }
        _undo.Push(entry);
        _redo.Clear();
        NotifyChanged();
    }

    /// <summary>
    /// Everything pushed until the returned scope is disposed - by any editor - becomes one
    /// undo step: undone in reverse order, redone in order. For one action that changes
    /// several panes at once, such as a margin every page follows. A group opened inside
    /// another just joins it; an empty group leaves no step at all.
    /// </summary>
    public IDisposable Group(string description, object? source = null)
    {
        if (_group != null)
            return new GroupScope(null);
        var entries = new List<HistoryEntry>();
        _group = entries;
        return new GroupScope(() =>
        {
            _group = null;
            if (entries.Count == 0)
                return;
            Push(description,
                () => { for (var i = entries.Count - 1; i >= 0; i--) entries[i].RestoreBefore(); },
                () => { foreach (var entry in entries) entry.RestoreAfter(); },
                source);
        });
    }

    public void Undo()
    {
        if (_undo.Count == 0)
            return;

        var entry = _undo.Pop();
        entry.RestoreBefore();
        _redo.Push(entry);
        NotifyChanged();
        Restored?.Invoke(entry.Source);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;

        var entry = _redo.Pop();
        entry.RestoreAfter();
        _undo.Push(entry);
        NotifyChanged();
        Restored?.Invoke(entry.Source);
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(IsDirty));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private sealed record HistoryEntry(string Description, Action RestoreBefore, Action RestoreAfter, object? Source);

    private sealed class GroupScope(Action? close) : IDisposable
    {
        private Action? _close = close;

        public void Dispose()
        {
            _close?.Invoke();
            _close = null;
        }
    }
}
