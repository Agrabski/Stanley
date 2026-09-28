using System.Text.Json.Serialization;
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
/// <param name="Elements">
/// Drawn shapes, text and pictures, in z-order; each is behind or in front of the
/// characters (<see cref="PanelElement.Layer"/>). Absent in panel files written before
/// elements existed, which read as none.
/// </param>
/// <param name="Borderless">No border drawn around the panel - an open panel, or a title page's
/// background. Written only when set, so older panel files read unchanged.</param>
/// <param name="BorderStyle">How the border is drawn when there is one - its colour, thickness and
/// dashes (Shape Outline); null is the usual black ink line. Written only when set.</param>
/// <param name="Kind">What <see cref="Shape"/> means beyond its raw anchors (Insert › Thought
/// cloud): <see cref="PanelKind.Rectangle"/> unless it says otherwise, so a resize regenerates
/// the right outline instead of stretching it. Written only when not the default.</param>
/// <param name="Trail">A thought cloud's trail towards the thinker (<see cref="PanelKind.Cloud"/>
/// only) - absent for an ordinary panel, and for a cloud until one is added. Written only when set.</param>
public sealed record Panel(
    PanelId Id,
    PanelShape Shape,
    PanelBackground? Background,
    IReadOnlyList<CharacterInstance> CharacterInstances,
    IReadOnlyList<Bubble> Bubbles,
    IReadOnlyList<PanelElement> Elements = null!,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Borderless = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PanelBorderStyle? BorderStyle = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] PanelKind Kind = PanelKind.Rectangle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ThoughtTrail? Trail = null)
{
    public IReadOnlyList<PanelElement> Elements { get; init; } = Elements ?? [];
}

/// <summary>A panel border drawn other than in the usual black ink line: in <paramref name="Color"/>, <paramref name="WidthMm"/> thick, in <paramref name="Dash"/>'s pattern (a flashback's dashed frame).</summary>
public sealed record PanelBorderStyle(ColorValue Color, double WidthMm, LineDash Dash = LineDash.Solid);
