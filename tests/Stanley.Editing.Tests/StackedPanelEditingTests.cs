using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Editing.Tests;

/// <summary>What copying, pasting and splitting do with a panel whose stacking was arranged by hand (<see cref="Panel.Stack"/>).</summary>
public class StackedPanelEditingTests
{
    private static Bubble BubbleAt(Rect2D box) => BubbleEditing.Create(box, BubbleStylePreset.Speech).Value with { Text = "Hi" };

    private static ShapeElement Blob(Rect2D box) => ShapeEditing.Ellipse(box, ShapeEditing.DefaultStyle, ElementLayer.Background).Value;

    private static CharacterInstance NamedCharacterAt(double x) =>
        new(CharacterId.New(), new CharacterPlacement(new Point2D(x, 80), 1, false), null, new PoseData(ViewAngle.Front, [], []), null, Id: CharacterInstanceId.New());

    private static Panel PanelOf(Rect2D bounds, IReadOnlyList<CharacterInstance> characters, IReadOnlyList<Bubble> bubbles, IReadOnlyList<PanelElement> elements) =>
        new(PanelId.New(), PanelShapes.Rectangle(bounds), null, characters, bubbles, elements);

    [Fact]
    public void A_copied_character_keeps_neither_its_group_nor_its_stacking_id()
    {
        var original = NamedCharacterAt(50) with { Link = GroupLinkId.New() };

        var copy = Clippings.Copy(original);

        Assert.Null(copy.Id);
        Assert.Null(copy.Link);
        Assert.Equal(original.Placement, copy.Placement);
    }

    [Fact]
    public void A_copied_panel_stacks_its_copies_the_way_the_original_stacked_theirs()
    {
        var bubble = BubbleAt(new Rect2D(20, 20, 30, 20));
        var shape = Blob(new Rect2D(60, 20, 30, 30));
        var character = NamedCharacterAt(50);
        var panel = PanelOf(new Rect2D(0, 0, 200, 100), [character], [bubble], [shape]) with
        {
            // from the back: the bubble, the character, then the drawing - nothing like the usual order
            Stack = [PanelStack.Token(bubble), PanelStack.Token(character)!, PanelStack.Token(shape)]
        };

        var copy = Clippings.Copy(panel);

        Assert.Equal(PanelStack.Order(panel), PanelStack.Order(copy));
        Assert.Equal([new StackItem(StackKind.Bubble, 0), new StackItem(StackKind.Character, 0), new StackItem(StackKind.Element, 0)], PanelStack.Order(copy));
        // the copies have new ids, so the list names them and not what they were copied from
        Assert.DoesNotContain(PanelStack.Token(bubble), copy.Stack!);
        Assert.DoesNotContain(PanelStack.Token(shape), copy.Stack!);
        Assert.Contains(PanelStack.Token(copy.Bubbles[0]), copy.Stack!);
        Assert.Contains(PanelStack.Token(copy.Elements[0]), copy.Stack!);
    }

    [Fact]
    public void A_copied_panel_with_the_usual_stacking_has_no_stack()
    {
        var panel = PanelOf(new Rect2D(0, 0, 200, 100), [], [BubbleAt(new Rect2D(20, 20, 30, 20))], []);

        Assert.Null(Clippings.Copy(panel).Stack);
    }

    [Fact]
    public void Splitting_a_panel_keeps_what_was_in_front_of_what_in_each_half()
    {
        var leftBubble = BubbleAt(new Rect2D(20, 20, 30, 20));
        var rightBubble = BubbleAt(new Rect2D(150, 20, 30, 20));
        var leftCharacter = NamedCharacterAt(40);
        var rightCharacter = NamedCharacterAt(160);
        var panel = PanelOf(new Rect2D(0, 0, 200, 100), [leftCharacter, rightCharacter], [leftBubble, rightBubble], []) with
        {
            // from the back: both bubbles, then both characters - every bubble behind a character, where the usual order has them on top
            Stack = [PanelStack.Token(rightBubble), PanelStack.Token(leftBubble), PanelStack.Token(leftCharacter)!, PanelStack.Token(rightCharacter)!]
        };

        var result = PanelLayoutEditing.Split(panel, BoundaryOrientation.Vertical, 0.5);

        Assert.True(result.IsValid);
        var bubbleBehindCharacter = new[] { new StackItem(StackKind.Bubble, 0), new StackItem(StackKind.Character, 0) };
        Assert.Equal(leftBubble.Id, Assert.Single(result.Value.First.Bubbles).Id);
        Assert.Equal(rightBubble.Id, Assert.Single(result.Value.Second.Bubbles).Id);
        Assert.Equal(bubbleBehindCharacter, PanelStack.Order(result.Value.First));
        Assert.Equal(bubbleBehindCharacter, PanelStack.Order(result.Value.Second));
    }

    [Fact]
    public void Splitting_a_panel_with_the_usual_stacking_leaves_both_halves_with_it()
    {
        var panel = PanelOf(new Rect2D(0, 0, 200, 100), [], [BubbleAt(new Rect2D(20, 20, 30, 20))], []);

        var result = PanelLayoutEditing.Split(panel, BoundaryOrientation.Vertical, 0.5);

        Assert.Null(result.Value.First.Stack);
        Assert.Null(result.Value.Second.Stack);
    }
}
