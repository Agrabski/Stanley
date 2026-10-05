using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// Drawing shapes into a panel: turning a freehand pointer trail into a smooth bezier
/// shape, the straight-line/rectangle/ellipse shortcuts, and moving, resizing and restyling
/// what's there. Everything ends up as the same <see cref="ShapeElement"/> anchor model, so
/// a rectangle and a scribble edit alike.
/// </summary>
public static class ShapeEditing
{
    /// <summary>Smallest size a shape can be resized down to, each way (a flat line keeps its zero height).</summary>
    public const double MinSizeMm = 1;

    public const double MaxStrokeWidthMm = 20;

    /// <summary>The pen new shapes start with: a black 0.7mm line, no fill.</summary>
    public static readonly ShapeStyle DefaultStyle = new(ColorValue.FromHex("#1c1c1c"), Fill: null, 0.7);

    /// <summary>Bends sharper than this (degrees between the incoming and outgoing direction) stay corners; gentler ones are smoothed.</summary>
    private const double CornerAngleDegrees = 70;

    /// <summary>
    /// A shape from a freehand drag, in page millimetres: the trail is simplified (points
    /// that stray less than <paramref name="toleranceMm"/> from the line through their
    /// neighbours go), then smoothed into bezier curves that keep sharp bends as corners.
    /// A trail that ends within <paramref name="closeDistanceMm"/> of where it began - and
    /// went somewhere in between - is closed into a ring, which the style's fill fills.
    /// </summary>
    public static EditResult<ShapeElement> Freehand(IReadOnlyList<Point2D> trail, ShapeStyle style, ElementLayer layer, double toleranceMm, double closeDistanceMm)
    {
        var points = new List<Point2D>();
        foreach (var p in trail)
        {
            if (points.Count == 0 || Distance(points[^1], p) > 1e-6)
                points.Add(p);
        }

        var length = 0.0;
        for (var i = 1; i < points.Count; i++)
            length += Distance(points[i - 1], points[i]);
        if (points.Count < 2 || length < MinSizeMm)
            return EditResult<ShapeElement>.Failure("Drag to draw a line.");

        var closed = points.Count > 3 && Distance(points[0], points[^1]) <= closeDistanceMm && length > closeDistanceMm * 4;
        if (closed)
            points[^1] = points[0];

        var simplified = Simplify(points, Math.Max(toleranceMm, 1e-6));
        if (closed)
        {
            simplified.RemoveAt(simplified.Count - 1); // the ring comes back round to the first point by itself
            if (simplified.Count < 3)
                closed = false;
        }

        return EditResult<ShapeElement>.Success(new ShapeElement(ElementId.New(), layer, Smooth(simplified, closed), closed, style));
    }

    /// <summary>A straight line from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static EditResult<ShapeElement> Line(Point2D from, Point2D to, ShapeStyle style, ElementLayer layer) =>
        Distance(from, to) < MinSizeMm
            ? EditResult<ShapeElement>.Failure("Drag to draw a line.")
            : EditResult<ShapeElement>.Success(new ShapeElement(ElementId.New(), layer, [AnchorRing.Corner(from), AnchorRing.Corner(to)], Closed: false, style));

    public static EditResult<ShapeElement> Rectangle(Rect2D bounds, ShapeStyle style, ElementLayer layer) =>
        TooSmall(bounds)
            ? EditResult<ShapeElement>.Failure($"A shape must be at least {MinSizeMm}x{MinSizeMm}mm.")
            : EditResult<ShapeElement>.Success(new ShapeElement(ElementId.New(), layer, AnchorRing.Rectangle(bounds), Closed: true, style));

    public static EditResult<ShapeElement> Ellipse(Rect2D bounds, ShapeStyle style, ElementLayer layer) =>
        TooSmall(bounds)
            ? EditResult<ShapeElement>.Failure($"A shape must be at least {MinSizeMm}x{MinSizeMm}mm.")
            : EditResult<ShapeElement>.Success(new ShapeElement(ElementId.New(), layer, AnchorRing.Ellipse(bounds), Closed: true, style));

    public static ShapeElement Move(ShapeElement shape, double dx, double dy) =>
        shape with { Anchors = shape.Anchors.Select(a => a with { Point = Offset(a.Point, dx, dy), InHandle = Offset(a.InHandle, dx, dy), OutHandle = Offset(a.OutHandle, dx, dy) }).ToList() };

    /// <summary>
    /// Stretches the shape so its box (<see cref="PanelElements.Bounds"/>: around its outline,
    /// curves and all) becomes <paramref name="newBounds"/>. A flat side (a horizontal line's
    /// height) can't be stretched open - it just follows the box's near edge.
    /// </summary>
    public static EditResult<ShapeElement> Resize(ShapeElement shape, Rect2D newBounds)
    {
        var from = PanelElements.Bounds(shape);
        if ((from.Width >= MinSizeMm && newBounds.Width < MinSizeMm - 1e-9) || (from.Height >= MinSizeMm && newBounds.Height < MinSizeMm - 1e-9))
            return EditResult<ShapeElement>.Failure($"A shape must be at least {MinSizeMm}x{MinSizeMm}mm.");

        Point2D Map(Point2D p) => new(
            from.Width > 1e-9 ? newBounds.Left + (p.X - from.Left) * newBounds.Width / from.Width : newBounds.Left + (p.X - from.Left),
            from.Height > 1e-9 ? newBounds.Top + (p.Y - from.Top) * newBounds.Height / from.Height : newBounds.Top + (p.Y - from.Top));

        return EditResult<ShapeElement>.Success(shape with
        {
            Anchors = shape.Anchors.Select(a => a with { Point = Map(a.Point), InHandle = Map(a.InHandle), OutHandle = Map(a.OutHandle) }).ToList()
        });
    }

    public static EditResult<ShapeElement> SetStyle(ShapeElement shape, ShapeStyle style) =>
        style.StrokeWidthMm < 0 || style.StrokeWidthMm > MaxStrokeWidthMm
            ? EditResult<ShapeElement>.Failure($"A line can be at most {MaxStrokeWidthMm}mm thick.")
            : EditResult<ShapeElement>.Success(shape with { Style = style });

    // ---------------------------------------------------------------- freehand fitting

    /// <summary>Ramer-Douglas-Peucker: keeps the points that stray more than <paramref name="tolerance"/> from the straight line their neighbours would draw; always keeps both ends.</summary>
    public static List<Point2D> Simplify(IReadOnlyList<Point2D> points, double tolerance)
    {
        if (points.Count < 3)
            return points.ToList();

        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int First, int Last)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            var farthest = -1;
            var farthestDistance = tolerance;
            for (var i = first + 1; i < last; i++)
            {
                var d = DistanceToSegment(points[i], points[first], points[last]);
                if (d > farthestDistance)
                {
                    farthestDistance = d;
                    farthest = i;
                }
            }
            if (farthest < 0)
                continue;
            keep[farthest] = true;
            stack.Push((first, farthest));
            stack.Push((farthest, last));
        }

        return points.Where((_, i) => keep[i]).ToList();
    }

    /// <summary>
    /// Curves through <paramref name="points"/>: each point's handles lie along the line
    /// from its previous to its next neighbour (a Catmull-Rom style tangent), a third of the
    /// way to each, so the curve passes through every point without overshooting; a sharp
    /// bend keeps its handles on the point and stays a corner. An open line's ends point
    /// straight at their neighbours.
    /// </summary>
    public static List<ShapeAnchor> Smooth(IReadOnlyList<Point2D> points, bool closed)
    {
        var n = points.Count;
        var anchors = new List<ShapeAnchor>(n);
        for (var i = 0; i < n; i++)
        {
            var p = points[i];
            Point2D? prev = i > 0 ? points[i - 1] : closed ? points[n - 1] : null;
            Point2D? next = i < n - 1 ? points[i + 1] : closed ? points[0] : null;

            if (prev is not { } a || next is not { } b)
            {
                // An open end: aim the one handle straight at the neighbour.
                var neighbour = prev ?? next ?? p;
                var toward = Lerp(p, neighbour, 1.0 / 3);
                anchors.Add(prev is null
                    ? new ShapeAnchor(p, p, toward, AnchorHandleKind.Corner)
                    : new ShapeAnchor(p, toward, p, AnchorHandleKind.Corner));
                continue;
            }

            var inLength = Distance(a, p);
            var outLength = Distance(p, b);
            if (inLength < 1e-9 || outLength < 1e-9 || TurnDegrees(a, p, b) > CornerAngleDegrees)
            {
                anchors.Add(AnchorRing.Corner(p));
                continue;
            }

            var span = Distance(a, b);
            var tx = span < 1e-9 ? 0 : (b.X - a.X) / span;
            var ty = span < 1e-9 ? 0 : (b.Y - a.Y) / span;
            anchors.Add(new ShapeAnchor(
                p,
                new Point2D(p.X - tx * inLength / 3, p.Y - ty * inLength / 3),
                new Point2D(p.X + tx * outLength / 3, p.Y + ty * outLength / 3),
                AnchorHandleKind.Smooth));
        }
        return anchors;
    }

    /// <summary>How far the direction turns at <paramref name="p"/> going from <paramref name="a"/> through it to <paramref name="b"/>: 0 for straight on, 180 for doubling back.</summary>
    private static double TurnDegrees(Point2D a, Point2D p, Point2D b)
    {
        var inAngle = Math.Atan2(p.Y - a.Y, p.X - a.X);
        var outAngle = Math.Atan2(b.Y - p.Y, b.X - p.X);
        return Math.Abs(Math.IEEERemainder(outAngle - inAngle, Math.PI * 2)) * 180 / Math.PI;
    }

    private static bool TooSmall(Rect2D bounds) => bounds.Width < MinSizeMm || bounds.Height < MinSizeMm;

    private static Point2D Offset(Point2D p, double dx, double dy) => new(p.X + dx, p.Y + dy);

    private static Point2D Lerp(Point2D a, Point2D b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    internal static double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double DistanceToSegment(Point2D p, Point2D a, Point2D b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSq = dx * dx + dy * dy;
        if (lengthSq < 1e-18)
            return Distance(p, a);
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq, 0, 1);
        return Distance(p, new Point2D(a.X + t * dx, a.Y + t * dy));
    }
}
