using Stanley.ProjectModel.Geometry;

namespace Stanley.Editing;

/// <summary>
/// The page grid the panel editor snaps to: an outer <see cref="MarginMm"/> between the
/// trim edge and the panel area, and a <see cref="GutterMm"/> between neighbouring
/// panels. Sensible comic defaults, so drawing or dragging panels "just lines up"
/// without the user ever opening a settings dialog.
/// </summary>
public sealed record PanelGrid(double MarginMm, double GutterMm)
{
    public static PanelGrid Default { get; } = new(10, 4);

    /// <summary>The area panels are meant to fill: the page inset by the margin on every side.</summary>
    public Rect2D LiveArea(Rect2D page) =>
        Rect2D.FromEdges(page.Left + MarginMm, page.Top + MarginMm, page.Right - MarginMm, page.Bottom - MarginMm);
}

[Flags]
public enum RectEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    All = Left | Top | Right | Bottom
}

/// <summary>A line an edge snapped onto, for the view to draw as feedback: an X for a <see cref="BoundaryOrientation.Vertical"/> guide, a Y for a horizontal one.</summary>
public readonly record struct SnapGuide(BoundaryOrientation Orientation, double Position);

public sealed record SnapResult(Rect2D Bounds, IReadOnlyList<SnapGuide> Guides);

/// <summary>
/// Pulls panel edges onto the page margin, onto a neighbour's edge plus one gutter
/// (so panels sit exactly a gutter apart), or into line with a neighbour's matching
/// edge. Pure geometry over rectangles; the caller supplies the tolerance (usually a
/// few screen pixels converted into millimetres at the current zoom).
/// </summary>
public static class PanelSnapping
{
    /// <summary>Snaps only the edges in <paramref name="moving"/>; the others stay put, as when dragging a corner or an edge.</summary>
    public static SnapResult SnapResize(Rect2D bounds, RectEdges moving, Rect2D page, IEnumerable<Rect2D> others, PanelGrid grid, double tolerance)
    {
        var otherList = others.ToList();
        var guides = new List<SnapGuide>();

        var left = bounds.Left;
        var top = bounds.Top;
        var right = bounds.Right;
        var bottom = bounds.Bottom;

        if (moving.HasFlag(RectEdges.Left))
            left = SnapValue(left, LeftTargets(page, otherList, grid), tolerance, BoundaryOrientation.Vertical, guides);
        if (moving.HasFlag(RectEdges.Right))
            right = SnapValue(right, RightTargets(page, otherList, grid), tolerance, BoundaryOrientation.Vertical, guides);
        if (moving.HasFlag(RectEdges.Top))
            top = SnapValue(top, TopTargets(page, otherList, grid), tolerance, BoundaryOrientation.Horizontal, guides);
        if (moving.HasFlag(RectEdges.Bottom))
            bottom = SnapValue(bottom, BottomTargets(page, otherList, grid), tolerance, BoundaryOrientation.Horizontal, guides);

        return new SnapResult(Rect2D.FromEdges(left, top, right, bottom), guides);
    }

    /// <summary>Translates without resizing: whichever of the two opposite edges is closest to a target wins, per axis.</summary>
    public static SnapResult SnapMove(Rect2D bounds, Rect2D page, IEnumerable<Rect2D> others, PanelGrid grid, double tolerance)
    {
        var otherList = others.ToList();
        var guides = new List<SnapGuide>();

        var dx = BestDelta(
            (bounds.Left, LeftTargets(page, otherList, grid)),
            (bounds.Right, RightTargets(page, otherList, grid)),
            tolerance);
        var dy = BestDelta(
            (bounds.Top, TopTargets(page, otherList, grid)),
            (bounds.Bottom, BottomTargets(page, otherList, grid)),
            tolerance);

        var moved = bounds with { X = bounds.X + dx.Delta, Y = bounds.Y + dy.Delta };
        if (dx.Snapped)
            guides.Add(new SnapGuide(BoundaryOrientation.Vertical, dx.Edge + dx.Delta));
        if (dy.Snapped)
            guides.Add(new SnapGuide(BoundaryOrientation.Horizontal, dy.Edge + dy.Delta));
        return new SnapResult(moved, guides);
    }

    /// <summary>Snaps a single coordinate to the nearest target within <paramref name="tolerance"/>, recording a guide if it snapped.</summary>
    public static double SnapValue(double value, IEnumerable<double> targets, double tolerance, BoundaryOrientation orientation, List<SnapGuide> guides)
    {
        var best = value;
        var bestDistance = tolerance;
        var snapped = false;
        foreach (var target in targets)
        {
            var distance = Math.Abs(target - value);
            if (distance <= bestDistance)
            {
                best = target;
                bestDistance = distance;
                snapped = true;
            }
        }

        if (snapped)
            guides.Add(new SnapGuide(orientation, best));
        return best;
    }

    public static IEnumerable<double> LeftTargets(Rect2D page, IReadOnlyList<Rect2D> others, PanelGrid grid) =>
        others.SelectMany(o => new[] { o.Right + grid.GutterMm, o.Left }).Prepend(page.Left + grid.MarginMm).Prepend(page.Left);

    public static IEnumerable<double> RightTargets(Rect2D page, IReadOnlyList<Rect2D> others, PanelGrid grid) =>
        others.SelectMany(o => new[] { o.Left - grid.GutterMm, o.Right }).Prepend(page.Right - grid.MarginMm).Prepend(page.Right);

    public static IEnumerable<double> TopTargets(Rect2D page, IReadOnlyList<Rect2D> others, PanelGrid grid) =>
        others.SelectMany(o => new[] { o.Bottom + grid.GutterMm, o.Top }).Prepend(page.Top + grid.MarginMm).Prepend(page.Top);

    public static IEnumerable<double> BottomTargets(Rect2D page, IReadOnlyList<Rect2D> others, PanelGrid grid) =>
        others.SelectMany(o => new[] { o.Top - grid.GutterMm, o.Bottom }).Prepend(page.Bottom - grid.MarginMm).Prepend(page.Bottom);

    private static (double Delta, double Edge, bool Snapped) BestDelta(
        (double Edge, IEnumerable<double> Targets) first,
        (double Edge, IEnumerable<double> Targets) second,
        double tolerance)
    {
        var result = (Delta: 0.0, Edge: 0.0, Snapped: false);
        var bestDistance = tolerance;
        foreach (var (edge, targets) in new[] { first, second })
        {
            foreach (var target in targets)
            {
                var distance = Math.Abs(target - edge);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    result = (target - edge, edge, true);
                }
            }
        }
        return result;
    }
}
