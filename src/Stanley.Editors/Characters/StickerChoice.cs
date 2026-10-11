using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.StickerLibrary;
namespace Stanley.Editors;

/// <summary>One thing a slot gallery offers: nothing, a sticker from the wardrobe, or one from the library - previewed on this character.</summary>
/// <param name="Pose">The stage's preview pose (its view and expression), so a face gallery shows the expression being previewed.</param>
/// <param name="Holding">What the slot is wearing now, named the way the gallery's button names it, or null when it's empty - so the tooltip can say a click goes on over it.</param>
public sealed record StickerChoice(string Label, string Slot, CharacterDefinition Preview, StickerAsset? Asset, LibrarySticker? Library, bool IsWorn,
	Stanley.ProjectModel.Poses.PoseData? Pose = null, string? Holding = null)
{
	public bool IsNone => Asset is null && Library is null;

	/// <summary>Worn on the head (hair, face, hats, glasses): previewed as a close-up.</summary>
	public bool Closeup => StickerSlots.Get(Slot).Region == BodyRegion.Head;

	/// <summary>A click puts on another copy (a print or a badge, in a slot that stamps copies) rather than taking a worn one off.</summary>
	public bool StampsCopy => !IsNone && StickerSlots.Get(Slot).StampsCopies && StickerCopies.IsPlaceable((Asset ?? Library!.Asset).Sticker);

	/// <summary>What a click will do - layering isn't obvious: what isn't worn says it goes on over what the slot holds, and what is worn says it comes off.</summary>
	public string Tip =>
		IsNone ? (IsWorn ? "Nothing on in this slot" : "Take off everything in this slot")
		: IsWorn ? (StampsCopy ? $"{Label} - wearing it; click for another" : $"{Label} - wearing it; click to take it off")
		: $"{Label} - {(Library is not null ? "from the starter library; " : "")}{ClickToPutOn}";

	/// <summary>A placed design (a print, a badge) goes in its own spot rather than over anything, so only the rest say what they cover.</summary>
	private string ClickToPutOn => Holding is { } over && !StampsCopy ? $"click to put it on over {over}" : "click to put it on";
}