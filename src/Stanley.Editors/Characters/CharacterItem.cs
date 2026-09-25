using CommunityToolkit.Mvvm.ComponentModel;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
namespace Stanley.Editors;

/// <summary>One entry in the Characters pane: a character's editor (which holds its definition and undo) plus how often it's placed.</summary>
public sealed partial class CharacterItem : ObservableObject
{

	public CharacterItem(CharacterEditorViewModel editor)
	{
		Editor = editor;
		editor.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(CharacterEditorViewModel.Working))
			{
				OnPropertyChanged(nameof(Name));
				OnPropertyChanged(nameof(Character));
				OnPropertyChanged(nameof(Details));
			}
		};
	}

	public CharacterEditorViewModel Editor { get; }

	public CharacterId Id => Editor.CharacterId;

	public string Name
	{
		get => Editor.Name;
		set
		{
			Editor.Name = value;
			IsEditingName = false;
		}

	}

	public CharacterDefinition Character => Editor.Working;

	/// <summary>How many panels (across every page) show this character.</summary>
	public int Usage
	{
		get;
		set
		{
			if (SetProperty(ref field, value))
				OnPropertyChanged(nameof(Details));
		}
	}

	public string Details => $"{Editor.Working.Body.Height * 100:0}% tall" + (Usage == 0 ? " · not placed" : Usage == 1 ? " · in 1 panel" : $" · in {Usage} panels");

	public bool IsNotEditingName => !IsEditingName;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsNotEditingName))]
	public partial bool IsEditingName { get; set; }
}