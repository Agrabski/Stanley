using System.Text.Json.Serialization;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Characters;

/// <summary>How a sticker responds to expression state and the <c>build</c> slider.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<StickerKind>))]
public enum StickerKind
{
    /// <summary>One fixed variant; renders unaffected by expression or build.</summary>
    Static,

    /// <summary>Face sticker (eyes, mouth, eyebrows) with a variant per expression state.</summary>
    FaceExpressionVariant,

    /// <summary>Touches the body silhouette; a single variant is non-uniformly scaled to the current build value via its <see cref="StretchRegion"/>.</summary>
    BuildStretch,

    /// <summary>Opts out of stretching; declares a variant per build breakpoint and the renderer snaps to the nearest one.</summary>
    BuildBreakpoints
}

/// <summary>
/// <c>characters/&lt;characterId&gt;/stickers/&lt;id&gt;-slug/</c> - one reusable piece
/// of character art. <see cref="VariantNames"/> names the <c>variants/&lt;name&gt;/</c>
/// subfolders, each of which holds <c>front.svg</c>/<c>three-quarter.svg</c>/<c>profile.svg</c>
/// by convention (no path stored here - the folder layout is not optional). A
/// <see cref="StickerKind.BuildStretch"/> sticker additionally has a <c>stretch.json</c>
/// sibling, loaded separately as a <see cref="StretchRegion"/>.
/// </summary>
public sealed record Sticker(StickerId Id, string Name, string SlotName, StickerKind Kind, IReadOnlyList<string> VariantNames);
