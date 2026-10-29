using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>
/// A part's shape from an SVG file (docs/shape-characters.md §9.3): the file is read through
/// the one SVG adapter (<see cref="StickerSvg"/>), and its <b>first closed path</b>, in paint
/// order, becomes the outline - cubics as they are, quadratics and conics raised to cubics, lines
/// as corners (arcs reach here as curves already) - fitted into the unit box
/// (<see cref="ModuleOutlines.Fit"/>). Open paths and any further shapes are reported, as
/// sticker import reports what it doesn't draw.
/// </summary>
/// <remarks>
/// Why the first closed path rather than the union of them all: a module is one ring, and a
/// union can come out with holes or as separate islands, neither of which a ring can be; Skia's
/// path union also re-cuts every curve into many small pieces, so the result would lose the
/// points the user drew and be a poor start for Edit Points. The first path is predictable,
/// keeps the drawing's own points, and the report says what was left out.
/// </remarks>
public static class ShapeOutlineImport
{
    /// <summary>The outline read (null if none could be), anything in the file it leaves out, and why it couldn't, if it couldn't.</summary>
    public sealed record Result(FittedOutline? Outline, IReadOnlyList<string> Report, string? Error);

    /// <summary>Points closer than this (in the file's units) are one point: a path closed by a line back to its start, a zero-length piece.</summary>
    private const double SamePoint = 1e-3;

    /// <summary>The outline of <paramref name="file"/>'s first closed path, fitted to fill the unit box.</summary>
    public static Result FromFile(ArtFile file)
    {
        if (!file.IsSvg)
            return new Result(null, [], "a shape has to be an SVG drawing, not a picture");
        if (StickerSvg.Parse(file) is not { } art)
            return new Result(null, [], "not an SVG file Stanley can read");
        var elements = art.Part(ParsedArt.WholeFile);
        var result = FromPaths(elements.Select(e => e.Path));
        return result with { Report = [.. result.Report, .. art.Report] };
    }

    /// <summary>The first closed contour of <paramref name="paths"/> (in order), fitted to fill the unit box, and a report of what else there was.</summary>
    public static Result FromPaths(IEnumerable<SKPath> paths)
    {
        var closed = new List<IReadOnlyList<ShapeAnchor>>();
        var open = 0;
        string? previous = null;
        foreach (var path in paths)
        {
            // A filled and stroked path reaches here twice, once for each: count it once.
            var key = Key(path);
            if (key == previous)
                continue;
            previous = key;
            var (c, o) = Contours(path);
            closed.AddRange(c);
            open += o;
        }
        var report = new List<string>();
        if (open > 0)
            report.Add(open == 1 ? "1 open path ignored" : $"{open} open paths ignored");
        if (closed.Count == 0)
            return new Result(null, report, "there's no closed shape in it - draw the outline as one closed path");
        if (closed.Count > 1)
            report.Add(closed.Count == 2 ? "1 more shape ignored (only the first is used)" : $"{closed.Count - 1} more shapes ignored (only the first is used)");
        if (ModuleOutlines.Fit(closed[0]) is not { } outline)
            return new Result(null, report, "its first shape has no width or height");
        return new Result(outline, report, null);
    }

    /// <summary>
    /// The contours of <paramref name="path"/> as anchor rings (in the path's own units): the
    /// closed ones, each with at least three points, and how many open ones there were.
    /// </summary>
    public static (IReadOnlyList<IReadOnlyList<ShapeAnchor>> Closed, int Open) Contours(SKPath path)
    {
        var closed = new List<IReadOnlyList<ShapeAnchor>>();
        var open = 0;
        var ring = new List<ShapeAnchor>();

        void EndContour(bool isClosed)
        {
            if (ring.Count >= 2 || ring.Count == 1 && isClosed)
            {
                if (isClosed && Closed(ring) is { } done)
                    closed.Add(done);
                else if (!isClosed)
                    open++;
            }
            ring = [];
        }

        void CurveTo(Point2D c1, Point2D c2, Point2D to)
        {
            var last = ring[^1];
            if (Near(last.Point, to) && Near(last.Point, c1) && Near(last.Point, c2))
                return; // a zero-length piece
            ring[^1] = last with { OutHandle = c1 };
            ring.Add(new ShapeAnchor(to, c2, to, AnchorHandleKind.Corner));
        }

        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    EndContour(isClosed: false);
                    ring.Add(AnchorRing.Corner(P(points[0])));
                    break;
                case SKPathVerb.Line when ring.Count > 0:
                    CurveTo(P(points[0]), P(points[1]), P(points[1]));
                    break;
                case SKPathVerb.Quad when ring.Count > 0:
                    QuadTo(P(points[0]), P(points[1]), P(points[2]));
                    break;
                case SKPathVerb.Conic when ring.Count > 0:
                    var quads = SKPath.ConvertConicToQuads(points[0], points[1], points[2], iterator.ConicWeight(), 2);
                    for (var q = 0; q + 2 < quads.Length; q += 2)
                        QuadTo(P(quads[q]), P(quads[q + 1]), P(quads[q + 2]));
                    break;
                case SKPathVerb.Cubic when ring.Count > 0:
                    CurveTo(P(points[1]), P(points[2]), P(points[3]));
                    break;
                case SKPathVerb.Close:
                    EndContour(isClosed: true);
                    break;
            }
        }
        EndContour(isClosed: false);
        return (closed, open);

        // A quadratic raised to the cubic that draws the same curve.
        void QuadTo(Point2D from, Point2D control, Point2D to) =>
            CurveTo(Lerp(from, control, 2.0 / 3), Lerp(to, control, 2.0 / 3), to);
    }

    /// <summary>
    /// A closed ring tidied: a last point back on the first (a path closed by drawing back to its
    /// start) merged into it, each point smooth where its two handles line up, and edges split
    /// until there are at least three points. Null if nothing is left of it.
    /// </summary>
    private static IReadOnlyList<ShapeAnchor>? Closed(List<ShapeAnchor> ring)
    {
        if (ring.Count >= 2 && Near(ring[^1].Point, ring[0].Point))
        {
            ring[0] = ring[0] with { InHandle = ring[^1].InHandle };
            ring.RemoveAt(ring.Count - 1);
        }
        if (ring.Count == 0)
            return null;
        while (ring.Count < 3)
        {
            // Split the longest edge in two (de Casteljau), which leaves the outline as it was.
            var longest = Enumerable.Range(0, ring.Count).MaxBy(i => Distance(ring[i].Point, ring[(i + 1) % ring.Count].Point));
            var a = ring[longest];
            var b = ring[(longest + 1) % ring.Count];
            if (Distance(a.Point, b.Point) < SamePoint && ring.Count > 1 || ring.Count == 1 && Near(a.OutHandle, a.Point) && Near(a.InHandle, a.Point))
                return null;
            var ab = Lerp(a.Point, a.OutHandle, 0.5);
            var bc = Lerp(a.OutHandle, b.InHandle, 0.5);
            var cd = Lerp(b.InHandle, b.Point, 0.5);
            var abc = Lerp(ab, bc, 0.5);
            var bcd = Lerp(bc, cd, 0.5);
            var mid = Lerp(abc, bcd, 0.5);
            ring[longest] = a with { OutHandle = ab };
            var nextIndex = (longest + 1) % ring.Count;
            ring[nextIndex] = ring[nextIndex] with { InHandle = cd };
            ring.Insert(longest + 1, new ShapeAnchor(mid, abc, bcd, AnchorHandleKind.Smooth));
        }
        return ring.Select(a => a with { HandleKind = IsSmooth(a) ? AnchorHandleKind.Smooth : AnchorHandleKind.Corner }).ToList();
    }

    /// <summary>Both handles pulled out, pointing opposite ways along one line.</summary>
    private static bool IsSmooth(ShapeAnchor a)
    {
        var (ix, iy) = (a.InHandle.X - a.Point.X, a.InHandle.Y - a.Point.Y);
        var (ox, oy) = (a.OutHandle.X - a.Point.X, a.OutHandle.Y - a.Point.Y);
        var (li, lo) = (Math.Sqrt(ix * ix + iy * iy), Math.Sqrt(ox * ox + oy * oy));
        if (li < SamePoint || lo < SamePoint)
            return false;
        var cross = (ix * oy - iy * ox) / (li * lo);
        var dot = (ix * ox + iy * oy) / (li * lo);
        return dot < 0 && Math.Abs(cross) < 0.02;
    }

    /// <summary>A path's verbs and points as text, to tell the same path drawn twice.</summary>
    private static string Key(SKPath path)
    {
        var text = new System.Text.StringBuilder();
        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            text.Append((int)verb).Append(':');
            foreach (var p in points)
                text.Append(p.X).Append(',').Append(p.Y).Append(' ');
        }
        return text.ToString();
    }

    private static Point2D P(SKPoint p) => new(p.X, p.Y);

    private static Point2D Lerp(Point2D a, Point2D b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static bool Near(Point2D a, Point2D b) => Distance(a, b) < SamePoint;
}
