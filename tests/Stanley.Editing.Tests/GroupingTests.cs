using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
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
