using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Editing.Tests;

public class GroupingTests
{
    private static ShapeElement Blob(Rect2D box, ElementLayer layer = ElementLayer.Background) =>
        ShapeEditing.Ellipse(box, ShapeEditing.DefaultStyle, layer).Value;

    private static TextElement Caption(Rect2D box, ElementLayer layer = ElementLayer.Background) =>
        TextEditing.Create(box, TextStylePresets.Style(TextStylePreset.Plain)).Value with { Layer = layer };

    [Fact]
    public void Grouping_fewer_than_two_elements_is_refused()
    {
        var one = Blob(new Rect2D(0, 0, 10, 10));

        Assert.False(Grouping.Group([]).IsValid);
        Assert.False(Grouping.Group([one]).IsValid);
    }

    [Fact]
    public void Grouping_across_background_and_foreground_is_refused()
    {
        var back = Blob(new Rect2D(0, 0, 10, 10), ElementLayer.Background);
        var front = Blob(new Rect2D(20, 0, 10, 10), ElementLayer.Foreground);

        var result = Grouping.Group([back, front]);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void Grouping_two_same_layer_elements_wraps_them_unchanged()
    {
        var shape = Blob(new Rect2D(0, 0, 10, 10));
        var text = Caption(new Rect2D(20, 0, 30, 5));

        var group = Grouping.Group([shape, text]).Value;

        Assert.Equal(ElementLayer.Background, group.Layer);
        Assert.Equal<PanelElement>([shape, text], group.Children);
    }

    [Fact]
    public void Ungrouping_returns_exactly_the_children()
    {
        var shape = Blob(new Rect2D(0, 0, 10, 10));
        var text = Caption(new Rect2D(20, 0, 30, 5));
        var group = Grouping.Group([shape, text]).Value;

        Assert.Equal<PanelElement>([shape, text], Grouping.Ungroup(group));
    }

    [Fact]
    public void Group_then_ungroup_round_trips_positions_losslessly_for_mixed_element_types()
    {
        var shape = Blob(new Rect2D(5, 5, 15, 25));
        var text = Caption(new Rect2D(40, 10, 30, 8));

        var group = Grouping.Group([shape, text]).Value;
        var back = Grouping.Ungroup(group);

        Assert.Equal(PanelElements.Bounds(shape), PanelElements.Bounds(back[0]));
        Assert.Equal(PanelElements.Bounds(text), PanelElements.Bounds(back[1]));
        Assert.Same(shape, back[0]);
        Assert.Same(text, back[1]);
    }
}

/// <summary>Tying things together that a <see cref="GroupElement"/> can't hold - characters, bubbles, elements from both sides of the characters (issue #125).</summary>
public class GroupingLinkTests
{
    private static ShapeElement Blob(Rect2D box, ElementLayer layer = ElementLayer.Background) =>
        ShapeEditing.Ellipse(box, ShapeEditing.DefaultStyle, layer).Value;

    private static CharacterInstance Standing(double x) =>
        new(CharacterId.New(), new CharacterPlacement(new Point2D(x, 100), 50, false), null, new PoseData(ViewAngle.Front, [], []), null);

    private static Bubble SaysHello() => BubbleEditing.Create(new Rect2D(60, 30, 40, 20), BubbleStylePreset.Speech).Value with { Text = "Hello" };

    private static Panel PanelWith(IReadOnlyList<Bubble> bubbles, IReadOnlyList<CharacterInstance> characters, IReadOnlyList<PanelElement> elements) =>
        new(PanelId.New(), PanelShapes.Rectangle(new Rect2D(0, 0, 150, 150)), null, characters, bubbles, elements);

    [Fact]
    public void Linking_ties_things_of_every_kind_under_one_new_link_and_moves_nothing()
    {
        var panel = PanelWith([SaysHello()], [Standing(30), Standing(90)], [Blob(new Rect2D(10, 10, 20, 20)), Blob(new Rect2D(50, 10, 20, 20), ElementLayer.Foreground)]);

        var linked = Grouping.Link(panel, bubbles: [0], characters: [1], elements: [0, 1]);

        Assert.True(linked.IsValid);
        var link = linked.Value.Elements[0].Link;
        Assert.NotNull(link);
        Assert.Equal(link, linked.Value.Elements[1].Link);
        Assert.Equal(link, linked.Value.Bubbles[0].Link);
        Assert.Equal(link, linked.Value.CharacterInstances[1].Link);
        Assert.Null(linked.Value.CharacterInstances[0].Link); // not selected: left out
        Assert.Equal(panel.Elements.Select(e => e with { Link = null }), linked.Value.Elements.Select(e => e with { Link = null }));
        Assert.Equal(panel.CharacterInstances[1], linked.Value.CharacterInstances[1] with { Link = null });
        Assert.Equal(panel.Elements.Select(e => e.Layer), linked.Value.Elements.Select(e => e.Layer));
    }

    [Fact]
    public void Linking_fewer_than_two_things_or_a_missing_one_is_refused()
    {
        var panel = PanelWith([], [Standing(30)], [Blob(new Rect2D(10, 10, 20, 20))]);

        Assert.False(Grouping.Link(panel, [], [], []).IsValid);
        Assert.False(Grouping.Link(panel, [], [0], []).IsValid);
        Assert.False(Grouping.Link(panel, [], [0], [5]).IsValid); // no such element
        Assert.False(Grouping.Link(panel, [3], [0], []).IsValid); // no such bubble
    }

    [Fact]
    public void Linking_again_replaces_the_old_link_so_two_groups_become_one()
    {
        var panel = PanelWith([], [Standing(30), Standing(90)], [Blob(new Rect2D(10, 10, 20, 20)), Blob(new Rect2D(50, 10, 20, 20))]);
        var first = Grouping.Link(panel, [], [0], [0]).Value;
        var second = Grouping.Link(first, [], [1], [1]).Value;
        Assert.NotEqual(first.Elements[0].Link, second.Elements[1].Link);

        var merged = Grouping.Link(second, [], [0, 1], [0, 1]).Value;

        var link = merged.Elements[0].Link;
        Assert.NotNull(link);
        Assert.All(merged.Elements, e => Assert.Equal(link, e.Link));
        Assert.All(merged.CharacterInstances, c => Assert.Equal(link, c.Link));
        Assert.Equal(4, Grouping.MembersOf(merged, link.Value).Count);
    }

    [Fact]
    public void Members_are_found_by_index_in_each_list()
    {
        var panel = PanelWith([SaysHello()], [Standing(30), Standing(90)], [Blob(new Rect2D(10, 10, 20, 20)), Blob(new Rect2D(50, 10, 20, 20))]);
        var linked = Grouping.Link(panel, [0], [1], [1]).Value;

        var members = Grouping.MembersOf(linked, linked.Elements[1].Link!.Value);

        Assert.Equal([0], members.Bubbles);
        Assert.Equal([1], members.Characters);
        Assert.Equal([1], members.Elements);
        Assert.Equal(3, members.Count);
    }

    [Fact]
    public void Unlinking_frees_only_the_named_groups()
    {
        var panel = PanelWith([], [Standing(30), Standing(90)], [Blob(new Rect2D(10, 10, 20, 20)), Blob(new Rect2D(50, 10, 20, 20))]);
        var one = Grouping.Link(panel, [], [0], [0]).Value;
        var both = Grouping.Link(one, [], [1], [1]).Value;
        var keepLink = both.Elements[0].Link!.Value;
        var dropLink = both.Elements[1].Link!.Value;

        var freed = Grouping.Unlink(both, [dropLink]);

        Assert.Equal(keepLink, freed.Elements[0].Link);
        Assert.Equal(keepLink, freed.CharacterInstances[0].Link);
        Assert.Null(freed.Elements[1].Link);
        Assert.Null(freed.CharacterInstances[1].Link);
        Assert.Equal(both.Elements[1] with { Link = null }, freed.Elements[1]);
    }

    [Fact]
    public void Welding_elements_into_a_group_element_drops_their_link()
    {
        var a = Blob(new Rect2D(0, 0, 10, 10)) with { Link = GroupLinkId.New() };
        var b = Blob(new Rect2D(20, 0, 10, 10)) with { Link = a.Link };

        var group = Grouping.Group([a, b]).Value;

        Assert.All(group.Children, child => Assert.Null(child.Link));
        Assert.Equal(PanelElements.Bounds(a), PanelElements.Bounds(group.Children[0]));
    }

    [Fact]
    public void A_lone_copy_joins_no_group_but_a_panels_copy_keeps_its_groups()
    {
        var panel = PanelWith([SaysHello()], [Standing(30)], [Blob(new Rect2D(10, 10, 20, 20))]);
        var linked = Grouping.Link(panel, [0], [0], [0]).Value;
        var link = linked.Elements[0].Link;

        Assert.Null(Clippings.Copy(linked.Elements[0]).Link);
        Assert.Null(Clippings.Copy(linked.Bubbles[0]).Link);
        Assert.Null(Clippings.Copy(linked.CharacterInstances[0]).Link);

        var copy = Clippings.Copy(linked);
        Assert.Equal(link, copy.Elements[0].Link);
        Assert.Equal(link, copy.Bubbles[0].Link);
        Assert.Equal(link, copy.CharacterInstances[0].Link);
    }

    [Fact]
    public void A_relinker_gives_one_new_link_per_old_one_and_none_to_the_ungrouped()
    {
        var old = GroupLinkId.New();
        var other = GroupLinkId.New();
        var relinker = new Clippings.Relinker();

        var first = relinker.For(old);

        Assert.NotNull(first);
        Assert.NotEqual(old, first);
        Assert.Equal(first, relinker.For(old));
        Assert.NotEqual(first, relinker.For(other));
        Assert.Null(relinker.For(null));
    }
}
