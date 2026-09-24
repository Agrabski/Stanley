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

    public EditorHistory()
    {
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }

    /// <summary>Records one committed edit. <paramref name="restoreBefore"/>/<paramref name="restoreAfter"/> must each fully restore the affected pane's state - they are the entire undo/redo implementation for this entry.</summary>
    public void Push(string description, Action restoreBefore, Action restoreAfter)
    {
        _undo.Push(new HistoryEntry(description, restoreBefore, restoreAfter));
        _redo.Clear();
        NotifyChanged();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
            return;

        var entry = _undo.Pop();
        entry.RestoreBefore();
        _redo.Push(entry);
        NotifyChanged();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;

        var entry = _redo.Pop();
        entry.RestoreAfter();
        _undo.Push(entry);
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private sealed record HistoryEntry(string Description, Action RestoreBefore, Action RestoreAfter);
}
