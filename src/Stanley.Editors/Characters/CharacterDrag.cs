using Avalonia.Input;
namespace Stanley.Editors;

/// <summary>The drag-and-drop payload of a character dragged out of the Characters pane: its id.</summary>
public static class CharacterDrag
{
	public static readonly DataFormat<string> Format = DataFormat.CreateStringApplicationFormat("stanley-character");
}