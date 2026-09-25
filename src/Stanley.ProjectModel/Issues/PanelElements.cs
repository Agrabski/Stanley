using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Issues;

/// <summary>Plain geometry questions about a <see cref="PanelElement"/>, shared by rendering, editing and hit testing.</summary>
public static class PanelElements
{
    /// <summary>The element's box on the page: a shape's anchor points' bounding box (the box its resize handles drag), a text's own bounds.</summary>
    public static Rect2D Bounds(PanelElement element) => element switch
    {
        ShapeElement shape => AnchorRing.BoundingBox(shape.Anchors),
        TextElement text => text.Bounds,
        _ => default
    };
}
