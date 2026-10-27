using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// Plain outlines in a module's unit box (docs/shape-characters.md §4.1): what a hand-edited
/// file with a broken outline falls back to (<see cref="ModularBodies.Normalized"/>), and the
/// simplest shapes to build a body from in code and tests. The shipped library's shapes are
/// drawn with more character than these.
/// </summary>
public static class ModuleShapes
{
    private static readonly Rect2D Unit = new(0, 0, 1, 1);

    /// <summary>The ellipse filling the unit box: four smooth anchors, clockwise from the top.</summary>
    public static ModuleShape Ellipse() => new(AnchorRing.Ellipse(Unit));

    /// <summary>
    /// The unit box with its corners rounded by <paramref name="radius"/> (a fraction of the box,
    /// 0 square to 0.5 a pill or circle): eight anchors, clockwise from the top edge's left end.
    /// </summary>
    public static ModuleShape RoundedRectangle(double radius = 0.2)
    {
        var r = Math.Clamp(double.IsFinite(radius) ? radius : 0.2, 0, 0.5);
        if (r <= 0)
            return new ModuleShape(AnchorRing.Rectangle(Unit));
        const double kappa = 0.5522848;
        var k = r * kappa;
        ShapeAnchor A(double x, double y, double inX, double inY, double outX, double outY) =>
            new(new Point2D(x, y), new Point2D(x + inX, y + inY), new Point2D(x + outX, y + outY), AnchorHandleKind.Smooth);
        return new ModuleShape(
        [
            A(r, 0, -k, 0, 0, 0),
            A(1 - r, 0, 0, 0, k, 0),
            A(1, r, 0, -k, 0, 0),
            A(1, 1 - r, 0, 0, 0, k),
            A(1 - r, 1, k, 0, 0, 0),
            A(r, 1, 0, 0, -k, 0),
            A(0, 1 - r, 0, k, 0, 0),
            A(0, r, 0, 0, 0, -k),
        ]);
    }

    /// <summary>A triangle standing on its base, its point at the top centre; FlipY on the module turns it upside down.</summary>
    public static ModuleShape Triangle() => new(
    [
        AnchorRing.Corner(new Point2D(0.5, 0)),
        AnchorRing.Corner(new Point2D(1, 1)),
        AnchorRing.Corner(new Point2D(0, 1)),
    ]);
}
