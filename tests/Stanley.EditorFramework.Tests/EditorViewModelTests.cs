using Stanley.Editing.Abstractions;
using Xunit;

namespace Stanley.EditorFramework.Tests;

public sealed record TestDocument(int Value);

public sealed class TestEditor(EditorHistory history, TestDocument initial)
    : EditorViewModel<TestDocument>(history, "Test", initial);

public class EditorViewModelTests
{
    [Fact]
    public void CommitGesture_PushesOneUndoEntryAndUpdatesCommitted()
    {
        var history = new EditorHistory();
        var editor = new TestEditor(history, new TestDocument(0));

        editor.BeginGesture();
        editor.UpdateGesture(EditResult<TestDocument>.Success(new TestDocument(5)));
        editor.CommitGesture();

        Assert.Equal(new TestDocument(5), editor.Committed);
        Assert.Equal(new TestDocument(5), editor.Working);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void CommitGesture_WithNoUpdate_PushesNothing()
    {
        var history = new EditorHistory();
        var editor = new TestEditor(history, new TestDocument(0));

        editor.BeginGesture();
        editor.CommitGesture();

        Assert.False(history.CanUndo);
        Assert.Equal(new TestDocument(0), editor.Committed);
    }

    [Fact]
    public void CancelGesture_RestoresWorkingToCommittedExactly_AndPushesNoHistory()
    {
        var history = new EditorHistory();
        var editor = new TestEditor(history, new TestDocument(0));

        editor.BeginGesture();
        editor.UpdateGesture(EditResult<TestDocument>.Success(new TestDocument(99)));
        editor.CancelGesture();

        Assert.Equal(new TestDocument(0), editor.Working);
        Assert.Equal(new TestDocument(0), editor.Committed);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void UpdateGesture_WithInvalidResult_LeavesWorkingAtLastValidValueAndSetsLastError()
    {
        var history = new EditorHistory();
        var editor = new TestEditor(history, new TestDocument(0));

        editor.BeginGesture();
        editor.UpdateGesture(EditResult<TestDocument>.Success(new TestDocument(3)));
        editor.UpdateGesture(EditResult<TestDocument>.Failure("too big"));

        Assert.Equal(new TestDocument(3), editor.Working);
        Assert.Equal("too big", editor.LastError);
    }

    [Fact]
    public void Undo_AfterCommit_RestoresPreGestureValue()
    {
        var history = new EditorHistory();
        var editor = new TestEditor(history, new TestDocument(0));
        editor.BeginGesture();
        editor.UpdateGesture(EditResult<TestDocument>.Success(new TestDocument(5)));
        editor.CommitGesture();

        history.Undo();

        Assert.Equal(new TestDocument(0), editor.Committed);
        Assert.Equal(new TestDocument(0), editor.Working);
    }

    [Fact]
    public void Apply_OneShotEdit_CommitsImmediatelyAndRecordsHistory()
    {
        var history = new EditorHistory();
        var editor = new TestEditor(history, new TestDocument(0));

        editor.Apply(EditResult<TestDocument>.Success(new TestDocument(7)));

        Assert.Equal(new TestDocument(7), editor.Committed);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void Apply_WithSameValue_PushesNothing()
    {
        var history = new EditorHistory();
        var editor = new TestEditor(history, new TestDocument(0));

        editor.Apply(EditResult<TestDocument>.Success(new TestDocument(0)));

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Apply_WithInvalidResult_LeavesCommittedUnchangedAndSetsLastError()
    {
        var history = new EditorHistory();
        var editor = new TestEditor(history, new TestDocument(0));

        editor.Apply(EditResult<TestDocument>.Failure("nope"));

        Assert.Equal(new TestDocument(0), editor.Committed);
        Assert.Equal("nope", editor.LastError);
        Assert.False(history.CanUndo);
    }
}

public class EditorWorkspaceTests
{
    [Fact]
    public void ActiveEditor_StartsOnTheFirstPane()
    {
        var history = new EditorHistory();
        var first = new TestEditor(history, new TestDocument(0));
        var second = new TestEditor(history, new TestDocument(1));

        var workspace = new EditorWorkspace(history, first, second);

        Assert.Same(first, workspace.ActiveEditor);
    }

    [Fact]
    public void ActiveEditor_FollowsTheDockLayoutsActivePane()
    {
        var history = new EditorHistory();
        var first = new TestEditor(history, new TestDocument(0));
        var second = new TestEditor(history, new TestDocument(1));
        var workspace = new EditorWorkspace(history, first, second);
        var changes = new List<string?>();
        workspace.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        workspace.Factory.SetActiveDockable(second);
        Assert.Same(second, workspace.ActiveEditor);

        workspace.Activate(first);
        Assert.Same(first, workspace.ActiveEditor);
        Assert.Contains(nameof(EditorWorkspace.ActiveEditor), changes);
    }
}
