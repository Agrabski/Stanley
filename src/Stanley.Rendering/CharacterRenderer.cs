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
    SKPath BuildSilhouette(CharacterDefinition character, CharacterPlacement placement, ViewAngle angle = ViewAngle.Front, PoseData? pose = null);

    void Draw(SKCanvas canvas, CharacterDefinition character, CharacterPlacement placement, float strokeMm, ViewAngle angle = ViewAngle.Front, PoseData? pose = null);
}

public static class CharacterRenderers
{
    public static ICharacterRenderer Default { get; } = new FigureRenderer();

    /// <summary>Draws one instance, or - when its character is missing from <paramref name="characters"/> (a hand-edited or half-copied project) - a dashed placeholder the size of a default body, instead of failing the whole page.</summary>
    public static void DrawInstance(SKCanvas canvas, CharacterInstance instance, IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters, float strokeMm)
    {
        if (characters != null && characters.TryGetValue(instance.CharacterId, out var character))
        {
            Default.Draw(canvas, character, instance.Placement, strokeMm, instance.Pose.ViewAngle, instance.Pose);
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
/// A figure ready to paint, in figure space (see <see cref="BodyFigure"/>): its items back
/// to front, and <see cref="Outline"/>, the union of everything, for hit-testing. Built
/// once per character, view and pose (cached by <see cref="FigureRenderer"/>) and never
/// changed afterwards, so it can be drawn from any thread.
/// </summary>
public sealed class FigureDrawing
{
    internal FigureDrawing(IReadOnlyList<FigureItem> items, SKPath outline)
    {
        Items = items;
        Outline = outline;
    }

    internal IReadOnlyList<FigureItem> Items { get; }

    /// <summary>The whole figure's outline in figure space.</summary>
    public SKPath Outline { get; }

    /// <summary>Paints every item onto <paramref name="canvas"/>, whose transform maps figure space to the page; <paramref name="strokeWidth"/> is in figure units.</summary>
    public void Draw(SKCanvas canvas, float strokeWidth)
    {
        using var ink = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        foreach (var item in Items)
            item.Draw(canvas, ink);
    }
}

/// <summary>One thing a <see cref="FigureDrawing"/> paints.</summary>
internal abstract class FigureItem
{
    public abstract void Draw(SKCanvas canvas, SKPaint ink);
}

/// <summary>
/// A filled shape (a layer's skin, a sticker cover) with an ink outline. Inside
/// <see cref="InkMask"/> - the layer's seams where they lie over what's painted before it -
/// the outline is left out, so joints read as one body.
/// </summary>
internal sealed class ShapeItem(SKPath path, SKColor fill, SKPath? inkMask) : FigureItem
{
    public SKPath Path { get; } = path;

    public SKPath? InkMask { get; } = inkMask;

    public override void Draw(SKCanvas canvas, SKPaint ink)
    {
        using (var paint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true })
            canvas.DrawPath(Path, paint);
        if (InkMask is { IsEmpty: false } mask)
        {
            canvas.Save();
            canvas.ClipPath(mask, SKClipOperation.Difference, antialias: true);
            canvas.DrawPath(Path, ink);
            canvas.Restore();
        }
        else
        {
            canvas.DrawPath(Path, ink);
        }
    }
}

/// <summary>
/// The V1 renderer: the body <see cref="BodyRig"/> generates, painted layer by layer
/// (<see cref="BodyFigure.Layers"/>) - legs, torso, head, arms in a front view; far arm,
/// body, head, near foot, near arm side on - each filled with the skin colour and inked,
/// with the ink left out at the seams where a layer joins the ones behind it. So an arm
/// crossing the chest keeps its outline, while shoulders and hips read as one body.
/// </summary>
public sealed class FigureRenderer : ICharacterRenderer
{
    // Building the paths (the unions especially) is the expensive part; definitions are
    // immutable, so drawings are cached per definition value (and view and pose). A limb
    // drag makes a new pose on every pointer move, so each definition's cache is dropped
    // when it grows past a small bound. Dropped drawings aren't disposed here - another
    // thread may still be painting one - their native paths are freed when collected.
    private const int MaxPosesPerCharacter = 64;
    private readonly ConditionalWeakTable<CharacterDefinition, Dictionary<string, FigureDrawing>> _drawings = new();
    private readonly Lock _lock = new();

    public SKPath BuildSilhouette(CharacterDefinition character, CharacterPlacement placement, ViewAngle angle = ViewAngle.Front, PoseData? pose = null) =>
        FigureGeometry.Transformed(Drawing(character, angle, pose).Outline, ToPage(placement));

    public void Draw(SKCanvas canvas, CharacterDefinition character, CharacterPlacement placement, float strokeMm, ViewAngle angle = ViewAngle.Front, PoseData? pose = null)
    {
        var drawing = Drawing(character, angle, pose);
        var matrix = ToPage(placement);
        canvas.Save();
        canvas.Concat(in matrix);
        drawing.Draw(canvas, strokeMm / (float)Math.Max(placement.UnitHeightMm, 1e-6));
        canvas.Restore();
    }

    /// <summary>The character's drawing in figure space, from the cache or built now.</summary>
    public FigureDrawing Drawing(CharacterDefinition character, ViewAngle angle = ViewAngle.Front, PoseData? pose = null)
    {
        var key = angle + ":" + pose?.HipsShift + ":" + string.Join(";", (pose?.BoneRotations ?? []).Select(r => $"{r.Bone}={r.Degrees:R}"));
        lock (_lock)
        {
            var cache = _drawings.GetValue(character, _ => []);
            if (cache.TryGetValue(key, out var cached))
                return cached;
            if (cache.Count >= MaxPosesPerCharacter)
                cache.Clear();
            return cache[key] = Build(character, BodyRig.Build(character.Body, angle, character.Skeleton, pose));
        }
    }

    /// <summary>Paints the figure's layers back to front in the character's skin colour.</summary>
    public static FigureDrawing Build(CharacterDefinition character, BodyFigure figure)
    {
        var skin = FigureGeometry.ToSk(character.Skin);
        var items = new List<FigureItem>();
        var below = FigureGeometry.Empty();
        foreach (var layer in figure.Layers)
        {
            if (!layer.HasBody)
                continue;
            var path = FigureGeometry.LayerSkin(layer);
            items.Add(new ShapeItem(path, skin, SeamMask(layer, below)));
            below = FigureGeometry.Union(below, FigureGeometry.Copy(path));
        }
        return new FigureDrawing(items, below);
    }

    /// <summary>Where a layer's ink is left out: its seams, where they lie over what's already painted.</summary>
    internal static SKPath? SeamMask(FigureLayer layer, SKPath below)
    {
        if (layer.Seams.Count == 0 || below.IsEmpty)
            return null;
        using var discs = FigureGeometry.Discs(layer.Seams);
        return FigureGeometry.Combine(discs, below, SKPathOp.Intersect);
    }

    internal static SKMatrix ToPage(CharacterPlacement placement) =>
        SKMatrix.CreateScale((float)(placement.Mirrored ? -placement.UnitHeightMm : placement.UnitHeightMm), (float)placement.UnitHeightMm)
            .PostConcat(SKMatrix.CreateTranslation((float)placement.Ground.X, (float)placement.Ground.Y));
}
