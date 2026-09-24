using Stanley.Editing;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Storage;
using Stanley.Rendering;
using PanelModel = Stanley.ProjectModel.Issues.Panel;

namespace Stanley.Editors;

/// <summary>One page as the editor sees it: its id, its print size, and its content.</summary>
public sealed record ComicPage(PageId Id, PageTrim Trim, PageDocument Document)
{
    public Rect2D Bounds => new(0, 0, Trim.Size.WidthMm, Trim.Size.HeightMm);
}

/// <summary>
/// An open comic - the thing File &gt; New/Open/Save act on, the way Word acts on a
/// document. On disk it's a Stanley project folder (see <see cref="ProjectRepository"/>);
/// the editor works on the pages of its first issue (created on the fly for a project
/// that has none yet, e.g. one fresh from <c>stanley init</c>), plus the project's
/// characters. Everything else in the folder is left untouched by a save, and carried
/// along by Save As.
/// </summary>
public sealed class ComicProject
{
    public const string UntitledTitle = "Untitled comic";
    public const double DefaultBleedMm = 3;

    private Issue _issue;
    // What's on disk (at Location) as of the last open/save: page records (kept so a
    // page's label and trim override survive a save) and each page's panel ids, so a
    // save can delete the files of pages and panels removed since.
    private Dictionary<PageId, Page> _pageRecords;
    private Dictionary<PageId, HashSet<PanelId>> _savedPanels;
    // Characters on disk as of the last open/save, so a save can delete removed ones.
    private HashSet<CharacterId> _savedCharacters;

    private ComicProject(string? location, string title, PageTrim trim, Issue issue, IReadOnlyList<ComicPage> pages,
        Dictionary<PageId, Page> pageRecords, Dictionary<PageId, HashSet<PanelId>> savedPanels,
        IReadOnlyList<CharacterDefinition> characters, HashSet<CharacterId> savedCharacters)
    {
        Characters = characters;
        _savedCharacters = savedCharacters;
        Location = location;
        Title = title;
        Trim = trim;
        _issue = issue;
        Pages = pages;
        _pageRecords = pageRecords;
        _savedPanels = savedPanels;
    }

    /// <summary>The project folder, or null for a comic that has never been saved (Save then behaves as Save As).</summary>
    public string? Location { get; private set; }

    public bool IsUntitled => Location is null;

    /// <summary>The series title (<c>stanley.json</c>'s title); shown in the window title and editable from File &gt; Info.</summary>
    public string Title { get; set; }

    /// <summary>The project's default page size - what new pages get.</summary>
    public PageTrim Trim { get; }

    public Rect2D PageBounds => new(0, 0, Trim.Size.WidthMm, Trim.Size.HeightMm);

    /// <summary>The pages as they were opened, in reading order - the editor's starting point.</summary>
    public IReadOnlyList<ComicPage> Pages { get; }

    /// <summary>The project's characters as they were opened, sorted by name.</summary>
    public IReadOnlyList<CharacterDefinition> Characters { get; }

    /// <summary>The issue's printed page numbers, as opened.</summary>
    public PageNumbering PageNumbering => _issue.PageNumbering ?? PageNumbering.Off;

    /// <summary>A brand-new, unsaved comic with one page: either one panel filling the live area or tiled with <paramref name="layout"/>.</summary>
    public static ComicProject CreateNew(PageTrim trim, PanelLayoutPreset? layout = null, PanelGrid? grid = null)
    {
        var issue = NewIssue();
        var page = new ComicPage(PageId.New(), trim, BlankDocument(new Rect2D(0, 0, trim.Size.WidthMm, trim.Size.HeightMm), grid ?? PanelGrid.Default, layout));
        return new ComicProject(null, UntitledTitle, trim, issue, [page], [], [], [], []);
    }

    public static ComicProject CreateNew(MetricPaperSize paper = MetricPaperSize.A4, PanelLayoutPreset? layout = null) =>
        CreateNew(new PageTrim(MetricPaperSizes.Size(paper), DefaultBleedMm), layout);

    /// <summary>Opens the project in <paramref name="folder"/>. Throws <see cref="InvalidDataException"/> if the folder isn't a Stanley project.</summary>
    public static ComicProject Open(string folder)
    {
        if (!ProjectRepository.IsInitialized(folder))
            throw new InvalidDataException($"'{folder}' isn't a Stanley project (it has no stanley.json).");

        var repository = new ProjectRepository(folder);
        var manifest = repository.LoadManifest();
        var issue = manifest.IssueIds.Count > 0 ? repository.LoadIssue(manifest.IssueIds[0]) : NewIssue();

        var pages = new List<ComicPage>();
        var records = new Dictionary<PageId, Page>();
        var saved = new Dictionary<PageId, HashSet<PanelId>>();
        foreach (var pageId in issue.PageIds)
        {
            var page = repository.LoadPage(issue.Id, pageId);
            var panels = page.PanelIds.ToDictionary(id => id, id => repository.LoadPanel(issue.Id, page.Id, id));
            pages.Add(new ComicPage(page.Id, page.TrimOverride ?? manifest.DefaultPageTrim, new PageDocument(page.PanelIds, panels, page.LayoutLocked)));
            records[page.Id] = page;
            saved[page.Id] = [.. page.PanelIds];
        }

        if (pages.Count == 0)
            pages.Add(NewPage(manifest.DefaultPageTrim));

        var characters = repository.ListCharacters();
        return new ComicProject(repository.RootDirectory, manifest.Title, manifest.DefaultPageTrim, issue, pages, records, saved,
            characters, [.. characters.Select(c => c.Id)]);
    }

    /// <summary>A blank page at the project's size: one panel filling the live area.</summary>
    public ComicPage CreateBlankPage(PanelGrid? grid = null) => NewPage(Trim, grid);

    /// <summary>
    /// Writes <paramref name="pages"/> (in this order) back to <see cref="Location"/>: the
    /// manifest title, the issue's page list, every page and panel - and deletes the
    /// folders/files of pages and panels removed since the last save. With
    /// <paramref name="characters"/>, writes every character too and deletes the ones
    /// removed since; without, leaves the characters on disk alone.
    /// </summary>
    public void Save(IReadOnlyList<(PageId Id, PageDocument Document)> pages, PageNumbering? pageNumbering = null,
        IReadOnlyList<CharacterDefinition>? characters = null)
    {
        if (Location is null)
            throw new InvalidOperationException("This comic hasn't been saved yet - use SaveAs.");

        var repository = ProjectRepository.IsInitialized(Location)
            ? new ProjectRepository(Location)
            : ProjectRepository.Initialize(Location, Title, Trim);

        (_issue, _pageRecords, _savedPanels) = WritePages(repository, pages, pageNumbering ?? PageNumbering, prune: true);
        if (characters != null)
            _savedCharacters = WriteCharacters(repository, Tidied(characters, pages), prune: true);
    }

    /// <summary>
    /// The characters as they're written: without library stickers that were tried on but
    /// aren't worn anywhere - not by the character, a named look or any panel - so trying
    /// things on leaves no files behind (docs/sticker-system.md §11).
    /// </summary>
    private static IReadOnlyList<CharacterDefinition> Tidied(IReadOnlyList<CharacterDefinition> characters, IReadOnlyList<(PageId Id, PageDocument Document)> pages)
    {
        var instances = pages.SelectMany(p => p.Document.Panels.Values.SelectMany(panel => panel.CharacterInstances)).ToList();
        return characters.Select(c => LookEditing.TidyWardrobe(c, LookEditing.WornElsewhere(c, instances))).ToList();
    }

    private HashSet<CharacterId> WriteCharacters(ProjectRepository repository, IReadOnlyList<CharacterDefinition> characters, bool prune)
    {
        foreach (var character in characters)
            repository.SaveCharacter(character);
        var saved = characters.Select(c => c.Id).ToHashSet();
        if (prune)
        {
            foreach (var removed in _savedCharacters.Except(saved))
                repository.DeleteCharacter(removed);
        }
        return saved;
    }

    /// <summary>
    /// Writes the comic's current pages as a self-contained project in
    /// <paramref name="folder"/> without making it the comic's home - the crash-recovery
    /// snapshot. Same issue and page ids as the real thing, so <see cref="OpenRecovered"/>
    /// can line it back up with the original folder.
    /// </summary>
    public void WriteCopy(string folder, IReadOnlyList<(PageId Id, PageDocument Document)> pages, PageNumbering? pageNumbering = null,
        IReadOnlyList<CharacterDefinition>? characters = null)
    {
        var repository = ProjectRepository.Initialize(folder, Title, Trim);
        WritePages(repository, pages, pageNumbering ?? PageNumbering, prune: false);
        WriteCharacters(repository, Tidied(characters ?? Characters, pages), prune: false);
    }

    /// <summary>
    /// Opens a snapshot written by <see cref="WriteCopy"/> as the comic it was taken from:
    /// the snapshot's pages, but at <paramref name="originalLocation"/> (so Save goes back
    /// where the work belongs, pruning against what's actually there) - or untitled if it
    /// had never been saved, or its folder is gone.
    /// </summary>
    public static ComicProject OpenRecovered(string snapshotFolder, string? originalLocation)
    {
        var copy = Open(snapshotFolder);
        if (originalLocation != null && ProjectRepository.IsInitialized(originalLocation))
        {
            var original = Open(originalLocation);
            return new ComicProject(original.Location, copy.Title, original.Trim, copy._issue, copy.Pages, original._pageRecords, original._savedPanels,
                copy.Characters, original._savedCharacters);
        }
        return new ComicProject(null, copy.Title, copy.Trim, copy._issue, copy.Pages, [], [], copy.Characters, []);
    }

    private (Issue Issue, Dictionary<PageId, Page> Records, Dictionary<PageId, HashSet<PanelId>> Saved) WritePages(
        ProjectRepository repository,
        IReadOnlyList<(PageId Id, PageDocument Document)> pages,
        PageNumbering pageNumbering,
        bool prune)
    {
        var manifest = repository.LoadManifest();
        var issueIds = manifest.IssueIds.Contains(_issue.Id) ? manifest.IssueIds : [.. manifest.IssueIds, _issue.Id];
        repository.SaveManifest(manifest with { Title = Title, IssueIds = issueIds });

        var issue = _issue with
        {
            PageIds = pages.Select(p => p.Id).ToList(),
            // "Off" is stored as absent, so turning numbers off leaves the file as it was before they existed.
            PageNumbering = pageNumbering is { Position: not PageNumberPosition.None } ? pageNumbering : null
        };
        repository.SaveIssue(issue);

        var records = new Dictionary<PageId, Page>();
        var saved = new Dictionary<PageId, HashSet<PanelId>>();
        for (var i = 0; i < pages.Count; i++)
        {
            var (id, document) = pages[i];
            var panelIds = document.PanelOrder.Where(document.Panels.ContainsKey).ToList();
            var record = (_pageRecords.TryGetValue(id, out var existing) ? existing : new Page(id, $"Page {i + 1}", TrimOverride: null, []))
                with { PanelIds = panelIds, LayoutLocked = document.LayoutLocked };
            repository.SavePage(issue.Id, record);
            foreach (var panelId in panelIds)
                repository.SavePanel(issue.Id, id, document.Panels[panelId]);
            if (prune && _savedPanels.TryGetValue(id, out var before))
            {
                foreach (var removed in before.Except(panelIds))
                    repository.DeletePanel(issue.Id, id, removed);
            }
            records[id] = record;
            saved[id] = [.. panelIds];
        }

        if (prune)
        {
            foreach (var removedPage in _savedPanels.Keys.Except(saved.Keys))
                repository.DeletePage(issue.Id, removedPage);
        }

        return (issue, records, saved);
    }

    /// <summary>
    /// Saves to a new location and makes it this comic's home, like Word's Save As. If
    /// <paramref name="folder"/> isn't empty the project goes into a new subfolder named
    /// after the title instead, so Save As never mixes files into (or overwrites) an
    /// existing folder. A comic that was already saved somewhere is copied over whole first
    /// (other issues, characters, art), since a project is a folder, not one file.
    /// Returns the folder actually saved to.
    /// </summary>
    public string SaveAs(string folder, IReadOnlyList<(PageId Id, PageDocument Document)> pages, PageNumbering? pageNumbering = null,
        IReadOnlyList<CharacterDefinition>? characters = null)
    {
        var target = ChooseTargetFolder(Path.GetFullPath(folder));
        if (Title == UntitledTitle)
            Title = Path.GetFileName(target);

        // The copy carries the pages as last saved, so _savedPanels still describes what's
        // on disk and Save prunes removed pages/panels in the new copy too. (An untitled
        // comic has nothing to copy, and nothing saved.)
        if (Location is { } source && Directory.Exists(source))
            CopyProject(source, target);

        Location = target;
        Save(pages, pageNumbering, characters);
        return target;
    }

    /// <summary>Every page, in order, as one PDF at trim size (bleed isn't drawn yet).</summary>
    public static void ExportPdf(string path, IEnumerable<(Rect2D Bounds, PageDocument Document, PageFolio? Folio)> pages,
        IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters = null)
    {
        using var stream = File.Create(path);
        PageRenderer.ExportPdf(stream, pages.Select(p => (p.Bounds, InOrder(p.Document), p.Folio)).ToList(), characters);
    }

    /// <summary>One page as a PNG.</summary>
    public static void ExportPng(string path, Rect2D bounds, PageDocument document, int dpi = 300, PageFolio? folio = null,
        IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters = null)
    {
        using var stream = File.Create(path);
        PageRenderer.ExportPng(stream, bounds, InOrder(document), dpi, folio, characters);
    }

    private static IEnumerable<PanelModel> InOrder(PageDocument document) =>
        document.PanelOrder.Where(document.Panels.ContainsKey).Select(id => document.Panels[id]).ToList();

    private static Issue NewIssue() => new(IssueId.New(), "1", "", [], new SortedDictionary<CharacterId, CharacterRevisionId>());

    private static ComicPage NewPage(PageTrim trim, PanelGrid? grid = null) =>
        new(PageId.New(), trim, BlankDocument(new Rect2D(0, 0, trim.Size.WidthMm, trim.Size.HeightMm), grid ?? PanelGrid.Default, null));

    private string ChooseTargetFolder(string folder)
    {
        if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
            return folder;

        var name = Title == UntitledTitle ? "Comic" : string.Concat(Title.Split(Path.GetInvalidFileNameChars())).Trim();
        if (name.Length == 0)
            name = "Comic";

        var candidate = Path.Combine(folder, name);
        for (var n = 2; Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any(); n++)
            candidate = Path.Combine(folder, $"{name} ({n})");
        return candidate;
    }

    private static void CopyProject(string source, string target)
    {
        if (string.Equals(Path.GetFullPath(source), target, StringComparison.Ordinal))
            return;

        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dir);
            if (!IsGitPath(relative))
                Directory.CreateDirectory(Path.Combine(target, relative));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (!IsGitPath(relative))
                File.Copy(file, Path.Combine(target, relative), overwrite: true);
        }
    }

    /// <summary>A copy is a new project, not a second checkout of the old one's history.</summary>
    private static bool IsGitPath(string relative) =>
        relative == ".git" || relative.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    internal static PageDocument BlankDocument(Rect2D pageBounds, PanelGrid grid, PanelLayoutPreset? layout)
    {
        var rects = layout is null
            ? [grid.LiveArea(pageBounds)]
            : PanelLayoutEditing.GridLayout(pageBounds, grid, layout.ColumnsPerRow) is { IsValid: true } result
                ? result.Value
                : [grid.LiveArea(pageBounds)];

        var panels = rects
            .Select(r => new PanelModel(PanelId.New(), PanelShapes.Rectangle(r), Background: null, CharacterInstances: [], Bubbles: []))
            .ToList();
        return new PageDocument(panels.Select(p => p.Id).ToList(), panels.ToDictionary(p => p.Id));
    }
}
