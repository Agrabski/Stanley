using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>
/// Grouping a character or a bubble with shapes (issue #125): a <see cref="GroupElement"/> can only
/// hold elements of one layer, so these are tied together by a shared link instead - one thing to
/// click, drag, nudge, copy and delete, each keeping its own place in the drawing order.
/// </summary>
public class GroupingMixedTests
{
    private static (PageEditorViewModel Editor, EditorHistory History, PanelId Panel) NewEditor()
    {
        var history = new EditorHistory();
        var panelId = PanelId.New();
        var panel = new Panel(panelId, PanelShapes.Rectangle(new Rect2D(10, 10, 150, 150)), null, [], []);
        var document = new PageDocument([panelId], new Dictionary<PanelId, Panel> { [panelId] = panel });
        return (new PageEditorViewModel(history, new Rect2D(0, 0, 210, 297), document), history, panelId);
    }

    private static int DrawRectangle(PageEditorViewModel editor, PanelId panel, Rect2D box)
    {
        editor.Tool = PageEditorTool.Rectangle;
        editor.BeginDrawShape(panel);
        editor.UpdateDrawShape(new Point2D(box.Left, box.Top), new Point2D(box.Right, box.Bottom));
        var index = editor.CommitDrawShape();
        editor.Tool = PageEditorTool.Select;
        return index;
    }

    /// <summary>A shape and a character selected together - what the issue's screenshots show being refused.</summary>
    private static (PageEditorViewModel Editor, EditorHistory History, PanelId Panel, int Shape, int Character) ShapeAndCharacterSelected()
    {
        var (editor, history, panel) = NewEditor();
        var shape = DrawRectangle(editor, panel, new Rect2D(20, 20, 30, 30));
        var character = editor.InsertCharacter(CharacterId.New(), panel);
        editor.ToggleSelect(panel, elementIndex: shape);
        Assert.Equal(2, editor.SelectionCount);
        return (editor, history, panel, shape, character);
    }

    private static Panel PanelOf(PageEditorViewModel editor, PanelId panel) => editor.Working.Panels[panel];

    [Fact]
    public void Grouping_a_shape_and_a_character_ties_them_together_as_one_undo_step()
    {
        var (editor, history, panel, shape, character) = ShapeAndCharacterSelected();

        Assert.True(editor.GroupSelectionCommand.CanExecute(null));
        editor.GroupSelectionCommand.Execute(null);

        var grouped = PanelOf(editor, panel);
        var link = grouped.Elements[shape].Link;
        Assert.NotNull(link);
        Assert.Equal(link, grouped.CharacterInstances[character].Link);
        Assert.IsType<ShapeElement>(grouped.Elements[shape]); // still its own element, not wrapped
        Assert.Equal(2, editor.SelectionCount);

        history.Undo();

        var undone = PanelOf(editor, panel);
        Assert.Null(undone.Elements[shape].Link);
        Assert.Null(undone.CharacterInstances[character].Link);
    }

    [Fact]
    public void Grouping_a_bubble_with_a_character_ties_them_together()
    {
        var (editor, _, panel) = NewEditor();
        var character = editor.InsertCharacter(CharacterId.New(), panel);
        var bubble = editor.CreateBubble(panel, new Point2D(90, 90));
        editor.ToggleSelect(panel, characterIndex: character);

        editor.GroupSelectionCommand.Execute(null);

        var grouped = PanelOf(editor, panel);
        Assert.NotNull(grouped.Bubbles[bubble].Link);
        Assert.Equal(grouped.Bubbles[bubble].Link, grouped.CharacterInstances[character].Link);
    }

    [Fact]
    public void Elements_on_both_sides_of_the_characters_are_tied_together_and_stay_on_their_own_sides()
    {
        var (editor, _, panel) = NewEditor();
        var back = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        var front = DrawRectangle(editor, panel, new Rect2D(60, 60, 20, 20));
        editor.SetElementLayer(panel, front, ElementLayer.Foreground);
        editor.SelectElement(panel, back);
        editor.ToggleSelect(panel, elementIndex: front);

        editor.GroupSelectionCommand.Execute(null);

        var elements = PanelOf(editor, panel).Elements;
        Assert.Equal(2, elements.Count);
        Assert.All(elements, e => Assert.IsNotType<GroupElement>(e));
        Assert.NotNull(elements[back].Link);
        Assert.Equal(elements[back].Link, elements[front].Link);
        Assert.Equal(ElementLayer.Background, elements[back].Layer);
        Assert.Equal(ElementLayer.Foreground, elements[front].Layer);
    }

    [Fact]
    public void Elements_of_one_layer_still_weld_into_a_group_element()
    {
        var (editor, _, panel) = NewEditor();
        var a = DrawRectangle(editor, panel, new Rect2D(20, 20, 20, 20));
        var b = DrawRectangle(editor, panel, new Rect2D(60, 60, 20, 20));
        editor.SelectElement(panel, a);
        editor.ToggleSelect(panel, elementIndex: b);

        editor.GroupSelectionCommand.Execute(null);

        Assert.IsType<GroupElement>(Assert.Single(PanelOf(editor, panel).Elements));
    }

    [Fact]
    public void Clicking_any_one_member_selects_the_whole_group()
    {
        var (editor, _, panel, shape, character) = ShapeAndCharacterSelected();
        editor.GroupSelectionCommand.Execute(null);
        editor.Select(panel);
        Assert.Equal(0, editor.SelectionCount);

        editor.SelectElement(panel, shape);
        Assert.Equal(2, editor.SelectionCount);
        Assert.True(editor.IsPartOfSelection(panel, elementIndex: shape));
        Assert.True(editor.IsPartOfSelection(panel, characterIndex: character));

        editor.Select(panel);
        editor.SelectCharacter(panel, character);
        Assert.Equal(2, editor.SelectionCount);
        Assert.True(editor.IsPartOfSelection(panel, elementIndex: shape));
    }

    [Fact]
    public void Pressing_a_member_that_is_not_selected_selects_its_group_first()
    {
        var (editor, _, panel, shape, character) = ShapeAndCharacterSelected();
        editor.GroupSelectionCommand.Execute(null);
        editor.Select(panel);

        Assert.True(editor.SelectGroupOf(panel, characterIndex: character));

        Assert.Equal(2, editor.SelectionCount);
        Assert.True(editor.IsPartOfSelection(panel, elementIndex: shape));
        Assert.False(editor.SelectGroupOf(panel, characterIndex: character)); // already selected: nothing to do
    }

    [Fact]
    public void Shift_click_adds_and_removes_the_whole_group()
    {
        var (editor, _, panel, shape, character) = ShapeAndCharacterSelected();
        editor.GroupSelectionCommand.Execute(null);
        var other = DrawRectangle(editor, panel, new Rect2D(100, 100, 20, 20)); // selected alone

        Assert.Equal(1, editor.SelectionCount);
        editor.ToggleSelect(panel, characterIndex: character);
        Assert.Equal(3, editor.SelectionCount);
        Assert.True(editor.IsPartOfSelection(panel, elementIndex: shape));
        Assert.True(editor.IsPartOfSelection(panel, elementIndex: other));

        editor.ToggleSelect(panel, elementIndex: shape); // any member takes the whole group out again
        Assert.Equal(1, editor.SelectionCount);
        Assert.Equal(other, editor.SelectedElementIndex);
        Assert.False(editor.IsPartOfSelection(panel, characterIndex: character));
    }

    [Fact]
    public void A_group_moves_and_deletes_as_one()
    {
        var (editor, history, panel, shape, character) = ShapeAndCharacterSelected();
        editor.GroupSelectionCommand.Execute(null);
        editor.Select(panel);
        editor.SelectElement(panel, shape); // one click picks the group

        var shapeBefore = PanelElements.Bounds(PanelOf(editor, panel).Elements[shape]);
        var groundBefore = PanelOf(editor, panel).CharacterInstances[character].Placement.Ground;
        editor.NudgeSelection(4, 0);

        var moved = PanelOf(editor, panel);
        Assert.Equal(shapeBefore.X + 4, PanelElements.Bounds(moved.Elements[shape]).X, 3);
        Assert.Equal(groundBefore.X + 4, moved.CharacterInstances[character].Placement.Ground.X, 3);

        editor.DeleteSelection();
        var deleted = PanelOf(editor, panel);
        Assert.Empty(deleted.Elements);
        Assert.Empty(deleted.CharacterInstances);

        history.Undo(); // one undo step brings both back, still a group
        var restored = PanelOf(editor, panel);
        Assert.Single(restored.Elements);
        Assert.Single(restored.CharacterInstances);
        Assert.Equal(restored.Elements[0].Link, restored.CharacterInstances[0].Link);
    }

    [Fact]
    public void Group_is_not_offered_for_a_selection_that_is_already_exactly_one_group()
    {
        var (editor, _, _, _, _) = ShapeAndCharacterSelected();
        editor.GroupSelectionCommand.Execute(null);

        Assert.True(editor.HasMultiSelection);
        Assert.False(editor.GroupSelectionCommand.CanExecute(null));
        Assert.True(editor.UngroupSelectionCommand.CanExecute(null));
    }

    [Fact]
    public void Grouping_a_group_with_something_else_makes_one_bigger_group()
    {
        var (editor, _, panel, shape, character) = ShapeAndCharacterSelected();
        editor.GroupSelectionCommand.Execute(null);
        var other = DrawRectangle(editor, panel, new Rect2D(100, 100, 20, 20));
        editor.ToggleSelect(panel, characterIndex: character); // the group joins the selection whole
        Assert.Equal(3, editor.SelectionCount);
        Assert.True(editor.GroupSelectionCommand.CanExecute(null));

        editor.GroupSelectionCommand.Execute(null);

        var grouped = PanelOf(editor, panel);
        var link = grouped.Elements[other].Link;
        Assert.NotNull(link);
        Assert.Equal(link, grouped.Elements[shape].Link);
        Assert.Equal(link, grouped.CharacterInstances[character].Link);
    }

    [Fact]
    public void Ungrouping_lets_the_members_go_where_they_are_as_one_undo_step()
    {
        var (editor, history, panel, shape, character) = ShapeAndCharacterSelected();
        editor.GroupSelectionCommand.Execute(null);
        var before = PanelOf(editor, panel);

        editor.UngroupSelectionCommand.Execute(null);

        var after = PanelOf(editor, panel);
        Assert.Null(after.Elements[shape].Link);
        Assert.Null(after.CharacterInstances[character].Link);
        Assert.Equal(before.Elements[shape] with { Link = null }, after.Elements[shape]);
        Assert.Equal(before.CharacterInstances[character] with { Link = null }, after.CharacterInstances[character]);
        Assert.False(editor.UngroupSelectionCommand.CanExecute(null));

        editor.Select(panel);
        editor.SelectElement(panel, shape); // a click now picks the shape alone
        Assert.Equal(1, editor.SelectionCount);

        history.Undo();
        Assert.NotNull(PanelOf(editor, panel).Elements[shape].Link);
    }

    [Fact]
    public void Alt_dragging_a_group_drags_off_a_group_of_its_own()
    {
        var (editor, _, panel, shape, character) = ShapeAndCharacterSelected();
        editor.GroupSelectionCommand.Execute(null);
        var original = PanelOf(editor, panel).Elements[shape].Link;

        Assert.True(editor.BeginDuplicateSelection(panel));
        editor.UpdateMoveSelection(panel, 8, 0);
        editor.EndGesture(commit: true);

        var duplicated = PanelOf(editor, panel);
        Assert.Equal(2, duplicated.Elements.Count);
        Assert.Equal(2, duplicated.CharacterInstances.Count);
        Assert.Equal(original, duplicated.Elements[shape].Link);
        Assert.Equal(original, duplicated.CharacterInstances[character].Link);
        var copyLink = duplicated.Elements[^1].Link;
        Assert.NotNull(copyLink);
        Assert.NotEqual(original, copyLink);
        Assert.Equal(copyLink, duplicated.CharacterInstances[^1].Link);
        Assert.Equal(2, editor.SelectionCount); // the copies are what's selected
    }

    [Fact]
    public void A_copy_of_one_member_joins_no_group()
    {
        var (editor, _, panel, _, character) = ShapeAndCharacterSelected();
        editor.GroupSelectionCommand.Execute(null);
        editor.Select(panel);
        editor.SelectCharacter(panel, character); // the whole group, with the character as the primary

        Assert.True(editor.Copy());
        Assert.True(editor.Paste());

        var pasted = PanelOf(editor, panel);
        Assert.Equal(2, pasted.CharacterInstances.Count);
        Assert.Null(pasted.CharacterInstances[^1].Link);
        Assert.NotNull(pasted.CharacterInstances[character].Link); // the original is still grouped
        Assert.Equal(1, editor.SelectionCount);
    }
}
