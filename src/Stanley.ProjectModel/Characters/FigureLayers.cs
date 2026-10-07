using System.Text.Json.Serialization;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Characters;

/// <summary>A named piece of the generated body that sticker parts attach to (docs/sticker-system.md §4.1).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<BodyRegion>))]
public enum BodyRegion
{
    /// <summary>The head ellipse; turns with the head.</summary>
    Head,

    /// <summary>From the base of the neck (0) to the chin (1).</summary>
    Neck,

    /// <summary>From the top of the torso outline (0) to the crotch (1), row by row.</summary>
    Torso,

    /// <summary>Shoulder (0) to wrist (1), through the elbow.</summary>
    Arm,

    Hand,

    /// <summary>Hip (0) to ankle (1), through the knee.</summary>
    Leg,

    Foot,

    /// <summary>The space around both legs, from the hips (0) to the ankles (1): skirts, dresses, coat tails.</summary>
    Skirt
}

/// <summary>The character's own left or right (as in VRM), for limb regions. A mirrored placement doesn't swap them.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<LimbSide>))]
public enum LimbSide
{
    Left,
    Right
}

/// <summary>
/// The layers a figure is painted in. A front view uses Back, LeftLeg, RightLeg, Torso,
/// Head, LeftArm, RightArm, Front - each limb its own layer, so one crossing the other
/// keeps its outline instead of merging with it; a side view Back, FarArm, Body (torso,
/// neck and both legs), Head, NearFoot, NearArm, Front. Back and Front hold no body - only
/// sticker parts that ask for them.
/// </summary>
public enum FigureLayerKind
{
    Back,
    FarArm,
    LeftLeg,
    RightLeg,
    Body,
    Torso,
    Head,
    NearFoot,
    LeftArm,
    RightArm,
    NearArm,
    Front
}

/// <summary>
/// One depth layer of a figure: its skin shapes (drawn filled, then inked around their
/// union) and its <paramref name="Seams"/> - discs at the joints where it meets the layers
/// painted before it (shoulders, hips, neck). Inside a seam, this layer's ink is left out
/// wherever it lies over what's already painted, so joints stay seamless while an arm
/// crossing the chest still gets its outline.
/// </summary>
/// <param name="Torso">The torso outline (drawn smoothed), for the one layer that holds it.</param>
public sealed record FigureLayer(
    FigureLayerKind Kind,
    IReadOnlyList<Point2D>? Torso,
    IReadOnlyList<BodyCapsule> Capsules,
    IReadOnlyList<BodyEllipse> Ellipses,
    IReadOnlyList<BodyEllipse> Seams)
{
    public static FigureLayer Empty(FigureLayerKind kind) => new(kind, null, [], [], []);

    public bool HasBody => Torso is { Count: > 0 } || Capsules.Count > 0 || Ellipses.Count > 0;
}

/// <summary>An arm or leg as drawn: the upper segment (upper arm, thigh) and the lower one (forearm, shin).</summary>
public sealed record LimbFrame(BodyCapsule Upper, BodyCapsule Lower)
{
    public double UpperLength => Distance(Upper.From, Upper.To);

    public double LowerLength => Distance(Lower.From, Lower.To);

    public double Length => UpperLength + LowerLength;

    /// <summary>The point <paramref name="t"/> of the way along the limb (0 = shoulder/hip, 1 = wrist/ankle), and the segment it's on.</summary>
    public (Point2D Point, BodyCapsule Segment, double SegmentT) At(double t)
    {
        var length = Length;
        var along = Math.Clamp(t, 0, 1) * length;
        if (length <= 0 || along <= UpperLength)
        {
            var s = UpperLength <= 0 ? 0 : along / UpperLength;
            return (Lerp(Upper.From, Upper.To, s), Upper, s);
        }
        var l = LowerLength <= 0 ? 0 : (along - UpperLength) / LowerLength;
        return (Lerp(Lower.From, Lower.To, l), Lower, l);
    }

    internal static double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    internal static Point2D Lerp(Point2D a, Point2D b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}

/// <summary>
/// A rigid move of one spine segment: a point <c>p</c> of it goes to
/// <c>To + rotate(p - From, Degrees)</c> (degrees clockwise on the page).
/// </summary>
public sealed record SegmentTransform(Point2D From, Point2D To, double Degrees)
{
    public static SegmentTransform Identity(Point2D at) => new(at, at, 0);

    public Point2D Apply(Point2D p)
    {
        var r = Degrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(r), Math.Sin(r));
        var (dx, dy) = (p.X - From.X, p.Y - From.Y);
        return new Point2D(To.X + dx * cos - dy * sin, To.Y + dx * sin + dy * cos);
    }
}

/// <summary>
/// How the posed spine bends the upright upper body. <see cref="KnotHeights"/> are the
/// heights (upright, y down, bottom first) of the hips, spine, chest and neck joints;
/// <see cref="Transforms"/> are the moves of the segment that ends at each knot (the
/// pelvis doesn't move). A point between two knots is moved by a blend of the two - so
/// the torso's outline curves through the bend instead of pivoting like a rigid board -
/// and then everything is shifted with the hips.
/// </summary>
public sealed record TrunkBend(IReadOnlyList<double> KnotHeights, IReadOnlyList<SegmentTransform> Transforms, Point2D Shift)
{
    /// <summary>Where a point of the upright upper body goes.</summary>
    public Point2D Map(Point2D rest)
    {
        var (i, t) = Segment(rest.Y);
        var a = Transforms[i].Apply(rest);
        var b = t > 0 ? Transforms[i + 1].Apply(rest) : a;
        return new Point2D(a.X + (b.X - a.X) * t + Shift.X, a.Y + (b.Y - a.Y) * t + Shift.Y);
    }

    /// <summary>How far the body is turned at upright height <paramref name="y"/> (degrees clockwise) - what an arm attached there turns by.</summary>
    public double AngleAt(double y)
    {
        var (i, t) = Segment(y);
        var a = Transforms[i].Degrees;
        return t > 0 ? a + (Transforms[i + 1].Degrees - a) * t : a;
    }

    /// <summary>The lower knot of the stretch height <paramref name="y"/> is in, and how far up towards the next it is (0-1).</summary>
    private (int Index, double T) Segment(double y)
    {
        if (y >= KnotHeights[0])
            return (0, 0);
        for (var i = 0; i + 1 < KnotHeights.Count; i++)
        {
            var (low, high) = (KnotHeights[i], KnotHeights[i + 1]);
            if (y >= high)
                return (i, low - high < 1e-12 ? 1 : (low - y) / (low - high));
        }
        return (KnotHeights.Count - 1, 0);
    }
}

/// <summary>
/// The torso as the rig built it: its outline standing upright (<see cref="RestOutline"/>)
/// and the spine's bend (<see cref="Bend"/>) that poses it. Sticker art is warped row by
/// row through the upright outline and then bent the same way, so a print curves with
/// the back.
/// </summary>
public sealed record TorsoFrame(IReadOnlyList<Point2D> RestOutline, TrunkBend Bend)
{
    /// <summary>The top of the upright outline (the base of the neck).</summary>
    public double Top => RestOutline.Min(p => p.Y);

    /// <summary>The bottom of the upright outline (the crotch).</summary>
    public double Bottom => RestOutline.Max(p => p.Y);

    /// <summary>A point of the upright torso, posed.</summary>
    public Point2D ToFigure(Point2D rest) => Bend.Map(rest);

    /// <summary>
    /// The upright outline's left and right edge at height <paramref name="y"/> (clamped to
    /// the outline's own height range): the straight polygon, not the smoothed curve drawn
    /// through it, which is close enough to map art through.
    /// </summary>
    public (double Left, double Right) RowAt(double y)
    {
        var (top, bottom) = (Top, Bottom);
        y = Math.Clamp(y, top + 1e-9, bottom - 1e-9);
        double left = double.MaxValue, right = double.MinValue;
        for (var i = 0; i < RestOutline.Count; i++)
        {
            var a = RestOutline[i];
            var b = RestOutline[(i + 1) % RestOutline.Count];
            // Half-open, so a corner exactly at y counts once.
            if ((a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y))
            {
                var x = a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y);
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        }
        return left <= right ? (left, right) : (0, 0);
    }

    /// <summary>The upright outline grown outwards by <paramref name="by"/> (along each corner's normal) - a garment's ease.</summary>
    public IReadOnlyList<Point2D> Inflated(double by)
    {
        if (by == 0 || RestOutline.Count < 3)
            return RestOutline;
        // Outward = away from the inside, whichever way round the outline runs.
        double area = 0;
        for (var i = 0; i < RestOutline.Count; i++)
        {
            var (a, b) = (RestOutline[i], RestOutline[(i + 1) % RestOutline.Count]);
            area += a.X * b.Y - b.X * a.Y;
        }
        var outward = area > 0 ? -1.0 : 1.0;
        var result = new List<Point2D>(RestOutline.Count);
        for (var i = 0; i < RestOutline.Count; i++)
        {
            var prev = RestOutline[(i - 1 + RestOutline.Count) % RestOutline.Count];
            var p = RestOutline[i];
            var next = RestOutline[(i + 1) % RestOutline.Count];
            var n1 = EdgeNormal(prev, p);
            var n2 = EdgeNormal(p, next);
            var (nx, ny) = (n1.X + n2.X, n1.Y + n2.Y);
            var length = Math.Sqrt(nx * nx + ny * ny);
            if (length < 1e-9)
                (nx, ny, length) = (n1.X, n1.Y, 1);
            result.Add(new Point2D(p.X + outward * by * nx / length, p.Y + outward * by * ny / length));
        }
        return result;
    }

    private static Point2D EdgeNormal(Point2D a, Point2D b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length < 1e-12 ? new Point2D(0, 0) : new Point2D(-dy / length, dx / length);
    }
}

/// <summary>
/// Where each region of a posed figure is (figure space), for attaching sticker parts:
/// the head, neck and torso, and each arm, hand, leg and foot by the character's own side.
/// </summary>
public sealed record FigureRegions(
    BodyEllipse Head,
    BodyCapsule Neck,
    TorsoFrame Torso,
    LimbFrame LeftArm,
    LimbFrame RightArm,
    BodyEllipse LeftHand,
    BodyEllipse RightHand,
    LimbFrame LeftLeg,
    LimbFrame RightLeg,
    BodyEllipse LeftFoot,
    BodyEllipse RightFoot)
{
    public LimbFrame Arm(LimbSide side) => side == LimbSide.Left ? LeftArm : RightArm;

    public LimbFrame Leg(LimbSide side) => side == LimbSide.Left ? LeftLeg : RightLeg;

    public BodyEllipse Hand(LimbSide side) => side == LimbSide.Left ? LeftHand : RightHand;

    public BodyEllipse Foot(LimbSide side) => side == LimbSide.Left ? LeftFoot : RightFoot;
}

public static class BodyShapes
{
    /// <summary>The capsule with both radii grown by <paramref name="by"/>.</summary>
    public static BodyCapsule Inflated(this BodyCapsule c, double by) =>
        by == 0 ? c : c with { FromRadius = c.FromRadius + by, ToRadius = c.ToRadius + by };

    /// <summary>The ellipse with both radii grown by <paramref name="by"/>.</summary>
    public static BodyEllipse Inflated(this BodyEllipse e, double by) =>
        by == 0 ? e : e with { RadiusX = e.RadiusX + by, RadiusY = e.RadiusY + by };
}
