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
    /// <param name="issueLooks">The issue's look per character, for an instance without a look of its own.</param>
    public static void DrawInstance(SKCanvas canvas, CharacterInstance instance, IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters, float strokeMm,
        IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null)
    {
        if (characters != null && characters.TryGetValue(instance.CharacterId, out var character))
        {
            Default.Draw(canvas, character, instance.Placement, strokeMm, instance.Pose.ViewAngle, instance.Pose, instance.Overrides, CharacterLooks.LookOf(instance, issueLooks));
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
    public bool HasStickers => Items.Any(i => i.Owner is not null);

    /// <summary>The worn sticker painted topmost at <paramref name="figurePoint"/> (figure space), or null for bare skin or nothing.</summary>
    public StickerId? StickerAt(Point2D figurePoint)
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].Area.Contains((float)figurePoint.X, (float)figurePoint.Y))
                return Items[i].Owner;
        }
        return null;
    }

    /// <summary>Everything <paramref name="sticker"/> paints, as one figure-space outline (empty if it paints nothing) - for highlighting it. The caller disposes it.</summary>
    public SKPath OutlineOf(StickerId sticker)
    {
        var all = FigureGeometry.Empty();
        foreach (var item in Items.Where(i => i.Owner == sticker))
            all = FigureGeometry.Union(all, FigureGeometry.Copy(item.Area));
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
    /// <summary>What it covers, in figure space - for hit-testing and highlighting.</summary>
    public abstract SKPath Area { get; }

    /// <summary>The sticker it belongs to, or null for skin.</summary>
    public abstract StickerId? Owner { get; }

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

    public override SKPath Area => Path;

    public override StickerId? Owner { get; } = owner;

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
/// each with its region's frame. Shaders are made once and shared by every draw. A dye
/// pattern (<see cref="PatternFill.IsDye"/>) isn't repeated per piece: it is one
/// <see cref="DyeLayout"/>, fitted across all the pieces together in the first one's frame
/// (or the part's, when the caller fitted it to more than these pieces).
/// </summary>
internal sealed class FabricFill
{
    private readonly List<(SKPath Path, SKShader? Pattern, SKShader? Texture)> _pieces;
    private readonly DyeLayout? _dye;
    private readonly float _strength;

    /// <param name="dye">The dye already fitted across the whole part these pieces are some of; null to fit it across the pieces (using <paramref name="seed"/> for streaks).</param>
    public FabricFill(IReadOnlyList<PartPiece> pieces, SKColor ground, Fabric fabric, double height, Func<string, ArtFile?> tiles, DyeLayout? dye = null, int seed = 0)
    {
        _pieces = pieces.Select(p => (p.Path,
            fabric.Pattern is { } pattern ? FabricShaders.Pattern(pattern, ground, p.Frame, height, tiles) : null,
            fabric.Texture is { } texture ? FabricShaders.Texture(texture, p.Frame, height, tiles) : null)).ToList();
        if (fabric.Pattern is { IsDye: true } dyed)
            _dye = dye ?? (pieces.Count > 0 ? DyeLayout.Fit(dyed, pieces.Select(p => p.Path), pieces[0].Frame, ground, seed) : null);
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
            _dye?.Draw(canvas, path);
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
    /// that layer: its covers merged per colour, then its drawn parts, with the sticker's
    /// cut parts taken out and its clipped parts clipped. Skin and covers are inked on
    /// their own outline, except in the layer's seams where they lie over the layers before
    /// it; drawn art brings its own lines.
    /// </summary>
    public static FigureDrawing Build(CharacterDefinition character, BodyFigure figure, CharacterLook look, PoseData? pose)
    {
        var height = character.Body.Normalized().Height;
        var skin = FigureGeometry.ToSk(look.Color(CharacterDefinition.SkinSlot, character.Skin));
        var tiles = TileLookup(character);
        var stickers = Geometry(figure, look, pose, height, tiles);
        var bodySkin = FigureGeometry.Empty();
        foreach (var layer in figure.Layers.Where(l => l.HasBody))
            bodySkin = FigureGeometry.Union(bodySkin, FigureGeometry.LayerSkin(layer));

        // What's been painted in earlier layers - everything, and each sticker's own pieces.
        // Skin at a seam merges with whatever it lies over; a garment only with itself (a
        // sleeve with its shirt at the shoulder), so a shirt's hem over the trousers keeps its line.
        var items = new List<FigureItem>();
        var below = FigureGeometry.Empty();
        var belowOwn = stickers.Select(_ => FigureGeometry.Empty()).ToList();
        foreach (var layer in figure.Layers)
        {
            var painted = FigureGeometry.Empty();
            if (layer.HasBody)
            {
                var path = FigureGeometry.LayerSkin(layer);
                items.Add(new ShapeItem(path, skin, SeamMask(layer, below)));
                painted = FigureGeometry.Union(painted, FigureGeometry.Copy(path));
            }
            for (var i = 0; i < stickers.Count; i++)
            {
                var mask = SeamMask(layer, belowOwn[i]);
                using var clothes = ClothesCoverage(stickers, i, layer.Kind);
                using var hair = HairCoverage(look.Stickers, stickers, i, layer.Kind);
                var own = FigureGeometry.Empty();
                foreach (var item in StickerItems(look.Stickers[i], stickers[i], layer.Kind, look, bodySkin, clothes, hair, mask, height, tiles))
                {
                    items.Add(item);
                    own = FigureGeometry.Union(own, FigureGeometry.Copy(item.Area));
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

    /// <summary>
    /// Every worn sticker on the figure, in the look's order. Layers in one slot are fitted
    /// as they go on (<see cref="StickerCovers.FittedOver"/>): each cover a little looser
    /// than the loosest one on its region under it in that slot, so nothing underneath
    /// shows round its edge. One sticker per slot is drawn exactly as it's made.
    /// </summary>
    private static List<StickerGeometry> Geometry(BodyFigure figure, CharacterLook look, PoseData? pose, double height, Func<string, ArtFile?> tiles)
    {
        var result = new List<StickerGeometry>(look.Stickers.Count);
        var under = new Dictionary<string, Dictionary<BodyRegion, double>>(StringComparer.Ordinal);
        foreach (var worn in look.Stickers)
        {
            var below = under.TryGetValue(worn.Slot, out var slot) ? slot : null;
            var geometry = StickerGeometry.Of(figure, worn, look, pose, height, tiles, below);
            result.Add(geometry);
            if (geometry.Eases.Count == 0)
                continue;
            below ??= under[worn.Slot] = [];
            foreach (var (region, ease) in geometry.Eases)
                below[region] = Math.Max(below.GetValueOrDefault(region), ease);
        }
        return result;
    }

    /// <summary>
    /// What's worn under sticker <paramref name="exclude"/> in <paramref name="layer"/>: every
    /// other sticker's own cover pieces there (<see cref="PartClip.Clothes"/>) - what a print
    /// clips to, so it stays on the garment underneath instead of spilling past its edge. The
    /// caller disposes it.
    /// </summary>
    private static SKPath ClothesCoverage(List<StickerGeometry> stickers, int exclude, FigureLayerKind layer)
    {
        var result = FigureGeometry.Empty();
        for (var j = 0; j < stickers.Count; j++)
        {
            if (j == exclude)
                continue;
            foreach (var (part, piece) in stickers[j].Covers)
            {
                if (piece.Layer == layer && part.Blend != PartBlend.Cut && part.Clip is null)
                    result = FigureGeometry.Union(result, FigureGeometry.Copy(piece.Path));
            }
        }
        return result;
    }

    /// <summary>
    /// What's drawn as hair beside sticker <paramref name="exclude"/> in <paramref name="layer"/>: the
    /// drawn-art areas of every other sticker worn in a hair slot (<see cref="StickerSlots.IsHair"/>),
    /// not counting cut or clipped parts (<see cref="PartClip.Hair"/>) - what a streak clips to, so
    /// it stays on the hair instead of spilling past its edge. The caller disposes it.
    /// </summary>
    private static SKPath HairCoverage(IReadOnlyList<WornSticker> worn, List<StickerGeometry> stickers, int exclude, FigureLayerKind layer)
    {
        var result = FigureGeometry.Empty();
        for (var j = 0; j < stickers.Count; j++)
        {
            if (j == exclude || !StickerSlots.IsHair(worn[j].Slot))
                continue;
            foreach (var piece in stickers[j].Art)
            {
                if (piece.Layer == layer && piece.Part.Blend != PartBlend.Cut && piece.Part.Clip is null)
                    result = FigureGeometry.Union(result, FigureGeometry.Copy(piece.Area));
            }
        }
        return result;
    }

    /// <summary>
    /// A worn sticker on the figure: its cover pieces, its drawn parts and its typed text
    /// parts, each mapped into figure space - and <see cref="Eases"/>, how loose its own
    /// covers went on per region (not cut or clipped ones), for the layers over it in its slot.
    /// </summary>
    private sealed record StickerGeometry(List<(StickerPart Part, PartPiece Piece)> Covers, List<ArtPiece> Art, List<TextPiece> Text, IReadOnlyDictionary<BodyRegion, double> Eases)
    {
        /// <param name="under">The loosest cover per region among the layers under this one in its slot, or null for the bottom layer.</param>
        public static StickerGeometry Of(BodyFigure figure, WornSticker worn, CharacterLook look, PoseData? pose, double height, Func<string, ArtFile?> tiles,
            IReadOnlyDictionary<BodyRegion, double>? under = null)
        {
            var sticker = worn.Asset.Sticker;
            // A split eye (docs/sticker-system.md §21) picks its expression from "eyesLeft"/
            // "eyesRight" instead of the shared "eyes" key, once it's worn on one side.
            var expressionSlot = worn.Side is { } wornSide ? StickerSlots.SidedSlot(worn.Slot, wornSide) : worn.Slot;
            var variant = StickerArtPieces.VariantFor(sticker, expressionSlot, pose?.Expression, worn.Variant);
            var parts = sticker.Parts.Where(p => p.AppliesTo(variant)).ToList();
            var covers = new List<(StickerPart, PartPiece)>();
            var eases = new Dictionary<BodyRegion, double>();
            foreach (var part in parts)
            {
                if (part.Cover is not { } cover)
                    continue;
                var fitted = StickerCovers.FittedOver(cover, part.Region, under);
                covers.AddRange(StickerCovers.Pieces(figure, part, fitted, height).Select(p => (part, p)));
                if (part.Blend != PartBlend.Cut && part.Clip is null)
                    eases[part.Region] = Math.Max(eases.GetValueOrDefault(part.Region), fitted.EaseOrDefault);
            }

            var text = new List<TextPiece>();
            foreach (var part in parts.Where(p => p is { Art: not null, Text: not null }))
            {
                foreach (var (side, matrix, runs, color, bounds, anchor) in StickerArtPieces.MapText(figure, part, part.Art!, part.Text!, look))
                    text.Add(new TextPiece(part, LayerOf(figure, part, side), matrix, runs, color, BoundsArea(bounds, matrix), anchor));
            }

            var art = new List<ArtPiece>();
            var drawn = parts.Where(p => p is { Art: not null, Cover: null, Text: null }).ToList();
            if (drawn.Count > 0 && StickerArtPieces.ArtFor(worn.Asset, variant, figure.Angle) is { } parsed)
            {
                foreach (var part in drawn)
                {
                    foreach (var (side, elements, anchor) in StickerArtPieces.Map(figure, worn, part, part.Art!, parsed, look, height, tiles))
                        art.Add(new ArtPiece(part, LayerOf(figure, part, side), elements, anchor, StickerArtPieces.Area(elements, height)));
                }
            }
            return new StickerGeometry(covers, art, text, eases);
        }

        private static FigureLayerKind LayerOf(BodyFigure figure, StickerPart part, LimbSide side) => part.Depth switch
        {
            PartDepth.Back => FigureLayerKind.Back,
            PartDepth.Front => FigureLayerKind.Front,
            _ => figure.LayerOf(part.Region, side)
        };
    }

    /// <summary>One drawn part (one side of it, for a limb) in figure space: its elements and the area they cover.</summary>
    private sealed record ArtPiece(StickerPart Part, FigureLayerKind Layer, IReadOnlyList<ArtStroke> Elements, SKPoint? Anchor, SKPath Area);

    /// <summary>One typed text part (one side of it, for a limb) in figure space: its runs, colour, placement and the area it covers (its bounds rectangle, mapped - not its glyphs' own shape).</summary>
    private sealed record TextPiece(StickerPart Part, FigureLayerKind Layer, SKMatrix Matrix, IReadOnlyList<Lettering.TextRun> Runs, SKColor Color, SKPath Area, SKPoint? Anchor);

    /// <summary>A text piece's hit-test area: <paramref name="bounds"/> (its own drawing space), mapped through <paramref name="matrix"/> - a quad, not necessarily axis-aligned once turned.</summary>
    private static SKPath BoundsArea(SKRect bounds, SKMatrix matrix)
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(matrix.MapPoint(bounds.Left, bounds.Top));
        builder.LineTo(matrix.MapPoint(bounds.Right, bounds.Top));
        builder.LineTo(matrix.MapPoint(bounds.Right, bounds.Bottom));
        builder.LineTo(matrix.MapPoint(bounds.Left, bounds.Bottom));
        builder.Close();
        return builder.Detach();
    }

    /// <summary>
    /// Where a clipped item can be clicked: its area inside <paramref name="keep"/> - or, if
    /// that comes out empty (the piece is off the hair it's clipped to, or the intersection
    /// failed on awkward art), its whole area, so it can still be found and dragged back.
    /// Its drawing is clipped either way.
    /// </summary>
    private static SKPath ClippedArea(SKPath area, SKPath keep)
    {
        var clipped = FigureGeometry.Combine(area, keep, SKPathOp.Intersect);
        if (!clipped.IsEmpty)
            return clipped;
        clipped.Dispose();
        return FigureGeometry.Copy(area);
    }

    /// <summary>What <paramref name="clip"/> keeps a long-lived art or text item inside - an owned copy, since <paramref name="bodySkin"/>, <paramref name="own"/>, <paramref name="clothes"/> and <paramref name="hair"/> are disposed once this layer is done.</summary>
    private static SKPath? PersistedClip(PartClip? clip, SKPath bodySkin, SKPath own, SKPath clothes, SKPath hair) => clip switch
    {
        PartClip.Body => FigureGeometry.Copy(bodySkin),
        PartClip.Sticker => FigureGeometry.Copy(own),
        PartClip.Clothes => clothes.IsEmpty ? null : FigureGeometry.Copy(clothes),
        PartClip.Hair => hair.IsEmpty ? null : FigureGeometry.Copy(hair),
        _ => null
    };

    /// <summary>What one worn sticker paints in one layer: its covers merged per colour slot, then its drawn and typed parts - minus its cuts, clipped as asked.</summary>
    private static IEnumerable<FigureItem> StickerItems(WornSticker worn, StickerGeometry geometry, FigureLayerKind layer,
        CharacterLook look, SKPath bodySkin, SKPath clothes, SKPath hair, SKPath? mask, double height, Func<string, ArtFile?> tiles)
    {
        var here = geometry.Covers.Where(p => p.Piece.Layer == layer).ToList();
        var art = geometry.Art.Where(a => a.Layer == layer).ToList();
        var text = geometry.Text.Where(t => t.Layer == layer).ToList();
        if (here.Count == 0 && art.Count == 0 && text.Count == 0)
            yield break;
        var sticker = worn.Asset.Sticker;
        var cuts = FigureGeometry.Empty();
        foreach (var (_, piece) in here.Where(p => p.Part.Blend == PartBlend.Cut))
            cuts = FigureGeometry.Union(cuts, FigureGeometry.Copy(piece.Path));
        foreach (var piece in art.Where(a => a.Part.Blend == PartBlend.Cut))
            cuts = FigureGeometry.Union(cuts, FigureGeometry.Copy(piece.Area));
        foreach (var piece in text.Where(t => t.Part.Blend == PartBlend.Cut))
            cuts = FigureGeometry.Union(cuts, FigureGeometry.Copy(piece.Area));
        var own = FigureGeometry.Empty();
        foreach (var (_, piece) in geometry.Covers.Where(p => p.Part.Blend != PartBlend.Cut && p.Part.Clip is null))
            own = FigureGeometry.Union(own, FigureGeometry.Copy(piece.Path));

        foreach (var group in here.Where(p => p.Part.Blend != PartBlend.Cut).GroupBy(p => p.Part.Cover!.Color))
        {
            var fallback = sticker.Colors.TryGetValue(group.Key, out var c) ? c : ColorValue.FromHex("#9a9a9a");
            var path = FigureGeometry.Empty();
            foreach (var (part, piece) in group)
            {
                var shape = part.Clip switch
                {
                    PartClip.Body => FigureGeometry.Combine(piece.Path, bodySkin, SKPathOp.Intersect),
                    PartClip.Sticker => FigureGeometry.Combine(piece.Path, own, SKPathOp.Intersect),
                    PartClip.Clothes => clothes.IsEmpty ? FigureGeometry.Copy(piece.Path) : FigureGeometry.Combine(piece.Path, clothes, SKPathOp.Intersect),
                    PartClip.Hair => hair.IsEmpty ? FigureGeometry.Copy(piece.Path) : FigureGeometry.Combine(piece.Path, hair, SKPathOp.Intersect),
                    _ => FigureGeometry.Copy(piece.Path)
                };
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
            var fabric = look.FabricOf(group.Key) is { } f
                ? new FabricFill(group.Select(g => g.Piece).ToList(), ground, f, height, tiles, seed: DyeLayout.Seed(worn.Asset.Id.Value, group.Key))
                : null;
            yield return new ShapeItem(path, ground, mask, worn.Asset.Id, fabric);
        }

        foreach (var piece in art.Where(a => a.Part.Blend != PartBlend.Cut))
        {
            var keep = PersistedClip(piece.Part.Clip, bodySkin, own, clothes, hair);
            var area = keep is null ? FigureGeometry.Copy(piece.Area) : ClippedArea(piece.Area, keep);
            if (!cuts.IsEmpty)
            {
                var cut = FigureGeometry.Combine(area, cuts, SKPathOp.Difference);
                area.Dispose();
                area = cut;
            }
            if (area.IsEmpty)
            {
                area.Dispose();
                keep?.Dispose();
                continue;
            }
            yield return new ArtItem(piece.Elements, area, worn.Asset.Id, piece.Anchor, keep, cuts.IsEmpty ? null : FigureGeometry.Copy(cuts));
        }

        foreach (var piece in text.Where(t => t.Part.Blend != PartBlend.Cut))
        {
            var keep = PersistedClip(piece.Part.Clip, bodySkin, own, clothes, hair);
            var area = keep is null ? FigureGeometry.Copy(piece.Area) : ClippedArea(piece.Area, keep);
            if (!cuts.IsEmpty)
            {
                var cut = FigureGeometry.Combine(area, cuts, SKPathOp.Difference);
                area.Dispose();
                area = cut;
            }
            if (area.IsEmpty)
            {
                area.Dispose();
                keep?.Dispose();
                continue;
            }
            yield return new TextItem(piece.Runs, piece.Color, piece.Matrix, area, worn.Asset.Id, piece.Anchor, keep, cuts.IsEmpty ? null : FigureGeometry.Copy(cuts));
        }
        cuts.Dispose();
        own.Dispose();
    }

    /// <summary>The character's pattern and texture tiles, by name.</summary>
    private static Func<string, ArtFile?> TileLookup(CharacterDefinition character) =>
        name => character.Wardrobe.Tiles.TryGetValue(name, out var file) ? file : null;

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
