using System.Runtime.CompilerServices;
using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;

namespace Stanley.Rendering;

/// <summary>
/// Draws a character on the page. Behind an interface so the V1 flat mannequin can later
/// be joined or replaced by a sticker (cutout) renderer or a 3D backend without touching
/// the pose/document model or any caller.
/// </summary>
public interface ICharacterRenderer
{
    /// <summary>The character's whole outline in page millimetres, seen from <paramref name="angle"/> - what a click hit-tests against. The caller disposes it.</summary>
    SKPath BuildSilhouette(CharacterDefinition character, CharacterPlacement placement, ViewAngle angle = ViewAngle.Front, IReadOnlyList<BoneRotation>? pose = null);

    void Draw(SKCanvas canvas, CharacterDefinition character, CharacterPlacement placement, float strokeMm, ViewAngle angle = ViewAngle.Front, IReadOnlyList<BoneRotation>? pose = null);
}

public static class CharacterRenderers
{
    public static ICharacterRenderer Default { get; } = new MannequinRenderer();

    /// <summary>Draws one instance, or - when its character is missing from <paramref name="characters"/> (a hand-edited or half-copied project) - a dashed placeholder the size of a default body, instead of failing the whole page.</summary>
    public static void DrawInstance(SKCanvas canvas, CharacterInstance instance, IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters, float strokeMm)
    {
        if (characters != null && characters.TryGetValue(instance.CharacterId, out var character))
        {
            Default.Draw(canvas, character, instance.Placement, strokeMm, instance.Pose.ViewAngle, instance.Pose.BoneRotations);
            return;
        }

        var box = instance.Placement.ToPage(BodyRig.Extent(BodyShape.Default, instance.Pose.ViewAngle));
        using var dashed = new SKPaint
        {
            Color = new SKColor(0x80, 0x80, 0x80),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeMm,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([2f, 1.5f], 0)
        };
        canvas.DrawRect(new SKRect((float)box.Left, (float)box.Top, (float)box.Right, (float)box.Bottom), dashed);
    }
}

/// <summary>
/// The V1 renderer: the body <see cref="BodyRig"/> generates, as one flat cartoon
/// silhouette - every shape unioned (<see cref="SKPathOp.Union"/>, the same trick bubble
/// tails use) so overlaps never show seams, filled with the skin colour and inked with an
/// outline.
/// </summary>
public sealed class MannequinRenderer : ICharacterRenderer
{
    // Building the union is the expensive part; definitions are immutable, so the
    // figure-space outlines are cached per definition value (and view and pose) and
    // only transformed per draw. A limb drag makes a new pose on every pointer move, so
    // each definition's cache is dropped when it grows past a small bound.
    private const int MaxPosesPerCharacter = 64;
    private readonly ConditionalWeakTable<CharacterDefinition, Dictionary<string, FigurePaths>> _figures = new();
    private readonly Lock _lock = new();

    /// <summary>A figure's outlines in figure space: the body, the near arm and foot drawn over it (side view; empty otherwise), and the two unioned - the whole outline, for hit-testing.</summary>
    public sealed record FigurePaths(SKPath Body, SKPath Near, SKPath Outline);

    public SKPath BuildSilhouette(CharacterDefinition character, CharacterPlacement placement, ViewAngle angle = ViewAngle.Front, IReadOnlyList<BoneRotation>? pose = null)
    {
        var matrix = ToPage(placement);
        using var builder = new SKPathBuilder();
        lock (_lock)
        {
            builder.AddPath(Figure(character, angle, pose).Outline, in matrix);
        }
        return builder.Detach();
    }

    public void Draw(SKCanvas canvas, CharacterDefinition character, CharacterPlacement placement, float strokeMm, ViewAngle angle = ViewAngle.Front, IReadOnlyList<BoneRotation>? pose = null)
    {
        var matrix = ToPage(placement);
        SKPath body, near;
        lock (_lock)
        {
            var paths = Figure(character, angle, pose);
            body = Transformed(paths.Body, matrix);
            near = Transformed(paths.Near, matrix);
        }

        using (body)
        using (near)
        using (var fill = new SKPaint { Color = ToSk(character.Skin), Style = SKPaintStyle.Fill, IsAntialias = true })
        using (var ink = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeMm,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        })
        {
            canvas.DrawPath(body, fill);
            canvas.DrawPath(body, ink);
            if (!near.IsEmpty)
            {
                canvas.DrawPath(near, fill);
                canvas.DrawPath(near, ink);
            }
        }
    }

    private FigurePaths Figure(CharacterDefinition character, ViewAngle angle, IReadOnlyList<BoneRotation>? pose)
    {
        var cache = _figures.GetValue(character, _ => []);
        var key = angle + ":" + string.Join(";", (pose ?? []).Select(r => $"{r.Bone}={r.Degrees:R}"));
        if (!cache.TryGetValue(key, out var paths))
        {
            if (cache.Count >= MaxPosesPerCharacter)
            {
                foreach (var old in cache.Values)
                {
                    old.Body.Dispose();
                    old.Near.Dispose();
                    old.Outline.Dispose();
                }
                cache.Clear();
            }
            cache[key] = paths = BuildPaths(BodyRig.Build(character.Body, angle, character.Skeleton, pose));
        }
        return paths;
    }

    private static SKMatrix ToPage(CharacterPlacement placement) =>
        SKMatrix.CreateScale((float)(placement.Mirrored ? -placement.UnitHeightMm : placement.UnitHeightMm), (float)placement.UnitHeightMm)
            .PostConcat(SKMatrix.CreateTranslation((float)placement.Ground.X, (float)placement.Ground.Y));

    private static SKPath Transformed(SKPath path, SKMatrix matrix)
    {
        using var builder = new SKPathBuilder();
        builder.AddPath(path, in matrix);
        return builder.Detach();
    }

    /// <summary>The figure's outlines in figure space (see <see cref="BodyFigure"/>).</summary>
    public static FigurePaths BuildPaths(BodyFigure figure)
    {
        var body = SmoothClosed(figure.Torso);
        foreach (var limb in figure.Limbs)
            body = Union(body, Capsule(limb));
        foreach (var blob in figure.Blobs)
            body = Union(body, Oval(blob));

        using var empty = new SKPathBuilder();
        var near = empty.Detach();
        foreach (var limb in figure.NearLimbs)
            near = Union(near, Capsule(limb));
        foreach (var blob in figure.NearBlobs)
            near = Union(near, Oval(blob));
        // One real union, not two overlapping sub-paths: those would cancel out under the fill rule where the near arm crosses the body.
        var outline = body.Op(near, SKPathOp.Union) ?? new SKPath(body);
        return new FigurePaths(body, near, outline);
    }

    private static SKPath Union(SKPath a, SKPath b)
    {
        var merged = a.Op(b, SKPathOp.Union);
        if (merged is null)
        {
            // Pathological input (degenerate shapes): keep both, drawn with a winding fill.
            using var builder = new SKPathBuilder();
            builder.AddPath(a);
            builder.AddPath(b);
            merged = builder.Detach();
        }
        a.Dispose();
        b.Dispose();
        return merged;
    }

    /// <summary>A closed curve through the midpoints of <paramref name="points"/>, using each point as a control point - soft corners without extra data.</summary>
    private static SKPath SmoothClosed(IReadOnlyList<Point2D> points)
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
    private static SKPath Capsule(BodyCapsule c)
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

    private static SKPath Circle(float x, float y, float r)
    {
        using var builder = new SKPathBuilder();
        builder.AddCircle(x, y, r);
        return builder.Detach();
    }

    private static SKPath Oval(BodyEllipse e)
    {
        using var builder = new SKPathBuilder();
        builder.AddOval(new SKRect(
            (float)(e.Center.X - e.RadiusX), (float)(e.Center.Y - e.RadiusY),
            (float)(e.Center.X + e.RadiusX), (float)(e.Center.Y + e.RadiusY)));
        return builder.Detach();
    }

    private static SKColor ToSk(ColorValue color) =>
        SKColor.TryParse(color.Hex.Length == 9 ? "#" + color.Hex[7..] + color.Hex[1..7] : color.Hex, out var parsed) ? parsed : SKColors.BurlyWood;
}
