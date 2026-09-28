using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.ProjectModel.Tests;

public class PanelElementsTests
{
    [Fact]
    public void A_groups_bounds_is_the_union_of_its_childrens_bounds()
    {
        var a = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(10, 10, 20, 10), "A", Style: new(10, null));
        var b = new PictureElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(50, 5, 10, 40), "tree.png");
        var group = new GroupElement(ElementId.New(), ElementLayer.Foreground, [a, b]);

        var bounds = PanelElements.Bounds(group);

        Assert.Equal(Rect2D.FromEdges(10, 5, 60, 45), bounds);
    }

    [Fact]
    public void A_nested_groups_bounds_covers_every_descendant()
    {
        var leaf = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(100, 100, 5, 5), "X", Style: new(10, null));
        var inner = new GroupElement(ElementId.New(), ElementLayer.Foreground, [leaf]);
        var outer = new GroupElement(ElementId.New(), ElementLayer.Foreground, [
            new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(0, 0, 5, 5), "Y", Style: new(10, null)),
            inner
        ]);

        Assert.Equal(Rect2D.FromEdges(0, 0, 105, 105), PanelElements.Bounds(outer));
    }

    [Fact]
    public void ArtFileNames_finds_pictures_nested_inside_a_group()
    {
        var picture = new PictureElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(0, 0, 10, 10), "tree.png");
        var group = new GroupElement(ElementId.New(), ElementLayer.Foreground, [picture]);
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(0, 0, 100, 80)), null, [], [], [group]);

        Assert.Equal(["tree.png"], PanelElements.ArtFileNames(panel));
    }
}
