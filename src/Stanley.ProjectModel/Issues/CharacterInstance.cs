using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Poses;

namespace Stanley.ProjectModel.Issues;

/// <summary>
/// Sparse per-panel overrides on top of a character instance's revision (e.g. sunglasses
/// for one shot): each map replaces only the slots it names.
/// </summary>
public sealed record CharacterInstanceOverrides(
    SortedDictionary<string, IReadOnlyList<StickerId>>? ActiveStickerOverrides,
    SortedDictionary<string, ColorValue>? ColorSlotOverrides,
    SortedDictionary<string, Fabric>? FabricOverrides = null)
{
    public bool IsEmpty => (ActiveStickerOverrides is null || ActiveStickerOverrides.Count == 0)
        && (ColorSlotOverrides is null || ColorSlotOverrides.Count == 0)
        && (FabricOverrides is null || FabricOverrides.Count == 0);
}

/// <summary>
/// One character placed in a panel: a reference to a definition + revision (falling
/// back to the issue's default revision for that character when
/// <see cref="RevisionOverride"/> is null), plus the ad hoc pose it's posed in for this
/// panel - not a library <see cref="Pose"/> reference, since most panel poses are
/// one-off drags rather than saved library entries. Not itself a stable-id entity: it's
/// embedded directly in the one file (panel.json) that ever references it, and
/// addressed by its index in <see cref="Panel.CharacterInstances"/> (which is also its
/// z-order, back to front) - the same way bubbles are.
/// </summary>
public sealed record CharacterInstance(
    CharacterId CharacterId,
    CharacterPlacement Placement,
    CharacterRevisionId? RevisionOverride,
    PoseData Pose,
    CharacterInstanceOverrides? Overrides);
