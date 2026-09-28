using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
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
}
