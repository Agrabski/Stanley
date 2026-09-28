using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Storage;

/// <summary>
/// Reads and writes character folders under any <c>characters/</c>-shaped root - a comic's
/// own (via <see cref="ProjectRepository"/>) or My Assets' (via <see cref="MyAssets"/>) -
/// so the format can never drift between the two (docs/my-characters.md §9). Extracted from
/// <see cref="ProjectRepository"/> with no change in behaviour.
/// </summary>
internal sealed class CharacterStore(string rootDirectory)
{
    private string CharacterDirOrThrow(CharacterId id) =>
        ProjectPaths.FindEntityDir(rootDirectory, id) ?? throw new DirectoryNotFoundException($"No character with id '{id.Value}' was found.");

    public CharacterDefinition Load(CharacterId id) => ReadCharacterFolder(CharacterDirOrThrow(id));

    /// <param name="skipUnreadable">Leave out a folder that can't be read instead of throwing - for My Assets, a convenience where one damaged folder mustn't hide the rest. Never for a comic, whose save would then delete what it didn't read.</param>
    public IReadOnlyList<CharacterDefinition> List(bool skipUnreadable = false)
    {
        if (!Directory.Exists(rootDirectory))
            return [];

        return Directory.EnumerateDirectories(rootDirectory)
            .Where(dir => File.Exists(Path.Combine(dir, ProjectPaths.CharacterFileName)))
            .Select(dir => StoreReads.Read(dir, ReadCharacterFolder, skipUnreadable))
            .OfType<CharacterDefinition>()
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    public void Delete(CharacterId id)
    {
        if (ProjectPaths.FindEntityDir(rootDirectory, id) is { } dir)
            Directory.Delete(dir, recursive: true);
    }

    // A character.json written before bodies (or stickers) existed lacks them; it gets the defaults.
    private static CharacterDefinition ReadCharacterFolder(string dir)
    {
        var character = ProjectJson.Read<CharacterDefinition>(Path.Combine(dir, ProjectPaths.CharacterFileName)).Normalized();
        return character with { Wardrobe = ReadWardrobe(dir), Revisions = ReadRevisions(dir) };
    }

    /// <summary>
    /// Writes the character's whole folder: <c>character.json</c>, every sticker in its
    /// wardrobe (<c>sticker.json</c> plus its art files, byte for byte), its pattern tiles
    /// and its named looks - and removes the stickers, art files, tiles and looks it no
    /// longer has. Files whose content is unchanged aren't rewritten.
    /// </summary>
    public void Save(CharacterDefinition character)
    {
        var dir = ProjectPaths.ResolveOrCreateEntityDir(rootDirectory, character.Id, character.Name);
        ProjectJson.Write(Path.Combine(dir, ProjectPaths.CharacterFileName), character);
        WriteWardrobe(dir, character.Wardrobe);
        WriteRevisions(dir, character.Id, character.Revisions);
    }

    private static Wardrobe ReadWardrobe(string characterDir)
    {
        var stickers = new Dictionary<StickerId, StickerAsset>();
        var stickersDir = Path.Combine(characterDir, ProjectPaths.StickersDirName);
        if (Directory.Exists(stickersDir))
        {
            foreach (var stickerDir in Directory.EnumerateDirectories(stickersDir))
            {
                var json = Path.Combine(stickerDir, ProjectPaths.StickerFileName);
                if (!File.Exists(json))
                    continue;
                var sticker = ProjectJson.Read<Sticker>(json).Normalized();
                var files = Directory.EnumerateFiles(stickerDir, "*", SearchOption.AllDirectories)
                    .Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(json), StringComparison.Ordinal))
                    .ToDictionary(f => Path.GetRelativePath(stickerDir, f).Replace('\\', '/'), ArtFileIO.ReadArtFile, StringComparer.Ordinal);
                stickers[sticker.Id] = new StickerAsset(sticker, files);
            }
        }

        var tiles = ArtFileIO.ReadFlat(Path.Combine(characterDir, ProjectPaths.PatternsDirName));
        return new Wardrobe(stickers, tiles);
    }

    private static void WriteWardrobe(string characterDir, Wardrobe wardrobe)
    {
        var stickersDir = Path.Combine(characterDir, ProjectPaths.StickersDirName);
        Directory.CreateDirectory(stickersDir);
        foreach (var asset in wardrobe.Stickers.Values)
        {
            var stickerDir = ProjectPaths.ResolveOrCreateEntityDir(stickersDir, asset.Id, asset.Sticker.Name);
            var json = Path.Combine(stickerDir, ProjectPaths.StickerFileName);
            ProjectJson.Write(json, asset.Sticker);
            ArtFileIO.WriteFiles(stickerDir, asset.Files, keep: json);
        }
        foreach (var stale in Directory.EnumerateDirectories(stickersDir)
                     .Where(d => !StickerId.TryParse(ProjectPaths.EntityIdPart(Path.GetFileName(d)), null, out var id) || !wardrobe.Stickers.ContainsKey(id))
                     .ToList())
            Directory.Delete(stale, recursive: true);

        var tilesDir = Path.Combine(characterDir, ProjectPaths.PatternsDirName);
        if (wardrobe.Tiles.Count > 0 || Directory.Exists(tilesDir))
        {
            Directory.CreateDirectory(tilesDir);
            ArtFileIO.WriteFiles(tilesDir, wardrobe.Tiles, keep: null);
        }
    }

    private static IReadOnlyDictionary<CharacterRevisionId, CharacterRevision> ReadRevisions(string characterDir)
    {
        var dir = Path.Combine(characterDir, ProjectPaths.RevisionsDirName);
        if (!Directory.Exists(dir))
            return new Dictionary<CharacterRevisionId, CharacterRevision>();
        return Directory.EnumerateFiles(dir, "*." + ProjectPaths.JsonExtension)
            .Select(ProjectJson.Read<CharacterRevision>)
            .ToDictionary(r => r.Id);
    }

    private static void WriteRevisions(string characterDir, CharacterId characterId, IReadOnlyDictionary<CharacterRevisionId, CharacterRevision> revisions)
    {
        var dir = Path.Combine(characterDir, ProjectPaths.RevisionsDirName);
        foreach (var revision in revisions.Values)
            ProjectJson.Write(ProjectPaths.ResolveOrCreateEntityFilePath(dir, revision.Id, revision.Name, ProjectPaths.JsonExtension), revision with { CharacterId = characterId });
        if (!Directory.Exists(dir))
            return;
        foreach (var stale in Directory.EnumerateFiles(dir, "*." + ProjectPaths.JsonExtension)
                     .Where(f => !CharacterRevisionId.TryParse(ProjectPaths.EntityIdPart(Path.GetFileName(f)), null, out var id) || !revisions.ContainsKey(id))
                     .ToList())
            File.Delete(stale);
    }

    public CharacterRevision LoadRevision(CharacterId characterId, CharacterRevisionId revisionId)
    {
        var dir = Path.Combine(CharacterDirOrThrow(characterId), ProjectPaths.RevisionsDirName);
        var path = ProjectPaths.FindEntityFile(dir, revisionId, ProjectPaths.JsonExtension)
            ?? throw new FileNotFoundException($"No character revision with id '{revisionId.Value}' was found.");
        return ProjectJson.Read<CharacterRevision>(path);
    }

    public void SaveRevision(CharacterRevision revision)
    {
        var dir = Path.Combine(CharacterDirOrThrow(revision.CharacterId), ProjectPaths.RevisionsDirName);
        var path = ProjectPaths.ResolveOrCreateEntityFilePath(dir, revision.Id, revision.Name, ProjectPaths.JsonExtension);
        ProjectJson.Write(path, revision);
    }
}
