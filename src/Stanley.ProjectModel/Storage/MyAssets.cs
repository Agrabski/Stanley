using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Storage;

/// <summary>
/// One user's My Assets folder (docs/asset-packs.md §7.2): characters, object groups and
/// packs, read and written the same way a comic's own folder is - just a different root, so
/// a comic and My Assets share the exact same <see cref="CharacterStore"/> and
/// <see cref="ObjectGroupStore"/> (docs/asset-packs.md §9, "share one implementation").
/// Only the two kinds #99 and the original "My Characters" ask need are usable yet; the
/// other four (docs/asset-packs.md §5) are added in a later slice reusing this same class.
/// Takes a root directory rather than resolving one itself - the real default path
/// (<c>Documents/Stanley/My Assets/</c>, a <c>STANLEY_DATA_DIR</c> test override) and the
/// settings entry are <c>Stanley.App</c>'s job, added when the "Keep" UI ships.
/// </summary>
public sealed class MyAssets
{
    public string RootDirectory { get; }

    private readonly CharacterStore _characters;
    private readonly ObjectGroupStore _objectGroups;

    public MyAssets(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        _characters = new CharacterStore(Path.Combine(RootDirectory, ProjectPaths.CharactersDirName));
        _objectGroups = new ObjectGroupStore(Path.Combine(RootDirectory, ProjectPaths.ObjectsDirName));
    }

    private string PacksDir => Path.Combine(RootDirectory, ProjectPaths.PacksDirName);

    public IReadOnlyList<CharacterDefinition> ListCharacters() => _characters.List();

    public CharacterDefinition LoadCharacter(CharacterId id) => _characters.Load(id);

    public void SaveCharacter(CharacterDefinition character) => _characters.Save(character);

    /// <summary>Removes a character from My Assets (comics that use it keep their own copy) and drops it from every pack that referenced it.</summary>
    public void RemoveCharacter(CharacterId id)
    {
        _characters.Delete(id);
        RemoveMember(AssetKind.Character, id.Value);
    }

    public IReadOnlyList<ObjectGroup> ListObjectGroups() => _objectGroups.List();

    public ObjectGroup LoadObjectGroup(ObjectGroupId id) => _objectGroups.Load(id);

    public void SaveObjectGroup(ObjectGroup group) => _objectGroups.Save(group);

    /// <summary>Removes an object group from My Assets and drops it from every pack that referenced it.</summary>
    public void RemoveObjectGroup(ObjectGroupId id)
    {
        _objectGroups.Delete(id);
        RemoveMember(AssetKind.ObjectGroup, id.Value);
    }

    /// <summary>Every pack in <c>packs/</c>, sorted by name - there's no index file, matching every other kind.</summary>
    public IReadOnlyList<AssetPack> ListPacks()
    {
        if (!Directory.Exists(PacksDir))
            return [];

        return Directory.EnumerateFiles(PacksDir, "*." + ProjectPaths.JsonExtension)
            .Select(ProjectJson.Read<AssetPack>)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    public AssetPack LoadPack(AssetPackId id)
    {
        var path = ProjectPaths.FindEntityFile(PacksDir, id, ProjectPaths.JsonExtension)
            ?? throw new FileNotFoundException($"No pack with id '{id.Value}' was found.");
        return ProjectJson.Read<AssetPack>(path);
    }

    /// <summary>Writes a pack with its members sorted (by kind, then id) so adding one is a one-line diff.</summary>
    public void SavePack(AssetPack pack)
    {
        var path = ProjectPaths.ResolveOrCreateEntityFilePath(PacksDir, pack.Id, pack.Name, ProjectPaths.JsonExtension);
        ProjectJson.Write(path, pack with { Members = SortedMembers(pack.Members) });
    }

    /// <summary>Deletes a pack; the assets it listed stay in My Assets. A no-op if it was never saved.</summary>
    public void RemovePack(AssetPackId id)
    {
        if (ProjectPaths.FindEntityFile(PacksDir, id, ProjectPaths.JsonExtension) is { } path)
            File.Delete(path);
    }

    private static IReadOnlyList<AssetPackMember> SortedMembers(IReadOnlyList<AssetPackMember> members) =>
        members.OrderBy(m => m.Kind).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();

    private void RemoveMember(AssetKind kind, string id)
    {
        foreach (var pack in ListPacks())
        {
            if (!pack.Members.Any(m => m.Kind == kind && m.Id == id))
                continue;
            SavePack(pack with { Members = pack.Members.Where(m => m.Kind != kind || m.Id != id).ToList() });
        }
    }
}
