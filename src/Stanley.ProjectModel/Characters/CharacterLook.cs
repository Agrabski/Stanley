using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Characters;

/// <summary>A sticker being worn: which slot, and where in that slot's stack (0 = bottom).</summary>
public sealed record WornSticker(StickerAsset Asset, string Slot, int StackIndex);

/// <summary>
/// What a character wears, in which colours and fabrics, after the chain definition →
/// named look → panel (docs/sticker-system.md §9, §12). <see cref="Stickers"/> are in paint
/// order: by slot z-order, then stacking order.
/// </summary>
public sealed record CharacterLook(
    IReadOnlyList<WornSticker> Stickers,
    IReadOnlyDictionary<string, ColorValue> Colors,
    IReadOnlyDictionary<string, Fabric> Fabrics)
{
    public ColorValue Color(string slot, ColorValue fallback) => Colors.TryGetValue(slot, out var c) ? c : fallback;

    public Fabric? FabricOf(string slot) => Fabrics.TryGetValue(slot, out var f) && !f.IsPlain ? f : null;
}

public static class CharacterLooks
{
    /// <summary>
    /// A look id meaning "the character's own default look", for a panel that wants it
    /// even though its issue uses a named look. Never a real revision's id (those are
    /// minted), so resolving it finds no revision - the default.
    /// </summary>
    public static CharacterRevisionId DefaultLook { get; } = CharacterRevisionId.FromValue("default");

    /// <summary>The look an instance is drawn in: its own, else its issue's for that character (<paramref name="issueLooks"/>), else none (the default).</summary>
    public static CharacterRevisionId? LookOf(CharacterInstance instance, IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks) =>
        instance.RevisionOverride ?? (issueLooks is not null && issueLooks.TryGetValue(instance.CharacterId, out var look) ? look : null);

    /// <summary>The named look <paramref name="look"/> names on <paramref name="character"/>, or null for the default (or a look that's gone).</summary>
    public static CharacterRevision? Revision(CharacterDefinition character, CharacterRevisionId? look) =>
        look is { } id && character.Revisions.TryGetValue(id, out var revision) ? revision : null;

    /// <summary>
    /// Resolves what <paramref name="character"/> wears: its own default stickers per slot,
    /// replaced slot by slot by <paramref name="revision"/>, then by <paramref name="overrides"/>
    /// (one panel). Colours and fabrics go stickers' defaults → character → revision → panel.
    /// Ids missing from the wardrobe (a hand-edited file) are skipped.
    /// </summary>
    public static CharacterLook Resolve(CharacterDefinition character, CharacterRevision? revision = null, CharacterInstanceOverrides? overrides = null)
    {
        var slots = new SortedDictionary<string, IReadOnlyList<StickerId>>(character.Stickers ?? new SortedDictionary<string, IReadOnlyList<StickerId>>(), StringComparer.Ordinal);
        foreach (var (slot, ids) in revision?.ActiveStickers ?? [])
            slots[slot] = ids;
        foreach (var (slot, ids) in overrides?.ActiveStickerOverrides ?? [])
            slots[slot] = ids;

        var worn = slots
            .SelectMany(kv => kv.Value.Select((id, i) => (Slot: kv.Key, Id: id, Index: i)))
            .Select(w => character.Wardrobe.Find(w.Id) is { } asset ? new WornSticker(asset, w.Slot, w.Index) : null)
            .OfType<WornSticker>()
            .OrderBy(w => StickerSlots.ZOrder(w.Slot))
            .ThenBy(w => w.Slot, StringComparer.Ordinal)
            .ThenBy(w => w.StackIndex)
            .ToList();

        var colors = new Dictionary<string, ColorValue>(StringComparer.Ordinal);
        var fabrics = new Dictionary<string, Fabric>(StringComparer.Ordinal);
        foreach (var w in worn)
        {
            foreach (var (slot, color) in w.Asset.Sticker.Colors ?? [])
                colors.TryAdd(slot, color);
            foreach (var (slot, fabric) in w.Asset.Sticker.Fabrics ?? [])
                fabrics.TryAdd(slot, fabric);
        }
        Overlay(colors, character.ColorSlots);
        Overlay(colors, revision?.ColorSlotValues);
        Overlay(colors, overrides?.ColorSlotOverrides);
        Overlay(fabrics, character.Fabrics);
        Overlay(fabrics, revision?.FabricValues);
        Overlay(fabrics, overrides?.FabricOverrides);
        return new CharacterLook(worn, colors, fabrics);
    }

    /// <summary>A short string that differs whenever <paramref name="revision"/> or <paramref name="overrides"/> would change the look - for render caches.</summary>
    public static string CacheKey(CharacterRevision? revision, CharacterInstanceOverrides? overrides) =>
        (revision is null ? "" : revision.Id.Value + "@" + ProjectJson.Serialize(revision)) + "|" +
        (overrides is null || overrides.IsEmpty ? "" : ProjectJson.Serialize(overrides));

    private static void Overlay<T>(Dictionary<string, T> into, IReadOnlyDictionary<string, T>? values)
    {
        foreach (var (slot, value) in values ?? new Dictionary<string, T>())
            into[slot] = value;
    }
}
