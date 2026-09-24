using Dock.Model.Mvvm.Controls;
using Stanley.Editing.Abstractions;

namespace Stanley.EditorFramework;

/// <summary>
/// Base for every editor pane: live-preview drag lifecycle plus commits into a shared,
/// project-wide <see cref="EditorHistory"/>. Extends Dock.Avalonia's <see cref="Document"/>
/// directly (rather than wrapping one) so a concrete editor - e.g. a future
/// <c>PageEditorViewModel</c> - can be dropped straight into a <c>DocumentDock.VisibleDockables</c>
/// list with no adapter layer; it already is a dockable pane.
///
/// <typeparamref name="TDocument"/> must be an immutable value (a C# <c>record</c>
/// satisfies this): <see cref="Committed"/>/<see cref="Working"/> are pushed onto the
/// undo stack by value, so a mutable reference type here would let later mutation
/// corrupt history silently.
/// </summary>
public abstract class EditorViewModel<TDocument> : Document, IEditorPane
    where TDocument : notnull, IEquatable<TDocument>
{
    private readonly EditorHistory _history;
    private TDocument _committed;
    private TDocument _working;
    private TDocument _gestureBaseline;
    private bool _gestureActive;
    private bool _gestureDirty;
    private string? _lastError;

    protected EditorViewModel(EditorHistory history, string title, TDocument initial)
    {
        _history = history;
        _committed = initial;
        _working = initial;
        _gestureBaseline = initial;
        Id = title;
        Title = title;
    }

    /// <summary>The last value committed to <see cref="EditorHistory"/> - what would be saved to disk.</summary>
    public TDocument Committed
    {
        get => _committed;
        private set => SetProperty(ref _committed, value);
    }

    /// <summary>What the view renders. Equal to <see cref="Committed"/> outside an active gesture; diverges during a live-preview drag.</summary>
    public TDocument Working
    {
        get => _working;
        private set => SetProperty(ref _working, value);
    }

    /// <summary>The most recent validation failure from <see cref="UpdateGesture"/>/<see cref="Apply"/>, or null. An invalid result during a gesture leaves <see cref="Working"/> at its last valid value rather than advancing the preview.</summary>
    public string? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    /// <summary>The shared undo/redo stack this editor commits into, so a pane's toolbar can offer undo/redo without being handed it separately.</summary>
    public EditorHistory History => _history;

    public bool IsGestureActive => _gestureActive;

    /// <summary>Starts a drag/gesture: captures the baseline to restore on <see cref="CancelGesture"/> or to record as the undo target on <see cref="CommitGesture"/>.</summary>
    public void BeginGesture()
    {
        _gestureBaseline = Committed;
        _gestureActive = true;
        _gestureDirty = false;
        LastError = null;
    }

    /// <summary>Called on every pointer move with the result of the pure editing function for the current pointer position. Ignored once a gesture has ended.</summary>
    public void UpdateGesture(EditResult<TDocument> result)
    {
        if (!_gestureActive)
            return;

        if (!result.IsValid)
        {
            LastError = result.Error;
            return;
        }

        LastError = null;
        _gestureDirty = true;
        Working = result.Value;
    }

    /// <summary>
    /// Ends the gesture, committing it as one undo entry if it actually changed
    /// anything. Uses a dirty flag rather than comparing <see cref="Working"/> to the
    /// baseline by value: a record containing a list/dictionary member only gets
    /// reference equality from its compiler-generated Equals, so two structurally
    /// identical values built independently can compare unequal - a flag has no such
    /// false negative.
    /// </summary>
    public void CommitGesture()
    {
        if (!_gestureActive)
            return;

        _gestureActive = false;
        if (!_gestureDirty)
            return;

        var before = _gestureBaseline;
        var after = Working;
        _history.Push(Title, () => Working = Committed = before, () => Working = Committed = after);
        Committed = after;
    }

    /// <summary>Ends the gesture, snapping <see cref="Working"/> back to <see cref="Committed"/> exactly. No history entry - nothing was ever committed.</summary>
    public void CancelGesture()
    {
        if (!_gestureActive)
            return;

        _gestureActive = false;
        Working = Committed;
    }

    /// <summary>A one-shot edit outside any drag (a button click, a menu action): applied immediately and recorded as one undo entry.</summary>
    public void Apply(EditResult<TDocument> result)
    {
        if (!result.IsValid)
        {
            LastError = result.Error;
            return;
        }

        LastError = null;
        var before = Committed;
        var after = result.Value;
        if (before.Equals(after))
            return;

        _history.Push(Title, () => Working = Committed = before, () => Working = Committed = after);
        Working = Committed = after;
    }
}
