using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Storage;

namespace Stanley.Editors;

/// <summary>One tile in the Add character gallery: a character from My Assets or from another comic.</summary>
/// <param name="InThisComic">This comic already has it (same id) - picking it just shows it in the list.</param>
public sealed record CharacterChoice(CharacterDefinition Character, bool FromOtherComic, bool InThisComic = false)
{
    public string Name => Character.Name;

    /// <summary>One already in this comic is drawn faded.</summary>
    public double FigureOpacity => InThisComic ? 0.5 : 1;

    public string Tip => InThisComic ? $"{Name} is already in this comic"
        : FromOtherComic ? $"Add {Name} to this comic - and keep them in My Assets"
        : $"Add {Name} to this comic";
}

/// <summary>A recent comic's characters, as the Add character gallery lists them.</summary>
public sealed record OtherComicCharacters(string Title, IReadOnlyList<CharacterChoice> Characters);

/// <summary>
/// The Characters pane and My Assets (docs/asset-packs.md §6.1, §6.3; docs/my-characters.md
/// §4.1-§4.2): keeping a character, and the Add character gallery - a new character, the
/// ones in My Assets, and the ones in the user's other recent comics.
/// </summary>
public sealed partial class CharacterLibraryViewModel
{
    private HashSet<CharacterId> _keptIds = [];
    // Recent comics' characters, read once a session: title and characters, or null for one that couldn't be read.
    private readonly Dictionary<string, (string Title, IReadOnlyList<CharacterDefinition> Characters)?> _otherComics = new(StringComparer.Ordinal);

    /// <summary>The user's My Assets; null for none, which hides keeping and the gallery's My Assets section.</summary>
    public MyAssetsLibrary? MyAssets
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;
            if (field != null)
                field.Changed -= OnMyAssetsChanged;
            field = value;
            if (field != null)
                field.Changed += OnMyAssetsChanged;
            OnMyAssetsChanged();
        }
    }

    public bool HasMyAssets => MyAssets != null;

    /// <summary>The comics on the recent list, most recent first - where "From your other comics" looks. Set by the app.</summary>
    public Func<IReadOnlyList<string>>? RecentComics { get; set; }

    /// <summary>This comic's own folder, left out of "From your other comics"; null while it's unsaved.</summary>
    public string? ComicLocation { get; set; }

    /// <summary>Raised to bring a character into view in the pane without opening it - one just added from the gallery, or one it already had.</summary>
    public event Action<CharacterItem>? CharacterRevealed;

    public IRelayCommand<CharacterItem?> KeepInMyAssetsCommand { get; private set; } = null!;
    public IRelayCommand<CharacterChoice?> AddChoiceCommand { get; private set; } = null!;

    /// <summary>Gallery: every character in My Assets. Filled by <see cref="RefreshGallery"/>.</summary>
    public IReadOnlyList<CharacterChoice> GalleryMyAssets { get; private set; } = [];

    /// <summary>Gallery: characters in the other recent comics that are neither in this comic nor in My Assets, by comic. Filled by <see cref="RefreshGallery"/>.</summary>
    public IReadOnlyList<OtherComicCharacters> GalleryOtherComics { get; private set; } = [];

    public bool HasGalleryMyAssets => GalleryMyAssets.Count > 0;
    public bool HasGalleryOtherComics => GalleryOtherComics.Count > 0;

    /// <summary>With nothing to pick from, the gallery says where things come from instead.</summary>
    public bool IsGalleryEmpty => !HasGalleryMyAssets && !HasGalleryOtherComics;

    private void InitializeMyAssetsCommands()
    {
        KeepInMyAssetsCommand = new RelayCommand<CharacterItem?>(
            item =>
            {
                if ((item ?? Current) is { } target)
                    KeepInMyAssets(target);
            },
            item => MyAssets != null && (item ?? Current) != null);
        AddChoiceCommand = new RelayCommand<CharacterChoice?>(choice =>
        {
            if (choice != null)
                AddFromGallery(choice);
        });
    }

    private void OnMyAssetsChanged()
    {
        try
        {
            _keptIds = MyAssets?.Characters().Select(c => c.Id).ToHashSet() ?? [];
        }
        catch (Exception e) when (IsUnreadable(e))
        {
            _keptIds = [];
        }
        foreach (var item in Items)
            item.IsKept = IsKept(item.Editor.Committed);
        OnPropertyChanged(nameof(HasMyAssets));
        KeepInMyAssetsCommand?.NotifyCanExecuteChanged();
    }

    /// <summary>Kept, as the pane's star shows it: this comic's copy records a My Assets version, and My Assets still has it.</summary>
    private bool IsKept(CharacterDefinition character) => character.MyAssetsVersion != null && _keptIds.Contains(character.Id);

    /// <summary>Whether <paramref name="item"/> is kept in My Assets and whether this comic's copy still matches it - what its right-click menu offers.</summary>
    public KeptState KeptStateOf(CharacterItem item)
    {
        var character = item.Editor.Committed;
        if (!IsKept(character))
            return KeptState.NotKept;
        return AssetFingerprint.CharacterFingerprint(character) == character.MyAssetsVersion ? KeptState.Kept : KeptState.ChangedHere;
    }

    /// <summary>
    /// Right-click › Keep in My Assets (or Save to My Assets): writes the character to My
    /// Assets straight away - its stickers, patterns and looks with it - and records in this
    /// comic which version it now matches, as one undo step. Undoing forgets that record but
    /// leaves My Assets as it is: the comic's undo can't reach My Assets. Returns what was
    /// kept, or null (with no My Assets, or when it couldn't be written).
    /// </summary>
    public CharacterDefinition? KeepInMyAssets(CharacterItem item)
    {
        if (MyAssets is not { } myAssets || !Items.Contains(item))
            return null;
        CharacterDefinition kept;
        try
        {
            kept = myAssets.KeepCharacter(item.Editor.Committed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            KeepFailed?.Invoke($"Couldn't keep {item.Name} in My Assets: {e.Message}");
            return null;
        }
        if (item.Editor.Committed.MyAssetsVersion != kept.MyAssetsVersion)
            EditCharacter(item.Id, $"Keep \"{item.Name}\" in My Assets", c => c with { MyAssetsVersion = kept.MyAssetsVersion }, this);
        item.IsKept = true;
        return kept;
    }

    /// <summary>Raised when something couldn't be written to My Assets, with what to tell the user.</summary>
    public event Action<string>? KeepFailed;

    /// <summary>
    /// Fills the gallery: My Assets (read fresh - another window may have kept something) and
    /// the other recent comics (read once a session; one that can't be read is skipped
    /// without a message).
    /// </summary>
    public void RefreshGallery()
    {
        var inComic = Items.Select(i => i.Id).ToHashSet();
        IReadOnlyList<CharacterDefinition> kept;
        try
        {
            kept = MyAssets?.Characters() ?? [];
        }
        catch (Exception e) when (IsUnreadable(e))
        {
            kept = [];
        }
        GalleryMyAssets = kept.Select(c => new CharacterChoice(c, FromOtherComic: false, InThisComic: inComic.Contains(c.Id))).ToList();

        var keptIds = kept.Select(c => c.Id).ToHashSet();
        var seen = new HashSet<CharacterId>();
        var others = new List<OtherComicCharacters>();
        foreach (var folder in RecentComics?.Invoke() ?? [])
        {
            if (ComicLocation is { } here && SameFolder(here, folder) || ReadComic(folder) is not { } comic)
                continue;
            // A character in several recent comics shows once, under the most recent.
            var choices = comic.Characters
                .Where(c => !inComic.Contains(c.Id) && !keptIds.Contains(c.Id) && seen.Add(c.Id))
                .Select(c => new CharacterChoice(c, FromOtherComic: true))
                .ToList();
            if (choices.Count > 0)
                others.Add(new OtherComicCharacters(comic.Title, choices));
        }
        GalleryOtherComics = others;

        OnPropertyChanged(nameof(GalleryMyAssets));
        OnPropertyChanged(nameof(GalleryOtherComics));
        OnPropertyChanged(nameof(HasGalleryMyAssets));
        OnPropertyChanged(nameof(HasGalleryOtherComics));
        OnPropertyChanged(nameof(IsGalleryEmpty));
    }

    /// <summary>
    /// Picking a gallery tile: a copy of the character in this comic with the same ids (one
    /// undo step), brought into view but not opened - most likely it's about to be placed. One
    /// the comic already has is just brought into view. A character from another comic is
    /// kept in My Assets too, so it has one home. Returns the character's entry in the pane.
    /// </summary>
    public CharacterItem? AddFromGallery(CharacterChoice choice)
    {
        var character = choice.Character;
        if (Items.FirstOrDefault(i => i.Id == character.Id) is { } existing)
        {
            CharacterRevealed?.Invoke(existing);
            return existing;
        }

        if (!choice.FromOtherComic)
            // Whatever My Assets' file says, this copy matches it exactly now.
            character = character with { MyAssetsVersion = AssetFingerprint.CharacterFingerprint(character) };
        else
            character = KeepPicked(character);

        var item = AddCharacter(character, $"Add character \"{character.Name}\"");
        item.IsKept = IsKept(character);
        CharacterRevealed?.Invoke(item);
        return item;
    }

    /// <summary>A character picked from another comic, kept in My Assets so it has one home - or, without My Assets (or if it can't be written), not linked to it at all.</summary>
    private CharacterDefinition KeepPicked(CharacterDefinition character)
    {
        if (MyAssets is { } myAssets)
        {
            try
            {
                return myAssets.KeepCharacter(character);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                KeepFailed?.Invoke($"Couldn't keep {character.Name} in My Assets: {e.Message}");
            }
        }
        return character with { MyAssetsVersion = null };
    }

    private (string Title, IReadOnlyList<CharacterDefinition> Characters)? ReadComic(string folder)
    {
        if (_otherComics.TryGetValue(folder, out var cached))
            return cached;
        (string, IReadOnlyList<CharacterDefinition>)? read = null;
        try
        {
            if (ProjectRepository.IsInitialized(folder))
            {
                var repository = new ProjectRepository(folder);
                read = (repository.LoadManifest().Title, repository.ListCharacters());
            }
        }
        catch (Exception e) when (IsUnreadable(e))
        {
        }
        _otherComics[folder] = read;
        return read;
    }

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsUnreadable(Exception e) =>
        e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException or ArgumentException or NotSupportedException;
}
