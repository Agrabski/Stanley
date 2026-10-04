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

    private static Panel PanelOf(IReadOnlyList<CharacterInstance> characters, IReadOnlyList<Bubble> bubbles, params PanelElement[] elements) =>
        new(PanelId.New(), PanelShapes.Rectangle(Bounds), null, characters, bubbles, elements);

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
}
