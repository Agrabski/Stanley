using System.Text.Json.Serialization;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.ProjectModel.Objects;

/// <summary>
/// <c>objects/&lt;id&gt;-slug/group.json</c> - the out-of-line, canonical copy of a kept
/// object group (docs/asset-packs.md §3, §7.1): a <see cref="GroupElement"/> given its own
/// entity folder once it's been kept in My Assets, so a panel can reference it by id
/// (<see cref="GroupElement.SourceId"/>) instead of only holding it inline. Self-contained,
/// like a sticker's own folder: <see cref="ArtFiles"/> holds whatever its
/// <see cref="Children"/>' picture elements reference, under this group's own
/// <c>art/</c> folder rather than an issue's shared one.
/// </summary>
/// <param name="MyAssetsVersion">The My Assets fingerprint this copy last matched; absent for a group that exists only in this comic's <c>objects/</c> folder and was never also kept in My Assets.</param>
public sealed record ObjectGroup(ObjectGroupId Id, string Name, IReadOnlyList<PanelElement> Children, string? MyAssetsVersion = null)
{
    /// <summary>The art files <see cref="Children"/>'s picture elements reference, keyed by name under this group's own <c>art/</c> folder - not part of <c>group.json</c>, loaded alongside it.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, ArtFile> ArtFiles { get; init; } = new Dictionary<string, ArtFile>();
}
