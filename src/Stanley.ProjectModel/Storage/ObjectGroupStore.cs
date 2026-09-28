using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Storage;

/// <summary>
/// Reads and writes object group folders under any <c>objects/</c>-shaped root - a comic's
/// own (via <see cref="ProjectRepository"/>) or My Assets' (via <see cref="MyAssets"/>) -
/// the same "share one implementation" shape as <see cref="CharacterStore"/>
/// (docs/asset-packs.md §9).
/// </summary>
internal sealed class ObjectGroupStore(string rootDirectory)
{
    public ObjectGroup Load(ObjectGroupId id) => ReadFolder(DirOrThrow(id));

    /// <summary>Every object group in this root, sorted by name - there's no index file, matching every other kind.</summary>
    /// <param name="skipUnreadable">As <see cref="CharacterStore.List"/>: leave out a folder that can't be read (My Assets only).</param>
    public IReadOnlyList<ObjectGroup> List(bool skipUnreadable = false)
    {
        if (!Directory.Exists(rootDirectory))
            return [];

        return Directory.EnumerateDirectories(rootDirectory)
            .Where(dir => File.Exists(Path.Combine(dir, ProjectPaths.GroupFileName)))
            .Select(dir => StoreReads.Read(dir, ReadFolder, skipUnreadable))
            .OfType<ObjectGroup>()
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Writes <c>group.json</c> and this group's art files, removing art it no longer references. Files whose content is unchanged aren't rewritten.</summary>
    public void Save(ObjectGroup group)
    {
        var dir = ProjectPaths.ResolveOrCreateEntityDir(rootDirectory, group.Id, group.Name);
        ProjectJson.Write(Path.Combine(dir, ProjectPaths.GroupFileName), group);

        var artDir = Path.Combine(dir, ProjectPaths.ArtDirName);
        if (group.ArtFiles.Count > 0 || Directory.Exists(artDir))
        {
            Directory.CreateDirectory(artDir);
            ArtFileIO.WriteFiles(artDir, group.ArtFiles, keep: null);
        }
    }

    public void Delete(ObjectGroupId id)
    {
        if (ProjectPaths.FindEntityDir(rootDirectory, id) is { } dir)
            Directory.Delete(dir, recursive: true);
    }

    private string DirOrThrow(ObjectGroupId id) =>
        ProjectPaths.FindEntityDir(rootDirectory, id) ?? throw new DirectoryNotFoundException($"No object group with id '{id.Value}' was found.");

    private static ObjectGroup ReadFolder(string dir)
    {
        var group = ProjectJson.Read<ObjectGroup>(Path.Combine(dir, ProjectPaths.GroupFileName));
        return group with { ArtFiles = ArtFileIO.ReadFlat(Path.Combine(dir, ProjectPaths.ArtDirName)) };
    }
}
