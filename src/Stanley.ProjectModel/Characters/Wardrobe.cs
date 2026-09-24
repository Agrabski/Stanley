using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// One art file exactly as on disk - SVG text or PNG bytes - so saving writes it back byte
/// for byte (Inkscape metadata survives, an unchanged save is an empty diff). Parsing it
/// is the renderer's business.
/// </summary>
public sealed record ArtFile(string? Text, byte[]? Bytes)
{
    public static ArtFile Svg(string text) => new(text, null);

    public static ArtFile Png(byte[] bytes) => new(null, bytes);

    public bool IsSvg => Text is not null;

    public byte[] ToBytes() => Bytes ?? System.Text.Encoding.UTF8.GetBytes(Text ?? "");

    public bool SameContent(ArtFile other) =>
        Text is not null ? Text == other.Text : other.Bytes is not null && Bytes is not null && Bytes.AsSpan().SequenceEqual(other.Bytes);
}

/// <summary>A sticker and its art files, keyed by path inside the sticker's folder (<c>variants/happy/front.svg</c>, always '/').</summary>
public sealed record StickerAsset(Sticker Sticker, IReadOnlyDictionary<string, ArtFile> Files)
{
    public StickerId Id => Sticker.Id;

    public static string ViewFileStem(ViewAngle view) => view switch
    {
        ViewAngle.ThreeQuarter => "three-quarter",
        ViewAngle.Profile => "profile",
        _ => "front"
    };

    /// <summary>The art for <paramref name="variant"/> seen from exactly <paramref name="view"/>, if drawn (SVG preferred over PNG).</summary>
    public ArtFile? ArtFor(string variant, ViewAngle view)
    {
        var stem = $"variants/{variant}/{ViewFileStem(view)}";
        return Files.TryGetValue(stem + ".svg", out var svg) ? svg : Files.TryGetValue(stem + ".png", out var png) ? png : null;
    }

    /// <summary>The views this variant has art for.</summary>
    public IEnumerable<ViewAngle> DrawnViews(string variant) =>
        Enum.GetValues<ViewAngle>().Where(v => ArtFor(variant, v) is not null);

    public bool HasArt => Sticker.Parts.Any(p => p.Art is not null);
}

/// <summary>
/// Everything a character owns besides <c>character.json</c>: its stickers (worn or not -
/// the wardrobe) and its pattern/texture tiles, as loaded from its folder. Carried on
/// <see cref="CharacterDefinition.Wardrobe"/> in memory, so an edit to any of it is an edit
/// to the character (one undo step, one redraw everywhere).
/// </summary>
public sealed record Wardrobe(IReadOnlyDictionary<StickerId, StickerAsset> Stickers, IReadOnlyDictionary<string, ArtFile> Tiles)
{
    public static Wardrobe Empty { get; } = new(new Dictionary<StickerId, StickerAsset>(), new Dictionary<string, ArtFile>());

    public StickerAsset? Find(StickerId id) => Stickers.TryGetValue(id, out var asset) ? asset : null;

    public Wardrobe With(StickerAsset asset)
    {
        var stickers = new Dictionary<StickerId, StickerAsset>(Stickers) { [asset.Id] = asset };
        return this with { Stickers = stickers };
    }

    public Wardrobe Without(StickerId id)
    {
        if (!Stickers.ContainsKey(id))
            return this;
        var stickers = new Dictionary<StickerId, StickerAsset>(Stickers);
        stickers.Remove(id);
        return this with { Stickers = stickers };
    }

    public Wardrobe WithTile(string name, ArtFile file)
    {
        var tiles = new Dictionary<string, ArtFile>(Tiles) { [name] = file };
        return this with { Tiles = tiles };
    }

    public Wardrobe WithoutTile(string name)
    {
        if (!Tiles.ContainsKey(name))
            return this;
        var tiles = new Dictionary<string, ArtFile>(Tiles);
        tiles.Remove(name);
        return this with { Tiles = tiles };
    }
}
