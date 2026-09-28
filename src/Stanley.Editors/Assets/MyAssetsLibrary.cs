using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Storage;

namespace Stanley.Editors;

/// <summary>
/// The user's My Assets folder as the editors see it (docs/asset-packs.md §6): one per
/// user, shared by every comic opened in this window, so keeping something from one comic
/// shows up in every picker and on File › My Assets straight away (<see cref="Changed"/>).
/// Writing happens immediately, not on the comic's next save - My Assets isn't part of any
/// comic, so the comic's undo can't reach it (docs/my-characters.md §4.1).
/// </summary>
public sealed class MyAssetsLibrary
{
    private MyAssets _store;

    public MyAssetsLibrary(string rootDirectory) => _store = new MyAssets(rootDirectory);

    /// <summary>The folder My Assets lives in.</summary>
    public string RootDirectory => _store.RootDirectory;

    /// <summary>Raised after anything is kept, saved, renamed or removed - and when the folder itself moves.</summary>
    public event Action? Changed;

    /// <summary>Points at another folder (File › Options › My Assets); what's in the old one stays there.</summary>
    public void MoveTo(string rootDirectory)
    {
        if (string.Equals(Path.GetFullPath(rootDirectory), RootDirectory, StringComparison.Ordinal))
            return;
        _store = new MyAssets(rootDirectory);
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- characters

    /// <summary>Every kept character, by name. A folder that can't be read is left out; a missing My Assets folder is just empty.</summary>
    public IReadOnlyList<CharacterDefinition> Characters() => _store.ListCharacters();

    /// <summary>
    /// Keeps <paramref name="character"/> in My Assets - or saves it over the copy already
    /// there - and returns it stamped with the fingerprint it now matches
    /// (<see cref="CharacterDefinition.MyAssetsVersion"/>), for the comic to record.
    /// </summary>
    public CharacterDefinition KeepCharacter(CharacterDefinition character)
    {
        var kept = character with { MyAssetsVersion = AssetFingerprint.CharacterFingerprint(character) };
        _store.SaveCharacter(kept);
        Changed?.Invoke();
        return kept;
    }

    /// <summary>Takes a character out of My Assets (and out of every pack). Comics that use it keep their own copy.</summary>
    public void RemoveCharacter(CharacterId id)
    {
        _store.RemoveCharacter(id);
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- object groups

    /// <summary>Every kept object group, by name. A folder that can't be read is left out.</summary>
    public IReadOnlyList<ObjectGroup> ObjectGroups() => _store.ListObjectGroups();

    /// <summary>The kept object group with <paramref name="id"/>, or null if My Assets doesn't have it (or can't read it).</summary>
    public ObjectGroup? FindObjectGroup(ObjectGroupId id) => ObjectGroups().FirstOrDefault(g => g.Id == id);

    /// <summary>Keeps <paramref name="group"/> in My Assets (or saves over the copy there) and returns it stamped with the fingerprint it now matches.</summary>
    public ObjectGroup KeepObjectGroup(ObjectGroup group)
    {
        var kept = group with { MyAssetsVersion = AssetFingerprint.ObjectGroupFingerprint(group) };
        _store.SaveObjectGroup(kept);
        Changed?.Invoke();
        return kept;
    }

    /// <summary>Renames a kept object group. Comics that use it keep the name they have until they take the new version.</summary>
    public void RenameObjectGroup(ObjectGroupId id, string name)
    {
        if (FindObjectGroup(id) is not { } group || string.IsNullOrWhiteSpace(name) || group.Name == name.Trim())
            return;
        KeepObjectGroup(group with { Name = name.Trim() });
    }

    /// <summary>Takes an object group out of My Assets (and out of every pack). Comics that use it keep their own copy.</summary>
    public void RemoveObjectGroup(ObjectGroupId id)
    {
        _store.RemoveObjectGroup(id);
        Changed?.Invoke();
    }

    /// <summary>"Group 1", "Group 2"... - the first name neither My Assets nor <paramref name="alsoTaken"/> uses yet.</summary>
    public string NextObjectGroupName(IEnumerable<string>? alsoTaken = null)
    {
        var taken = ObjectGroups().Select(g => g.Name).Concat(alsoTaken ?? []).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        for (var n = 1; ; n++)
        {
            var name = $"Group {n}";
            if (!taken.Contains(name))
                return name;
        }
    }
}
