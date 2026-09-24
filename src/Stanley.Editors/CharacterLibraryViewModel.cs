using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>One entry in the Characters pane: a character's editor (which holds its definition and undo) plus how often it's placed.</summary>
public sealed class CharacterItem : ObservableObject
{
    private int _usage;

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

    public string Name => Editor.Working.Name;

    public CharacterDefinition Character => Editor.Working;

    /// <summary>How many panels (across every page) show this character.</summary>
    public int Usage
    {
        get => _usage;
        set
        {
            if (SetProperty(ref _usage, value))
                OnPropertyChanged(nameof(Details));
        }
    }

    public string Details => $"{Editor.Working.Body.Height * 100:0}% tall" + (Usage == 0 ? " · not placed" : Usage == 1 ? " · in 1 panel" : $" · in {Usage} panels");
}

/// <summary>
/// The Characters side pane (a dock <see cref="Tool"/>, next to Pages): the comic's
/// characters, which one is being edited, and character-level operations (new,
/// duplicate, delete) - all in the one shared <see cref="EditorHistory"/>, so "deleted a
/// character" undoes like any edit. It's also the <see cref="ICharacterCatalog"/> the page
/// editors draw placed characters from.
///
/// Each character has its own <see cref="CharacterEditorViewModel"/>. Showing one swaps
/// it into the editor area in place of the page (<see cref="CharacterShown"/>), the same
/// one-thing-at-a-time model as the page navigator; <see cref="ReturnToPage"/> goes back.
/// </summary>
public sealed class CharacterLibraryViewModel : Tool, ICharacterCatalog
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
        DuplicateCharacterCommand = new RelayCommand<CharacterItem?>(item =>
        {
            if ((item ?? Current) is { } source)
                Show(AddCharacter(source.Character with { Id = CharacterId.New(), Name = source.Name + " (copy)" }));
        }, item => (item ?? Current) != null);
        DeleteCharacterCommand = new RelayCommand<CharacterItem?>(item =>
        {
            if ((item ?? Current) is { } target)
                DeleteCharacter(target);
        }, item => (item ?? Current) is { Usage: 0 });

        PlaceOnPageCommand = new RelayCommand<CharacterItem?>(item =>
        {
            if ((item ?? Current) is { } target)
                PlaceRequested?.Invoke(target.Id);
        });

        Items = new ObservableCollection<CharacterItem>(characters.Select(c => CreateItem(c)));
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

    /// <summary>Raised when a character's editor should replace the page in the editor area.</summary>
    public event Action<CharacterItem>? CharacterShown;

    /// <summary>Raised when the page should come back into the editor area (the character editor's "Close").</summary>
    public event Action? PageRequested;

    /// <summary>Raised to put a character on the page being edited (the pane's "Place on page"; dragging onto the page places it where it's dropped instead).</summary>
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

    /// <summary>Back to the page: no character is current any more.</summary>
    public void ReturnToPage()
    {
        _current = null;
        OnPropertyChanged(nameof(Current));
        NotifyCommands();
        PageRequested?.Invoke();
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

    private CharacterItem AddCharacter(CharacterDefinition character)
    {
        var item = CreateItem(character);
        var before = Items.ToList();
        var after = before.Append(item).ToList();
        SetItems(after);
        _history.Push($"New character \"{character.Name}\"", () => SetItems(before), () => SetItems(after), this);
        return item;
    }

    /// <summary>Removes a character that isn't placed anywhere (placed ones can't be deleted - remove them from their panels first).</summary>
    public void DeleteCharacter(CharacterItem item)
    {
        if (!Items.Contains(item) || _usage(item.Id) > 0)
            return;
        var before = Items.ToList();
        var after = before.Where(i => !ReferenceEquals(i, item)).ToList();
        var wasCurrent = ReferenceEquals(item, _current);
        SetItems(after);
        if (wasCurrent)
            ReturnToPage();
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
        return new CharacterItem(editor) { Usage = _usage(character.Id) };
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CharacterEditorViewModel.Working))
            Refresh();
    }

    private void Refresh()
    {
        var all = Items.Select(i => i.Editor.Working).ToList();
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
        for (var n = Items.Count + 1; ; n++)
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
    }
}
