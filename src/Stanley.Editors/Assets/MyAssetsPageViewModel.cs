using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Objects;

namespace Stanley.Editors;

/// <summary>Which kinds File › My Assets shows - the chips above the grid (docs/asset-packs.md §6.2). They narrow what's shown, nothing else.</summary>
public enum MyAssetsFilter
{
    All,
    ObjectGroups,
    Characters
}

/// <summary>One tile on File › My Assets: a kept character or object group.</summary>
public sealed partial class MyAssetTile : ObservableObject
{
    private readonly Action<MyAssetTile, string>? _rename;

    public MyAssetTile(CharacterDefinition character)
    {
        Kind = AssetKind.Character;
        Id = character.Id.Value;
        Character = character;
        _name = character.Name;
    }

    public MyAssetTile(ObjectGroup group, Action<MyAssetTile, string> rename)
    {
        Kind = AssetKind.ObjectGroup;
        Id = group.Id.Value;
        ObjectGroup = group;
        _name = group.Name;
        _rename = rename;
    }

    public AssetKind Kind { get; }

    /// <summary>The asset's id token, unique within its kind.</summary>
    public string Id { get; }

    public CharacterDefinition? Character { get; }
    public ObjectGroup? ObjectGroup { get; }

    public bool IsCharacter => Character != null;
    public bool IsObjectGroup => ObjectGroup != null;

    public string KindText => Kind == AssetKind.Character ? "Character" : "Object group";

    /// <summary>Only an object group is renamed here - a character's name is changed in a comic, then saved to My Assets.</summary>
    public bool CanRename => _rename != null;

    /// <summary>The name shown; setting it (from the rename box) renames the asset in My Assets.</summary>
    public string Name
    {
        get => _name;
        set
        {
            IsEditingName = false;
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == _name)
                return;
            _rename?.Invoke(this, value.Trim());
        }
    }

    private readonly string _name;

    public bool IsNotEditingName => !IsEditingName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditingName))]
    public partial bool IsEditingName { get; set; }
}

/// <summary>
/// File › My Assets (docs/asset-packs.md §6.2, §9): everything kept, in one grid, with kind
/// chips above it - the page packs will later be built on. For now it shows, renames (object
/// groups) and removes; keeping happens where the thing is (a group's or a character's
/// right-click menu), and adding from the pickers. Removing asks first, in a bar rather than
/// a dialog, and never touches a comic: each keeps its own copy.
/// </summary>
public sealed partial class MyAssetsPageViewModel : ObservableObject
{
    private readonly MyAssetsLibrary _library;
    private IReadOnlyList<MyAssetTile> _all = [];

    public MyAssetsPageViewModel(MyAssetsLibrary library)
    {
        _library = library;
        _library.Changed += Refresh;
        RemoveCommand = new RelayCommand<MyAssetTile?>(tile => PendingRemoval = tile, tile => tile != null);
        ConfirmRemoveCommand = new RelayCommand(ConfirmRemove, () => PendingRemoval != null);
        CancelRemoveCommand = new RelayCommand(() => PendingRemoval = null);
        RenameCommand = new RelayCommand<MyAssetTile?>(tile =>
        {
            if (tile is { CanRename: true })
                tile.IsEditingName = true;
        }, tile => tile is { CanRename: true });
        Refresh();
    }

    /// <summary>Where My Assets lives on disk - shown on the page, for people who look after their own files.</summary>
    public string FolderText => _library.RootDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tiles), nameof(IsAll), nameof(IsObjectGroups), nameof(IsCharacters), nameof(IsFilteredEmpty))]
    public partial MyAssetsFilter Filter { get; set; }

    public bool IsAll { get => Filter == MyAssetsFilter.All; set { if (value) Filter = MyAssetsFilter.All; } }
    public bool IsObjectGroups { get => Filter == MyAssetsFilter.ObjectGroups; set { if (value) Filter = MyAssetsFilter.ObjectGroups; } }
    public bool IsCharacters { get => Filter == MyAssetsFilter.Characters; set { if (value) Filter = MyAssetsFilter.Characters; } }

    /// <summary>What the grid shows under the current chip, object groups first, then characters, each by name.</summary>
    public IReadOnlyList<MyAssetTile> Tiles => Filter switch
    {
        MyAssetsFilter.ObjectGroups => _all.Where(t => t.IsObjectGroup).ToList(),
        MyAssetsFilter.Characters => _all.Where(t => t.IsCharacter).ToList(),
        _ => _all
    };

    public string AllText => $"All ({_all.Count})";
    public string ObjectGroupsText => $"Object groups ({_all.Count(t => t.IsObjectGroup)})";
    public string CharactersText => $"Characters ({_all.Count(t => t.IsCharacter)})";

    /// <summary>Nothing kept yet at all: the page explains how things get here instead of showing a grid.</summary>
    public bool IsEmpty => _all.Count == 0;

    /// <summary>Things are kept, just none of the chosen kind.</summary>
    public bool IsFilteredEmpty => !IsEmpty && Tiles.Count == 0;

    /// <summary>The tile whose removal is waiting on the bar's Remove / Cancel; null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingRemoval), nameof(RemovalQuestion))]
    public partial MyAssetTile? PendingRemoval { get; set; }

    public bool HasPendingRemoval => PendingRemoval != null;

    public string RemovalQuestion => PendingRemoval is { } tile
        ? $"Remove {tile.Name} from My Assets? Comics that use {(tile.IsCharacter ? "them" : "it")} keep their own copy."
        : "";

    public IRelayCommand<MyAssetTile?> RemoveCommand { get; }
    public IRelayCommand ConfirmRemoveCommand { get; }
    public IRelayCommand CancelRemoveCommand { get; }
    public IRelayCommand<MyAssetTile?> RenameCommand { get; }

    /// <summary>What went wrong reading or writing My Assets, if anything did last time; null when all's well.</summary>
    [ObservableProperty]
    public partial string? Problem { get; set; }

    /// <summary>Reads My Assets again - on showing the page, and after anything is kept or removed.</summary>
    public void Refresh()
    {
        try
        {
            _all = _library.ObjectGroups().Select(g => new MyAssetTile(g, Rename))
                .Concat(_library.Characters().Select(c => new MyAssetTile(c)))
                .ToList();
            Problem = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _all = [];
            Problem = $"Couldn't read My Assets: {e.Message}";
        }
        if (PendingRemoval is { } pending && !_all.Any(t => t.Kind == pending.Kind && t.Id == pending.Id))
            PendingRemoval = null;
        OnPropertyChanged(nameof(Tiles));
        OnPropertyChanged(nameof(AllText));
        OnPropertyChanged(nameof(ObjectGroupsText));
        OnPropertyChanged(nameof(CharactersText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFilteredEmpty));
        OnPropertyChanged(nameof(FolderText));
    }

    private void ConfirmRemove()
    {
        if (PendingRemoval is not { } tile)
            return;
        PendingRemoval = null;
        Write(() =>
        {
            if (tile.IsCharacter)
                _library.RemoveCharacter(CharacterId.FromValue(tile.Id));
            else
                _library.RemoveObjectGroup(ObjectGroupId.FromValue(tile.Id));
        });
    }

    private void Rename(MyAssetTile tile, string name) =>
        Write(() => _library.RenameObjectGroup(ObjectGroupId.FromValue(tile.Id), name));

    private void Write(Action write)
    {
        try
        {
            write();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Problem = $"Couldn't change My Assets: {e.Message}";
        }
    }

    partial void OnPendingRemovalChanged(MyAssetTile? value) => ConfirmRemoveCommand.NotifyCanExecuteChanged();
}
