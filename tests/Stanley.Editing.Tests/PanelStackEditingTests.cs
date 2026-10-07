using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Editing.Tests;

/// <summary>Arranging what's in front of what in a panel: bubbles, characters and drawings in one stack (the Layers pane).</summary>
public class PanelStackEditingTests
{
    private static readonly Rect2D Bounds = new(0, 0, 200, 100);

    private static Bubble BubbleAt(double x) => BubbleEditing.Create(new Rect2D(x, 20, 30, 20), BubbleStylePreset.Speech).Value with { Text = "Hi" };

    private static ShapeElement Blob(double x, ElementLayer layer) => ShapeEditing.Ellipse(new Rect2D(x, 50, 10, 10), ShapeEditing.DefaultStyle, layer).Value;

    private static CharacterInstance CharacterAt(double x) =>
        new(CharacterId.New(), new CharacterPlacement(new Point2D(x, 80), 1, false), null, new PoseData(ViewAngle.Front, [], []), null);

    private static Panel PanelOf(IReadOnlyList<CharacterInstance> characters, IReadOnlyList<Bubble> bubbles, params PanelElement[] elements) =>
        new(PanelId.New(), PanelShapes.Rectangle(Bounds), null, characters, bubbles, elements);

    private static StackItem AtBubble(int i) => new(StackKind.Bubble, i);

    private static StackItem AtChar(int i) => new(StackKind.Character, i);

    private static StackItem AtElement(int i) => new(StackKind.Element, i);

    /// <summary>One drawing behind the character, one character, a bubble: the usual order is [drawing, character, bubble].</summary>
    private static Panel Usual() => PanelOf([CharacterAt(100)], [BubbleAt(20)], Blob(10, ElementLayer.Background));

    private static Panel MoveOk(Panel panel, StackItem item, StackMove move)
    {
        var result = PanelStackEditing.Move(panel, item, move);
        Assert.True(result.IsValid);
        return result.Value;
    }

    [Fact]
    public void A_bubble_can_go_one_place_back_behind_a_character()
    {
        var moved = MoveOk(Usual(), AtBubble(0), StackMove.Backward);

        Assert.Equal([AtElement(0), AtBubble(0), AtChar(0)], PanelStack.Order(moved));
    }

    [Fact]
    public void A_character_can_go_one_place_forward_past_a_bubble_and_a_drawing_one_place_forward_past_a_character()
    {
        var charForward = MoveOk(PanelOf([CharacterAt(100)], [BubbleAt(20)]), AtChar(0), StackMove.Forward);
        Assert.Equal([AtBubble(0), AtChar(0)], PanelStack.Order(charForward));

        var drawingForward = MoveOk(Usual(), AtElement(0), StackMove.Forward);
        Assert.Equal([AtChar(0), AtElement(0), AtBubble(0)], PanelStack.Order(drawingForward));
    }

    [Fact]
    public void All_the_way_to_the_front_and_the_back_pass_everything_of_every_kind()
    {
        var toFront = MoveOk(Usual(), AtElement(0), StackMove.ToFront);
        Assert.Equal([AtChar(0), AtBubble(0), AtElement(0)], PanelStack.Order(toFront));

        var toBack = MoveOk(Usual(), AtBubble(0), StackMove.ToBack);
        Assert.Equal([AtBubble(0), AtElement(0), AtChar(0)], PanelStack.Order(toBack));
    }

    [Fact]
    public void Moving_what_is_already_there_hands_back_the_very_same_panel()
    {
        var panel = Usual();

        Assert.Same(panel, MoveOk(panel, AtBubble(0), StackMove.Forward));
        Assert.Same(panel, MoveOk(panel, AtBubble(0), StackMove.ToFront));
        Assert.Same(panel, MoveOk(panel, AtElement(0), StackMove.Backward));
        Assert.Same(panel, MoveOk(panel, AtElement(0), StackMove.ToBack));
    }

    [Fact]
    public void A_layer_dropped_at_a_place_in_the_stack_lands_there_and_the_rest_keep_their_order()
    {
        // [drawing, character, bubble, caption] from the back
        var panel = PanelOf([CharacterAt(100)], [BubbleAt(20)], Blob(10, ElementLayer.Background), Blob(40, ElementLayer.Foreground));
        Assert.Equal([AtElement(0), AtChar(0), AtElement(1), AtBubble(0)], PanelStack.Order(panel));

        var bubbleSecond = PanelStackEditing.MoveTo(panel, AtBubble(0), 1);
        Assert.True(bubbleSecond.IsValid);
        Assert.Equal([AtElement(0), AtBubble(0), AtChar(0), AtElement(1)], PanelStack.Order(bubbleSecond.Value));

        var drawingThird = PanelStackEditing.MoveTo(panel, AtElement(0), 2);
        Assert.Equal([AtChar(0), AtElement(1), AtElement(0), AtBubble(0)], PanelStack.Order(drawingThird.Value));
        Assert.Equal(ElementLayer.Foreground, drawingThird.Value.Elements[0].Layer); // now in front of the only character
    }

    [Fact]
    public void A_drop_past_either_end_of_the_stack_lands_at_that_end_and_one_where_it_was_changes_nothing()
    {
        var panel = Usual();

        Assert.Equal([AtChar(0), AtBubble(0), AtElement(0)], PanelStack.Order(PanelStackEditing.MoveTo(panel, AtElement(0), 99).Value));
        Assert.Equal([AtBubble(0), AtElement(0), AtChar(0)], PanelStack.Order(PanelStackEditing.MoveTo(panel, AtBubble(0), -3).Value));
        Assert.Same(panel, PanelStackEditing.MoveTo(panel, AtChar(0), 1).Value);
        Assert.False(PanelStackEditing.MoveTo(panel, AtChar(4), 0).IsValid);
    }

    [Fact]
    public void Arranging_a_panel_the_first_time_writes_a_stack_naming_everything_and_gives_each_character_an_id()
    {
        var panel = Usual();
        Assert.Null(panel.Stack);
        Assert.Null(panel.CharacterInstances[0].Id);

        var moved = MoveOk(panel, AtBubble(0), StackMove.Backward);

        Assert.NotNull(moved.CharacterInstances[0].Id);
        Assert.Equal(3, moved.Stack!.Count);
        Assert.Equal(3, moved.Stack.Distinct().Count());
        Assert.Contains(PanelStack.Token(moved.CharacterInstances[0])!, moved.Stack);
        Assert.Contains(PanelStack.Token(moved.Bubbles[0]), moved.Stack);
        Assert.Contains(PanelStack.Token(moved.Elements[0]), moved.Stack);
        // the lists themselves aren't reordered, so nothing that points into them (the selection) moves
        Assert.Equal(panel.Bubbles, moved.Bubbles);
        Assert.Equal(panel.Elements, moved.Elements);
    }

    [Fact]
    public void Arranging_again_keeps_the_characters_ids()
    {
        var once = MoveOk(Usual(), AtBubble(0), StackMove.Backward);
        var id = once.CharacterInstances[0].Id;

        var twice = MoveOk(once, AtBubble(0), StackMove.ToFront);

        Assert.Equal(id, twice.CharacterInstances[0].Id);
        Assert.Equal([AtElement(0), AtChar(0), AtBubble(0)], PanelStack.Order(twice));
    }

    [Fact]
    public void A_thing_never_leaves_its_panels_stack_and_asking_for_one_that_is_not_there_fails()
    {
        var result = PanelStackEditing.Move(Usual(), AtBubble(5), StackMove.Forward);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_drawing_sent_behind_every_character_is_flagged_behind_and_one_brought_in_front_of_them_all_in_front()
    {
        var panel = PanelOf([CharacterAt(100)], [], Blob(10, ElementLayer.Foreground), Blob(30, ElementLayer.Background));

        var behind = MoveOk(panel, AtElement(0), StackMove.ToBack);
        Assert.Equal(ElementLayer.Background, behind.Elements[0].Layer);

        var inFront = MoveOk(panel, AtElement(1), StackMove.ToFront);
        Assert.Equal(ElementLayer.Foreground, inFront.Elements[1].Layer);
    }

    [Fact]
    public void A_drawing_between_two_characters_keeps_the_flag_it_had()
    {
        var panel = PanelOf([CharacterAt(60), CharacterAt(140)], [], Blob(10, ElementLayer.Background));
        // [drawing, character, character] -> [character, drawing, character]
        var between = MoveOk(panel, AtElement(0), StackMove.Forward);

        Assert.Equal([AtChar(0), AtElement(0), AtChar(1)], PanelStack.Order(between));
        Assert.Equal(ElementLayer.Background, between.Elements[0].Layer);
    }

    [Fact]
    public void Moving_a_drawing_past_a_group_flags_the_groups_children_too()
    {
        var group = new GroupElement(ElementId.New(), ElementLayer.Foreground, [Blob(10, ElementLayer.Foreground), Blob(30, ElementLayer.Foreground)]);
        var panel = PanelOf([CharacterAt(100)], [], group);

        var moved = (GroupElement)MoveOk(panel, AtElement(0), StackMove.ToBack).Elements[0];

        Assert.Equal(ElementLayer.Background, moved.Layer);
        Assert.All(moved.Children, c => Assert.Equal(ElementLayer.Background, c.Layer));
    }

    [Fact]
    public void Two_things_sharing_an_id_are_told_apart_so_the_order_written_is_the_order_read_back()
    {
        var shared = BubbleAt(20);
        var panel = PanelOf([CharacterAt(100)], [shared, shared with { Text = "copy" }]);

        var moved = MoveOk(panel, AtBubble(1), StackMove.ToBack);

        Assert.NotEqual(PanelStack.Token(moved.Bubbles[0]), PanelStack.Token(moved.Bubbles[1]));
        Assert.Equal([AtBubble(1), AtChar(0), AtBubble(0)], PanelStack.Order(moved));
    }

    // ---------------------------------------------------------------- in front of / behind the characters

    [Fact]
    public void A_drawing_can_be_put_right_in_front_of_the_frontmost_character_or_right_behind_the_rearmost()
    {
        var panel = PanelOf([CharacterAt(60), CharacterAt(140)], [BubbleAt(20)], Blob(10, ElementLayer.Background));
        var arranged = MoveOk(panel, AtElement(0), StackMove.Forward); // between the characters

        var inFront = PanelStackEditing.MoveBeyondCharacters(arranged, AtElement(0), inFront: true).Value;
        Assert.Equal([AtChar(0), AtChar(1), AtElement(0), AtBubble(0)], PanelStack.Order(inFront));
        Assert.Equal(ElementLayer.Foreground, inFront.Elements[0].Layer);

        var behind = PanelStackEditing.MoveBeyondCharacters(arranged, AtElement(0), inFront: false).Value;
        Assert.Equal([AtElement(0), AtChar(0), AtChar(1), AtBubble(0)], PanelStack.Order(behind));
        Assert.Equal(ElementLayer.Background, behind.Elements[0].Layer);
    }

    [Fact]
    public void Putting_a_drawing_beyond_the_characters_changes_nothing_when_it_is_there_or_there_are_none()
    {
        var panel = Usual();
        Assert.Same(panel, PanelStackEditing.MoveBeyondCharacters(panel, AtElement(0), inFront: false).Value);

        var noCharacters = PanelOf([], [], Blob(10, ElementLayer.Background));
        Assert.Same(noCharacters, PanelStackEditing.MoveBeyondCharacters(noCharacters, AtElement(0), inFront: true).Value);
    }

    // ---------------------------------------------------------------- grouping

    [Fact]
    public void A_group_takes_its_frontmost_members_place_in_the_stack()
    {
        var a = Blob(10, ElementLayer.Background);
        var b = Blob(30, ElementLayer.Background);
        var c = Blob(50, ElementLayer.Background);
        var before = PanelOf([], [], a, b, c) with { Stack = [PanelStack.Token(c), PanelStack.Token(a), PanelStack.Token(b)] };
        var group = Grouping.Group([a, c]).Value;
        var after = before with { Elements = [b, group] };

        var grouped = PanelStackEditing.Grouped(before, after, [a.Id, c.Id], group);

        // a was the frontmost of the two (c was behind it): the group stands where a was, in front of c's old place and behind b
        Assert.Equal([PanelStack.Token(group), PanelStack.Token(b)], grouped.Stack);
        Assert.Equal([AtElement(1), AtElement(0)], PanelStack.Order(grouped));
    }

    [Fact]
    public void Taking_a_group_apart_puts_its_children_where_it_stood_in_the_order_they_were_in_it()
    {
        var a = Blob(10, ElementLayer.Background);
        var c = Blob(50, ElementLayer.Background);
        var b = Blob(30, ElementLayer.Background);
        var group = Grouping.Group([a, c]).Value;
        var before = PanelOf([], [], b, group) with { Stack = [PanelStack.Token(group), PanelStack.Token(b)] };
        var after = before with { Elements = [b, a, c] };

        var ungrouped = PanelStackEditing.Ungrouped(before, after, group, [a, c]);

        Assert.Equal([PanelStack.Token(a), PanelStack.Token(c), PanelStack.Token(b)], ungrouped.Stack);
    }

    [Fact]
    public void Grouping_and_ungrouping_in_a_panel_stacked_the_usual_way_leave_it_that_way()
    {
        var a = Blob(10, ElementLayer.Background);
        var b = Blob(30, ElementLayer.Background);
        var before = PanelOf([], [], a, b);
        var group = Grouping.Group([a, b]).Value;
        var after = before with { Elements = [group] };

        Assert.Same(after, PanelStackEditing.Grouped(before, after, [a.Id, b.Id], group));
        Assert.Same(before, PanelStackEditing.Ungrouped(after, before, group, [a, b]));
    }

    [Fact]
    public void Grouping_in_a_stacked_panel_names_the_characters_the_stack_now_lists()
    {
        var a = Blob(10, ElementLayer.Background);
        var b = Blob(30, ElementLayer.Background);
        var before = PanelOf([CharacterAt(100)], [], a, b) with { Stack = [PanelStack.Token(a), PanelStack.Token(b)] }; // names no character: it has no id
        var group = Grouping.Group([a, b]).Value;
        var after = before with { Elements = [group] };

        var grouped = PanelStackEditing.Grouped(before, after, [a.Id, b.Id], group);

        Assert.NotNull(grouped.CharacterInstances[0].Id);
        Assert.Contains(PanelStack.Token(grouped.CharacterInstances[0])!, grouped.Stack!);
    }
}
