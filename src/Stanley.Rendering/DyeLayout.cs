using SkiaSharp;
using Stanley.ProjectModel.Characters;

namespace Stanley.Rendering;

/// <summary>
/// A dye (docs: modular hair, <see cref="PatternFill.IsDye"/>) laid once across a drawn part,
/// not repeated in its region's frame: "tips" is the bottom of that piece however long it is.
/// It is fitted in the region frame's orientation (for hair, the head's, so it turns with the
/// head): the part's bounds in that frame, turned by the dye's <see cref="PatternFill.Angle"/>,
/// are what <see cref="PatternFill.Weight"/> and <see cref="PatternFill.Size"/> are fractions of.
/// It is all vector - polygons, and for an ombre one linear gradient - drawn clipped to an
/// element's path, over its ground colour, so PDF export stays vector.
/// </summary>
internal sealed class DyeLayout
{
    /// <summary>A streak dye never lays more bands than this, however small its spacing.</summary>
    private const int MaxStreaks = 64;

    private readonly List<(SKPath Shape, SKColor Color)> _bands;
    private readonly SKShader? _gradient;

    private DyeLayout(List<(SKPath Shape, SKColor Color)> bands, SKShader? gradient)
    {
        _bands = bands;
        _gradient = gradient;
    }

    /// <summary>The bounds of <paramref name="paths"/> together (empty for none).</summary>
    public static SKRect Bounds(IEnumerable<SKPath> paths)
    {
        var bounds = SKRect.Empty;
        var first = true;
        foreach (var path in paths)
        {
            var b = path.TightBounds;
            bounds = first ? b : SKRect.Union(bounds, b);
            first = false;
        }
        return bounds;
    }

    /// <summary>
    /// A stable number for the pseudo-random parts of a dye (the widths and gaps of streaks),
    /// from the names of what it's laid on: the same on every render and every run, unlike
    /// <see cref="string.GetHashCode()"/>.
    /// </summary>
    public static int Seed(params string?[] names)
    {
        var hash = 2166136261u;
        foreach (var name in names)
        {
            foreach (var c in name ?? "")
                hash = (hash ^ c) * 16777619u;
            hash = (hash ^ 0x1F) * 16777619u;
        }
        return (int)hash;
    }

    /// <summary>
    /// The dye <paramref name="dye"/> fitted across <paramref name="paths"/> (figure space, all of
    /// them the one part), in <paramref name="frame"/> - the region's frame, local space to figure
    /// space. Null if there is nothing to fit it to. <paramref name="ground"/> only picks a colour
    /// for a dye given none; <paramref name="seed"/> (see <see cref="Seed"/>) gives streaks their look.
    /// </summary>
    public static DyeLayout? Fit(PatternFill dye, IEnumerable<SKPath> paths, SKMatrix frame, SKColor ground, int seed)
    {
        var toFigure = SKMatrix.CreateRotationDegrees((float)(dye.Angle ?? 0)).PostConcat(frame);
        if (!toFigure.TryInvert(out var toDye))
            return null;
        var bounds = SKRect.Empty;
        var any = false;
        foreach (var path in paths)
        {
            using var local = FigureGeometry.Transformed(path, toDye);
            bounds = any ? SKRect.Union(bounds, local.TightBounds) : local.TightBounds;
            any = true;
        }
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        var (left, top, right, bottom) = (bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        // Outer bands run a little past the bounds, so an edge that lies on them isn't half-covered.
        var pad = 0.02f * Math.Max(bounds.Width, bounds.Height);
        var color = dye.Colors.Count > 0 ? FigureGeometry.ToSk(dye.Colors[0]) : FabricShaders.Contrast(ground);
        var bands = new List<(SKPath, SKColor)>();
        SKShader? gradient = null;

        SKPath Band(float l, float t, float r, float b)
        {
            using var builder = new SKPathBuilder();
            builder.MoveTo(toFigure.MapPoint(l, t));
            builder.LineTo(toFigure.MapPoint(r, t));
            builder.LineTo(toFigure.MapPoint(r, b));
            builder.LineTo(toFigure.MapPoint(l, b));
            builder.Close();
            return builder.Detach();
        }

        switch (dye.Kind)
        {
            case PatternKind.Tips:
                var tips = Weight(dye, 0.02, 1);
                bands.Add((Band(left - pad, bottom - tips * bounds.Height, right + pad, bottom + pad), color));
                break;
            case PatternKind.Roots:
                var roots = Weight(dye, 0.02, 1);
                bands.Add((Band(left - pad, top - pad, right + pad, top + roots * bounds.Height), color));
                break;
            case PatternKind.Ombre:
                // Clear at the start and the dye at the bottom, over the ground: the ground colour
                // fading into the dye, whatever the ground is.
                var start = top + Weight(dye, 0, 0.95) * bounds.Height;
                gradient = SKShader.CreateLinearGradient(toFigure.MapPoint(bounds.MidX, start), toFigure.MapPoint(bounds.MidX, bottom),
                    [color.WithAlpha(0), color], SKShaderTileMode.Clamp);
                break;
            case PatternKind.Streaks:
                Streaks(dye, bounds, pad, color, seed, bands, Band);
                break;
            case PatternKind.Rainbow:
                // Each band paints from its start to the far side, so where two meet no ground shows through.
                var colors = dye.Colors.Count > 0 ? dye.Colors : PatternFill.RainbowColors;
                var width = bounds.Width / colors.Count;
                for (var i = 0; i < colors.Count; i++)
                    bands.Add((Band(i == 0 ? left - pad : left + i * width, top - pad, right + pad, bottom + pad), FigureGeometry.ToSk(colors[i])));
                break;
            default:
                return null;
        }
        return new DyeLayout(bands, gradient);
    }

    /// <summary>
    /// Bands running down the part, of pseudo-random widths and gaps: on average one every
    /// <see cref="PatternFill.Size"/> of the part's width (0.18 by default), each
    /// <see cref="PatternFill.Weight"/> (0.4) of that.
    /// </summary>
    private static void Streaks(PatternFill dye, SKRect bounds, float pad, SKColor color, int seed, List<(SKPath, SKColor)> bands, Func<float, float, float, float, SKPath> band)
    {
        var spacing = (float)Math.Clamp(dye.Size ?? 0.18, 0.03, 1) * bounds.Width;
        var weight = Weight(dye, 0.05, 0.95);
        // xorshift32: the same numbers for the same seed, on any runtime.
        var state = (uint)seed | 1u;
        float Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state >> 8) / (float)(1 << 24);
        }
        for (var i = 0; i < 3; i++)
            Next();

        var x = bounds.Left + (1 - weight) * spacing * Next();
        for (var i = 0; i < MaxStreaks && x < bounds.Right; i++)
        {
            var width = weight * spacing * (0.5f + Next());
            bands.Add((band(x, bounds.Top - pad, Math.Min(x + width, bounds.Right + pad), bounds.Bottom + pad), color));
            x += width + (1 - weight) * spacing * (0.5f + Next());
        }
    }

    private static float Weight(PatternFill dye, double min, double max) => (float)Math.Clamp(dye.Weight ?? PatternFill.DefaultDyeWeight(dye.Kind), min, max);

    /// <summary>Paints the dye over <paramref name="path"/> (already filled with its ground colour), clipped to it.</summary>
    public void Draw(SKCanvas canvas, SKPath path)
    {
        canvas.Save();
        canvas.ClipPath(path, antialias: true);
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        if (_gradient != null)
        {
            paint.Shader = _gradient;
            canvas.DrawPaint(paint);
            paint.Shader = null;
        }
        foreach (var (shape, color) in _bands)
        {
            paint.Color = color;
            canvas.DrawPath(shape, paint);
        }
        canvas.Restore();
    }
}
