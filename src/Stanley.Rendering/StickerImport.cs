using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Rendering;

/// <summary>
/// Stickers from art files (docs/sticker-system.md §13.2): a file drawn on a template
/// imports as-is - its slot, view and layers say what it is - and any other SVG or PNG
/// becomes one pinned part, centred on the slot's region and fitted to it. Also the
/// "Draw your own" sticker (a template to draw on) and taking in a new version of a
/// drawn file.
/// </summary>
public static class StickerImport
{
    /// <summary>The result of reading a file: the sticker (null if the file can't be read) and anything its art doesn't draw.</summary>
    public sealed record Result(StickerAsset? Asset, IReadOnlyList<string> Report, string? Error);

    /// <summary>
    /// A new sticker from <paramref name="file"/>, for <paramref name="slot"/> (a template's
    /// own slot wins) seen from <paramref name="view"/> (likewise), named
    /// <paramref name="name"/>.
    /// </summary>
    public static Result FromFile(string slot, string name, ArtFile file, ViewAngle view)
    {
        if (StickerSvg.ParseAny(file) is not { } art)
            return new Result(null, [], file.IsSvg ? "not an SVG file Stanley can read" : "not an image Stanley can read");
        var fromTemplate = art.Slot is { Length: > 0 } || art.View is { Length: > 0 };
        if (art.Slot is { Length: > 0 } templateSlot)
            slot = templateSlot;
        if (ViewOf(art.View) is { } templateView)
            view = templateView;

        IReadOnlyList<StickerPart> parts;
        if (fromTemplate && file.IsSvg)
            parts = PartsFrom(slot, art, []);
        else
        {
            // Anything else: one pinned part, its centre on the region's and fitted to it.
            var region = StickerSlots.Get(slot).Region;
            var bounds = art.Bounds(art.Part(ParsedArt.WholeFile));
            var (centre, size) = RegionBox(view, region);
            var scale = bounds.IsEmpty ? 1 : size / Math.Max(Math.Max(bounds.Width, bounds.Height), 1e-6);
            var offset = bounds.IsEmpty ? default : new Point2D(Math.Round(centre.X - bounds.MidX, 2), Math.Round(centre.Y - bounds.MidY, 2));
            parts = [new StickerPart(ParsedArt.WholeFile, region, Art: new PartArt(ArtMapping.Pin, offset, Math.Round(scale, 5)))];
        }
        var sticker = new Sticker(StickerId.New(), name, slot, parts, ColorsOf(art, new Dictionary<string, ColorValue>()), [Sticker.DefaultVariant]);
        var files = new Dictionary<string, ArtFile> { [FilePath(Sticker.DefaultVariant, view, file.IsSvg)] = file };
        return new Result(new StickerAsset(sticker, files), art.Report, null);
    }

    /// <summary>A sticker to draw yourself: the slot's usual parts, and the template as its art for <paramref name="view"/>.</summary>
    public static StickerAsset NewDrawn(string slot, string name, ViewAngle view)
    {
        var parts = StickerTemplates.PartsFor(slot).Select(p => p.ToPart()).ToList();
        var sticker = new Sticker(StickerId.New(), name, slot, parts, new SortedDictionary<string, ColorValue>(), [Sticker.DefaultVariant]);
        return new StickerAsset(sticker, new Dictionary<string, ArtFile> { [FilePath(Sticker.DefaultVariant, view, svg: true)] = ArtFile.Svg(StickerTemplates.Export(view, slot)) });
    }

    /// <summary>
    /// <paramref name="asset"/> with <paramref name="text"/> as its SVG for <paramref name="variant"/>
    /// (its first if null; added if it hasn't that one) seen from <paramref name="view"/>: a new layer becomes a new part,
    /// a new colour class a new colour slot, and it's the user's own from now on. Null, with
    /// the reason, if the text can't be read.
    /// </summary>
    public static Result WithArt(StickerAsset asset, ViewAngle view, string text, string? variant = null)
    {
        var file = ArtFile.Svg(text);
        if (StickerSvg.Parse(file) is not { } art)
            return new Result(null, [], "not an SVG file Stanley can read");
        var sticker = asset.Sticker;
        variant ??= FirstVariant(sticker);
        var files = new Dictionary<string, ArtFile>(asset.Files) { [FilePath(variant, view, svg: true)] = file };
        files.Remove(FilePath(variant, view, svg: false));
        var parts = sticker.Parts.Any(p => p.Name == ParsedArt.WholeFile) ? sticker.Parts : PartsFrom(sticker.Slot, art, sticker.Parts);
        // A drawing saved for a variant makes it one of the sticker's, even if it wasn't (any more - an undone "new expression").
        var variants = sticker.Variants.Contains(variant) ? sticker.Variants : [.. sticker.Variants, variant];
        var edited = sticker with { Parts = parts, Colors = ColorsOf(art, sticker.Colors), Variants = variants, Source = null };
        return new Result(new StickerAsset(edited, files), art.Report, null);
    }

    /// <summary>
    /// <paramref name="asset"/>'s art for <paramref name="variant"/> (its first if null) seen
    /// from <paramref name="view"/>, or a template for its slot if that isn't drawn yet - what
    /// "Draw your own" opens.
    /// </summary>
    public static string ArtToEdit(StickerAsset asset, ViewAngle view, string? variant = null) =>
        asset.Files.TryGetValue(FilePath(variant ?? FirstVariant(asset.Sticker), view, svg: true), out var file) && file.Text is { } text
            ? text
            : StickerTemplates.Export(view, asset.Sticker.Slot);

    /// <summary>
    /// <paramref name="asset"/> with a new variant <paramref name="variant"/>, drawn for now
    /// as a copy of <paramref name="copyOf"/> in every view that has - a new expression to
    /// draw from there. It's the user's own sticker from now on.
    /// </summary>
    public static StickerAsset WithVariant(StickerAsset asset, string variant, string copyOf)
    {
        var from = $"variants/{copyOf}/";
        var files = new Dictionary<string, ArtFile>(asset.Files);
        foreach (var (path, file) in asset.Files.Where(f => f.Key.StartsWith(from, StringComparison.Ordinal)))
            files[$"variants/{variant}/{path[from.Length..]}"] = file;
        var sticker = asset.Sticker with { Variants = [.. asset.Sticker.Variants.Where(v => v != variant), variant], Source = null };
        return new StickerAsset(sticker, files);
    }

    private static string FirstVariant(Sticker sticker) => sticker.Variants.Count > 0 ? sticker.Variants[0] : Sticker.DefaultVariant;

    public static string FilePath(string variant, ViewAngle view, bool svg) => $"variants/{variant}/{StickerAsset.ViewFileStem(view)}.{(svg ? "svg" : "png")}";

    /// <summary>Where a region sits in template space and a comfortable size for a picture on it.</summary>
    public static (Point2D Centre, double Size) RegionBox(ViewAngle view, BodyRegion region)
    {
        var t = RegionMapping.Template(view).Regions;
        var u = RegionMapping.TemplateUnits;
        Point2D Scaled(Point2D p) => new(p.X * u, p.Y * u);
        Point2D Mid(BodyCapsule c) => Scaled(new Point2D((c.From.X + c.To.X) / 2, (c.From.Y + c.To.Y) / 2));
        switch (region)
        {
            case BodyRegion.Head:
                return (Scaled(t.Head.Center), t.Head.RadiusX * u);
            case BodyRegion.Neck:
                return (Mid(t.Neck), 2 * t.Neck.FromRadius * u);
            case BodyRegion.Arm:
                return (Mid(t.Arm(LimbSide.Right).Upper), 1.6 * t.Arm(LimbSide.Right).Upper.FromRadius * u);
            case BodyRegion.Leg:
                return (Mid(t.Leg(LimbSide.Right).Upper), 1.6 * t.Leg(LimbSide.Right).Upper.FromRadius * u);
            case BodyRegion.Hand:
                return (Scaled(t.Hand(LimbSide.Right).Center), 1.6 * t.Hand(LimbSide.Right).RadiusX * u);
            case BodyRegion.Foot:
                return (Scaled(t.Foot(LimbSide.Right).Center), 1.6 * t.Foot(LimbSide.Right).RadiusY * u);
            default:
                // The upper chest - where a print or a badge goes.
                var torso = t.Torso;
                var y = torso.Top + 0.3 * (torso.Bottom - torso.Top);
                var (left, right) = torso.RowAt(y);
                return (Scaled(new Point2D((left + right) / 2, y)), 0.45 * (right - left) * u);
        }
    }

    /// <summary>Parts for the named layers of <paramref name="art"/>: existing parts kept, the slot's usual ones for their layer names, anything else a new part on the slot's region.</summary>
    private static IReadOnlyList<StickerPart> PartsFrom(string slot, ParsedArt art, IReadOnlyList<StickerPart> existing)
    {
        var info = StickerSlots.Get(slot);
        var usual = StickerTemplates.PartsFor(slot);
        var parts = existing.ToList();
        foreach (var (layer, elements) in art.Layers)
        {
            if (elements.Count == 0 || parts.Any(p => p.Name == layer))
                continue;
            var name = layer.Length == 0 ? ParsedArt.WholeFile : layer;
            if (parts.Any(p => p.Name == name))
                continue;
            parts.Add(usual.FirstOrDefault(p => p.Name == name)?.ToPart()
                ?? new StickerPart(name, info.Region, Art: new PartArt(info.IsFace ? ArtMapping.Pin : ArtMapping.Warp)));
        }
        if (parts.Count == 0)
            parts.AddRange(usual.Select(p => p.ToPart()));
        return parts;
    }

    /// <summary>The colour slots the art's classes name, each defaulting to the first colour drawn in it; <paramref name="existing"/> defaults are kept.</summary>
    private static SortedDictionary<string, ColorValue> ColorsOf(ParsedArt art, IReadOnlyDictionary<string, ColorValue> existing)
    {
        var colors = new SortedDictionary<string, ColorValue>(existing.ToDictionary(), StringComparer.Ordinal);
        foreach (var e in art.Layers.Values.SelectMany(l => l))
        {
            if (e.Slot is { } slot && (e.Fill ?? e.Stroke) is { } paint && !colors.ContainsKey(slot))
                colors[slot] = ColorValue.FromHex($"#{paint.Color.Red:x2}{paint.Color.Green:x2}{paint.Color.Blue:x2}");
        }
        return colors;
    }

    private static ViewAngle? ViewOf(string? stem) => stem switch
    {
        "front" => ViewAngle.Front,
        "profile" => ViewAngle.Profile,
        "three-quarter" => ViewAngle.ThreeQuarter,
        _ => null
    };
}
