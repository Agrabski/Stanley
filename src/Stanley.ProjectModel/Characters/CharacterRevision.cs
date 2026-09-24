using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// <c>characters/&lt;characterId&gt;-slug/revisions/&lt;id&gt;-slug.json</c> - a
/// user-named, project-level snapshot of a character's look, reused freely across
/// issues (not auto-generated or locked to one issue). Redesigns, aging up, and
/// "Post-Haircut" are all just new revisions; earlier issues keep referencing whichever
/// revision they already point to.
/// </summary>
/// <param name="ActiveStickers">Slot name -&gt; ordered, stacked list of simultaneously-active sticker ids.</param>
/// <param name="ColorSlotValues">Slot name -&gt; colour, overriding the definition's default for that slot.</param>
/// <param name="ProportionOverride">Sparse skeleton proportion override (aging up, redesigns, build-driven width) - only moved bones present.</param>
/// <param name="Build">Continuous 0 (slim) - 1 (heavy) body-type parameter.</param>
public sealed record CharacterRevision(
    CharacterRevisionId Id,
    CharacterId CharacterId,
    string Name,
    SortedDictionary<string, IReadOnlyList<StickerId>> ActiveStickers,
    SortedDictionary<string, ColorValue> ColorSlotValues,
    Skeleton? ProportionOverride,
    double? Build);
