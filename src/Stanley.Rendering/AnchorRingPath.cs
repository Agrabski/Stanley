using SkiaSharp;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>Turns any anchor ring (a panel's or a bubble's) into an <see cref="SKPath"/>. Pure geometry-to-pixels conversion; no data ownership.</summary>
public static class AnchorRingPath
{
    public static SKPath ToSkPath(IReadOnlyList<ShapeAnchor> anchors)
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(ToSk(anchors[0].Point));
        for (var i = 0; i < anchors.Count; i++)
        {
            var current = anchors[i];
            var next = anchors[(i + 1) % anchors.Count];
            builder.CubicTo(ToSk(current.OutHandle), ToSk(next.InHandle), ToSk(next.Point));
        }
        builder.Close();
        return builder.Detach();
    }

    public static SKPoint ToSk(Point2D p) => new((float)p.X, (float)p.Y);
}
