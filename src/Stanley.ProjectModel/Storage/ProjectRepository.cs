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
    public static ProjectRepository Initialize(string rootDirectory, string title, PageTrim defaultPageTrim)
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

        repository.SaveManifest(new SeriesManifest(title, defaultPageTrim, []));
        return repository;
    }

    // --- Series manifest ---

    public SeriesManifest LoadManifest() => ProjectJson.Read<SeriesManifest>(ManifestPath);

    public void SaveManifest(SeriesManifest manifest) => ProjectJson.Write(ManifestPath, manifest);

    private string ManifestPath => Path.Combine(RootDirectory, ProjectPaths.ManifestFileName);

    // --- Characters ---

    private string CharactersDir => Path.Combine(RootDirectory, ProjectPaths.CharactersDirName);

    private string CharacterDirOrThrow(CharacterId id) =>
        ProjectPaths.FindEntityDir(CharactersDir, id) ?? throw NotFoundDir("character", id.Value);

    public CharacterDefinition LoadCharacter(CharacterId id) =>
        ProjectJson.Read<CharacterDefinition>(Path.Combine(CharacterDirOrThrow(id), ProjectPaths.CharacterFileName));

    public void SaveCharacter(CharacterDefinition character)
    {
        var dir = ProjectPaths.ResolveOrCreateEntityDir(CharactersDir, character.Id, character.Name);
        Directory.CreateDirectory(Path.Combine(dir, ProjectPaths.RevisionsDirName));
        Directory.CreateDirectory(Path.Combine(dir, ProjectPaths.StickersDirName));
        ProjectJson.Write(Path.Combine(dir, ProjectPaths.CharacterFileName), character);
    }

    // --- Character revisions ---

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

    // --- Stickers ---

    private string StickersDir(CharacterId characterId) => Path.Combine(CharacterDirOrThrow(characterId), ProjectPaths.StickersDirName);

    private string StickerDirOrThrow(CharacterId characterId, StickerId stickerId) =>
        ProjectPaths.FindEntityDir(StickersDir(characterId), stickerId) ?? throw NotFoundDir("sticker", stickerId.Value);

    public Sticker LoadSticker(CharacterId characterId, StickerId stickerId) =>
        ProjectJson.Read<Sticker>(Path.Combine(StickerDirOrThrow(characterId, stickerId), ProjectPaths.StickerFileName));

    public void SaveSticker(CharacterId characterId, Sticker sticker)
    {
        var dir = ProjectPaths.ResolveOrCreateEntityDir(StickersDir(characterId), sticker.Id, sticker.Name);
        Directory.CreateDirectory(Path.Combine(dir, ProjectPaths.VariantsDirName));
        ProjectJson.Write(Path.Combine(dir, ProjectPaths.StickerFileName), sticker);
    }

    /// <summary>The <see cref="StickerKind.BuildStretch"/> region sibling of a sticker.</summary>
    public StretchRegion LoadStretchRegion(CharacterId characterId, StickerId stickerId) =>
        ProjectJson.Read<StretchRegion>(Path.Combine(StickerDirOrThrow(characterId, stickerId), ProjectPaths.StretchFileName));

    public void SaveStretchRegion(CharacterId characterId, StickerId stickerId, StretchRegion region) =>
        ProjectJson.Write(Path.Combine(StickerDirOrThrow(characterId, stickerId), ProjectPaths.StretchFileName), region);

    // --- Poses ---

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

    // --- Props ---

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

    // --- Backgrounds ---

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

    // --- Issues, pages, panels ---

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

    private static string DisplayName(string number, string title) => string.IsNullOrWhiteSpace(title) ? number : $"{number} {title}";

    private static DirectoryNotFoundException NotFoundDir(string kind, string id) =>
        new($"No {kind} with id '{id}' was found.");

    private static FileNotFoundException NotFoundFile(string kind, string id) =>
        new($"No {kind} with id '{id}' was found.");
}
