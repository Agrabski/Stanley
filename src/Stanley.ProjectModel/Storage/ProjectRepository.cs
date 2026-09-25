using Stanley.ProjectModel.Backgrounds;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Stanley.ProjectModel.Props;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Storage;

/// <summary>
/// Reads and writes a Stanley project on disk. Every path is computed from
/// <see cref="ProjectPaths"/>; callers only ever supply ids and entity values, never a
/// path, so the folder structure from docs/character-and-project-plan.md is enforced
/// rather than optional.
/// </summary>
public sealed class ProjectRepository
{
    public string RootDirectory { get; }

    public ProjectRepository(string rootDirectory) => RootDirectory = Path.GetFullPath(rootDirectory);

    /// <summary>Whether <paramref name="rootDirectory"/> already contains a Stanley project (i.e. has a <c>stanley.json</c>).</summary>
    public static bool IsInitialized(string rootDirectory) =>
        File.Exists(Path.Combine(Path.GetFullPath(rootDirectory), ProjectPaths.ManifestFileName));

    /// <summary>Creates a brand-new, empty project on disk: the manifest, top-level folders, and the LFS <c>.gitattributes</c> rule.</summary>
    /// <param name="format">What the comic is set up as (a strip, a webcomic...); null for a printed comic book.</param>
    public static ProjectRepository Initialize(string rootDirectory, string title, PageTrim defaultPageTrim, ComicFormat? format = null)
    {
        var repository = new ProjectRepository(rootDirectory);
        Directory.CreateDirectory(repository.RootDirectory);

        Directory.CreateDirectory(repository.CharactersDir);
        Directory.CreateDirectory(repository.PosesDir);
        Directory.CreateDirectory(repository.PropsDir);
        Directory.CreateDirectory(repository.BackgroundsDir);
        Directory.CreateDirectory(repository.IssuesDir);

        File.WriteAllText(
            Path.Combine(repository.RootDirectory, ProjectPaths.GitAttributesFileName),
            "*.png filter=lfs diff=lfs merge=lfs -text\n" +
            "*.jpg filter=lfs diff=lfs merge=lfs -text\n" +
            "*.psd filter=lfs diff=lfs merge=lfs -text\n");

        repository.SaveManifest(new SeriesManifest(title, defaultPageTrim, [], format));
        return repository;
    }

    public SeriesManifest LoadManifest() => ProjectJson.Read<SeriesManifest>(ManifestPath);

    public void SaveManifest(SeriesManifest manifest) => ProjectJson.Write(ManifestPath, manifest);

    private string ManifestPath => Path.Combine(RootDirectory, ProjectPaths.ManifestFileName);

    private string CharactersDir => Path.Combine(RootDirectory, ProjectPaths.CharactersDirName);

    private string CharacterDirOrThrow(CharacterId id) =>
        ProjectPaths.FindEntityDir(CharactersDir, id) ?? throw NotFoundDir("character", id.Value);

    /// <summary>A character with everything in its folder: its stickers and pattern tiles (<see cref="CharacterDefinition.Wardrobe"/>) and its named looks.</summary>
    public CharacterDefinition LoadCharacter(CharacterId id) => ReadCharacterFolder(CharacterDirOrThrow(id));

    /// <summary>
    /// Every character in the project, sorted by name. There's no index file: a character
    /// is whatever <c>characters/&lt;id&gt;-slug/character.json</c> folders exist, so adding
    /// one never touches a shared file (and never conflicts in a merge).
    /// </summary>
    public IReadOnlyList<CharacterDefinition> ListCharacters()
    {
        if (!Directory.Exists(CharactersDir))
            return [];

        return Directory.EnumerateDirectories(CharactersDir)
            .Where(dir => File.Exists(Path.Combine(dir, ProjectPaths.CharacterFileName)))
            .Select(ReadCharacterFolder)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Removes a character's whole folder (revisions and stickers too). A no-op if it was never saved.</summary>
    public void DeleteCharacter(CharacterId id)
    {
        if (ProjectPaths.FindEntityDir(CharactersDir, id) is { } dir)
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
    public void SaveCharacter(CharacterDefinition character)
    {
        var dir = ProjectPaths.ResolveOrCreateEntityDir(CharactersDir, character.Id, character.Name);
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
                    .ToDictionary(f => Path.GetRelativePath(stickerDir, f).Replace('\\', '/'), ReadArtFile, StringComparer.Ordinal);
                stickers[sticker.Id] = new StickerAsset(sticker, files);
            }
        }

        var tilesDir = Path.Combine(characterDir, ProjectPaths.PatternsDirName);
        var tiles = Directory.Exists(tilesDir)
            ? Directory.EnumerateFiles(tilesDir).ToDictionary(f => Path.GetFileName(f), ReadArtFile, StringComparer.Ordinal)
            : new Dictionary<string, ArtFile>(StringComparer.Ordinal);
        return new Wardrobe(stickers, tiles);
    }

    private static ArtFile ReadArtFile(string path) =>
        string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase)
            ? ArtFile.Svg(File.ReadAllText(path))
            : ArtFile.Png(File.ReadAllBytes(path));

    private static void WriteWardrobe(string characterDir, Wardrobe wardrobe)
    {
        var stickersDir = Path.Combine(characterDir, ProjectPaths.StickersDirName);
        Directory.CreateDirectory(stickersDir);
        foreach (var asset in wardrobe.Stickers.Values)
        {
            var stickerDir = ProjectPaths.ResolveOrCreateEntityDir(stickersDir, asset.Id, asset.Sticker.Name);
            var json = Path.Combine(stickerDir, ProjectPaths.StickerFileName);
            ProjectJson.Write(json, asset.Sticker);
            WriteFiles(stickerDir, asset.Files, keep: json);
        }
        foreach (var stale in Directory.EnumerateDirectories(stickersDir)
                     .Where(d => !StickerId.TryParse(ProjectPaths.EntityIdPart(Path.GetFileName(d)), null, out var id) || !wardrobe.Stickers.ContainsKey(id))
                     .ToList())
            Directory.Delete(stale, recursive: true);

        var tilesDir = Path.Combine(characterDir, ProjectPaths.PatternsDirName);
        if (wardrobe.Tiles.Count > 0 || Directory.Exists(tilesDir))
        {
            Directory.CreateDirectory(tilesDir);
            WriteFiles(tilesDir, wardrobe.Tiles, keep: null);
        }
    }

    /// <summary>Writes <paramref name="files"/> (relative paths) under <paramref name="dir"/> where their content differs, and deletes every other file there except <paramref name="keep"/>.</summary>
    private static void WriteFiles(string dir, IReadOnlyDictionary<string, ArtFile> files, string? keep)
    {
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (relative, file) in files)
        {
            var path = Path.GetFullPath(Path.Combine(dir, relative));
            if (!path.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue; // never write outside the folder, whatever a hand-edited name says
            wanted.Add(path);
            if (File.Exists(path) && ReadArtFile(path).SameContent(file))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file.ToBytes());
        }
        foreach (var existing in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList())
        {
            var path = Path.GetFullPath(existing);
            if (!wanted.Contains(path) && (keep is null || path != Path.GetFullPath(keep)))
                File.Delete(path);
        }
        foreach (var empty in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length)
                     .Where(d => !Directory.EnumerateFileSystemEntries(d).Any())
                     .ToList())
            Directory.Delete(empty);
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

    public CharacterRevision LoadCharacterRevision(CharacterId characterId, CharacterRevisionId revisionId)
    {
        var dir = Path.Combine(CharacterDirOrThrow(characterId), ProjectPaths.RevisionsDirName);
        var path = ProjectPaths.FindEntityFile(dir, revisionId, ProjectPaths.JsonExtension)
            ?? throw NotFoundFile("character revision", revisionId.Value);
        return ProjectJson.Read<CharacterRevision>(path);
    }

    public void SaveCharacterRevision(CharacterRevision revision)
    {
        var dir = Path.Combine(CharacterDirOrThrow(revision.CharacterId), ProjectPaths.RevisionsDirName);
        var path = ProjectPaths.ResolveOrCreateEntityFilePath(dir, revision.Id, revision.Name, ProjectPaths.JsonExtension);
        ProjectJson.Write(path, revision);
    }

    private string PosesDir => Path.Combine(RootDirectory, ProjectPaths.PosesDirName);

    public Pose LoadPose(PoseId id)
    {
        var path = ProjectPaths.FindEntityFile(PosesDir, id, ProjectPaths.JsonExtension) ?? throw NotFoundFile("pose", id.Value);
        return ProjectJson.Read<Pose>(path);
    }

    public void SavePose(Pose pose)
    {
        var path = ProjectPaths.ResolveOrCreateEntityFilePath(PosesDir, pose.Id, pose.Name, ProjectPaths.JsonExtension);
        ProjectJson.Write(path, pose);
    }

    private string PropsDir => Path.Combine(RootDirectory, ProjectPaths.PropsDirName);

    public Prop LoadProp(PropId id)
    {
        var dir = ProjectPaths.FindEntityDir(PropsDir, id) ?? throw NotFoundDir("prop", id.Value);
        return ProjectJson.Read<Prop>(Path.Combine(dir, ProjectPaths.PropFileName));
    }

    public void SaveProp(Prop prop)
    {
        var dir = ProjectPaths.ResolveOrCreateEntityDir(PropsDir, prop.Id, prop.Name);
        Directory.CreateDirectory(Path.Combine(dir, ProjectPaths.VariantsDirName));
        ProjectJson.Write(Path.Combine(dir, ProjectPaths.PropFileName), prop);
    }

    private string BackgroundsDir => Path.Combine(RootDirectory, ProjectPaths.BackgroundsDirName);

    private string BackgroundDirOrThrow(BackgroundId id) =>
        ProjectPaths.FindEntityDir(BackgroundsDir, id) ?? throw NotFoundDir("background", id.Value);

    public Background LoadBackground(BackgroundId id) =>
        ProjectJson.Read<Background>(Path.Combine(BackgroundDirOrThrow(id), ProjectPaths.BackgroundFileName));

    public void SaveBackground(Background background)
    {
        var dir = ProjectPaths.ResolveOrCreateEntityDir(BackgroundsDir, background.Id, background.Name);
        Directory.CreateDirectory(Path.Combine(dir, ProjectPaths.BackdropsDirName));
        Directory.CreateDirectory(Path.Combine(dir, ProjectPaths.RevisionsDirName));
        ProjectJson.Write(Path.Combine(dir, ProjectPaths.BackgroundFileName), background);
    }

    public BackgroundRevision LoadBackgroundRevision(BackgroundId backgroundId, BackgroundRevisionId revisionId)
    {
        var dir = Path.Combine(BackgroundDirOrThrow(backgroundId), ProjectPaths.RevisionsDirName);
        var path = ProjectPaths.FindEntityFile(dir, revisionId, ProjectPaths.JsonExtension)
            ?? throw NotFoundFile("background revision", revisionId.Value);
        return ProjectJson.Read<BackgroundRevision>(path);
    }

    public void SaveBackgroundRevision(BackgroundRevision revision)
    {
        var dir = Path.Combine(BackgroundDirOrThrow(revision.BackgroundId), ProjectPaths.RevisionsDirName);
        var path = ProjectPaths.ResolveOrCreateEntityFilePath(dir, revision.Id, revision.Name, ProjectPaths.JsonExtension);
        ProjectJson.Write(path, revision);
    }

    private string IssuesDir => Path.Combine(RootDirectory, ProjectPaths.IssuesDirName);

    private string IssueDirOrThrow(IssueId id) =>
        ProjectPaths.FindEntityDir(IssuesDir, id) ?? throw NotFoundDir("issue", id.Value);

    public Issue LoadIssue(IssueId id) =>
        ProjectJson.Read<Issue>(Path.Combine(IssueDirOrThrow(id), ProjectPaths.IssueFileName));

    public void SaveIssue(Issue issue)
    {
        var dir = ProjectPaths.ResolveOrCreateEntityDir(IssuesDir, issue.Id, DisplayName(issue.Number, issue.Title));
        Directory.CreateDirectory(Path.Combine(dir, ProjectPaths.PagesDirName));
        Directory.CreateDirectory(Path.Combine(dir, ProjectPaths.ArtDirName));
        ProjectJson.Write(Path.Combine(dir, ProjectPaths.IssueFileName), issue);
    }

    private string PagesDir(IssueId issueId) => Path.Combine(IssueDirOrThrow(issueId), ProjectPaths.PagesDirName);

    private string PageDirOrThrow(IssueId issueId, PageId pageId) =>
        ProjectPaths.FindEntityDir(PagesDir(issueId), pageId) ?? throw NotFoundDir("page", pageId.Value);

    public Page LoadPage(IssueId issueId, PageId pageId) =>
        ProjectJson.Read<Page>(Path.Combine(PageDirOrThrow(issueId, pageId), ProjectPaths.PageFileName));

    public void SavePage(IssueId issueId, Page page)
    {
        var dir = ProjectPaths.ResolveOrCreateEntityDir(PagesDir(issueId), page.Id, page.Label ?? "page");
        Directory.CreateDirectory(Path.Combine(dir, ProjectPaths.PanelsDirName));
        ProjectJson.Write(Path.Combine(dir, ProjectPaths.PageFileName), page);
    }

    public Panel LoadPanel(IssueId issueId, PageId pageId, PanelId panelId)
    {
        var path = ProjectPaths.PanelFilePath(Path.Combine(PageDirOrThrow(issueId, pageId), ProjectPaths.PanelsDirName), panelId);
        if (!File.Exists(path))
            throw NotFoundFile("panel", panelId.Value);
        return ProjectJson.Read<Panel>(path);
    }

    public void SavePanel(IssueId issueId, PageId pageId, Panel panel)
    {
        var panelsDir = Path.Combine(PageDirOrThrow(issueId, pageId), ProjectPaths.PanelsDirName);
        Directory.CreateDirectory(panelsDir);
        ProjectJson.Write(ProjectPaths.PanelFilePath(panelsDir, panel.Id), panel);
    }

    /// <summary>Removes a page's folder (its page.json and all its panels), e.g. after the page was deleted in the editor. A no-op if it was never saved. The caller removes it from its issue's <c>PageIds</c>.</summary>
    public void DeletePage(IssueId issueId, PageId pageId)
    {
        if (ProjectPaths.FindEntityDir(PagesDir(issueId), pageId) is { } dir)
            Directory.Delete(dir, recursive: true);
    }

    /// <summary>Removes a panel's file, e.g. after the panel was deleted in the editor. A no-op if it was never saved.</summary>
    public void DeletePanel(IssueId issueId, PageId pageId, PanelId panelId)
    {
        var path = ProjectPaths.PanelFilePath(Path.Combine(PageDirOrThrow(issueId, pageId), ProjectPaths.PanelsDirName), panelId);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string IssueArtDir(IssueId issueId) => Path.Combine(IssueDirOrThrow(issueId), ProjectPaths.ArtDirName);

    /// <summary>A picture from the issue's <c>art/</c> folder (see <see cref="IssueArt"/>), or null if there's no such file (or the name isn't a plain picture file name).</summary>
    public ArtFile? LoadIssueArt(IssueId issueId, string name)
    {
        if (!IssueArt.IsValidName(name))
            return null;
        var path = Path.Combine(IssueArtDir(issueId), name);
        return File.Exists(path) ? ReadArtFile(path) : null;
    }

    /// <summary>Writes a picture into the issue's <c>art/</c> folder - unless the same content is already there, so an unchanged save touches nothing.</summary>
    public void SaveIssueArt(IssueId issueId, string name, ArtFile file)
    {
        if (!IssueArt.IsValidName(name))
            throw new ArgumentException($"'{name}' isn't a picture file name.", nameof(name));
        var dir = IssueArtDir(issueId);
        var path = Path.Combine(dir, name);
        if (File.Exists(path) && ReadArtFile(path).SameContent(file))
            return;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, file.ToBytes());
    }

    /// <summary>Removes a picture from the issue's <c>art/</c> folder; a no-op if it isn't there.</summary>
    public void DeleteIssueArt(IssueId issueId, string name)
    {
        if (!IssueArt.IsValidName(name))
            return;
        var path = Path.Combine(IssueArtDir(issueId), name);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string DisplayName(string number, string title) => string.IsNullOrWhiteSpace(title) ? number : $"{number} {title}";

    private static DirectoryNotFoundException NotFoundDir(string kind, string id) =>
        new($"No {kind} with id '{id}' was found.");

    private static FileNotFoundException NotFoundFile(string kind, string id) =>
        new($"No {kind} with id '{id}' was found.");
}
