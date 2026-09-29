using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>
/// The Characters side pane (a dock <see cref="Tool"/>, next to Pages): the comic's
/// characters, which one is being edited, and character-level operations (new,
/// duplicate, delete) - all in the one shared <see cref="EditorHistory"/>, so "deleted a
/// character" undoes like any edit. It's also the <see cref="ICharacterCatalog"/> the page
/// editors draw placed characters from.
///
/// Each character has its own <see cref="CharacterEditorViewModel"/>. Showing one opens it
/// as its own tab alongside the page - and any other character already open - rather than
/// replacing what's there (<see cref="CharacterShown"/>); <see cref="ReturnToPage"/> ("Close"
/// in that character's ribbon) closes just that one tab and shows the page.
/// </summary>
public sealed partial class CharacterLibraryViewModel : Tool, ICharacterCatalog
{
	private readonly EditorHistory _history;
	private CharacterItem? _current;
	private IReadOnlyDictionary<CharacterId, CharacterDefinition> _snapshot = new Dictionary<CharacterId, CharacterDefinition>();
	private IReadOnlyList<CharacterDefinition> _inOrder = [];
	private Func<CharacterId, int> _usage = _ => 0;


	public CharacterLibraryViewModel(EditorHistory history, IEnumerable<CharacterDefinition> characters)
	{
		_history = history;
		Id = "Characters";
		Title = "Characters";
		CanClose = false;
		CanFloat = false;

		NewCharacterCommand = new RelayCommand(() => Show(AddCharacter(NewDefinition())));
		DuplicateCharacterCommand = new RelayCommand<CharacterItem?>(
			item =>
			{
				if ((item ?? Current) is { } source)
					Show(AddCharacter(source.Character with { Id = CharacterId.New(), Name = source.Name + " (copy)", MyAssetsVersion = null }));
			},
			item => (item ?? Current) != null
		);
		DeleteCharacterCommand = new RelayCommand<CharacterItem?>(
			item =>
			{
				if ((item ?? Current) is { } target)
					DeleteCharacter(target);
			},
			item => (item ?? Current) is { Usage: 0 }
		);

		PlaceOnPageCommand = new RelayCommand<CharacterItem?>(item =>
			{
				if ((item ?? Current) is { } target)
					PlaceOnPage(target);
			}
		);

		InitializeMyAssetsCommands();

		Items = [.. characters.Select(CreateItem)];
		Items.CollectionChanged += (_, _) => Refresh();
		Refresh();

		_history.Restored += OnHistoryRestored;
	}

	public ObservableCollection<CharacterItem> Items { get; }

	public bool HasNoCharacters => Items.Count == 0;

	/// <summary>The character whose editor is shown, or null while a page is. Bound two-way to the list's selection; picking one shows it.</summary>
	public CharacterItem? Current
	{
		get => _current;
		set
		{
			if (ReferenceEquals(value, _current))
				return;
			if (value is null)
			{
				// A list box drops its selection while items shuffle; only an explicit
				// return to the page (ReturnToPage) clears the current character.
				OnPropertyChanged();
				return;
			}
			Show(value);
		}
	}

	/// <summary>Raised when a character's editor should be shown as a tab (adding it if it isn't open yet).</summary>
	public event Action<CharacterItem>? CharacterShown;

	/// <summary>Raised when the page should become the active tab (the character editor's "Close"); carries the character being left, whose own tab closes, or null if none was current.</summary>
	public event Action<CharacterItem?>? PageRequested;

	/// <summary>Raised when a character is deleted, so its tab closes even if it wasn't the active one.</summary>
	public event Action<CharacterItem>? CharacterDeleted;

	/// <summary>Raised to put a character on the page being edited (a double-click in the pane, or its "Place on page"; dragging onto the page places it where it's dropped instead).</summary>
	public event Action<CharacterId>? PlaceRequested;

	public IRelayCommand<CharacterItem?> PlaceOnPageCommand { get; }

	public IRelayCommand NewCharacterCommand { get; }
	public IRelayCommand<CharacterItem?> DuplicateCharacterCommand { get; }
	public IRelayCommand<CharacterItem?> DeleteCharacterCommand { get; }

	/// <summary>Counts where each character is placed - set by whoever knows the pages; drives "in N panels" and whether Delete is allowed.</summary>
	public Func<CharacterId, int> UsageCounter
	{
		get => _usage;
		set
		{
			_usage = value;
			RefreshUsage();
		}
	}

	/// <summary>How many panels (and issues) show a character in a named look - set by the session, which sees the pages; a look in use can't be deleted.</summary>
	public Func<CharacterId, CharacterRevisionId, int>? LookUsageCounter { get; set; }

	/// <summary>How "Draw your own" reaches the user's SVG editor; the app points it at its data folder, tests at a fake.</summary>
	public IArtEditing ArtEditing { get; set; } = new SystemArtEditing(Path.Combine(Path.GetTempPath(), "stanley-drawing"));

	/// <summary>Which characters keep their old whole hairstyle (the upgrade bar's "Keep"); the app points it at the user's settings, tests at memory.</summary>
	public HairUpgradeMemory HairUpgrades { get; set; } = new();

	// ---------------------------------------------------------------- ICharacterCatalog

	public IReadOnlyDictionary<CharacterId, CharacterDefinition> Characters => _snapshot;

	public IReadOnlyList<CharacterDefinition> InOrder => _inOrder;

	public event Action? CharactersChanged;

	public CharacterDefinition CreateCharacter() => AddCharacter(NewDefinition()).Character;

	/// <summary>A new character: the default body, with the library's default face on.</summary>
	private CharacterDefinition NewDefinition()
	{
		var character = CharacterDefinition.Create(NextName());
		foreach (var key in Stanley.StickerLibrary.StickerLibrary.DefaultFace)
		{
			if (Stanley.StickerLibrary.StickerLibrary.Find(key) is { } sticker)
				character = LookEditing.Wear(character, sticker.Instantiate());
		}
		return character;
	}

	public void OpenCharacter(CharacterId id)
	{
		if (Items.FirstOrDefault(i => i.Id == id) is { } item)
			Show(item);
	}

	public void EditCharacter(CharacterId id, string description, Func<CharacterDefinition, CharacterDefinition> edit, object? source)
	{
		if (Items.FirstOrDefault(i => i.Id == id) is not { } item)
			return;
		using (_history.Group(description, source))
			item.Editor.Apply(EditResult<CharacterDefinition>.Success(edit(item.Editor.Committed)));
	}

	public string DrawVariant(CharacterId id, StickerId sticker, string variant, ViewAngle view, object? source) =>
		Items.FirstOrDefault(i => i.Id == id) is { } item ? item.Editor.DrawVariant(sticker, variant, view, source) : "That character isn't there any more.";

	// ---------------------------------------------------------------- operations

	/// <summary>Makes <paramref name="item"/> the character shown in the editor area.</summary>
	public void Show(CharacterItem item)
	{
		if (!Items.Contains(item))
			return;
		_current = item;
		OnPropertyChanged(nameof(Current));
		NotifyCommands();
		CharacterShown?.Invoke(item);
	}

	/// <summary>
	/// Puts <paramref name="item"/> on the page being edited and brings the page back: a
	/// double-click in the pane, or its "Place on page". A double-click's first click has
	/// already opened the character, so if it's the one showing its tab closes again.
	/// </summary>
	public void PlaceOnPage(CharacterItem item)
	{
		if (!Items.Contains(item))
			return;
		if (ReferenceEquals(item, _current))
			ReturnToPage();
		PlaceRequested?.Invoke(item.Id);
	}

	/// <summary>Back to the page: no character is current any more.</summary>
	public void ReturnToPage()
	{
		var closing = _current;
		closing?.Editor.DismissHairReplaced();
		_current = null;
		OnPropertyChanged(nameof(Current));
		NotifyCommands();
		PageRequested?.Invoke(closing);
	}

	/// <summary>Forgets which character is current without asking for the page back - the page is already being shown (a page was picked in the navigator).</summary>
	public void Deselect()
	{
		if (_current is null)
			return;
		_current = null;
		OnPropertyChanged(nameof(Current));
		NotifyCommands();
	}

	/// <summary>Every character's committed definition - what Save writes.</summary>
	public IReadOnlyList<CharacterDefinition> Snapshot() => Items.Select(i => i.Editor.Committed).ToList();

	/// <summary>Recounts where characters are placed (after any page edit).</summary>
	public void RefreshUsage()
	{
		foreach (var item in Items)
			item.Usage = _usage(item.Id);
		NotifyCommands();
	}

	private CharacterItem AddCharacter(CharacterDefinition character, string? description = null)
	{
		var item = CreateItem(character);
		var before = Items.ToList();
		var after = before.Append(item).ToList();
		SetItems(after);
		_history.Push(description ?? $"New character \"{character.Name}\"", () => SetItems(before), () => SetItems(after), this);
		return item;
	}

	/// <summary>Removes a character that isn't placed anywhere (placed ones can't be deleted - remove them from their panels first). Its tab closes too, even if it wasn't the one showing.</summary>
	public void DeleteCharacter(CharacterItem item)
	{
		if (!Items.Contains(item) || _usage(item.Id) > 0)
			return;
		var before = Items.ToList();
		var after = before.Where(i => !ReferenceEquals(i, item)).ToList();
		SetItems(after); // returns to the page itself (raising PageRequested) if item was current
		CharacterDeleted?.Invoke(item);
		_history.Push($"Delete character \"{item.Name}\"", () => SetItems(before), () => SetItems(after), this);
	}

	private void SetItems(IReadOnlyList<CharacterItem> items)
	{
		Items.Clear();
		foreach (var item in items)
			Items.Add(item);
		if (_current != null && !Items.Contains(_current))
			ReturnToPage();
	}

	private CharacterItem CreateItem(CharacterDefinition character)
	{
		var editor = new CharacterEditorViewModel(_history, character, this);
		editor.PropertyChanged += OnEditorPropertyChanged;
		return new(editor) { Usage = _usage(character.Id), IsKept = IsKept(character) };
	}

	private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(CharacterEditorViewModel.Working))
			Refresh();
	}

	private void Refresh()
	{
		var all = Items.Select(i => i.Editor.Working).ToList();
		foreach (var item in Items)
			item.IsKept = IsKept(item.Editor.Working);
		_snapshot = all.ToDictionary(c => c.Id);
		_inOrder = all
			.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(c => c.Id.Value, StringComparer.Ordinal)
			.ToList();
		OnPropertyChanged(nameof(HasNoCharacters));
		NotifyCommands();
		CharactersChanged?.Invoke();
	}

	private string NextName()
	{
		for (var n = Items.Count + 1;; n++)
		{
			var name = $"Character {n}";
			if (Items.All(i => !string.Equals(i.Name, name, StringComparison.CurrentCultureIgnoreCase)))
				return name;
		}
	}

	private void OnHistoryRestored(object? source)
	{
		if (source is CharacterEditorViewModel editor && Items.FirstOrDefault(i => ReferenceEquals(i.Editor, editor)) is { } item)
			Show(item);
	}

	private void NotifyCommands()
	{
		DuplicateCharacterCommand.NotifyCanExecuteChanged();
		DeleteCharacterCommand.NotifyCanExecuteChanged();
		KeepInMyAssetsCommand.NotifyCanExecuteChanged();
	}
}