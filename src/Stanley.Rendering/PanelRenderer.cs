using SkiaSharp;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

public static class PanelRenderer
{
    public static SKPath ToSkPath(PanelShape shape) => AnchorRingPath.ToSkPath(shape.Anchors);
}
