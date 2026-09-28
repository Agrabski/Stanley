using SkiaSharp;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>
/// Sends every point of a path through a (possibly non-rigid) mapping - a torso bending
/// with the spine, art warped onto a region. Each segment is split into a few pieces
/// first and the pieces' control points mapped, so curves stay curves and straight
/// lines bend with the mapping.
/// </summary>
internal static class PathMapping
{
    public static SKPath Map(SKPath path, Func<Point2D, Point2D> map, int pieces = 6)
    {
        using var builder = new SKPathBuilder();
        builder.FillType = path.FillType;
        SKPoint M(SKPoint p)
        {
            var q = map(new Point2D(p.X, p.Y));
            return new SKPoint((float)q.X, (float)q.Y);
        }

        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    builder.MoveTo(M(points[0]));
                    break;
                case SKPathVerb.Line:
                    for (var i = 1; i <= pieces; i++)
                        builder.LineTo(M(Lerp(points[0], points[1], (float)i / pieces)));
                    break;
                case SKPathVerb.Quad:
                    // A quadratic as a cubic, then split like one.
                    SplitCubic(builder, points[0], Lerp(points[0], points[1], 2f / 3), Lerp(points[2], points[1], 2f / 3), points[2], pieces, M);
                    break;
                case SKPathVerb.Conic:
                    var weight = iterator.ConicWeight();
                    var quads = SKPath.ConvertConicToQuads(points[0], points[1], points[2], weight, 2);
                    for (var q = 0; q + 2 < quads.Length; q += 2)
                        SplitCubic(builder, quads[q], Lerp(quads[q], quads[q + 1], 2f / 3), Lerp(quads[q + 2], quads[q + 1], 2f / 3), quads[q + 2], Math.Max(2, pieces / 2), M);
                    break;
                case SKPathVerb.Cubic:
                    SplitCubic(builder, points[0], points[1], points[2], points[3], pieces, M);
                    break;
                case SKPathVerb.Close:
                    builder.Close();
                    break;
            }
        }
        return builder.Detach();
    }

    /// <summary>Splits a cubic into <paramref name="pieces"/> equal-parameter pieces (de Casteljau) and adds each, mapped.</summary>
    private static void SplitCubic(SKPathBuilder builder, SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, int pieces, Func<SKPoint, SKPoint> map)
    {
        var (a, b, c) = (p0, p1, p2);
        var d = p3;
        for (var i = pieces; i >= 1; i--)
        {
            // Cut off the first 1/i of what's left.
            var t = 1f / i;
            var ab = Lerp(a, b, t);
            var bc = Lerp(b, c, t);
            var cd = Lerp(c, d, t);
            var abc = Lerp(ab, bc, t);
            var bcd = Lerp(bc, cd, t);
            var abcd = Lerp(abc, bcd, t);
            builder.CubicTo(map(ab), map(abc), map(abcd));
            (a, b, c) = (abcd, bcd, cd);
        }
    }

    private static SKPoint Lerp(SKPoint a, SKPoint b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}
