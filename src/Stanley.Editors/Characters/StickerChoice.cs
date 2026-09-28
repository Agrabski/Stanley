using Stanley.Editing;
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

	/// <summary>A click puts on another copy (a print or a badge, in a slot that stamps copies) rather than taking a worn one off.</summary>
	public bool StampsCopy => !IsNone && StickerSlots.Get(Slot).StampsCopies && StickerCopies.IsPlaceable((Asset ?? Library!.Asset).Sticker);

	public string Tip =>
		IsNone ? "Take off everything in this slot"
		: IsWorn ? (StampsCopy ? $"{Label} - wearing it; click for another" : $"{Label} - wearing it; click to take it off")
		: Library is not null ? $"{Label} - from the starter library"
		: Label;
}