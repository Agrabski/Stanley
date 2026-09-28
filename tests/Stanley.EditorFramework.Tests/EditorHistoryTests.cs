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

public class EditorHistoryDirtyTests
{
    [Fact]
    public void NewHistory_IsClean_AndAnEditMakesItDirty()
    {
        var history = new EditorHistory();
        Assert.False(history.IsDirty);

        history.Push("edit", () => { }, () => { });
        Assert.True(history.IsDirty);
    }

    [Fact]
    public void MarkSaved_ThenUndoAndRedoBackToTheSavedState_IsCleanAgain()
    {
        var history = new EditorHistory();
        history.Push("a", () => { }, () => { });
        history.MarkSaved();
        Assert.False(history.IsDirty);

        history.Undo();
        Assert.True(history.IsDirty);

        history.Redo();
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void EditAfterUndoingPastTheSavePoint_StaysDirty()
    {
        var history = new EditorHistory();
        history.Push("a", () => { }, () => { });
        history.MarkSaved();
        history.Undo();
        history.Push("b", () => { }, () => { });

        Assert.True(history.IsDirty);
    }
    [Fact]
    public void Group_MakesEverythingPushedInsideOneUndoStep_UndoneInReverse()
    {
        var history = new EditorHistory();
        var log = new List<string>();
        var source = new object();
        object? restoredFrom = null;
        history.Restored += s => restoredFrom = s;

        using (history.Group("Margins", source))
        {
            history.Push("a", () => log.Add("undo a"), () => log.Add("redo a"));
            using (history.Group("inner"))
                history.Push("b", () => log.Add("undo b"), () => log.Add("redo b"));
            Assert.False(history.CanUndo); // nothing lands until the group closes
        }

        Assert.True(history.CanUndo);
        history.Undo();
        Assert.False(history.CanUndo);
        Assert.Equal(["undo b", "undo a"], log);
        Assert.Same(source, restoredFrom);
        history.Redo();
        Assert.Equal(["undo b", "undo a", "redo a", "redo b"], log);
    }

    [Fact]
    public void AnEmptyGroup_LeavesNoStep()
    {
        var history = new EditorHistory();
        history.MarkSaved();

        using (history.Group("Nothing"))
        {
        }

        Assert.False(history.CanUndo);
        Assert.False(history.IsDirty);
    }
}
