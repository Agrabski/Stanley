using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;

namespace Stanley.StickerLibrary;

/// <summary>One piece of a hairstyle: a library sticker's key (<c>hairFringe/blunt</c>) and the style it's worn in, or null for its default.</summary>
public sealed record HairstylePiece(string Key, string? Style = null);

/// <summary>
/// A named hairstyle (docs: modular hair): a recipe of library pieces, put on together in
/// one click. <paramref name="Replaces"/> is the library key of the old whole-hairstyle
/// sticker it stands in for (<c>hair/bob</c>), if any - a character still wearing that one
/// is offered a switch.
/// </summary>
public sealed record Hairstyle(string Name, IReadOnlyList<HairstylePiece> Pieces, string? Replaces = null);

/// <summary>The starter hairstyles, built from the library's hair pieces, in the Hairstyles tab's order.</summary>
public static class Hairstyles
{
    /// <summary>The top put on with a piece when nothing's on top yet, so it never floats on a bald crown.</summary>
    public const string DefaultTop = "hairTop/smooth";

    private static HairstylePiece P(string key, string? style = null) => new(key, style);

    public static IReadOnlyList<Hairstyle> All { get; } =
    [
        new("Short", [P("hairTop/smooth"), P("hairFringe/wispy"), P("hairBack/nape")], "hair/short"),
        new("Bob", [P("hairTop/smooth"), P("hairFringe/blunt"), P("hairSides/chin"), P("hairBack/bob")], "hair/bob"),
        new("Long", [P("hairTop/smooth", "middle"), P("hairSides/long"), P("hairBack/long")], "hair/long"),
        new("Ponytail", [P("hairTop/smooth"), P("hairFringe/wispy"), P("hairExtras/ponytail")], "hair/ponytail"),
        new("Curly", [P("hairTop/curly"), P("hairSides/curly"), P("hairBack/curly")], "hair/curly"),
        new("Bun", [P("hairTop/smooth"), P("hairBack/nape"), P("hairExtras/bun")], "hair/bun"),
        new("Pixie", [P("hairTop/spiky"), P("hairFringe/choppy"), P("hairBack/nape")]),
        new("Pigtails", [P("hairTop/smooth", "middle"), P("hairFringe/blunt"), P("hairExtras/pigtails")]),
        new("Space buns", [P("hairTop/smooth", "middle"), P("hairFringe/wispy"), P("hairExtras/space-buns")]),
        new("Side-swept", [P("hairTop/smooth", "left"), P("hairFringe/side-swept"), P("hairSides/chin"), P("hairBack/shoulder")]),
        new("Emo", [P("hairTop/smooth", "left"), P("hairFringe/side-swept"), P("hairSides/choppy"), P("hairBack/shoulder")]),
        new("Scene", [P("hairTop/teased"), P("hairFringe/side-swept"), P("hairSides/choppy"), P("hairBack/layered")]),
    ];

    public static Hairstyle? Get(string name) => All.FirstOrDefault(h => h.Name == name);

    /// <summary>Whether every piece of <paramref name="style"/> is in the library - a preset whose art isn't drawn yet isn't offered.</summary>
    public static bool IsAvailable(Hairstyle style) => style.Pieces.All(p => StickerLibrary.Find(p.Key) is not null);

    /// <summary>The hairstyles whose pieces are all in the library.</summary>
    public static IEnumerable<Hairstyle> Available => All.Where(IsAvailable);

    /// <summary>The hairstyle that replaces the old whole-hairstyle sticker <paramref name="sticker"/> - only while it's an unmodified library copy (one you've edited is yours).</summary>
    public static Hairstyle? ForLegacy(Sticker sticker) =>
        sticker.Source is { } source && source.StartsWith(StickerLibrary.SourcePrefix, StringComparison.Ordinal)
            ? All.FirstOrDefault(h => h.Replaces is { } key && StickerLibrary.SourcePrefix + key == source && IsAvailable(h))
            : null;

    /// <summary>
    /// <paramref name="style"/>'s pieces for <paramref name="character"/>, each with its style:
    /// the character's own unmodified copy of a piece if it has one (so trying styles on
    /// doesn't pile up copies), else a fresh copy from the library. Pieces missing from the
    /// library are skipped.
    /// </summary>
    public static IReadOnlyList<(StickerAsset Asset, string? Style)> PiecesFor(Hairstyle style, CharacterDefinition character)
    {
        var result = new List<(StickerAsset, string?)>();
        foreach (var piece in style.Pieces)
        {
            if (Piece(piece.Key, character) is { } asset)
                result.Add((asset, piece.Style));
        }
        return result;
    }

    /// <summary>The library piece <paramref name="key"/> for <paramref name="character"/>: its own unmodified copy if it has one, else a fresh copy - or null if the library hasn't got it.</summary>
    public static StickerAsset? Piece(string key, CharacterDefinition character)
    {
        var source = StickerLibrary.SourcePrefix + key;
        var owned = character.Wardrobe.Stickers.Values.FirstOrDefault(a => a.Sticker.Source == source);
        return owned ?? StickerLibrary.Find(key)?.Instantiate();
    }

    /// <summary>
    /// The hairstyle <paramref name="character"/> wears exactly - the same library pieces (as
    /// unmodified copies) in the same styles, and nothing else in the hairdo's slots - or null
    /// for your own mix (or bald).
    /// </summary>
    public static Hairstyle? Matching(CharacterDefinition character)
    {
        var worn = StickerSlots.Hairdo
            .SelectMany(slot => character.Stickers.TryGetValue(slot, out var ids) ? ids : [])
            .Select(id => character.Wardrobe.Find(id))
            .ToList();
        if (worn.Count == 0 || worn.Any(a => a?.Sticker.Source is null))
            return null;
        var wearing = worn.Select(a => (Key: a!.Sticker.Source![StickerLibrary.SourcePrefix.Length..], Style: (string?)StyleOf(character, a)))
            .OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        return Available.FirstOrDefault(h =>
            h.Pieces.Select(p => (p.Key, Style: p.Style ?? DefaultStyleOf(p.Key)))
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .SequenceEqual(wearing));
    }

    private static string StyleOf(CharacterDefinition character, StickerAsset asset) =>
        character.StickerVariants is { } styles && styles.TryGetValue(asset.Id, out var style) ? style : asset.Sticker.VariantFor(asset.Sticker.Slot, null);

    private static string? DefaultStyleOf(string key) => StickerLibrary.Find(key)?.Asset.Sticker is { } s ? s.VariantFor(s.Slot, null) : null;

    /// <summary>
    /// Whether putting a hairstyle on would lose something: the head wears hair that isn't
    /// exactly a hairstyle - your own mix. Bald, a hairstyle as it came, or just an old
    /// whole-hairstyle sticker from the library lose nothing.
    /// </summary>
    public static bool IsOwnMix(CharacterDefinition character)
    {
        var worn = StickerSlots.Hairdo.SelectMany(slot => character.Stickers.TryGetValue(slot, out var ids) ? ids : []).ToList();
        if (worn.Count == 0 || Matching(character) is not null)
            return false;
        return !(worn.Count == 1 && character.Wardrobe.Find(worn[0]) is { } only && ForLegacy(only.Sticker) is not null);
    }
}
