using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>Group and Ungroup (issue #86): welding a multi-selection of panel elements into one persistent <see cref="GroupElement"/>, and taking one apart again.</summary>
public class GroupingCommandsTests
{
    private static (PageEditorViewModel Editor, EditorHistory History, PanelId Panel) NewEditor()
    {
        var history = new EditorHistory();
        var panelId = PanelId.New();
        var panel = new Panel(panelId, PanelShapes.Rectangle(new Rect2D(10, 10, 150, 150)), null, [], []);
        var document = new PageDocument([panelId], new Dictionary<PanelId, Panel> { [panelId] = panel });
        return (new PageEditorViewModel(history, new Rect2D(0, 0, 210, 297), document), history, panelId);
    }

    private static ShapeElement DrawRectangle(PageEditorViewModel editor, PanelId panel, Rect2D box)
    {
        editor.Tool = PageEditorTool.Rectangle;
        editor.BeginDrawShape(panel);
        editor.UpdateDrawShape(new Point2D(box.Left, box.Top), new Point2D(box.Right, box.Bottom));
        var index = editor.CommitDrawShape();
        return (ShapeElement)editor.Working.Panels[panel].Elements[index];
    }

    [Fact]
    public void GroupSelectionCommand_CannotExecute_WithFewerThanTwoElementsSelected()
    {
        var (editor, _, panel) = NewEditor();
        Assert.False(editor.GroupSelectionCommand.CanExecute(null)); // nothing selected

        DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20)); // draws and selects it, alone
        Assert.False(editor.GroupSelectionCommand.CanExecute(null));
    }

    [Fact]
    public void GroupSelectionCommand_CannotExecute_WhenTheSelectionIncludesABubbleOrACharacter()
    {
        var (editor, _, panel) = NewEditor();
        var shape = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        var shapeIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == shape.Id);

        var bubbleIndex = editor.CreateBubble(panel, new Point2D(100, 100));
        editor.ToggleSelect(panel, elementIndex: shapeIndex);
        Assert.True(editor.HasMultiSelection);
        Assert.False(editor.GroupSelectionCommand.CanExecute(null)); // a bubble is part of the selection

        editor.ToggleSelect(panel, bubbleIndex: bubbleIndex); // drop the bubble
        var characterIndex = editor.InsertCharacter(CharacterId.New(), panel); // becomes sole primary
        editor.ToggleSelect(panel, elementIndex: shapeIndex);
        Assert.True(editor.HasMultiSelection);
        Assert.False(editor.GroupSelectionCommand.CanExecute(null)); // a character is part of the selection
    }

    [Fact]
    public void GroupSelectionCommand_CannotExecute_WhenSelectedElementsAreOnDifferentLayers()
    {
        var (editor, _, panel) = NewEditor();
        var a = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        var aIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == a.Id);
        var b = DrawRectangle(editor, panel, new Rect2D(60, 60, 20, 20));
        var bIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == b.Id);

        editor.SetElementLayer(panel, aIndex, ElementLayer.Foreground);
        editor.SelectElement(panel, aIndex);
        editor.ToggleSelect(panel, elementIndex: bIndex);
        Assert.True(editor.HasMultiSelection);
        Assert.False(editor.GroupSelectionCommand.CanExecute(null));
    }

    [Fact]
    public void GroupSelectionCommand_CanExecute_WithTwoOrMoreSameLayerElementsSelected()
    {
        var (editor, _, panel) = NewEditor();
        var a = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        var aIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == a.Id);
        var b = DrawRectangle(editor, panel, new Rect2D(60, 60, 20, 20));
        var bIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == b.Id);

        editor.SelectElement(panel, aIndex);
        editor.ToggleSelect(panel, elementIndex: bIndex);
        Assert.True(editor.GroupSelectionCommand.CanExecute(null));
    }

    [Fact]
    public void Grouping_two_elements_replaces_them_with_one_group_element_as_one_undo_step()
    {
        var (editor, history, panel) = NewEditor();
        var a = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        var aIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == a.Id);
        var b = DrawRectangle(editor, panel, new Rect2D(60, 60, 20, 20));
        var bIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == b.Id);

        editor.SelectElement(panel, aIndex);
        editor.ToggleSelect(panel, elementIndex: bIndex);
        editor.GroupSelectionCommand.Execute(null);

        var elements = editor.Working.Panels[panel].Elements;
        var group = Assert.IsType<GroupElement>(Assert.Single(elements));
        Assert.Equal(2, group.Children.Count);
        Assert.Contains(group.Children, c => c.Id == a.Id);
        Assert.Contains(group.Children, c => c.Id == b.Id);

        history.Undo(); // undoes only the group - drawing a and b were their own, earlier undo steps
        var restored = editor.Working.Panels[panel].Elements;
        Assert.Equal(2, restored.Count);
        Assert.Contains(restored, e => e.Id == a.Id);
        Assert.Contains(restored, e => e.Id == b.Id);
    }

    [Fact]
    public void Grouping_selects_the_new_group_afterward()
    {
        var (editor, _, panel) = NewEditor();
        var a = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        var aIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == a.Id);
        var b = DrawRectangle(editor, panel, new Rect2D(60, 60, 20, 20));
        var bIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == b.Id);

        editor.SelectElement(panel, aIndex);
        editor.ToggleSelect(panel, elementIndex: bIndex);
        editor.GroupSelectionCommand.Execute(null);

        Assert.False(editor.HasMultiSelection);
        Assert.Equal(1, editor.SelectionCount);
        Assert.IsType<GroupElement>(editor.SelectedElement);
    }

    [Fact]
    public void UngroupSelectionCommand_CanExecute_OnlyWhenAGroupElementIsSelected()
    {
        var (editor, _, panel) = NewEditor();
        Assert.False(editor.UngroupSelectionCommand.CanExecute(null)); // nothing selected

        var shape = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20)); // selected, but not a group
        Assert.False(editor.UngroupSelectionCommand.CanExecute(null));

        var b = DrawRectangle(editor, panel, new Rect2D(60, 60, 20, 20));
        var aIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == shape.Id);
        var bIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == b.Id);
        editor.SelectElement(panel, aIndex);
        editor.ToggleSelect(panel, elementIndex: bIndex);
        editor.GroupSelectionCommand.Execute(null); // selects the new group

        Assert.True(editor.UngroupSelectionCommand.CanExecute(null));
    }

    [Fact]
    public void Ungrouping_restores_the_original_children_at_the_groups_old_position_as_one_undo_step()
    {
        var (editor, history, panel) = NewEditor();
        var a = DrawRectangle(editor, panel, new Rect2D(10, 20, 20, 20));
        var b = DrawRectangle(editor, panel, new Rect2D(40, 40, 20, 20));
        var c = DrawRectangle(editor, panel, new Rect2D(70, 70, 20, 20));

        int IndexOf(ElementId id) => editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == id);

        // Group the two most recent (b, c), adjacent at the front - a stays loose in front of them.
        editor.SelectElement(panel, IndexOf(b.Id));
        editor.ToggleSelect(panel, elementIndex: IndexOf(c.Id));
        editor.GroupSelectionCommand.Execute(null);

        var afterGroup = editor.Working.Panels[panel].Elements;
        Assert.Equal(2, afterGroup.Count);
        Assert.Equal(a.Id, afterGroup[0].Id);
        var group = Assert.IsType<GroupElement>(afterGroup[1]);
        Assert.Equal(group.Id, editor.SelectedElement!.Id); // ungroup acts on the selected group

        editor.UngroupSelectionCommand.Execute(null);

        var restored = editor.Working.Panels[panel].Elements;
        Assert.Equal(3, restored.Count);
        Assert.Equal([a.Id, b.Id, c.Id], restored.Select(e => e.Id).ToList());

        history.Undo(); // one undo step brings the group back
        var undone = editor.Working.Panels[panel].Elements;
        Assert.Equal(2, undone.Count);
        Assert.Equal(a.Id, undone[0].Id);
        Assert.IsType<GroupElement>(undone[1]);
        Assert.Equal(group.Id, undone[1].Id);
    }

    [Fact]
    public void Nudging_a_selected_group_moves_every_child_by_the_same_delta()
    {
        var (editor, _, panel) = NewEditor();
        var a = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        var aIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == a.Id);
        var b = DrawRectangle(editor, panel, new Rect2D(60, 60, 20, 20));
        var bIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == b.Id);

        editor.SelectElement(panel, aIndex);
        editor.ToggleSelect(panel, elementIndex: bIndex);
        editor.GroupSelectionCommand.Execute(null); // selects the new group

        var group = Assert.IsType<GroupElement>(editor.SelectedElement);
        var before = group.Children.ToDictionary(c => c.Id, PanelElements.Bounds);

        editor.NudgeSelection(5, 3);

        var after = Assert.IsType<GroupElement>(editor.SelectedElement);
        foreach (var child in after.Children)
        {
            var was = before[child.Id];
            var now = PanelElements.Bounds(child);
            Assert.Equal(was.X + 5, now.X, 3);
            Assert.Equal(was.Y + 3, now.Y, 3);
        }
    }

    [Fact]
    public void Duplicating_a_selected_group_produces_a_group_with_a_new_id_and_new_child_ids()
    {
        var (editor, _, panel) = NewEditor();
        var a = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        var aIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == a.Id);
        var b = DrawRectangle(editor, panel, new Rect2D(60, 60, 20, 20));
        var bIndex = editor.Working.Panels[panel].Elements.ToList().FindIndex(e => e.Id == b.Id);

        editor.SelectElement(panel, aIndex);
        editor.ToggleSelect(panel, elementIndex: bIndex);
        editor.GroupSelectionCommand.Execute(null);
        var original = Assert.IsType<GroupElement>(editor.SelectedElement);

        Assert.True(editor.Duplicate());

        var elements = editor.Working.Panels[panel].Elements;
        Assert.Equal(2, elements.Count); // the original group plus its copy
        var copy = Assert.IsType<GroupElement>(editor.SelectedElement);

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(original.Children.Count, copy.Children.Count);
        Assert.Empty(copy.Children.Select(c => c.Id).Intersect(original.Children.Select(c => c.Id)));
    }
}
