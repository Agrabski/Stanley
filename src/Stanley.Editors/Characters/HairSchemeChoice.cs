using Avalonia.Media;
using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Poses;
namespace Stanley.Editors;

/// <summary>
/// One colour scheme on offer in the Hair colour's dropdown (Natural, Two-tone, Peekaboo...):
/// a close-up of this character with the scheme applied, in the accent colour picked.
/// </summary>
/// <param name="Preview">The character (in the look being edited) with the scheme applied.</param>
/// <param name="Pose">The stage's preview pose (its expression), so the preview matches the stage; null at rest.</param>
/// <param name="Angle">The stage's view, front or side.</param>
public sealed record HairSchemeChoice(HairScheme Scheme, string Label, CharacterDefinition Preview, PoseData? Pose, ViewAngle Angle)
{
	public string Tip => Scheme switch
	{
		HairScheme.Natural => "Every piece back to the hair colour, no dye",
		HairScheme.TwoTone => "The top and the fringe in the accent colour",
		HairScheme.Peekaboo => "The hair underneath, at the back, in the accent colour",
		HairScheme.FringeOnly => "Just the fringe in the accent colour",
		HairScheme.DipDye => "The ends dipped in the accent colour",
		HairScheme.Ombre => "Fading into the accent colour",
		_ => "Every colour of the rainbow, in bands"
	};
}

/// <summary>A colour on offer as a scheme's accent; <paramref name="IsCurrent"/> marks the one picked.</summary>
public sealed record AccentChoice(string Name, ColorValue Color, bool IsCurrent)
{
	public IBrush Brush { get; } = new SolidColorBrush(Avalonia.Media.Color.Parse(ColorSwatchChoice.ColorHex(Color)));
}
