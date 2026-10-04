using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace Stanley.EditorFramework.Tests;

public class RightToolsTests
{
    private static (EditorWorkspace Workspace, TestEditor Editor, Tool Left, Tool Right) Build(bool rightVisible = true, bool withRight = true)
    {
        var history = new EditorHistory();
        var editor = new TestEditor(history, new TestDocument(0));
        var left = new Tool { Id = "Pages" };
        var right = new Tool { Id = "Layers" };
        var workspace = withRight
            ? new EditorWorkspace(history, [editor], [left], [right], rightVisible)
            : new EditorWorkspace(history, [editor], [left]);
        return (workspace, editor, left, right);
    }

    private static IDock DockById(EditorWorkspace workspace, string id)
    {
        var pending = new Stack<IDockable>([workspace.Layout]);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (next is not IDock dock)
                continue;
            if (dock.Id == id)
                return dock;
            foreach (var child in dock.VisibleDockables ?? [])
                pending.Push(child);
        }
        throw new InvalidOperationException($"no dock '{id}' in this layout");
    }

    private static IDock Row(EditorWorkspace workspace) => DockById(workspace, EditorDockHost.MainRowId);

    /// <summary>The row's columns left to right, with a splitter shown as "|".</summary>
    private static string Columns(EditorWorkspace workspace) =>
        string.Join(' ', (Row(workspace).VisibleDockables ?? []).Select(d => d is IProportionalDockSplitter ? "|" : d.Id));

    [Fact]
    public void A_workspace_with_right_tools_lays_them_out_after_the_editors()
    {
        var (workspace, _, _, _) = Build();

        Assert.Equal("LeftTools | Editors | RightTools", Columns(workspace));
        Assert.True(workspace.RightToolsVisible);
    }

    [Fact]
    public void Right_tools_can_start_hidden()
    {
        var (workspace, _, _, _) = Build(rightVisible: false);

        Assert.Equal("LeftTools | Editors", Columns(workspace));
        Assert.False(workspace.RightToolsVisible);
    }

    [Fact]
    public void Hiding_and_showing_the_right_tools_restores_the_same_columns_and_leaves_no_stray_splitter()
    {
        var (workspace, _, _, _) = Build();

        workspace.SetRightToolsVisible(false);
        Assert.Equal("LeftTools | Editors", Columns(workspace));
        Assert.False(workspace.RightToolsVisible);

        workspace.SetRightToolsVisible(true);
        Assert.Equal("LeftTools | Editors | RightTools", Columns(workspace));
        Assert.True(workspace.RightToolsVisible);
    }

    [Fact]
    public void Showing_or_hiding_twice_in_a_row_changes_nothing_the_second_time()
    {
        var (workspace, _, _, _) = Build();
        var changes = new List<string?>();
        workspace.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        workspace.SetRightToolsVisible(true); // already showing
        Assert.Empty(changes);

        workspace.SetRightToolsVisible(false);
        workspace.SetRightToolsVisible(false);
        Assert.Equal([nameof(EditorWorkspace.RightToolsVisible)], changes);
        Assert.Equal("LeftTools | Editors", Columns(workspace));
    }

    [Fact]
    public void Toggling_repeatedly_keeps_every_columns_width()
    {
        var (workspace, _, _, _) = Build();
        var columns = (Row(workspace).VisibleDockables ?? []).Where(d => d is not IProportionalDockSplitter).ToList();
        columns[0].Proportion = 0.10; // left
        columns[1].Proportion = 0.75; // editors
        columns[2].Proportion = 0.15; // right

        for (var i = 0; i < 5; i++)
        {
            workspace.SetRightToolsVisible(false);
            // What Dock's panel does with the room: hand it all to whoever is left.
            columns[0].Proportion = 0.12;
            columns[1].Proportion = 0.88;
            workspace.SetRightToolsVisible(true);
        }

        Assert.Equal(0.10, columns[0].Proportion, 6);
        Assert.Equal(0.75, columns[1].Proportion, 6);
        Assert.Equal(0.15, columns[2].Proportion, 6);
    }

    [Fact]
    public void A_workspace_without_right_tools_ignores_the_switch()
    {
        var (workspace, _, _, _) = Build(withRight: false);

        workspace.SetRightToolsVisible(true);
        workspace.SetRightToolsVisible(false);

        Assert.False(workspace.RightToolsVisible);
        Assert.Equal("LeftTools | Editors", Columns(workspace));
    }

    [Fact]
    public void Activating_a_right_tool_never_moves_the_ribbon_off_the_editor()
    {
        var (workspace, editor, _, right) = Build();

        workspace.Factory.SetActiveDockable(right);
        workspace.Factory.SetFocusedDockable(DockById(workspace, EditorDockHost.RightToolsDockId), right);

        Assert.Same(editor, workspace.ActiveEditor);
    }
}
