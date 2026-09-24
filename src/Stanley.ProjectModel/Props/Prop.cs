using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Props;

/// <summary>
/// <c>props/&lt;id&gt;-slug/prop.json</c> - a reusable, project-level set piece
/// (furniture, windows, signage), the background equivalent of a character sticker.
/// <see cref="VariantNames"/> names its state variants (a door's "open"/"closed"), each a
/// <c>variants/&lt;name&gt;/art.svg</c> by convention - props aren't rigged and don't
/// switch camera angle, so unlike stickers there's no per-view-angle axis.
/// </summary>
public sealed record Prop(PropId Id, string Name, Point2D PivotPoint, IReadOnlyList<string> VariantNames);
