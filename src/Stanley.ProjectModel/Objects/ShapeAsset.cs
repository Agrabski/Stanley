using System.Text.Json.Serialization;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Objects;

/// <summary>
/// <c>shapes/&lt;id&gt;-slug.json</c> in My Assets - the outline of a part of a character
/// built from shapes, kept so every comic's shape galleries can offer it under *My shapes*
/// (docs/shape-characters.md §9.3, docs/asset-packs.md §5). One file, like a pack: a shape
/// has no art of its own.
/// </summary>
/// <param name="Shape">
/// The outline in its own proportions: its longer side 1, centred across the unit box and
/// standing at its top (the box a hand or foot is kept in, <see cref="ModuleShapes.Fit"/>
/// with <c>keepProportions</c>). A gallery fits it to the part it's picked for - a head's or
/// torso's box is filled, and the part takes these proportions. Never carries a
/// <see cref="ModuleShape.Source"/> or <see cref="ModuleShape.MyAssetsVersion"/>.
/// </param>
public sealed record ShapeAsset(ShapeAssetId Id, string Name, ModuleShape Shape)
{
    /// <summary>What a comic's copy's <see cref="ModuleShape.Source"/> starts with, before the kept shape's id.</summary>
    public const string SourcePrefix = "my-assets:shapes/";

    /// <summary>The source a comic's copy carries while unmodified: <c>my-assets:shapes/&lt;id&gt;</c>.</summary>
    [JsonIgnore]
    public string Source => SourcePrefix + Id.Value;

    /// <summary>The kept shape a character's shape was copied from, if it's still an unmodified copy of one; null otherwise.</summary>
    public static ShapeAssetId? IdOf(ModuleShape shape) =>
        shape.Source is { } source && source.StartsWith(SourcePrefix, StringComparison.Ordinal)
        && ShapeAssetId.TryParse(source[SourcePrefix.Length..], null, out var id)
            ? id
            : null;
}
