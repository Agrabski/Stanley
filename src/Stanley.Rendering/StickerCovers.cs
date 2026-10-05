using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>
/// One piece of a sticker part, in figure space: its path, the layer it's painted in, and
/// the frame of the body region it sits on (a rigid figure-space transform whose origin
/// and x axis follow the region), which fabrics are laid out in.
/// </summary>
internal sealed record PartPiece(SKPath Path, FigureLayerKind Layer, SKMatrix Frame);

/// <summary>
/// Cover parts (docs/sticker-system.md §4.1): garments generated from the body itself -
/// the region's own capsules, ellipses or torso outline, grown by the part's ease and cut
/// to its from/to range. So they follow every body slider, every pose and every view.
/// </summary>
internal static class StickerCovers
{
    private const float Far = 10; // "past the end" for clipping bands, in figure units

    /// <summary>How much looser than the loosest layer under it a layer in the same slot goes on (a fraction of the character's height): enough to hide that layer's edge.</summary>
    public const double LayerMargin = PartCover.DefaultEase / 2;

    /// <summary>
    /// Automatic layer fit (docs/sticker-system.md §8), at draw time only - never saved: a
    /// cover worn over others in the same slot goes on at least <see cref="LayerMargin"/>
    /// looser than the loosest cover on its region among them (<paramref name="under"/>,
    /// region to ease), so a tight shirt over a loose T-shirt hides it instead of showing a
    /// sliver of it round its edge. The cover itself when nothing is under it, or when it's
    /// loose enough already.
    /// </summary>
    public static PartCover FittedOver(PartCover cover, BodyRegion region, IReadOnlyDictionary<BodyRegion, double>? under) =>
        under is not null && under.TryGetValue(region, out var below) && cover.EaseOrDefault < below + LayerMargin
            ? cover with { Ease = below + LayerMargin }
            : cover;

    /// <summary>The pieces of one cover part on <paramref name="figure"/>; <paramref name="height"/> is the character's height (ease is a fraction of it).</summary>
    public static IEnumerable<PartPiece> Pieces(BodyFigure figure, StickerPart part, PartCover cover, double height)
    {
        var ease = cover.EaseOrDefault * height;
        var (from, to) = (Math.Clamp(Math.Min(cover.From, cover.To), 0, 1), Math.Clamp(Math.Max(cover.From, cover.To), 0, 1));
        var regions = figure.Regions;
        IEnumerable<LimbSide> Sides() => part.Side is { } side ? [side] : [LimbSide.Left, LimbSide.Right];
        FigureLayerKind Layer(LimbSide side = LimbSide.Left) =>
            part.Depth switch
            {
                PartDepth.Back => FigureLayerKind.Back,
                PartDepth.Front => FigureLayerKind.Front,
                _ => figure.LayerOf(part.Region, side)
            };

        switch (part.Region)
        {
            case BodyRegion.Torso:
                yield return Torso(regions.Torso, from, to, ease, Layer());
                break;
            case BodyRegion.Neck:
                // The neck capsule runs chin → base; covers are measured base (0) → chin (1).
                var neck = new BodyCapsule(regions.Neck.To, regions.Neck.From, regions.Neck.ToRadius, regions.Neck.FromRadius).Inflated(ease);
                yield return Segment(neck, from, to, capStart: true, capEnd: false, Layer());
                break;
            case BodyRegion.Arm or BodyRegion.Leg:
                foreach (var side in Sides())
                {
                    var limb = part.Region == BodyRegion.Arm ? regions.Arm(side) : regions.Leg(side);
                    foreach (var piece in Limb(limb, from, to, ease, Layer(side)))
                        yield return piece;
                }
                break;
            case BodyRegion.Hand or BodyRegion.Foot:
                foreach (var side in Sides())
                {
                    var blob = (part.Region == BodyRegion.Hand ? regions.Hand(side) : regions.Foot(side)).Inflated(ease);
                    yield return new PartPiece(FigureGeometry.Oval(blob), Layer(side), EllipseFrame(blob));
                }
                break;
            case BodyRegion.Head:
                yield return Head(regions.Head.Inflated(ease), from, to, Layer());
                break;
            case BodyRegion.Skirt:
                yield return Skirt(regions, from, to, ease, cover.Flare ?? 0, Layer());
                break;
        }
    }

    /// <summary>The torso outline, grown and cut to a band of its height while upright, then bent with the spine.</summary>
    private static PartPiece Torso(TorsoFrame frame, double from, double to, double ease, FigureLayerKind layer)
    {
        var (top, bottom) = (frame.Top, frame.Bottom);
        var y0 = from <= 0 ? top - Far : top + from * (bottom - top);
        var y1 = to >= 1 ? bottom + Far : top + to * (bottom - top);
        using var outline = FigureGeometry.SmoothClosed(frame.Inflated(ease));
        using var band = Rect(-Far, (float)y0, Far, (float)y1);
        using var cut = FigureGeometry.Combine(outline, band, SKPathOp.Intersect);
        var path = PathMapping.Map(cut, frame.ToFigure);
        var middle = (top + bottom) / 2;
        return new PartPiece(path, layer, Frame(frame.ToFigure(new Point2D(0, middle)), frame.Bend.AngleAt(middle)));
    }

    /// <summary>
    /// An arm or leg from <paramref name="from"/> to <paramref name="to"/> of the way down it:
    /// each segment it reaches, round where it joins the next, cut square at the ends. Its
    /// fabric runs on unbroken from the shoulder or hip (#14): the lower segment's frame
    /// carries on from the upper one's, and where the two overlap at the elbow or knee each
    /// side of the joint's bisector wears its own segment's fabric, as a sleeve creases.
    /// </summary>
    private static IEnumerable<PartPiece> Limb(LimbFrame limb, double from, double to, double ease, FigureLayerKind layer)
    {
        var length = limb.Length;
        if (length <= 1e-9)
            yield break;
        var (a, b) = (from * length, to * length);
        var upper = limb.UpperLength;
        var top = a < upper ? SegmentAlong(limb.Upper.Inflated(ease), a, Math.Min(b, upper), capStart: a <= 0, capEnd: b > upper, layer) : null;
        var bottom = b > upper ? SegmentAlong(limb.Lower.Inflated(ease), Math.Max(a, upper) - upper, b - upper, capStart: a < upper, capEnd: false, layer, offset: upper) : null;
        if (top is not null)
            yield return top;
        if (bottom is not null)
            yield return top is null ? bottom : CutAtJoint(bottom, top, limb);
    }

    /// <summary>
    /// The lower segment's piece without the part of the upper one's on the upper side of the
    /// joint's bisector. Drawn over the whole upper piece, it covers the upper side's fabric
    /// past the bisector and leaves it before - so the two meet along the crease, with no
    /// seam between them, and still cover exactly what they did.
    /// </summary>
    private static PartPiece CutAtJoint(PartPiece lower, PartPiece upper, LimbFrame limb)
    {
        var (ux, uy) = Direction(limb.Upper.From, limb.Upper.To);
        var (lx, ly) = Direction(limb.Lower.From, limb.Lower.To);
        // The bisector's normal points down the limb, into the lower segment's side.
        var (nx, ny) = (ux + lx, uy + ly);
        if (nx * nx + ny * ny < 1e-12)
            (nx, ny) = (-uy, ux); // folded flat: either way along the arm splits it
        using var half = Rect(-Far, -Far, 0, Far);
        using var upperSide = FigureGeometry.Transformed(half, Frame(limb.Upper.To, Math.Atan2(ny, nx) * 180 / Math.PI));
        using var upperPart = FigureGeometry.Combine(upper.Path, upperSide, SKPathOp.Intersect);
        var cut = lower with { Path = FigureGeometry.Combine(lower.Path, upperPart, SKPathOp.Difference) };
        lower.Path.Dispose();
        return cut;
    }

    private static (double X, double Y) Direction(Point2D from, Point2D to)
    {
        var (dx, dy) = (to.X - from.X, to.Y - from.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length < 1e-12 ? (0, 1) : (dx / length, dy / length);
    }

    /// <summary>A capsule cut to <paramref name="t0"/>-<paramref name="t1"/> (0-1) of its length.</summary>
    private static PartPiece Segment(BodyCapsule capsule, double t0, double t1, bool capStart, bool capEnd, FigureLayerKind layer)
    {
        var length = Math.Sqrt((capsule.To.X - capsule.From.X) * (capsule.To.X - capsule.From.X) + (capsule.To.Y - capsule.From.Y) * (capsule.To.Y - capsule.From.Y));
        return SegmentAlong(capsule, t0 * length, t1 * length, capStart && t0 <= 0, capEnd && t1 >= 1, layer);
    }

    /// <summary>A capsule cut square at distances <paramref name="start"/> and <paramref name="end"/> along it, or left round at an end that isn't cut; its fabric frame starts <paramref name="offset"/> before it (<see cref="SegmentFrame"/>).</summary>
    private static PartPiece SegmentAlong(BodyCapsule capsule, double start, double end, bool capStart, bool capEnd, FigureLayerKind layer, double offset = 0)
    {
        var along = AngleOf(capsule.From, capsule.To);
        var frame = Frame(capsule.From, along);
        using var shape = FigureGeometry.Capsule(capsule);
        using var localBand = Rect(capStart ? -Far : (float)start, -Far, capEnd ? Far : (float)end, Far);
        using var band = FigureGeometry.Transformed(localBand, frame);
        return new PartPiece(FigureGeometry.Combine(shape, band, SKPathOp.Intersect), layer, SegmentFrame(capsule, offset));
    }

    /// <summary>
    /// The frame fabrics lie in on a segment: "down" along it, as on the torso, so stripes go
    /// round a sleeve; its origin <paramref name="offset"/> back up the segment's line from
    /// its start. A forearm or shin measured from the shoulder or hip (offset = the upper
    /// segment's length) carries on the pattern above it instead of starting it again.
    /// </summary>
    internal static SKMatrix SegmentFrame(BodyCapsule segment, double offset = 0) =>
        SKMatrix.CreateTranslation(0, -(float)offset).PostConcat(Frame(segment.From, AngleOf(segment.From, segment.To) - 90));

    /// <summary>A band of the (grown) head, from the crown (0) to the chin (1), turning with the head.</summary>
    private static PartPiece Head(BodyEllipse head, double from, double to, FigureLayerKind layer)
    {
        var frame = EllipseFrame(head);
        using var local = FigureGeometry.Oval(head with { Center = new Point2D(0, 0), RotationDegrees = 0 });
        var y0 = from <= 0 ? -Far : (float)(-head.RadiusY + from * 2 * head.RadiusY);
        var y1 = to >= 1 ? Far : (float)(-head.RadiusY + to * 2 * head.RadiusY);
        using var band = Rect(-Far, y0, Far, y1);
        using var cut = FigureGeometry.Combine(local, band, SKPathOp.Intersect);
        return new PartPiece(FigureGeometry.Transformed(cut, frame), layer, frame);
    }

    /// <summary>
    /// The shape a skirt hangs in: the hull of the hips and both (grown) legs down to
    /// <paramref name="to"/> of their length, widened towards the hem by
    /// <paramref name="flare"/>. Walking spreads it with the legs; sitting lays it over the thighs.
    /// </summary>
    private static PartPiece Skirt(FigureRegions regions, double from, double to, double ease, double flare, FigureLayerKind layer)
    {
        var torso = regions.Torso;
        var hipY = torso.Top + (torso.Bottom - torso.Top) * (0.72 + 0.28 * from);
        var (left, right) = torso.RowAt(hipY);
        var points = new List<Point2D>
        {
            torso.ToFigure(new Point2D(left - ease, hipY)),
            torso.ToFigure(new Point2D(right + ease, hipY)),
        };
        var hem = new List<Point2D>();
        foreach (var leg in new[] { regions.LeftLeg, regions.RightLeg })
        {
            for (var t = 0.0; t <= to + 1e-9; t += Math.Max(to / 8, 0.01))
            {
                var (at, segment, s) = leg.At(t);
                var radius = segment.FromRadius + (segment.ToRadius - segment.FromRadius) * s + ease;
                var angle = AngleOf(segment.From, segment.To) * Math.PI / 180;
                var (nx, ny) = (-Math.Sin(angle) * radius, Math.Cos(angle) * radius);
                points.Add(new Point2D(at.X + nx, at.Y + ny));
                points.Add(new Point2D(at.X - nx, at.Y - ny));
            }
            hem.Add(leg.At(to).Point);
        }
        var hull = ConvexHull(points);
        if (flare != 0 && hull.Count >= 3)
        {
            var (top, bottom) = (hull.Min(p => p.Y), hull.Max(p => p.Y));
            var centre = (hem[0].X + hem[1].X) / 2;
            hull = hull.Select(p => new Point2D(centre + (p.X - centre) * (1 + flare * (p.Y - top) / Math.Max(bottom - top, 1e-9)), p.Y)).ToList();
        }
        var anchor = torso.ToFigure(new Point2D((left + right) / 2, hipY));
        return new PartPiece(FigureGeometry.SmoothClosed(Soften(hull)), layer, Frame(anchor, 0));
    }

    /// <summary>A hull's corners doubled up, so drawing it smoothed keeps the corners' positions and just rounds them a little.</summary>
    private static List<Point2D> Soften(List<Point2D> hull)
    {
        var result = new List<Point2D>(hull.Count * 2);
        for (var i = 0; i < hull.Count; i++)
        {
            var (a, b) = (hull[i], hull[(i + 1) % hull.Count]);
            result.Add(new Point2D(a.X + (b.X - a.X) * 0.1, a.Y + (b.Y - a.Y) * 0.1));
            result.Add(new Point2D(a.X + (b.X - a.X) * 0.9, a.Y + (b.Y - a.Y) * 0.9));
        }
        return result;
    }

    private static List<Point2D> ConvexHull(List<Point2D> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3)
            return sorted;
        static double Cross(Point2D o, Point2D a, Point2D b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<Point2D>();
        foreach (var pass in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            var start = hull.Count;
            foreach (var p in pass)
            {
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }
        return hull;
    }

    internal static SKMatrix Frame(Point2D origin, double degrees) =>
        SKMatrix.CreateRotationDegrees((float)degrees).PostConcat(SKMatrix.CreateTranslation((float)origin.X, (float)origin.Y));

    internal static SKMatrix EllipseFrame(BodyEllipse e) => Frame(e.Center, e.RotationDegrees);

    private static double AngleOf(Point2D from, Point2D to) => Math.Atan2(to.Y - from.Y, to.X - from.X) * 180 / Math.PI;

    private static SKPath Rect(float left, float top, float right, float bottom)
    {
        using var builder = new SKPathBuilder();
        builder.AddRect(new SKRect(left, top, right, bottom));
        return builder.Detach();
    }
}
