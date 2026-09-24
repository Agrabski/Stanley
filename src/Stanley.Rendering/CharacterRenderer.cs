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
    /// <summary>The character's whole outline in page millimetres, seen from <paramref name="angle"/>, stickers included - what a click hit-tests against. The caller disposes it.</summary>
    /// <param name="overrides">One panel's changes to what's worn (and its colours and fabrics).</param>
    /// <param name="revision">The named look to draw (from <see cref="CharacterDefinition.Revisions"/>); null = the character's default look.</param>
    SKPath BuildSilhouette(CharacterDefinition character, CharacterPlacement placement, ViewAngle angle = ViewAngle.Front, PoseData? pose = null,
        CharacterInstanceOverrides? overrides = null, CharacterRevisionId? revision = null);

    void Draw(SKCanvas canvas, CharacterDefinition character, CharacterPlacement placement, float strokeMm, ViewAngle angle = ViewAngle.Front, PoseData? pose = null,
        CharacterInstanceOverrides? overrides = null, CharacterRevisionId? revision = null);

    /// <summary>The character's bounding box in figure space, stickers included (a flared skirt, hair) - the body's own extent when nothing sticks out.</summary>
    Rect2D Extent(CharacterDefinition character, ViewAngle angle = ViewAngle.Front, PoseData? pose = null,
        CharacterInstanceOverrides? overrides = null, CharacterRevisionId? revision = null);

    /// <summary>The worn sticker drawn topmost at <paramref name="pagePoint"/>, or null (bare skin, or off the character).</summary>
    StickerId? StickerAt(CharacterDefinition character, CharacterPlacement placement, Point2D pagePoint, ViewAngle angle = ViewAngle.Front, PoseData? pose = null,
        CharacterInstanceOverrides? overrides = null, CharacterRevisionId? revision = null);

    /// <summary>Everything <paramref name="sticker"/> draws, as one outline in page millimetres (empty if it draws nothing). The caller disposes it.</summary>
    SKPath BuildStickerOutline(CharacterDefinition character, CharacterPlacement placement, StickerId sticker, ViewAngle angle = ViewAngle.Front, PoseData? pose = null,
        CharacterInstanceOverrides? overrides = null, CharacterRevisionId? revision = null);
}

public static class CharacterRenderers
{
    public static ICharacterRenderer Default { get; } = new FigureRenderer();

    /// <summary>Draws one instance, or - when its character is missing from <paramref name="characters"/> (a hand-edited or half-copied project) - a dashed placeholder the size of a default body, instead of failing the whole page.</summary>
    public static void DrawInstance(SKCanvas canvas, CharacterInstance instance, IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters, float strokeMm)
    {
        if (characters != null && characters.TryGetValue(instance.CharacterId, out var character))
        {
            Default.Draw(canvas, character, instance.Placement, strokeMm, instance.Pose.ViewAngle, instance.Pose, instance.Overrides, instance.RevisionOverride);
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

    /// <summary>Whether any sticker paints anything.</summary>
    public bool HasStickers => Items.OfType<ShapeItem>().Any(i => i.Owner is not null);

    /// <summary>The worn sticker painted topmost at <paramref name="figurePoint"/> (figure space), or null for bare skin or nothing.</summary>
    public StickerId? StickerAt(Point2D figurePoint)
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i] is ShapeItem shape && shape.Path.Contains((float)figurePoint.X, (float)figurePoint.Y))
                return shape.Owner;
        }
        return null;
    }

    /// <summary>Everything <paramref name="sticker"/> paints, as one figure-space outline (empty if it paints nothing) - for highlighting it. The caller disposes it.</summary>
    public SKPath OutlineOf(StickerId sticker)
    {
        var all = FigureGeometry.Empty();
        foreach (var item in Items.OfType<ShapeItem>().Where(i => i.Owner == sticker))
            all = FigureGeometry.Union(all, FigureGeometry.Copy(item.Path));
        return all;
    }

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
internal sealed class ShapeItem(SKPath path, SKColor fill, SKPath? inkMask, StickerId? owner = null, FabricFill? fabric = null) : FigureItem
{
    public SKPath Path { get; } = path;

    /// <summary>The sticker this belongs to, or null for skin.</summary>
    public StickerId? Owner { get; } = owner;

    public SKPath? InkMask { get; } = inkMask;

    public override void Draw(SKCanvas canvas, SKPaint ink)
    {
        if (fabric is null)
        {
            using var paint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(Path, paint);
        }
        else
        {
            // Each piece in its own region's frame (a sleeve's stripes turn with the arm),
            // all inside the item's outline, so its cuts stay cut.
            canvas.Save();
            canvas.ClipPath(Path, antialias: true);
            fabric.Draw(canvas, fill);
            canvas.Restore();
        }
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
/// A fabric (pattern and/or texture) over an item's colour: the pieces it's laid out in,
/// each with its region's frame. Shaders are made once and shared by every draw.
/// </summary>
internal sealed class FabricFill
{
    private readonly List<(SKPath Path, SKShader? Pattern, SKShader? Texture)> _pieces;
    private readonly float _strength;

    public FabricFill(IReadOnlyList<PartPiece> pieces, SKColor ground, Fabric fabric, double height, Func<string, SKPicture?> tiles)
    {
        _pieces = pieces.Select(p => (p.Path,
            fabric.Pattern is { } pattern ? FabricShaders.Pattern(pattern, ground, p.Frame, height, tiles) : null,
            fabric.Texture is { } texture ? FabricShaders.Texture(texture, p.Frame, height, tiles) : null)).ToList();
        _strength = (float)Math.Clamp(fabric.Texture?.Strength ?? TextureFill.DefaultStrength, 0, 1);
    }

    public void Draw(SKCanvas canvas, SKColor ground)
    {
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        foreach (var (path, pattern, texture) in _pieces)
        {
            paint.Shader = null;
            paint.BlendMode = SKBlendMode.SrcOver;
            paint.Color = ground;
            canvas.DrawPath(path, paint);
            if (pattern != null)
            {
                paint.Color = SKColors.Black;
                paint.Shader = pattern;
                canvas.DrawPath(path, paint);
            }
            if (texture != null)
            {
                paint.Shader = texture;
                paint.BlendMode = SKBlendMode.Multiply;
                paint.Color = SKColors.White.WithAlpha((byte)(_strength * 255));
                canvas.DrawPath(path, paint);
            }
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

    public SKPath BuildSilhouette(CharacterDefinition character, CharacterPlacement placement, ViewAngle angle = ViewAngle.Front, PoseData? pose = null,
        CharacterInstanceOverrides? overrides = null, CharacterRevisionId? revision = null) =>
        FigureGeometry.Transformed(Drawing(character, angle, pose, overrides, revision).Outline, ToPage(placement));

    public void Draw(SKCanvas canvas, CharacterDefinition character, CharacterPlacement placement, float strokeMm, ViewAngle angle = ViewAngle.Front, PoseData? pose = null,
        CharacterInstanceOverrides? overrides = null, CharacterRevisionId? revision = null)
    {
        var drawing = Drawing(character, angle, pose, overrides, revision);
        var matrix = ToPage(placement);
        canvas.Save();
        canvas.Concat(in matrix);
        drawing.Draw(canvas, strokeMm / (float)Math.Max(placement.UnitHeightMm, 1e-6));
        canvas.Restore();
    }

    public Rect2D Extent(CharacterDefinition character, ViewAngle angle = ViewAngle.Front, PoseData? pose = null,
        CharacterInstanceOverrides? overrides = null, CharacterRevisionId? revision = null)
    {
        var body = BodyRig.Extent(character.Body, angle, character.Skeleton, pose);
        if (character.Wardrobe.Stickers.Count == 0)
            return body;
        var drawing = Drawing(character, angle, pose, overrides, revision);
        if (!drawing.HasStickers)
            return body;
        var b = drawing.Outline.TightBounds;
        return Rect2D.FromEdges(Math.Min(body.Left, b.Left), Math.Min(body.Top, b.Top), Math.Max(body.Right, b.Right), Math.Max(body.Bottom, b.Bottom));
    }

    public StickerId? StickerAt(CharacterDefinition character, CharacterPlacement placement, Point2D pagePoint, ViewAngle angle = ViewAngle.Front, PoseData? pose = null,
        CharacterInstanceOverrides? overrides = null, CharacterRevisionId? revision = null) =>
        Drawing(character, angle, pose, overrides, revision).StickerAt(placement.ToFigure(pagePoint));

    public SKPath BuildStickerOutline(CharacterDefinition character, CharacterPlacement placement, StickerId sticker, ViewAngle angle = ViewAngle.Front, PoseData? pose = null,
        CharacterInstanceOverrides? overrides = null, CharacterRevisionId? revision = null)
    {
        using var outline = Drawing(character, angle, pose, overrides, revision).OutlineOf(sticker);
        return FigureGeometry.Transformed(outline, ToPage(placement));
    }

    /// <summary>The character's drawing in figure space, from the cache or built now.</summary>
    public FigureDrawing Drawing(CharacterDefinition character, ViewAngle angle = ViewAngle.Front, PoseData? pose = null,
        CharacterInstanceOverrides? overrides = null, CharacterRevisionId? revision = null)
    {
        var look = revision is { } id && character.Revisions.TryGetValue(id, out var r) ? r : null;
        var key = angle + ":" + pose?.HipsShift + ":" + string.Join(";", (pose?.BoneRotations ?? []).Select(b => $"{b.Bone}={b.Degrees:R}"))
            + ":" + string.Join(";", (pose?.Expression ?? []).Select(e => e.Key + "=" + e.Value))
            + ":" + CharacterLooks.CacheKey(look, overrides);
        lock (_lock)
        {
            var cache = _drawings.GetValue(character, _ => []);
            if (cache.TryGetValue(key, out var cached))
                return cached;
            if (cache.Count >= MaxPosesPerCharacter)
                cache.Clear();
            var figure = BodyRig.Build(character.Body, angle, character.Skeleton, pose);
            return cache[key] = Build(character, figure, CharacterLooks.Resolve(character, look, overrides), pose);
        }
    }

    /// <summary>A bare figure: the body alone, in the character's skin colour.</summary>
    public static FigureDrawing Build(CharacterDefinition character, BodyFigure figure) =>
        Build(character, figure, new CharacterLook([], new Dictionary<string, ColorValue>(), new Dictionary<string, Fabric>()), null);

    /// <summary>
    /// Paints the figure's layers back to front: each layer's skin, then - in the look's
    /// order (slot z-order, then stacking) - every worn sticker's pieces that belong in
    /// that layer, merged per colour, with the sticker's cut parts taken out and its
    /// clipped parts clipped. Every item is inked on its own outline, except in the
    /// layer's seams where it lies over the layers before it.
    /// </summary>
    public static FigureDrawing Build(CharacterDefinition character, BodyFigure figure, CharacterLook look, PoseData? pose)
    {
        var height = character.Body.Normalized().Height;
        var skin = FigureGeometry.ToSk(look.Color(CharacterDefinition.SkinSlot, character.Skin));
        var tiles = TileLookup(character);
        var pieces = look.Stickers.Select(w => StickerPieces(figure, w, height)).ToList();
        var bodySkin = FigureGeometry.Empty();
        foreach (var layer in figure.Layers.Where(l => l.HasBody))
            bodySkin = FigureGeometry.Union(bodySkin, FigureGeometry.LayerSkin(layer));

        // What's been painted in earlier layers - everything, and each sticker's own pieces.
        // Skin at a seam merges with whatever it lies over; a garment only with itself (a
        // sleeve with its shirt at the shoulder), so a shirt's hem over the trousers keeps its line.
        var items = new List<FigureItem>();
        var below = FigureGeometry.Empty();
        var belowOwn = pieces.Select(_ => FigureGeometry.Empty()).ToList();
        foreach (var layer in figure.Layers)
        {
            var painted = FigureGeometry.Empty();
            if (layer.HasBody)
            {
                var path = FigureGeometry.LayerSkin(layer);
                items.Add(new ShapeItem(path, skin, SeamMask(layer, below)));
                painted = FigureGeometry.Union(painted, FigureGeometry.Copy(path));
            }
            for (var i = 0; i < pieces.Count; i++)
            {
                var mask = SeamMask(layer, belowOwn[i]);
                var own = FigureGeometry.Empty();
                foreach (var item in StickerItems(look.Stickers[i], pieces[i], layer.Kind, look, bodySkin, mask, height, tiles))
                {
                    items.Add(item);
                    own = FigureGeometry.Union(own, FigureGeometry.Copy(item.Path));
                }
                painted = FigureGeometry.Union(painted, FigureGeometry.Copy(own));
                belowOwn[i] = FigureGeometry.Union(belowOwn[i], own);
            }
            below = FigureGeometry.Union(below, painted);
        }
        bodySkin.Dispose();
        foreach (var own in belowOwn)
            own.Dispose();
        return new FigureDrawing(items, below);
    }

    /// <summary>A sticker's pieces, each with the part it came from.</summary>
    private static List<(StickerPart Part, PartPiece Piece)> StickerPieces(BodyFigure figure, WornSticker worn, double height)
    {
        var result = new List<(StickerPart, PartPiece)>();
        foreach (var part in worn.Asset.Sticker.Parts)
        {
            if (part.Cover is { } cover)
                result.AddRange(StickerCovers.Pieces(figure, part, cover, height).Select(p => (part, p)));
        }
        return result;
    }

    /// <summary>What one worn sticker paints in one layer: its painted pieces merged per colour slot, minus its cuts, clipped as asked.</summary>
    private static IEnumerable<ShapeItem> StickerItems(WornSticker worn, List<(StickerPart Part, PartPiece Piece)> pieces, FigureLayerKind layer,
        CharacterLook look, SKPath bodySkin, SKPath? mask, double height, Func<string, SKPicture?> tiles)
    {
        var here = pieces.Where(p => p.Piece.Layer == layer).ToList();
        if (here.Count == 0)
            yield break;
        var sticker = worn.Asset.Sticker;
        var cuts = FigureGeometry.Empty();
        foreach (var (_, piece) in here.Where(p => p.Part.Blend == PartBlend.Cut))
            cuts = FigureGeometry.Union(cuts, FigureGeometry.Copy(piece.Path));
        var own = FigureGeometry.Empty();
        foreach (var (_, piece) in pieces.Where(p => p.Part.Blend != PartBlend.Cut && p.Part.Clip is null))
            own = FigureGeometry.Union(own, FigureGeometry.Copy(piece.Path));

        foreach (var group in here.Where(p => p.Part.Blend != PartBlend.Cut && p.Part.Cover is not null).GroupBy(p => p.Part.Cover!.Color))
        {
            var fallback = sticker.Colors.TryGetValue(group.Key, out var c) ? c : ColorValue.FromHex("#9a9a9a");
            var path = FigureGeometry.Empty();
            foreach (var (part, piece) in group)
            {
                var shape = part.Clip is { } clip
                    ? FigureGeometry.Combine(piece.Path, clip == PartClip.Body ? bodySkin : own, SKPathOp.Intersect)
                    : FigureGeometry.Copy(piece.Path);
                path = FigureGeometry.Union(path, shape);
            }
            if (!cuts.IsEmpty)
            {
                var cut = FigureGeometry.Combine(path, cuts, SKPathOp.Difference);
                path.Dispose();
                path = cut;
            }
            if (path.IsEmpty)
            {
                path.Dispose();
                continue;
            }
            var ground = FigureGeometry.ToSk(look.Color(group.Key, fallback));
            var fabric = look.FabricOf(group.Key) is { } f ? new FabricFill(group.Select(g => g.Piece).ToList(), ground, f, height, tiles) : null;
            yield return new ShapeItem(path, ground, mask, worn.Asset.Id, fabric);
        }
        cuts.Dispose();
        own.Dispose();
    }

    /// <summary>The character's pattern tiles, as pictures one repeat (a unit square) in size.</summary>
    private static Func<string, SKPicture?> TileLookup(CharacterDefinition character) =>
        name => character.Wardrobe.Tiles.TryGetValue(name, out var file) ? ArtPictures.Tile(file) : null;

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
