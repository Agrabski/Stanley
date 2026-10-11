using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Editing.Tests;

public class ClippingsTests
{
    private static ShapeElement Blob(Rect2D box) => ShapeEditing.Ellipse(box, ShapeEditing.DefaultStyle, ElementLayer.Background).Value;

    [Fact]
    public void Copying_a_group_gives_the_group_and_every_child_a_fresh_id()
    {
        var a = Blob(new Rect2D(0, 0, 10, 10));
        var b = Blob(new Rect2D(20, 0, 10, 10));
        var group = new GroupElement(ElementId.New(), ElementLayer.Background, [a, b]);

        var copy = (GroupElement)Clippings.Copy(group);

        Assert.NotEqual(group.Id, copy.Id);
        Assert.NotEqual(a.Id, copy.Children[0].Id);
        Assert.NotEqual(b.Id, copy.Children[1].Id);
        // Geometry is untouched by copying.
        Assert.Equal(PanelElements.Bounds(a), PanelElements.Bounds(copy.Children[0]));
        Assert.Equal(PanelElements.Bounds(b), PanelElements.Bounds(copy.Children[1]));
    }

    [Fact]
    public void Copying_a_group_recurses_into_nested_groups()
    {
        var leaf = Blob(new Rect2D(0, 0, 10, 10));
        var inner = new GroupElement(ElementId.New(), ElementLayer.Background, [leaf]);
        var outer = new GroupElement(ElementId.New(), ElementLayer.Background, [inner]);

        var copy = (GroupElement)Clippings.Copy(outer);
        var copiedInner = (GroupElement)copy.Children[0];

        Assert.NotEqual(outer.Id, copy.Id);
        Assert.NotEqual(inner.Id, copiedInner.Id);
        Assert.NotEqual(leaf.Id, copiedInner.Children[0].Id);
    }

    [Fact]
    public void Copying_the_same_group_twice_gives_each_copys_children_distinct_ids()
    {
        var a = Blob(new Rect2D(0, 0, 10, 10));
        var group = new GroupElement(ElementId.New(), ElementLayer.Background, [a]);

        var copy1 = (GroupElement)Clippings.Copy(group);
        var copy2 = (GroupElement)Clippings.Copy(group);

        Assert.NotEqual(copy1.Id, copy2.Id);
        Assert.NotEqual(copy1.Children[0].Id, copy2.Children[0].Id);
    }

    // ---------------------------------------------------------------- several things at once

    private static Bubble SpeechBubble(Rect2D box) => BubbleEditing.Create(box, BubbleStylePreset.Speech).Value;

    private static CharacterInstance Person(double x, double y) =>
        new(CharacterId.New(), new CharacterPlacement(new Point2D(x, y), 50, Mirrored: false), null, new PoseData(ViewAngle.Front, [], []), null);

    private static SelectionClipping Several(IReadOnlyList<Bubble>? bubbles = null, IReadOnlyList<CharacterInstance>? characters = null, IReadOnlyList<PanelElement>? elements = null) =>
        new(PanelId.New(), new Rect2D(0, 0, 100, 80), bubbles ?? [], characters ?? [], elements ?? []);

    [Fact]
    public void Copying_several_things_gives_each_a_fresh_id_and_changes_nothing_else()
    {
        var bubble = SpeechBubble(new Rect2D(10, 10, 30, 15));
        var character = Person(60, 70) with { Id = CharacterInstanceId.New() };
        var blob = Blob(new Rect2D(5, 40, 10, 10));
        var clipping = Several([bubble], [character], [blob]);

        var copy = Clippings.Copy(clipping);

        Assert.Equal(3, copy.Count);
        Assert.NotEqual(bubble.Id, copy.Bubbles[0].Id);
        Assert.Null(copy.Characters[0].Id); // two characters in a panel never share the id they're stacked under
        Assert.NotEqual(blob.Id, copy.Elements[0].Id);
        Assert.Equal(bubble.Shape, copy.Bubbles[0].Shape);
        Assert.Equal(character.Placement, copy.Characters[0].Placement);
        Assert.Equal(PanelElements.Bounds(blob), PanelElements.Bounds(copy.Elements[0]));
        Assert.Equal((clipping.FromPanel, clipping.FromPanelBounds), (copy.FromPanel, copy.FromPanelBounds));
    }

    [Fact]
    public void Copying_a_group_gives_a_group_of_its_own_and_leaves_the_original_group_alone()
    {
        var link = GroupLinkId.New();
        var bubble = SpeechBubble(new Rect2D(10, 10, 30, 15)) with { Link = link };
        var character = Person(60, 70) with { Link = link };
        var blob = Blob(new Rect2D(5, 40, 10, 10)) with { Link = link };
        var other = GroupLinkId.New();
        var blobA = Blob(new Rect2D(0, 60, 20, 5)) with { Link = other };
        var blobB = Blob(new Rect2D(30, 60, 20, 5)) with { Link = other };

        var copy = Clippings.Copy(Several([bubble], [character], [blob, blobA, blobB]));

        var copiedLink = copy.Bubbles[0].Link;
        Assert.NotNull(copiedLink);
        Assert.NotEqual(link, copiedLink);
        Assert.Equal(copiedLink, copy.Characters[0].Link);
        Assert.Equal(copiedLink, copy.Elements[0].Link);
        // A second group stays a second group, not folded into the first.
        Assert.NotNull(copy.Elements[1].Link);
        Assert.NotEqual(copiedLink, copy.Elements[1].Link);
        Assert.NotEqual(other, copy.Elements[1].Link);
        Assert.Equal(copy.Elements[1].Link, copy.Elements[2].Link);
    }

    [Fact]
    public void A_lone_copy_never_joins_the_group_of_its_original()
    {
        var link = GroupLinkId.New();

        Assert.Null(Clippings.Copy(SpeechBubble(new Rect2D(10, 10, 30, 15)) with { Link = link }).Link);
        Assert.Null(Clippings.Copy(Person(60, 70) with { Link = link }).Link);
        Assert.Null(Clippings.Copy(Blob(new Rect2D(5, 40, 10, 10)) with { Link = link }).Link);
    }

    [Fact]
    public void A_member_copied_without_the_rest_of_its_group_joins_no_group()
    {
        var link = GroupLinkId.New();
        var bubble = SpeechBubble(new Rect2D(10, 10, 30, 15)) with { Link = link };

        var copy = Clippings.Copy(Several([bubble], elements: [Blob(new Rect2D(5, 40, 10, 10))]));

        Assert.Null(copy.Bubbles[0].Link);
        Assert.Null(copy.Elements[0].Link);
    }

    [Fact]
    public void A_free_offset_for_one_box_is_the_free_spot()
    {
        var container = new Rect2D(0, 0, 100, 80);
        var box = new Rect2D(10, 10, 30, 20);
        Rect2D[] taken = [box, box with { X = box.X + Clippings.CascadeStepMm, Y = box.Y + Clippings.CascadeStepMm }];

        var (dx, dy) = Clippings.FreeOffset([box], taken, container);

        Assert.Equal((2 * Clippings.CascadeStepMm, 2 * Clippings.CascadeStepMm), (dx, dy));
        Assert.Equal(Clippings.FreeSpot(box, taken, container), box with { X = box.X + dx, Y = box.Y + dy });
    }

    [Fact]
    public void Several_boxes_move_by_one_offset_until_none_of_them_is_on_top_of_something()
    {
        var container = new Rect2D(0, 0, 100, 80);
        Rect2D[] boxes = [new(10, 10, 30, 20), new(60, 30, 20, 40)];
        // The first box's own spot is free, but the second's is taken: both have to step.
        Rect2D[] taken = [boxes[1]];

        var (dx, dy) = Clippings.FreeOffset(boxes, taken, container);

        Assert.Equal((Clippings.CascadeStepMm, Clippings.CascadeStepMm), (dx, dy));
    }

    [Fact]
    public void A_free_offset_can_step_sideways_only()
    {
        var container = new Rect2D(0, 0, 100, 80);
        Rect2D[] boxes = [new(10, 10, 30, 20), new(60, 30, 20, 40)];

        var (dx, dy) = Clippings.FreeOffset(boxes, boxes, container, stepY: 0);

        Assert.Equal(Clippings.CascadeStepMm, dx);
        Assert.Equal(0, dy);
    }

    [Fact]
    public void A_free_offset_steps_back_when_forwards_would_leave_the_container_and_stays_put_with_no_room()
    {
        var container = new Rect2D(0, 0, 100, 80);
        var corner = new Rect2D(70, 50, 30, 30); // centre (85, 65): three steps on is out of the container
        var step = Clippings.CascadeStepMm;
        Rect2D[] taken = [corner, corner with { X = 70 + step, Y = 50 + step }, corner with { X = 70 + 2 * step, Y = 50 + 2 * step }];

        Assert.Equal((-step, -step), Clippings.FreeOffset([corner], taken, container));

        var tiny = new Rect2D(0, 0, 10, 10);
        Assert.Equal((0, 0), Clippings.FreeOffset([tiny], [tiny], tiny));
        Assert.Equal((0, 0), Clippings.FreeOffset([], [corner], container));
    }
}
