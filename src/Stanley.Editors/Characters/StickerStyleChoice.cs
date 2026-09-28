using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Poses;
namespace Stanley.Editors;

/// <summary>
/// One way to wear the selected sticker, on the Sticker tab's "Worn" gallery (a hood up or
/// down, a cap's brim forward or back): one of its variants, previewed on this character
/// wearing it that way.
/// </summary>
/// <param name="Sticker">The sticker it's a style of.</param>
/// <param name="Variant">The variant key it's stored under.</param>
/// <param name="Preview">The character (in the look being edited) wearing the sticker this way.</param>
/// <param name="Closeup">Worn on the head: previewed as a close-up, like the hat gallery.</param>
/// <param name="Pose">The stage's preview pose (its expression), so the preview matches the stage; null at rest.</param>
/// <param name="Angle">The stage's view, front or side.</param>
public sealed record StickerStyleChoice(StickerId Sticker, string Variant, string Label, CharacterDefinition Preview, bool IsCurrent, bool Closeup, PoseData? Pose, ViewAngle Angle)
{
	public string Tip => IsCurrent ? $"{Label} - wearing it this way" : $"Wear it {Label.ToLowerInvariant()}";
}
