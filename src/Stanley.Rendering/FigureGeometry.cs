using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>
/// Turns the rig's simple shapes (smoothed outlines, tapered capsules, turned ellipses)
/// into Skia paths, and combines paths. Shared by the bare body and by sticker covers,
/// which are the same shapes, clipped and grown.
/// </summary>
internal static class FigureGeometry
{
    public static SKPath Empty()
    {
        using var builder = new SKPathBuilder();
        return builder.Detach();
    }

    public static SKPath Copy(SKPath path)
    {
        using var builder = new SKPathBuilder(path);
        return builder.Detach();
    }

    /// <summary>Unions <paramref name="a"/> and <paramref name="b"/> and disposes both.</summary>
    public static SKPath Union(SKPath a, SKPath b)
    {
        if (a.IsEmpty)
        {
            a.Dispose();
            return b;
        }
        if (b.IsEmpty)
        {
            b.Dispose();
            return a;
        }
        var merged = a.Op(b, SKPathOp.Union);
        if (merged is null || !Covers(merged, a) || !Covers(merged, b))
        {
            // Skia's path ops can fail - or, on warped art whose lines run along its own fills'
            // edges, silently drop a shape or its inside. Union the two as regions instead.
            merged?.Dispose();
            merged = RegionUnion(a, b);
        }
        a.Dispose();
        b.Dispose();
        return merged;
    }

    /// <summary>Whether <paramref name="union"/> contains <paramref name="part"/>, going by a 3 x 3 grid of sample points inside it.</summary>
    private static bool Covers(SKPath union, SKPath part)
    {
        var box = part.Bounds;
        for (var i = 1; i <= 3; i++)
        {
            for (var j = 1; j <= 3; j++)
            {
                var x = box.Left + box.Width * i / 4;
                var y = box.Top + box.Height * j / 4;
                if (part.Contains(x, y) && !union.Contains(x, y))
                    return false;
            }
        }
        return true;
    }

    /// <summary>Cells across the larger side of a <see cref="RegionUnion"/> - fine enough that its steps never show.</summary>
    private const float RegionCells = 512;

    /// <summary><paramref name="a"/> and <paramref name="b"/> unioned as rasterised regions (which never fail), traced back into a path.</summary>
    private static SKPath RegionUnion(SKPath a, SKPath b)
    {
        var box = SKRect.Union(a.Bounds, b.Bounds);
        var scale = RegionCells / Math.Max(Math.Max(box.Width, box.Height), 1e-6f);
        var toCells = SKMatrix.CreateTranslation(-box.Left, -box.Top).PostConcat(SKMatrix.CreateScale(scale, scale));
        using var cellsA = Transformed(a, toCells);
        using var cellsB = Transformed(b, toCells);
        using var clip = new SKRegion(new SKRectI(-2, -2, (int)Math.Ceiling(box.Width * scale) + 2, (int)Math.Ceiling(box.Height * scale) + 2));
        using var region = new SKRegion();
        region.SetPath(cellsA, clip);
        using var other = new SKRegion();
        other.SetPath(cellsB, clip);
        region.Op(other, SKRegionOperation.Union);
        using var boundary = region.GetBoundaryPath();
        return Transformed(boundary, toCells.Invert());
    }

    /// <summary>A new path: <paramref name="a"/> combined with <paramref name="b"/> by <paramref name="op"/> (neither is disposed).</summary>
    public static SKPath Combine(SKPath a, SKPath b, SKPathOp op) =>
        a.Op(b, op) ?? (op == SKPathOp.Difference || op == SKPathOp.Union ? Copy(a) : Empty());

    public static SKPath Transformed(SKPath path, SKMatrix matrix)
    {
        using var builder = new SKPathBuilder();
        builder.FillType = path.FillType;
        builder.AddPath(path, in matrix);
        return builder.Detach();
    }

    /// <summary>A closed curve through the midpoints of <paramref name="points"/>, using each point as a control point - soft corners without extra data.</summary>
    public static SKPath SmoothClosed(IReadOnlyList<Point2D> points)
    {
        using var builder = new SKPathBuilder();
        if (points.Count >= 3)
        {
            SKPoint P(int i) => new((float)points[i % points.Count].X, (float)points[i % points.Count].Y);
            SKPoint Mid(SKPoint a, SKPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

            builder.MoveTo(Mid(P(0), P(1)));
            for (var i = 1; i <= points.Count; i++)
                builder.QuadTo(P(i), Mid(P(i), P(i + 1)));
            builder.Close();
        }
        return builder.Detach();
    }

    /// <summary>Two circles joined by their outer tangents: a limb segment that tapers from one radius to the other.</summary>
    public static SKPath Capsule(BodyCapsule c)
    {
        var (x1, y1, r1) = ((float)c.From.X, (float)c.From.Y, (float)c.FromRadius);
        var (x2, y2, r2) = ((float)c.To.X, (float)c.To.Y, (float)c.ToRadius);
        var dx = x2 - x1;
        var dy = y2 - y1;
        var d = MathF.Sqrt(dx * dx + dy * dy);
        var k = d > 1e-6f ? (r1 - r2) / d : 1;

        var circles = Union(Circle(x1, y1, r1), Circle(x2, y2, r2));
        if (MathF.Abs(k) >= 1)
            return circles; // one circle contains the other

        var (ux, uy) = (dx / d, dy / d);
        var (nx, ny) = (-uy, ux);
        var s = MathF.Sqrt(1 - k * k);
        var (ax, ay) = (k * ux + s * nx, k * uy + s * ny);
        var (bx, by) = (k * ux - s * nx, k * uy - s * ny);
        using var body = new SKPathBuilder();
        body.MoveTo(x1 + r1 * ax, y1 + r1 * ay);
        body.LineTo(x2 + r2 * ax, y2 + r2 * ay);
        body.LineTo(x2 + r2 * bx, y2 + r2 * by);
        body.LineTo(x1 + r1 * bx, y1 + r1 * by);
        body.Close();
        return Union(circles, body.Detach());
    }

    public static SKPath Circle(float x, float y, float r)
    {
        using var builder = new SKPathBuilder();
        builder.AddCircle(x, y, r);
        return builder.Detach();
    }

    public static SKPath Oval(BodyEllipse e)
    {
        using var builder = new SKPathBuilder();
        builder.AddOval(new SKRect(
            (float)(e.Center.X - e.RadiusX), (float)(e.Center.Y - e.RadiusY),
            (float)(e.Center.X + e.RadiusX), (float)(e.Center.Y + e.RadiusY)));
        var oval = builder.Detach();
        if (e.RotationDegrees == 0)
            return oval;
        using (oval)
            return Transformed(oval, SKMatrix.CreateRotationDegrees((float)e.RotationDegrees, (float)e.Center.X, (float)e.Center.Y));
    }

    public static SKPath Polygon(IReadOnlyList<Point2D> points)
    {
        using var builder = new SKPathBuilder();
        if (points.Count >= 3)
        {
            builder.MoveTo((float)points[0].X, (float)points[0].Y);
            for (var i = 1; i < points.Count; i++)
                builder.LineTo((float)points[i].X, (float)points[i].Y);
            builder.Close();
        }
        return builder.Detach();
    }

    /// <summary>Everything a layer's skin covers: its torso outline (smoothed), capsules and ellipses, as one path.</summary>
    public static SKPath LayerSkin(FigureLayer layer)
    {
        var skin = layer.Torso is { Count: >= 3 } torso ? SmoothClosed(torso) : Empty();
        foreach (var capsule in layer.Capsules)
            skin = Union(skin, Capsule(capsule));
        foreach (var ellipse in layer.Ellipses)
            skin = Union(skin, Oval(ellipse));
        return skin;
    }

    /// <summary>The union of the discs <paramref name="seams"/>.</summary>
    public static SKPath Discs(IReadOnlyList<BodyEllipse> seams)
    {
        var all = Empty();
        foreach (var seam in seams)
            all = Union(all, Oval(seam));
        return all;
    }

    public static SKColor ToSk(ColorValue color) =>
        SKColor.TryParse(color.Hex.Length == 9 ? "#" + color.Hex[7..] + color.Hex[1..7] : color.Hex, out var parsed) ? parsed : SKColors.BurlyWood;
}
