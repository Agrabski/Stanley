using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>A gutter the pointer is over: the <see cref="PanelBoundaryDrag"/> that moves it, where its "before" edge currently is, and the extent it spans across (for drawing a hover highlight).</summary>
public sealed record GutterHit(PanelBoundaryDrag Drag, double Position, double SpanStart, double SpanEnd);

/// <summary>
/// Finds the gutter (the gap - possibly zero-width - between two neighbouring panels)
/// under a point, and every panel whose edge lines up along it, so dragging it moves
/// the whole run of panels on both sides together instead of opening a gap. Works for
/// any number of panels in any rectangular layout, not just a two-panel page.
/// </summary>
public static class PanelGutters
{
    private const double AlignEpsilon = 0.5;

    /// <param name="tolerance">How far outside the gap itself the point may be and still grab it.</param>
    /// <param name="maxGap">The widest gap still treated as a gutter rather than empty page.</param>
    public static GutterHit? FindAt(IEnumerable<Panel> panels, Point2D point, double tolerance, double maxGap = 20)
    {
        var rects = panels.Select(p => (p.Id, Bounds: AnchorRing.BoundingBox(p.Shape.Anchors))).ToList();
        return FindAlong(rects, point, tolerance, maxGap, BoundaryOrientation.Vertical)
            ?? FindAlong(rects, point, tolerance, maxGap, BoundaryOrientation.Horizontal);
    }

    private static GutterHit? FindAlong(List<(PanelId Id, Rect2D Bounds)> rects, Point2D point, double tolerance, double maxGap, BoundaryOrientation orientation)
    {
        var vertical = orientation == BoundaryOrientation.Vertical;
        var along = vertical ? point.X : point.Y;
        var across = vertical ? point.Y : point.X;
        var axis = rects.Select(r => (r.Id, Span: Project(r.Bounds, vertical))).ToList();

        GutterHit? best = null;
        var bestDistance = double.MaxValue;
        foreach (var a in axis)
        {
            foreach (var b in axis)
            {
                if (a.Id.Equals(b.Id))
                    continue;

                var gap = b.Span.Lo - a.Span.Hi;
                if (gap < -AlignEpsilon || gap > maxGap)
                    continue;

                var crossLo = Math.Max(a.Span.CrossLo, b.Span.CrossLo);
                var crossHi = Math.Min(a.Span.CrossHi, b.Span.CrossHi);
                if (crossHi <= crossLo || across < crossLo || across > crossHi)
                    continue;

                if (along < a.Span.Hi - tolerance || along > b.Span.Lo + tolerance)
                    continue;

                var distance = Math.Abs(along - (a.Span.Hi + b.Span.Lo) / 2);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = BuildChain(axis, a, b, orientation, maxGap);
            }
        }
        return best;
    }

    /// <summary>Grows the pair into the full run of panels sharing this gutter line, as long as they're contiguous across it (no more than <paramref name="maxGap"/> apart).</summary>
    private static GutterHit BuildChain(
        List<(PanelId Id, AxisSpan Span)> axis,
        (PanelId Id, AxisSpan Span) a,
        (PanelId Id, AxisSpan Span) b,
        BoundaryOrientation orientation,
        double maxGap)
    {
        var before = new List<PanelId> { a.Id };
        var after = new List<PanelId> { b.Id };
        var spanLo = Math.Min(a.Span.CrossLo, b.Span.CrossLo);
        var spanHi = Math.Max(a.Span.CrossHi, b.Span.CrossHi);

        bool grew;
        do
        {
            grew = false;
            foreach (var r in axis)
            {
                if (before.Contains(r.Id) || after.Contains(r.Id))
                    continue;
                if (r.Span.CrossHi < spanLo - maxGap || r.Span.CrossLo > spanHi + maxGap)
                    continue;

                if (Math.Abs(r.Span.Hi - a.Span.Hi) < AlignEpsilon)
                    before.Add(r.Id);
                else if (Math.Abs(r.Span.Lo - b.Span.Lo) < AlignEpsilon)
                    after.Add(r.Id);
                else
                    continue;

                spanLo = Math.Min(spanLo, r.Span.CrossLo);
                spanHi = Math.Max(spanHi, r.Span.CrossHi);
                grew = true;
            }
        } while (grew);

        var gap = Math.Max(0, b.Span.Lo - a.Span.Hi);
        return new GutterHit(new PanelBoundaryDrag(orientation, before, after, gap), a.Span.Hi, spanLo, spanHi);
    }

    private static AxisSpan Project(Rect2D r, bool vertical) =>
        vertical ? new AxisSpan(r.Left, r.Right, r.Top, r.Bottom) : new AxisSpan(r.Top, r.Bottom, r.Left, r.Right);

    /// <summary>A rectangle seen along one axis: <see cref="Lo"/>/<see cref="Hi"/> along it, <see cref="CrossLo"/>/<see cref="CrossHi"/> across it.</summary>
    private readonly record struct AxisSpan(double Lo, double Hi, double CrossLo, double CrossHi);
}
