namespace Stanley.ProjectModel.Geometry;

/// <summary>Pure <c>bounds -&gt; anchors</c> generators for panel shapes, the same shape the preset tables (<see cref="Bubbles.BubbleStylePresets"/>, <see cref="MetricPaperSizes"/>) use.</summary>
public static class PanelShapes
{
    /// <summary>An axis-aligned rectangular panel - the default a panel-layout editor creates and resizes; an arbitrary <see cref="PanelShape"/> is still the escape hatch for hand-edited panel outlines.</summary>
    public static PanelShape Rectangle(Rect2D bounds) => new([
        Corner(bounds.Left, bounds.Top),
        Corner(bounds.Right, bounds.Top),
        Corner(bounds.Right, bounds.Bottom),
        Corner(bounds.Left, bounds.Bottom)
    ]);

    private static ShapeAnchor Corner(double x, double y)
    {
        var p = new Point2D(x, y);
        return new ShapeAnchor(p, p, p, AnchorHandleKind.Corner);
    }
}
