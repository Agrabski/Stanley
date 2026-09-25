using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Issues;

/// <summary>Plain questions about a panel's <see cref="PanelElement"/>s, shared by rendering, editing, hit testing and saving.</summary>
public static class PanelElements
{
    /// <summary>The element's box on the page: a shape's anchor points' bounding box (the box its resize handles drag), a text's own bounds.</summary>
    public static Rect2D Bounds(PanelElement element) => element switch
    {
        ShapeElement shape => AnchorRing.BoundingBox(shape.Anchors),
        TextElement text => text.Bounds,
        PictureElement picture => picture.Bounds,
        _ => default
    };

    /// <summary>Every art file name <paramref name="panel"/> uses - its background picture and its picture elements.</summary>
    public static IEnumerable<string> ArtFileNames(Panel panel)
    {
        if (panel.Background is InlineBackground inline)
            yield return inline.ArtFileName;
        foreach (var element in panel.Elements)
        {
            if (element is PictureElement picture)
                yield return picture.ArtFileName;
        }
    }
}
