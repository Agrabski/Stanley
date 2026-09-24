using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Characters;

/// <summary>A sticker slot's fixed z-order and the full catalogue of stickers ever available for it.</summary>
public sealed record StickerSlotDefinition(int ZOrder, IReadOnlyList<StickerId> Catalogue);

/// <summary>
/// <c>characters/&lt;id&gt;-slug/character.json</c> - the "wardrobe": skeleton, named
/// colour slots, and the sticker catalogue per slot. Nothing here is issue-specific; it
/// only grows as the series goes. Which stickers/colours are actually active on a given
/// look lives in a <see cref="CharacterRevision"/>, not here.
/// </summary>
public sealed record CharacterDefinition(
    CharacterId Id,
    string Name,
    Skeleton Skeleton,
    SortedDictionary<string, ColorValue> ColorSlots,
    SortedDictionary<string, StickerSlotDefinition> StickerSlots);
