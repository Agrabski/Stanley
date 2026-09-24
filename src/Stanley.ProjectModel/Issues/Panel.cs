using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Issues;

/// <summary>
/// <c>issues/&lt;issueId&gt;/pages/&lt;pageId&gt;/panels/&lt;id&gt;.json</c> - one panel,
/// one file, so editing one panel's content touches exactly one file. Holds its own
/// character instances, bubbles and background reference; there is no shared scene
/// graph spanning panels.
/// </summary>
/// <param name="Bubbles">
/// Embedded directly, in z-order, the same way <see cref="CharacterInstances"/> is -
/// nothing outside this panel ever references a bubble by id.
/// </param>
public sealed record Panel(
    PanelId Id,
    PanelShape Shape,
    PanelBackground? Background,
    IReadOnlyList<CharacterInstance> CharacterInstances,
    IReadOnlyList<Bubble> Bubbles);
