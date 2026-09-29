using Stanley.ProjectModel.Characters;
using Stanley.StickerLibrary;

namespace Stanley.Editors;

/// <summary>One tab along the top of the Hair flyout: Hairstyles, then a piece slot each (Top, Fringe, Sides, Back, Extras) and Streaks.</summary>
/// <param name="Key">"hairstyles" for the first, else the slot's name.</param>
public sealed record HairTab(string Key, string Label, bool IsCurrent);

/// <summary>
/// One thing the Hairstyles tab offers: bald, a named hairstyle (a preset of library pieces),
/// or one of the character's own whole-hairstyle stickers - previewed close-up on this
/// character in its colours, like a <see cref="StickerChoice"/>.
/// </summary>
/// <param name="Style">The preset, or null for bald and for the character's own sticker.</param>
/// <param name="Own">The character's own whole-hairstyle sticker (the <c>hair</c> slot), or null.</param>
/// <param name="IsWorn">Bald with nothing on, the preset worn exactly, or the one sticker worn alone.</param>
public sealed record HairstyleChoice(string Label, Hairstyle? Style, StickerAsset? Own, CharacterDefinition Preview, bool IsWorn,
	Stanley.ProjectModel.Poses.PoseData? Pose = null)
{
	public bool IsBald => Style is null && Own is null;

	/// <summary>Hair is previewed as a close-up of the head, like the face and hats.</summary>
	public bool Closeup => true;

	public string Tip =>
		IsBald ? "Take all the hair off"
		: IsWorn ? $"{Label} - wearing it"
		: Own is not null ? $"{Label} - a whole hairstyle of this character's own; it replaces the hair"
		: $"{Label} - replaces the hair with a hairstyle made of pieces; the colours stay";
}

/// <summary>The Hairstyles tab of the Hair flyout: bald, the presets, then this character's own whole hairstyles.</summary>
/// <param name="Wear">Puts on a choice (carried here so the flyout's popup can reach it).</param>
/// <param name="Draw">Draws a whole hairstyle in the user's SVG editor (the <c>hair</c> slot's name is the parameter).</param>
/// <param name="Import">Imports an SVG or PNG as a whole hairstyle.</param>
public sealed record HairstyleGallery(IReadOnlyList<HairstyleChoice> Choices, System.Windows.Input.ICommand Wear,
	System.Windows.Input.ICommand Draw, System.Windows.Input.ICommand Import);
