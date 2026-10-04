using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;

namespace Stanley.ProjectModel.Tests;

public class PanelStackTests
{
    private static readonly Rect2D Bounds = new(10, 10, 100, 80);

    private static ShapeElement Shape(ElementLayer layer) =>
        new(ElementId.New(), layer, [], Closed: true, new ShapeStyle(null, ColorValue.FromHex("#111111"), 0));

    private static Bubble Bubble() =>
        new(BubbleId.New(), new BubbleShape(PanelShapes.Rectangle(new Rect2D(20, 20, 40, 20)).Anchors), BubbleStylePreset.Speech, [], "Hi");

    private static CharacterInstance Character() =>
        new(CharacterId.New(), new CharacterPlacement(new Point2D(50, 80), 1, false), null, new PoseData(ViewAngle.Front, [], []), null);

    private static CharacterInstance NamedCharacter() => Character() with { Id = CharacterInstanceId.New() };

    private static Panel PanelOf(IReadOnlyList<CharacterInstance> characters, IReadOnlyList<Bubble> bubbles, params PanelElement[] elements) =>
        new(PanelId.New(), PanelShapes.Rectangle(Bounds), null, characters, bubbles, elements);

    private static Panel Stacked(Panel panel, params string?[] tokens) => panel with { Stack = tokens.OfType<string>().ToList() };

    private static StackItem AtBubble(int i) => new(StackKind.Bubble, i);

    private static StackItem AtChar(int i) => new(StackKind.Character, i);

    private static StackItem AtElement(int i) => new(StackKind.Element, i);

    [Fact]
    public void An_empty_panel_stacks_nothing()
    {
        Assert.Empty(PanelStack.Order(PanelOf([], [])));
    }

    [Fact]
    public void Things_stack_behind_the_characters_then_the_characters_then_things_in_front_then_the_bubbles()
    {
        // Elements are one list; each is behind or in front of the characters by its layer.
        var panel = PanelOf([Character(), Character()], [Bubble(), Bubble()],
            Shape(ElementLayer.Foreground), Shape(ElementLayer.Background), Shape(ElementLayer.Foreground), Shape(ElementLayer.Background));

        Assert.Equal(
        [
            new StackItem(StackKind.Element, 1), new StackItem(StackKind.Element, 3),
            new StackItem(StackKind.Character, 0), new StackItem(StackKind.Character, 1),
            new StackItem(StackKind.Element, 0), new StackItem(StackKind.Element, 2),
            new StackItem(StackKind.Bubble, 0), new StackItem(StackKind.Bubble, 1)
        ], PanelStack.Order(panel));
    }

    [Fact]
    public void Within_a_tier_a_later_list_entry_stacks_in_front_of_an_earlier_one()
    {
        var order = PanelStack.Order(PanelOf([], [Bubble(), Bubble(), Bubble()]));

        Assert.Equal([0, 1, 2], order.Select(i => i.Index));
        Assert.All(order, i => Assert.Equal(StackKind.Bubble, i.Kind));
    }

    [Fact]
    public void Every_item_appears_exactly_once()
    {
        var panel = PanelOf([Character()], [Bubble(), Bubble()], Shape(ElementLayer.Background), Shape(ElementLayer.Foreground));

        var order = PanelStack.Order(panel);

        Assert.Equal(5, order.Count);
        Assert.Equal(5, order.Distinct().Count());
    }

    // ---------------------------------------------------------------- an arranged stack

    [Fact]
    public void A_stack_can_put_a_bubble_behind_a_character_and_a_drawing_between_two_characters()
    {
        var panel = PanelOf([NamedCharacter(), NamedCharacter()], [Bubble()], Shape(ElementLayer.Background));
        var bubble = PanelStack.Token(panel.Bubbles[0]);
        var shape = PanelStack.Token(panel.Elements[0]);
        var first = PanelStack.Token(panel.CharacterInstances[0])!;
        var second = PanelStack.Token(panel.CharacterInstances[1])!;

        // from the back: the first character, the drawing, the bubble, the second character
        var order = PanelStack.Order(Stacked(panel, first, shape, bubble, second));

        Assert.Equal([AtChar(0), AtElement(0), AtBubble(0), AtChar(1)], order);
    }

    [Fact]
    public void A_stack_names_every_item_by_a_token_that_says_which_list_it_is_in()
    {
        var panel = PanelOf([NamedCharacter()], [Bubble()], Shape(ElementLayer.Foreground));

        Assert.StartsWith("b:", PanelStack.Token(panel.Bubbles[0]), StringComparison.Ordinal);
        Assert.StartsWith("e:", PanelStack.Token(panel.Elements[0]), StringComparison.Ordinal);
        Assert.StartsWith("c:", PanelStack.Token(panel.CharacterInstances[0]), StringComparison.Ordinal);
        Assert.Equal(PanelStack.Token(panel.Bubbles[0]), PanelStack.Token(panel, AtBubble(0)));
        Assert.Null(PanelStack.Token(Character()));
    }

    [Fact]
    public void Tokens_that_name_nothing_are_skipped_and_a_token_met_twice_counts_once()
    {
        var panel = PanelOf([], [Bubble(), Bubble()]);
        var a = PanelStack.Token(panel.Bubbles[0]);
        var b = PanelStack.Token(panel.Bubbles[1]);

        var order = PanelStack.Order(Stacked(panel, b, "b:gone", a, b, "nonsense", "e:" + panel.Bubbles[0].Id.Value));

        Assert.Equal([AtBubble(1), AtBubble(0)], order);
    }

    [Fact]
    public void A_bubble_added_since_lands_in_front_of_the_frontmost_bubble_wherever_that_was_put()
    {
        // The first bubble was arranged behind the character; the second was added afterwards, so the stack doesn't name it.
        var old = Bubble();
        var panel = PanelOf([NamedCharacter()], [old, Bubble()]);
        var stack = Stacked(panel, PanelStack.Token(old), PanelStack.Token(panel.CharacterInstances[0])!);

        Assert.Equal([AtBubble(0), AtBubble(1), AtChar(0)], PanelStack.Order(stack));
    }

    [Fact]
    public void A_character_with_no_id_cannot_be_named_so_it_stays_where_the_usual_order_puts_it()
    {
        var panel = PanelOf([Character()], [Bubble()], Shape(ElementLayer.Background));
        // only the bubble and the drawing are named, the drawing in front of the bubble
        var order = PanelStack.Order(Stacked(panel, PanelStack.Token(panel.Bubbles[0]), PanelStack.Token(panel.Elements[0])));

        // the character follows the drawing in the usual order, so it lands just in front of it
        Assert.Equal([AtBubble(0), AtElement(0), AtChar(0)], order);
    }

    [Fact]
    public void Something_the_usual_order_puts_at_the_very_back_and_the_stack_does_not_name_goes_to_the_back()
    {
        var panel = PanelOf([NamedCharacter()], [], Shape(ElementLayer.Background));

        var order = PanelStack.Order(Stacked(panel, PanelStack.Token(panel.CharacterInstances[0])!));

        Assert.Equal([AtElement(0), AtChar(0)], order);
    }

    [Fact]
    public void Two_items_sharing_a_token_leave_the_second_to_be_placed_by_the_usual_order()
    {
        var shared = Bubble();
        var panel = PanelOf([NamedCharacter()], [shared, shared with { Text = "copy" }]);
        var stack = Stacked(panel, PanelStack.Token(shared), PanelStack.Token(panel.CharacterInstances[0])!);

        // the first bubble owns the token; the copy follows it in the usual order
        Assert.Equal([AtBubble(0), AtBubble(1), AtChar(0)], PanelStack.Order(stack));
    }

    [Fact]
    public void However_mangled_a_stack_is_every_item_appears_exactly_once()
    {
        var panel = PanelOf([NamedCharacter(), Character()], [Bubble(), Bubble(), Bubble()], Shape(ElementLayer.Background), Shape(ElementLayer.Foreground));
        var tokens = Enumerable.Range(0, 5).SelectMany(_ => new[] { PanelStack.Token(panel.Bubbles[0]), PanelStack.Token(panel.Bubbles[2]), PanelStack.Token(panel.Elements[1]), "x:?", "", PanelStack.Token(panel.CharacterInstances[0])! }).ToList();
        var random = new Random(7);

        for (var round = 0; round < 50; round++)
        {
            var shuffled = tokens.OrderBy(_ => random.Next()).Take(random.Next(1, tokens.Count)).ToList();
            var order = PanelStack.Order(panel with { Stack = shuffled });

            Assert.Equal(7, order.Count);
            Assert.Equal(7, order.Distinct().Count());
        }
    }

    [Fact]
    public void An_empty_stack_is_no_stack()
    {
        var panel = PanelOf([], [Bubble()]) with { Stack = [] };

        // `with` skips the constructor's tidying, so read it the way the order does
        Assert.Equal([AtBubble(0)], PanelStack.Order(panel));
        Assert.Null(new Panel(PanelId.New(), PanelShapes.Rectangle(Bounds), null, [], [], Stack: []).Stack);
    }
}

