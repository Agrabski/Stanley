using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>Which of an anchor's two bezier handles: the one shaping the edge coming into it, or the one going out of it.</summary>
public enum HandleSide
{
    In,
    Out
}

/// <summary>
/// A spot on a shape's outline: the edge (<paramref name="Segment"/>) from anchor
/// <paramref name="Segment"/> to the next one, how far along it (<paramref name="T"/>, 0 to 1),
/// where that is and how far it lies from the point that was asked about.
/// </summary>
public readonly record struct OutlineSpot(int Segment, double T, Point2D Point, double Distance);

/// <summary>
/// Edit Points (issue #84): reshaping a drawn shape anchor by anchor, the way Figma's vector
/// editing and PowerPoint's Edit Points do - move a point, pull its curve handles, add a point
/// anywhere on the outline (the curve keeps its shape), delete one, make it a smooth curve or
/// a sharp corner, close a line into a shape or open a shape into a line. Also builds the
/// Freeform tool's shapes, placed point by point. Everything works on the same
/// <see cref="ShapeElement"/> anchor model the other drawing tools produce, so any shape -
/// a rectangle, an ellipse, a scribble - can be edited this way.
/// </summary>
public static class ShapePointEditing
{
    /// <summary>A handle closer to its point than this (mm) is "on the point": the edge leaves it straight and there's no handle to show or grab.</summary>
    public const double HandleEpsilonMm = 1e-3;

    /// <summary>How many edges the shape has: one between each pair of neighbours, plus the closing one back to the start for a closed shape.</summary>
    public static int SegmentCount(ShapeElement shape) => shape.Closed ? shape.Anchors.Count : Math.Max(0, shape.Anchors.Count - 1);

    /// <summary>The fewest points a shape can have and still be what it is: three for a closed shape, two for a line.</summary>
    public static int MinPoints(bool closed) => closed ? 3 : 2;

    /// <summary>Moves one point, its handles along with it, so the curves either side keep their bend.</summary>
    public static EditResult<ShapeElement> MovePoint(ShapeElement shape, int index, Point2D to)
    {
        if (!IsPoint(shape, index))
            return EditResult<ShapeElement>.Failure("No such point.");
        var a = shape.Anchors[index];
        var (dx, dy) = (to.X - a.Point.X, to.Y - a.Point.Y);
        return EditResult<ShapeElement>.Success(Replace(shape, index, a with
        {
            Point = to,
            InHandle = Offset(a.InHandle, dx, dy),
            OutHandle = Offset(a.OutHandle, dx, dy)
        }));
    }

    /// <summary>
    /// Drags one of a point's curve handles to <paramref name="to"/>. On a smooth point the
    /// other handle swings round to stay in line (keeping its own length), so the curve stays
    /// smooth through the point; <paramref name="independent"/> (Alt) lets the handle go its
    /// own way, turning the point into a corner. A corner's handles always move on their own.
    /// </summary>
    public static EditResult<ShapeElement> MoveHandle(ShapeElement shape, int index, HandleSide side, Point2D to, bool independent = false)
    {
        if (!IsPoint(shape, index))
            return EditResult<ShapeElement>.Failure("No such point.");
        var a = shape.Anchors[index];
        var moved = side == HandleSide.Out ? a with { OutHandle = to } : a with { InHandle = to };

        if (independent)
            return EditResult<ShapeElement>.Success(Replace(shape, index, moved with { HandleKind = AnchorHandleKind.Corner }));

        if (a.HandleKind == AnchorHandleKind.Smooth)
        {
            var other = side == HandleSide.Out ? a.InHandle : a.OutHandle;
            var otherLength = Distance(a.Point, other);
            var pulled = Distance(a.Point, to);
            if (otherLength > HandleEpsilonMm && pulled > HandleEpsilonMm)
            {
                var mirrored = new Point2D(a.Point.X - (to.X - a.Point.X) / pulled * otherLength, a.Point.Y - (to.Y - a.Point.Y) / pulled * otherLength);
                moved = side == HandleSide.Out ? moved with { InHandle = mirrored } : moved with { OutHandle = mirrored };
            }
        }
        return EditResult<ShapeElement>.Success(Replace(shape, index, moved));
    }

    /// <summary>
    /// The handles worth showing on point <paramref name="index"/>: only those shaping an edge
    /// that's actually there (an open line's ends have one each) and pulled away from the
    /// point - a sharp corner's sit on it and have nothing to grab.
    /// </summary>
    public static IReadOnlyList<(HandleSide Side, Point2D Point)> VisibleHandles(ShapeElement shape, int index)
    {
        if (!IsPoint(shape, index))
            return [];
        var a = shape.Anchors[index];
        var handles = new List<(HandleSide, Point2D)>(2);
        if ((shape.Closed || index > 0) && Distance(a.Point, a.InHandle) > HandleEpsilonMm)
            handles.Add((HandleSide.In, a.InHandle));
        if ((shape.Closed || index < shape.Anchors.Count - 1) && Distance(a.Point, a.OutHandle) > HandleEpsilonMm)
            handles.Add((HandleSide.Out, a.OutHandle));
        return handles;
    }

    /// <summary>The point <paramref name="t"/> of the way along edge <paramref name="segment"/>.</summary>
    public static Point2D PointOn(ShapeElement shape, int segment, double t)
    {
        var (p0, c0, c1, p1) = Edge(shape, segment);
        return Cubic(p0, c0, c1, p1, t);
    }

    /// <summary>The spot on the outline nearest <paramref name="p"/>, or null for a shape with no edges.</summary>
    public static OutlineSpot? Nearest(ShapeElement shape, Point2D p)
    {
        const int samples = 48;
        OutlineSpot? best = null;
        for (var s = 0; s < SegmentCount(shape); s++)
        {
            var (p0, c0, c1, p1) = Edge(shape, s);
            var bestT = 0.0;
            var bestDistance = double.MaxValue;
            for (var i = 0; i <= samples; i++)
            {
                var t = (double)i / samples;
                var d = Distance(Cubic(p0, c0, c1, p1, t), p);
                if (d < bestDistance)
                    (bestT, bestDistance) = (t, d);
            }

            // Narrow in on the nearest sample's neighbourhood.
            var lo = Math.Max(0, bestT - 1.0 / samples);
            var hi = Math.Min(1, bestT + 1.0 / samples);
            for (var k = 0; k < 40; k++)
            {
                var m1 = lo + (hi - lo) / 3;
                var m2 = hi - (hi - lo) / 3;
                if (Distance(Cubic(p0, c0, c1, p1, m1), p) < Distance(Cubic(p0, c0, c1, p1, m2), p))
                    hi = m2;
                else
                    lo = m1;
            }
            var refined = (lo + hi) / 2;
            var point = Cubic(p0, c0, c1, p1, refined);
            var distance = Distance(point, p);
            if (best is not { } b || distance < b.Distance)
                best = new OutlineSpot(s, refined, point, distance);
        }
        return best;
    }

    /// <summary>
    /// A new point <paramref name="t"/> of the way along edge <paramref name="segment"/>,
    /// without changing how the outline looks: a curved edge is split in two (de Casteljau),
    /// the new point smooth; a straight edge gets a corner, so dragging it out makes two
    /// straight edges - a rectangle's side pulled into a point. Returns the shape and the new
    /// point's index.
    /// </summary>
    public static EditResult<(ShapeElement Shape, int Index)> InsertPoint(ShapeElement shape, int segment, double t)
    {
        if (segment < 0 || segment >= SegmentCount(shape))
            return EditResult<(ShapeElement, int)>.Failure("No such edge.");
        t = Math.Clamp(t, 1e-3, 1 - 1e-3);
        var n = shape.Anchors.Count;
        var next = (segment + 1) % n;
        var a = shape.Anchors[segment];
        var b = shape.Anchors[next];

        var list = shape.Anchors.ToList();
        ShapeAnchor added;
        if (Distance(a.Point, a.OutHandle) <= HandleEpsilonMm && Distance(b.Point, b.InHandle) <= HandleEpsilonMm)
        {
            added = AnchorRing.Corner(Lerp(a.Point, b.Point, t));
        }
        else
        {
            var q0 = Lerp(a.Point, a.OutHandle, t);
            var q1 = Lerp(a.OutHandle, b.InHandle, t);
            var q2 = Lerp(b.InHandle, b.Point, t);
            var r0 = Lerp(q0, q1, t);
            var r1 = Lerp(q1, q2, t);
            added = new ShapeAnchor(Lerp(r0, r1, t), r0, r1, AnchorHandleKind.Smooth);
            list[segment] = a with { OutHandle = q0 };
            list[next] = b with { InHandle = q2 };
        }
        list.Insert(segment + 1, added);
        return EditResult<(ShapeElement, int)>.Success((shape with { Anchors = list }, segment + 1));
    }

    /// <summary>Removes a point; the edges either side join up. A closed shape keeps at least three points and a line two.</summary>
    public static EditResult<ShapeElement> DeletePoint(ShapeElement shape, int index)
    {
        if (!IsPoint(shape, index))
            return EditResult<ShapeElement>.Failure("No such point.");
        if (shape.Anchors.Count - 1 < MinPoints(shape.Closed))
            return EditResult<ShapeElement>.Failure(shape.Closed ? "A shape needs at least three points." : "A line needs at least two points.");
        return EditResult<ShapeElement>.Success(shape with { Anchors = shape.Anchors.Where((_, i) => i != index).ToList() });
    }

    /// <summary>
    /// Makes a point a sharp corner (its handles fold onto it, so the outline turns there) or a
    /// smooth curve: handles already pulled out are lined up with each other, otherwise new
    /// ones are aimed along the line between its neighbours, a third of the way to each - the
    /// same rounding the freehand pen gives a curve.
    /// </summary>
    public static EditResult<ShapeElement> SetPointKind(ShapeElement shape, int index, AnchorHandleKind kind)
    {
        if (!IsPoint(shape, index))
            return EditResult<ShapeElement>.Failure("No such point.");
        var a = shape.Anchors[index];
        if (kind == AnchorHandleKind.Corner)
            return EditResult<ShapeElement>.Success(Replace(shape, index, AnchorRing.Corner(a.Point)));

        var n = shape.Anchors.Count;
        Point2D? prev = index > 0 ? shape.Anchors[index - 1].Point : shape.Closed ? shape.Anchors[n - 1].Point : null;
        Point2D? next = index < n - 1 ? shape.Anchors[index + 1].Point : shape.Closed ? shape.Anchors[0].Point : null;
        var inLength = Distance(a.Point, a.InHandle);
        var outLength = Distance(a.Point, a.OutHandle);

        // Already pulled out both ways (a cusp): line the two up, keeping their lengths.
        if (prev is not null && next is not null && inLength > HandleEpsilonMm && outLength > HandleEpsilonMm)
        {
            var (ux, uy) = Unit(a.OutHandle.X - a.Point.X - (a.InHandle.X - a.Point.X) * outLength / inLength,
                a.OutHandle.Y - a.Point.Y - (a.InHandle.Y - a.Point.Y) * outLength / inLength);
            if (ux != 0 || uy != 0)
                return EditResult<ShapeElement>.Success(Replace(shape, index, new ShapeAnchor(a.Point,
                    new Point2D(a.Point.X - ux * inLength, a.Point.Y - uy * inLength),
                    new Point2D(a.Point.X + ux * outLength, a.Point.Y + uy * outLength),
                    AnchorHandleKind.Smooth)));
        }

        // An open line's end has one edge: aim its handle along it.
        if (prev is not { } before || next is not { } after)
        {
            var neighbour = prev ?? next;
            if (neighbour is not { } towards || Distance(a.Point, towards) <= HandleEpsilonMm)
                return EditResult<ShapeElement>.Success(Replace(shape, index, a with { HandleKind = AnchorHandleKind.Smooth }));
            var handle = Lerp(a.Point, towards, 1.0 / 3);
            return EditResult<ShapeElement>.Success(Replace(shape, index, prev is null
                ? new ShapeAnchor(a.Point, a.Point, handle, AnchorHandleKind.Smooth)
                : new ShapeAnchor(a.Point, handle, a.Point, AnchorHandleKind.Smooth)));
        }

        var (tx, ty) = Unit(after.X - before.X, after.Y - before.Y);
        if (tx == 0 && ty == 0)
            (tx, ty) = Unit(a.Point.X - before.X, a.Point.Y - before.Y);
        var backLength = Distance(before, a.Point) / 3;
        var onLength = Distance(a.Point, after) / 3;
        return EditResult<ShapeElement>.Success(Replace(shape, index, new ShapeAnchor(a.Point,
            new Point2D(a.Point.X - tx * backLength, a.Point.Y - ty * backLength),
            new Point2D(a.Point.X + tx * onLength, a.Point.Y + ty * onLength),
            AnchorHandleKind.Smooth)));
    }

    /// <summary>Joins a line's two ends into a closed shape, which a fill then fills - an end drawn right back onto the start becomes that one point.</summary>
    public static EditResult<ShapeElement> Close(ShapeElement shape)
    {
        if (shape.Closed)
            return EditResult<ShapeElement>.Failure("The shape is already closed.");
        var anchors = shape.Anchors.ToList();
        if (anchors.Count > MinPoints(closed: true) && Distance(anchors[0].Point, anchors[^1].Point) <= HandleEpsilonMm)
        {
            anchors[0] = anchors[0] with { InHandle = Offset(anchors[^1].InHandle, anchors[0].Point.X - anchors[^1].Point.X, anchors[0].Point.Y - anchors[^1].Point.Y) };
            anchors.RemoveAt(anchors.Count - 1);
        }
        if (anchors.Count < MinPoints(closed: true))
            return EditResult<ShapeElement>.Failure("A line needs at least three points to close into a shape.");
        return EditResult<ShapeElement>.Success(shape with { Anchors = anchors, Closed = true });
    }

    /// <summary>
    /// Cuts a closed shape open at point <paramref name="index"/>, leaving a line that runs all
    /// the way round from there and back - it looks the same but isn't filled any more, and its
    /// two ends can be pulled apart. Returns the line and the index of its far end (the copy of
    /// the cut point), ready to drag away.
    /// </summary>
    public static EditResult<(ShapeElement Shape, int Index)> OpenAt(ShapeElement shape, int index)
    {
        if (!shape.Closed)
            return EditResult<(ShapeElement, int)>.Failure("The shape is already open.");
        if (!IsPoint(shape, index))
            return EditResult<(ShapeElement, int)>.Failure("No such point.");
        var n = shape.Anchors.Count;
        var cut = shape.Anchors[index];
        var anchors = new List<ShapeAnchor>(n + 1) { cut with { InHandle = cut.Point, HandleKind = AnchorHandleKind.Corner } };
        for (var i = 1; i < n; i++)
            anchors.Add(shape.Anchors[(index + i) % n]);
        anchors.Add(cut with { OutHandle = cut.Point, HandleKind = AnchorHandleKind.Corner });
        return EditResult<(ShapeElement, int)>.Success((shape with { Anchors = anchors, Closed = false }, anchors.Count - 1));
    }

    // ---------------------------------------------------------------- the Freeform tool

    /// <summary>
    /// A point placed with the Freeform tool: where it was pressed, and - dragged before letting
    /// go - smooth, its out handle following the pointer and its in handle mirrored opposite, as
    /// in Figma; a plain click leaves a sharp corner.
    /// </summary>
    public static ShapeAnchor Placed(Point2D at, Point2D? pulledTo = null) =>
        pulledTo is { } pull && Distance(at, pull) > HandleEpsilonMm
            ? new ShapeAnchor(at, new Point2D(2 * at.X - pull.X, 2 * at.Y - pull.Y), pull, AnchorHandleKind.Smooth)
            : AnchorRing.Corner(at);

    /// <summary>The Freeform tool's shape from the points placed: a line through them, or - <paramref name="closed"/> by clicking the first point again - a shape the fill fills.</summary>
    public static EditResult<ShapeElement> Freeform(IReadOnlyList<ShapeAnchor> anchors, bool closed, ShapeStyle style, ElementLayer layer)
    {
        if (closed && anchors.Count < MinPoints(closed: true))
            closed = false;
        if (anchors.Count < MinPoints(closed: false))
            return EditResult<ShapeElement>.Failure("Click at least two points to draw a line.");
        var box = AnchorRing.BoundingBox(anchors);
        if (Math.Max(box.Width, box.Height) < ShapeEditing.MinSizeMm)
            return EditResult<ShapeElement>.Failure("Place the points a little further apart.");
        return EditResult<ShapeElement>.Success(new ShapeElement(ElementId.New(), layer, anchors.ToList(), closed, style));
    }

    /// <summary>Shift held: <paramref name="to"/> swung round <paramref name="from"/> to the nearest 45° - level, upright or diagonal - keeping its distance.</summary>
    public static Point2D SnapAngle(Point2D from, Point2D to)
    {
        var length = Distance(from, to);
        var angle = Math.Round(Math.Atan2(to.Y - from.Y, to.X - from.X) / (Math.PI / 4)) * (Math.PI / 4);
        return new Point2D(from.X + Math.Cos(angle) * length, from.Y + Math.Sin(angle) * length);
    }

    // ---------------------------------------------------------------- helpers

    private static bool IsPoint(ShapeElement shape, int index) => index >= 0 && index < shape.Anchors.Count;

    private static ShapeElement Replace(ShapeElement shape, int index, ShapeAnchor anchor)
    {
        var list = shape.Anchors.ToList();
        list[index] = anchor;
        return shape with { Anchors = list };
    }

    private static (Point2D P0, Point2D C0, Point2D C1, Point2D P1) Edge(ShapeElement shape, int segment)
    {
        var a = shape.Anchors[segment];
        var b = shape.Anchors[(segment + 1) % shape.Anchors.Count];
        return (a.Point, a.OutHandle, b.InHandle, b.Point);
    }

    private static Point2D Cubic(Point2D p0, Point2D c0, Point2D c1, Point2D p1, double t)
    {
        var mt = 1 - t;
        var a = mt * mt * mt;
        var b = 3 * mt * mt * t;
        var c = 3 * mt * t * t;
        var d = t * t * t;
        return new Point2D(a * p0.X + b * c0.X + c * c1.X + d * p1.X, a * p0.Y + b * c0.Y + c * c1.Y + d * p1.Y);
    }

    private static (double X, double Y) Unit(double x, double y)
    {
        var length = Math.Sqrt(x * x + y * y);
        return length < 1e-12 ? (0, 0) : (x / length, y / length);
    }

    private static Point2D Offset(Point2D p, double dx, double dy) => new(p.X + dx, p.Y + dy);

    private static Point2D Lerp(Point2D a, Point2D b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double Distance(Point2D a, Point2D b) => ShapeEditing.Distance(a, b);
}
