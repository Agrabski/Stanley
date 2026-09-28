using System.Text.Json.Serialization;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Objects;

/// <summary>
/// A kind of asset My Assets can hold (docs/asset-packs.md §5). Only the two kinds #99 and
/// the original "My Characters" ask need are usable yet (<see cref="MyAssets"/> only stores
/// these); the rest are added one at a time in a later slice (§10 item 8).
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter<AssetKind>))]
public enum AssetKind
{
    ObjectGroup,
    Character
}

/// <summary>One asset in a pack: its kind (so a pack can freely mix kinds, docs/asset-packs.md §6.2) and its id's raw token.</summary>
public sealed record AssetPackMember(AssetKind Kind, string Id);

/// <summary>
/// <c>packs/&lt;id&gt;-slug.json</c> - a named, user-built collection sorting My Assets like
/// an album (docs/asset-packs.md §2.10): a name and a list of members, any mix of kinds.
/// Deleting a pack never deletes what's in it. <see cref="Members"/> is stored sorted (by
/// kind, then id) so adding one is a one-line diff, mirroring
/// docs/my-characters.md §6.2's <c>CharacterGroup</c>.
/// </summary>
public sealed record AssetPack(AssetPackId Id, string Name, IReadOnlyList<AssetPackMember> Members);
