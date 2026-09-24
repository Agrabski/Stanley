using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Characters;

/// <summary>A sticker slot's fixed z-order and the full catalogue of stickers ever available for it.</summary>
public sealed record StickerSlotDefinition(int ZOrder, IReadOnlyList<StickerId> Catalogue);

/// <summary>
/// <c>characters/&lt;id&gt;-slug/character.json</c> - the "wardrobe": body, skeleton, named
/// colour slots, and the sticker catalogue per slot. Nothing here is issue-specific; it
/// only grows as the series goes. Which stickers/colours are actually active on a given
/// look lives in a <see cref="CharacterRevision"/>, not here.
/// </summary>
/// <param name="Body">The numbers the body is generated from (<see cref="BodyRig"/>). Every placed instance draws from it, so changing it changes the character everywhere.</param>
/// <param name="Skeleton">Manual joint overrides on top of the rest layout <see cref="BodyRig"/> generates from <paramref name="Body"/> - the rig-editor escape hatch; empty for a body made only with sliders.</param>
/// <param name="ColorSlots">Named colours; <see cref="SkinSlot"/> fills the body.</param>
public sealed record CharacterDefinition(
    CharacterId Id,
    string Name,
    BodyShape Body,
    Skeleton Skeleton,
    SortedDictionary<string, ColorValue> ColorSlots,
    SortedDictionary<string, StickerSlotDefinition> StickerSlots)
{
    /// <summary>The colour slot the body is filled with.</summary>
    public const string SkinSlot = "skin";

    public static ColorValue DefaultSkin { get; } = ColorValue.FromHex("#f2c9a4");

    public ColorValue Skin => ColorSlots.TryGetValue(SkinSlot, out var skin) ? skin : DefaultSkin;

    /// <summary>A brand-new character: default body, default skin, no overrides or stickers.</summary>
    public static CharacterDefinition Create(string name, BodyShape? body = null) => new(
        CharacterId.New(),
        name,
        body ?? BodyShape.Default,
        new Skeleton([]),
        new SortedDictionary<string, ColorValue> { [SkinSlot] = DefaultSkin },
        new SortedDictionary<string, StickerSlotDefinition>());
}
