using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Issues;

/// <summary>Plain questions about a panel's <see cref="PanelElement"/>s, shared by rendering, editing, hit testing and saving.</summary>
public static class PanelElements
{
    /// <summary>The element's box on the page: around a shape's outline - curves bowed out past its points included (the box its resize handles drag) - a text's own bounds. A group's is the union of its children's.</summary>
    public static Rect2D Bounds(PanelElement element) => element switch
    {
        ShapeElement shape => AnchorRing.CurveBounds(shape.Anchors, closed: shape.Closed && shape.Anchors.Count > 2), // as ElementRenderer.ShapePath draws it
        TextElement text => text.Bounds,
        PictureElement picture => picture.Bounds,
        SpeedLinesElement speedLines => speedLines.Focus,
        GroupElement group => Rect2D.Union(group.Children.Select(Bounds)),
        _ => default
    };

    /// <summary>Every art file name <paramref name="panel"/> uses - its background picture and its picture elements, including those nested inside groups.</summary>
    public static IEnumerable<string> ArtFileNames(Panel panel)
    {
        if (panel.Background is InlineBackground inline)
            yield return inline.ArtFileName;
        foreach (var element in panel.Elements)
        {
            foreach (var name in ArtFileNames(element))
                yield return name;
        }
    }

    /// <summary>Every art file name <paramref name="element"/> uses - a picture's own, or every picture inside a group.</summary>
    public static IEnumerable<string> ArtFileNames(PanelElement element)
    {
        switch (element)
        {
            case PictureElement picture:
                yield return picture.ArtFileName;
                break;
            case GroupElement group:
                foreach (var child in group.Children)
                foreach (var name in ArtFileNames(child))
                    yield return name;
                break;
        }
    }
}
