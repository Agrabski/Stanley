using Xunit;

namespace Stanley.EditorFramework.Tests;

public class EditorHistoryTests
{
    [Fact]
    public void Undo_RunsTheEntrysRestoreBeforeClosure()
    {
        var history = new EditorHistory();
        var state = 0;
        history.Push("set to 1", () => state = 0, () => state = 1);
        state = 1;

        history.Undo();

        Assert.Equal(0, state);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void Redo_RunsTheEntrysRestoreAfterClosure()
    {
        var history = new EditorHistory();
        var state = 0;
        history.Push("set to 1", () => state = 0, () => state = 1);
        state = 1;
        history.Undo();

        history.Redo();

        Assert.Equal(1, state);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_ClearsTheRedoStack()
    {
        var history = new EditorHistory();
        history.Push("a", () => { }, () => { });
        history.Undo();
        Assert.True(history.CanRedo);

        history.Push("b", () => { }, () => { });

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Undo_WithNothingToUndo_DoesNothing()
    {
        var history = new EditorHistory();
        history.Undo(); // should not throw
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void MultipleEntries_UndoInLifoOrder()
    {
        var history = new EditorHistory();
        var log = new List<string>();
        history.Push("first", () => log.Add("undo-first"), () => log.Add("redo-first"));
        history.Push("second", () => log.Add("undo-second"), () => log.Add("redo-second"));

        history.Undo();
        history.Undo();

        Assert.Equal(["undo-second", "undo-first"], log);
    }
}
