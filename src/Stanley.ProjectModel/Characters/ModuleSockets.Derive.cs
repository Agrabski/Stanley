using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// Where a shape's sockets are when nobody has placed them by hand (docs/shape-characters.md
/// §4.2): read off the outline, so any shape - a library one or one drawn yourself - works
/// with no setup. Everything here is in the shape's own unit box, y down; a module's
/// <see cref="ModuleFrame"/> takes it to figure space. Left and right are the character's
/// own, as everywhere in the rig: the left shoulder and hip are on the box's right (+x),
/// the side that's the character's left seen from the front.
/// </summary>
public static partial class ModuleSockets
{
    /// <summary>How far down the outline the shoulders sit, as a fraction of its height.</summary>
    public const double ShoulderDrop = 0.15;

    /// <summary>How far across the outline the right hip sits (the left one mirrored: 1 minus this).</summary>
    public const double HipSpread = 0.3;

    /// <summary>The share of the outline, from its top, a torso's face is fitted into.</summary>
    public const double FaceBand = 2.0 / 3;

    /// <summary>
    /// Every socket in <see cref="All"/>: the hand-set ones from <see cref="ModuleShape.Sockets"/>
    /// (finite ones, by name) and the rest derived from the outline - <see cref="Neck"/> the top
    /// of the outline on its centre line; the shoulders its left and right edge
    /// <see cref="ShoulderDrop"/> down from its top; the hips its bottom edge
    /// <see cref="HipSpread"/> in from either side; <see cref="Face"/> the centre of the largest
    /// ellipse (of the outline's proportions) inside its upper <see cref="FaceBand"/>. A
    /// shoulder's inset by the arm's thickness is the rig's to add, not this.
    /// </summary>
    public static IReadOnlyDictionary<string, Point2D> Derive(ModuleShape shape) => Measured(shape).Sockets;

    /// <summary>Whether <paramref name="socket"/> was placed by hand (and so isn't moved by the rig, nor inset).</summary>
    public static bool IsHandSet(ModuleShape shape, string socket) =>
        shape.Sockets is { } sockets && sockets.TryGetValue(socket, out var p) && double.IsFinite(p.X) && double.IsFinite(p.Y);

    /// <summary>
    /// The face area, unit box: the largest ellipse with the outline's own proportions that fits
    /// inside it - inside its upper <see cref="FaceBand"/> only when <paramref name="upperBand"/>
    /// (a torso carrying the face; a head's face is the whole head, as the mannequin's is). A
    /// hand-set <see cref="Face"/> socket fixes its centre, and it grows as big as fits there.
    /// </summary>
    public static (Point2D Center, double RadiusX, double RadiusY) FaceEllipse(ModuleShape shape, bool upperBand = true) =>
        upperBand ? Measured(shape).BandFace : Measured(shape).WholeFace;

    /// <summary>The bottom of the outline on its centre line - where a head meets the neck socket below it.</summary>
    public static Point2D Chin(ModuleShape shape) => Measured(shape).Chin;

    /// <summary>
    /// The outline (and its hand-set sockets) mirrored within the unit box: <paramref name="x"/>
    /// left to right, <paramref name="y"/> upside down. Hand-set sockets stay on the spot of the
    /// outline they were put on; derived ones are derived again from the flipped outline, so an
    /// upside-down triangle's neck is its wide top edge and its shoulders the corners there.
    /// The same instance comes back for the same flip, so what's derived from it is worked out once.
    /// </summary>
    public static ModuleShape Flipped(ModuleShape shape, bool x, bool y)
    {
        if (!x && !y)
            return shape;
        var flips = FlipCache.GetValue(shape, _ => new ModuleShape?[3]);
        var index = x && y ? 2 : x ? 0 : 1;
        if (flips[index] is { } cached)
            return cached;
        Point2D F(Point2D p) => new(x ? 1 - p.X : p.X, y ? 1 - p.Y : p.Y);
        // A single flip reverses the ring's direction; reverse it back (swapping handles) so it runs the same way round.
        var anchors = shape.Anchors.Select(a => a with { Point = F(a.Point), InHandle = F(a.InHandle), OutHandle = F(a.OutHandle) }).ToList();
        if (x != y)
        {
            anchors.Reverse();
            anchors = anchors.Select(a => a with { InHandle = a.OutHandle, OutHandle = a.InHandle }).ToList();
        }
        SortedDictionary<string, Point2D>? sockets = null;
        if (shape.Sockets is { } own)
        {
            sockets = new SortedDictionary<string, Point2D>(StringComparer.Ordinal);
            foreach (var (name, p) in own)
                sockets[x ? MirrorName(name) : name] = F(p);
        }
        return flips[index] = shape with { Anchors = anchors, Sockets = sockets };
    }

    /// <summary>A left socket's right twin and back; others unchanged - a mirrored shape's left shoulder is the one that was its right.</summary>
    private static string MirrorName(string socket) => socket switch
    {
        LeftShoulder => RightShoulder,
        RightShoulder => LeftShoulder,
        LeftHip => RightHip,
        RightHip => LeftHip,
        _ => socket
    };

    // ---------------------------------------------------------------- measuring an outline

    private static readonly ConditionalWeakTable<ModuleShape, Measurement> MeasureCache = new();
    private static readonly ConditionalWeakTable<ModuleShape, ModuleShape?[]> FlipCache = new();

    private sealed record Measurement(
        IReadOnlyDictionary<string, Point2D> Sockets,
        (Point2D Center, double RadiusX, double RadiusY) BandFace,
        (Point2D Center, double RadiusX, double RadiusY) WholeFace,
        Point2D Chin);

    /// <summary>Everything derived from one shape, worked out once per instance (the rig asks for it on every build, and posing builds many times a drag).</summary>
    private static Measurement Measured(ModuleShape shape) => MeasureCache.GetValue(shape, Measure);

    private static Measurement Measure(ModuleShape shape)
    {
        var polygon = IsUsable(shape.Anchors) ? Flatten(shape.Anchors) : [];
        var box = polygon.Count >= 3 ? AnchorRing.CurveBounds(shape.Anchors) : new Rect2D(0, 0, 1, 1);
        if (!(box.Width > 1e-9 && box.Height > 1e-9))
        {
            polygon = [];
            box = new Rect2D(0, 0, 1, 1);
        }

        var hasOutline = polygon.Count >= 3;
        double? Column(double x, bool top) => hasOutline ? VerticalCrossing(polygon, x, top) : null;
        (double Left, double Right)? Row(double y) => hasOutline ? HorizontalCrossings(polygon, y) : null;

        var neck = new Point2D(box.MidX, Column(box.MidX, top: true) ?? box.Top);
        var chin = new Point2D(box.MidX, Column(box.MidX, top: false) ?? box.Bottom);
        var shoulderY = box.Top + box.Height * ShoulderDrop;
        var row = Row(shoulderY) ?? (box.Left, box.Right);
        var rightHipX = box.Left + box.Width * HipSpread;
        var leftHipX = box.Right - box.Width * HipSpread;

        var handFace = HandSet(shape, Face);
        var bandFace = Inscribed(polygon, box, box.Top + box.Height * FaceBand, handFace);
        var wholeFace = Inscribed(polygon, box, box.Bottom, handFace);

        var sockets = new SortedDictionary<string, Point2D>(StringComparer.Ordinal)
        {
            [Neck] = neck,
            [LeftShoulder] = new(row.Right, shoulderY),
            [RightShoulder] = new(row.Left, shoulderY),
            [LeftHip] = new(leftHipX, Column(leftHipX, top: false) ?? box.Bottom),
            [RightHip] = new(rightHipX, Column(rightHipX, top: false) ?? box.Bottom),
            [Face] = bandFace.Center,
        };
        foreach (var name in All)
            if (HandSet(shape, name) is { } p)
                sockets[name] = p;
        return new Measurement(new ReadOnlyDictionary<string, Point2D>(sockets), bandFace, wholeFace, chin);
    }

    private static Point2D? HandSet(ModuleShape shape, string socket) =>
        IsHandSet(shape, socket) ? shape.Sockets![socket] : null;

    /// <summary>An outline worth reading: three anchors or more, every number finite.</summary>
    internal static bool IsUsable(IReadOnlyList<ShapeAnchor>? anchors) =>
        anchors is { Count: >= 3 } && anchors.All(a => a is not null && Finite(a.Point) && Finite(a.InHandle) && Finite(a.OutHandle));

    private static bool Finite(Point2D p) => double.IsFinite(p.X) && double.IsFinite(p.Y);

    /// <summary>The outline as a polygon: a few points along every edge's curve.</summary>
    internal static IReadOnlyList<Point2D> Flatten(IReadOnlyList<ShapeAnchor> anchors, int samplesPerEdge = 0)
    {
        var n = anchors.Count;
        var per = samplesPerEdge > 0 ? samplesPerEdge : Math.Clamp(96 / n, 4, 16);
        var points = new List<Point2D>(n * per);
        for (var i = 0; i < n * per; i++)
            points.Add(AnchorRing.PointAt(anchors, (double)i / (n * per)));
        return points;
    }

    /// <summary>Where the vertical line at <paramref name="x"/> first meets the polygon from the top (or bottom), if it does.</summary>
    private static double? VerticalCrossing(IReadOnlyList<Point2D> polygon, double x, bool top)
    {
        double? best = null;
        for (var i = 0; i < polygon.Count; i++)
        {
            var (a, b) = (polygon[i], polygon[(i + 1) % polygon.Count]);
            if ((a.X <= x && x < b.X) || (b.X <= x && x < a.X))
            {
                var y = a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X);
                best = best is not { } current ? y : top ? Math.Min(current, y) : Math.Max(current, y);
            }
        }
        return best;
    }

    /// <summary>The polygon's leftmost and rightmost edge on the row at <paramref name="y"/>, if it reaches that row.</summary>
    private static (double Left, double Right)? HorizontalCrossings(IReadOnlyList<Point2D> polygon, double y)
    {
        double left = double.MaxValue, right = double.MinValue;
        for (var i = 0; i < polygon.Count; i++)
        {
            var (a, b) = (polygon[i], polygon[(i + 1) % polygon.Count]);
            if ((a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y))
            {
                var x = a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y);
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        }
        return left <= right ? (left, right) : null;
    }

    /// <summary>
    /// The largest ellipse with <paramref name="box"/>'s proportions inside the polygon and above
    /// <paramref name="bandBottom"/>: in the box scaled to a square it's a circle, so this is the
    /// polygon's largest inscribed circle there - found by a coarse grid of centres, then two
    /// finer grids round the best. <paramref name="fixedCenter"/> (a hand-set face) skips the search.
    /// </summary>
    private static (Point2D Center, double RadiusX, double RadiusY) Inscribed(IReadOnlyList<Point2D> polygon, Rect2D box, double bandBottom, Point2D? fixedCenter)
    {
        var small = 0.02;
        if (polygon.Count < 3)
        {
            var c = fixedCenter ?? new Point2D(box.MidX, box.Top + (bandBottom - box.Top) / 2);
            return (c, box.Width * 0.4, (bandBottom - box.Top) * 0.4);
        }
        var square = polygon.Select(p => new Point2D((p.X - box.Left) / box.Width, (p.Y - box.Top) / box.Height)).ToList();
        var band = (bandBottom - box.Top) / box.Height;
        double Fit(double x, double y)
        {
            if (y >= band || !Inside(square, x, y))
                return -1;
            var r = band - y;
            for (var i = 0; i < square.Count; i++)
                r = Math.Min(r, SegmentDistance(square[i], square[(i + 1) % square.Count], x, y));
            return r;
        }
        Point2D Unsquare(double x, double y) => new(box.Left + x * box.Width, box.Top + y * box.Height);

        if (fixedCenter is { } at)
        {
            var (fx, fy) = ((at.X - box.Left) / box.Width, (at.Y - box.Top) / box.Height);
            var fit = Math.Max(Fit(fx, fy), small);
            return (at, fit * box.Width, fit * box.Height);
        }

        const int coarse = 16;
        var (bestX, bestY, bestR) = (0.5, band / 2, -1.0);
        for (var i = 0; i < coarse; i++)
            for (var j = 0; j < coarse; j++)
            {
                var (x, y) = ((i + 0.5) / coarse, (j + 0.5) / coarse * band);
                var r = Fit(x, y);
                if (r > bestR)
                    (bestX, bestY, bestR) = (x, y, r);
            }
        if (bestR <= 0)
            return (Unsquare(0.5, band / 2), small * box.Width, small * box.Height);
        var step = 1.0 / coarse;
        for (var round = 0; round < 3; round++)
        {
            var (cx, cy) = (bestX, bestY);
            for (var i = -3; i <= 3; i++)
                for (var j = -3; j <= 3; j++)
                {
                    var (x, y) = (cx + i * step / 3, cy + j * step / 3);
                    var r = Fit(x, y);
                    if (r > bestR)
                        (bestX, bestY, bestR) = (x, y, r);
                }
            step /= 3;
        }
        return (Unsquare(bestX, bestY), bestR * box.Width, bestR * box.Height);
    }

    private static bool Inside(IReadOnlyList<Point2D> polygon, double x, double y)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var (a, b) = (polygon[i], polygon[(i + 1) % polygon.Count]);
            if ((a.Y > y) != (b.Y > y) && x < a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y))
                inside = !inside;
        }
        return inside;
    }

    private static double SegmentDistance(Point2D a, Point2D b, double x, double y)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var lengthSq = dx * dx + dy * dy;
        var t = lengthSq < 1e-18 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / lengthSq, 0, 1);
        var (px, py) = (a.X + t * dx - x, a.Y + t * dy - y);
        return Math.Sqrt(px * px + py * py);
    }
}
