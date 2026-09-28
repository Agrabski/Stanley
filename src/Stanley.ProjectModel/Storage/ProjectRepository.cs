using Stanley.ProjectModel.Backgrounds;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Objects;
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

    private readonly CharacterStore _characters;
    private readonly ObjectGroupStore _objectGroups;

    public ProjectRepository(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        _characters = new CharacterStore(CharactersDir);
        _objectGroups = new ObjectGroupStore(ObjectsDir);
    }

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
        Directory.CreateDirectory(repository.ObjectsDir);

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

    /// <summary>A character with everything in its folder: its stickers and pattern tiles (<see cref="CharacterDefinition.Wardrobe"/>) and its named looks.</summary>
    public CharacterDefinition LoadCharacter(CharacterId id) => _characters.Load(id);

    /// <summary>
    /// Every character in the project, sorted by name. There's no index file: a character
    /// is whatever <c>characters/&lt;id&gt;-slug/character.json</c> folders exist, so adding
    /// one never touches a shared file (and never conflicts in a merge).
    /// </summary>
    public IReadOnlyList<CharacterDefinition> ListCharacters() => _characters.List();

    /// <summary>Removes a character's whole folder (revisions and stickers too). A no-op if it was never saved.</summary>
    public void DeleteCharacter(CharacterId id) => _characters.Delete(id);

    /// <summary>
    /// Writes the character's whole folder: <c>character.json</c>, every sticker in its
    /// wardrobe (<c>sticker.json</c> plus its art files, byte for byte), its pattern tiles
    /// and its named looks - and removes the stickers, art files, tiles and looks it no
    /// longer has. Files whose content is unchanged aren't rewritten.
    /// </summary>
    public void SaveCharacter(CharacterDefinition character) => _characters.Save(character);

    public CharacterRevision LoadCharacterRevision(CharacterId characterId, CharacterRevisionId revisionId) => _characters.LoadRevision(characterId, revisionId);

    public void SaveCharacterRevision(CharacterRevision revision) => _characters.SaveRevision(revision);

    private string ObjectsDir => Path.Combine(RootDirectory, ProjectPaths.ObjectsDirName);

    /// <summary>An out-of-line object group (docs/asset-packs.md §7.1) a panel's <see cref="GroupElement.SourceId"/> can reference, with the art files its children use.</summary>
    public ObjectGroup LoadObjectGroup(ObjectGroupId id) => _objectGroups.Load(id);

    /// <summary>Every object group kept in this comic's <c>objects/</c> folder.</summary>
    public IReadOnlyList<ObjectGroup> ListObjectGroups() => _objectGroups.List();

    /// <summary>Writes an object group's <c>group.json</c> and its art files, removing art it no longer references.</summary>
    public void SaveObjectGroup(ObjectGroup group) => _objectGroups.Save(group);

    /// <summary>Removes an object group's whole folder. A no-op if it was never saved. Existing panels keep whatever inline copy they already have.</summary>
    public void DeleteObjectGroup(ObjectGroupId id) => _objectGroups.Delete(id);

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

    /// <summary>Removes an issue's whole folder (its pages, panels and art). A no-op if it was never saved. The caller removes it from the manifest's <c>IssueIds</c>.</summary>
    public void DeleteIssue(IssueId id)
    {
        if (ProjectPaths.FindEntityDir(IssuesDir, id) is { } dir)
            Directory.Delete(dir, recursive: true);
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
        return File.Exists(path) ? ArtFileIO.ReadArtFile(path) : null;
    }

    /// <summary>Writes a picture into the issue's <c>art/</c> folder - unless the same content is already there, so an unchanged save touches nothing.</summary>
    public void SaveIssueArt(IssueId issueId, string name, ArtFile file)
    {
        if (!IssueArt.IsValidName(name))
            throw new ArgumentException($"'{name}' isn't a picture file name.", nameof(name));
        var dir = IssueArtDir(issueId);
        var path = Path.Combine(dir, name);
        if (File.Exists(path) && ArtFileIO.ReadArtFile(path).SameContent(file))
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

    // ---------------------------------------------------------------- the comic's title page

    private string TitlePageDir => Path.Combine(RootDirectory, ProjectPaths.TitlePageDirName);

    private string TitlePagePanelsDir => Path.Combine(TitlePageDir, ProjectPaths.PanelsDirName);

    private string TitlePageArtDir => Path.Combine(TitlePageDir, ProjectPaths.ArtDirName);

    /// <summary>The comic's title page (<c>title-page/page.json</c>) - shared by every issue that has none of its own - or null if the comic has none.</summary>
    public Page? LoadTitlePage()
    {
        var path = Path.Combine(TitlePageDir, ProjectPaths.PageFileName);
        return File.Exists(path) ? ProjectJson.Read<Page>(path) : null;
    }

    public void SaveTitlePage(Page page)
    {
        Directory.CreateDirectory(TitlePagePanelsDir);
        ProjectJson.Write(Path.Combine(TitlePageDir, ProjectPaths.PageFileName), page);
    }

    public Panel LoadTitlePagePanel(PanelId panelId)
    {
        var path = ProjectPaths.PanelFilePath(TitlePagePanelsDir, panelId);
        if (!File.Exists(path))
            throw NotFoundFile("panel", panelId.Value);
        return ProjectJson.Read<Panel>(path);
    }

    public void SaveTitlePagePanel(Panel panel)
    {
        Directory.CreateDirectory(TitlePagePanelsDir);
        ProjectJson.Write(ProjectPaths.PanelFilePath(TitlePagePanelsDir, panel.Id), panel);
    }

    /// <summary>Removes one of the title page's panel files; a no-op if it isn't there.</summary>
    public void DeleteTitlePagePanel(PanelId panelId)
    {
        var path = ProjectPaths.PanelFilePath(TitlePagePanelsDir, panelId);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Removes the comic's title page - page, panels and pictures; a no-op if it has none.</summary>
    public void DeleteTitlePage()
    {
        if (Directory.Exists(TitlePageDir))
            Directory.Delete(TitlePageDir, recursive: true);
    }

    /// <summary>A picture from the title page's own <c>art/</c> folder (named like an issue's, see <see cref="IssueArt"/>), or null if it isn't there.</summary>
    public ArtFile? LoadTitlePageArt(string name)
    {
        if (!IssueArt.IsValidName(name))
            return null;
        var path = Path.Combine(TitlePageArtDir, name);
        return File.Exists(path) ? ArtFileIO.ReadArtFile(path) : null;
    }

    /// <summary>Writes a picture the title page uses - unless the same content is already there.</summary>
    public void SaveTitlePageArt(string name, ArtFile file)
    {
        if (!IssueArt.IsValidName(name))
            throw new ArgumentException($"'{name}' isn't a picture file name.", nameof(name));
        var path = Path.Combine(TitlePageArtDir, name);
        if (File.Exists(path) && ArtFileIO.ReadArtFile(path).SameContent(file))
            return;
        Directory.CreateDirectory(TitlePageArtDir);
        File.WriteAllBytes(path, file.ToBytes());
    }

    public void DeleteTitlePageArt(string name)
    {
        if (!IssueArt.IsValidName(name))
            return;
        var path = Path.Combine(TitlePageArtDir, name);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string DisplayName(string number, string title) => string.IsNullOrWhiteSpace(title) ? number : $"{number} {title}";

    private static DirectoryNotFoundException NotFoundDir(string kind, string id) =>
        new($"No {kind} with id '{id}' was found.");

    private static FileNotFoundException NotFoundFile(string kind, string id) =>
        new($"No {kind} with id '{id}' was found.");
}
