using Stanley.ProjectModel.Characters;
using Stanley.StickerLibrary;
namespace Stanley.Editors;

/// <summary>One thing a slot gallery offers: nothing, a sticker from the wardrobe, or one from the library - previewed on this character.</summary>
/// <param name="Pose">The stage's preview pose (its view and expression), so a face gallery shows the expression being previewed.</param>
public sealed record StickerChoice(string Label, string Slot, CharacterDefinition Preview, StickerAsset? Asset, LibrarySticker? Library, bool IsWorn,
	Stanley.ProjectModel.Poses.PoseData? Pose = null)
{
	public bool IsNone => Asset is null && Library is null;

	/// <summary>Worn on the head (hair, face, hats, glasses): previewed as a close-up.</summary>
	public bool Closeup => StickerSlots.Get(Slot).Region == BodyRegion.Head;

	public string Tip => IsNone ? "Nothing in this slot" : Library is not null ? $"{Label} - from the starter library" : IsWorn ? $"{Label} - wearing it" : Label;
}