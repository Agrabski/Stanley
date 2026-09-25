using System.Text.Json.Serialization;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// <c>characters/&lt;id&gt;-slug/character.json</c> - a character: body, skeleton, named
/// colour slots (with their fabrics), and what it wears by default. Nothing here is
/// issue-specific. A named look (<see cref="CharacterRevision"/>) or a single panel can
/// swap what's worn per slot.
/// </summary>
/// <param name="Body">The numbers the body is generated from (<see cref="BodyRig"/>). Every placed instance draws from it, so changing it changes the character everywhere.</param>
/// <param name="Skeleton">Manual joint overrides on top of the rest layout <see cref="BodyRig"/> generates from <paramref name="Body"/> - the rig-editor escape hatch; empty for a body made only with sliders.</param>
/// <param name="ColorSlots">Named colours the user picked; <see cref="SkinSlot"/> fills the body. A slot the character hasn't set falls back to its stickers' defaults.</param>
/// <param name="Stickers">Slot -&gt; the stickers worn by default, bottom to top (ids in <see cref="Wardrobe"/>).</param>
/// <param name="Fabrics">Colour slot -&gt; pattern/texture on top of its colour; absent when there are none.</param>
public sealed record CharacterDefinition(
    CharacterId Id,
    string Name,
    BodyShape Body,
    Skeleton Skeleton,
    SortedDictionary<string, ColorValue> ColorSlots,
    SortedDictionary<string, IReadOnlyList<StickerId>> Stickers,
    SortedDictionary<string, Fabric>? Fabrics = null)
{
    /// <summary>
    /// The character's stickers (worn or not) and pattern tiles, loaded from its folder -
    /// not part of <c>character.json</c> (each sticker is its own folder), but part of the
    /// character in memory, so the editor's undo and every renderer see them.
    /// </summary>
    [JsonIgnore]
    public Wardrobe Wardrobe { get; init; } = Wardrobe.Empty;

    /// <summary>The named looks (<c>revisions/</c>), loaded with the character, by id.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<CharacterRevisionId, CharacterRevision> Revisions { get; init; } = new Dictionary<CharacterRevisionId, CharacterRevision>();

    /// <summary>A character read from a file with missing collections gets empty ones.</summary>
    public CharacterDefinition Normalized() =>
        Body is not null && Skeleton is not null && ColorSlots is not null && Stickers is not null
            ? this
            : this with
            {
                Body = Body ?? BodyShape.Default,
                Skeleton = Skeleton ?? new Skeleton([]),
                ColorSlots = ColorSlots ?? new SortedDictionary<string, ColorValue>(),
                Stickers = Stickers ?? new SortedDictionary<string, IReadOnlyList<StickerId>>()
            };

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
        new SortedDictionary<string, IReadOnlyList<StickerId>>());
}
