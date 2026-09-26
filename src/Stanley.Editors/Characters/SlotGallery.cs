using Stanley.ProjectModel.Characters;
namespace Stanley.Editors;

/// <summary>A slot's gallery on the Look tab: its label, what's worn now, and everything it can wear.</summary>
/// <param name="Wear">Wears a choice (carried here so the gallery's popup can reach it).</param>
/// <param name="Draw">Draws a sticker for the slot in the user's SVG editor (the slot's name is the parameter).</param>
/// <param name="Import">Imports an SVG or PNG as a sticker for the slot.</param>
/// <param name="WearText">Prints only: wears a new text print with the typed text (the "Text..." entry).</param>
public sealed record SlotGallery(StickerSlotInfo Info, string Current, IReadOnlyList<StickerChoice> Choices, System.Windows.Input.ICommand Wear,
	System.Windows.Input.ICommand? Draw = null, System.Windows.Input.ICommand? Import = null, System.Windows.Input.ICommand? WearText = null)
{
	public string Label => Info.Label;

	public string Tip => $"{Info.Label}: {Current}";
}