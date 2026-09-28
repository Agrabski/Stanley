using Stanley.Editing;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Storage;
using Stanley.Rendering;
using PanelModel = Stanley.ProjectModel.Issues.Panel;

namespace Stanley.Editors;

/// <summary>One page as the editor sees it: its id, its print size, and its content.</summary>
public sealed record ComicPage(PageId Id, PageTrim Trim, PageDocument Document)
{
    public Rect2D Bounds => new(0, 0, Trim.Size.WidthMm, Trim.Size.HeightMm);
}

/// <summary>One of the comic's issues, in the order <see cref="ComicProject.Issues"/> lists them - enough to show and open it, not its pages/panels.</summary>
public sealed record IssueSummary(IssueId Id, string Number, string Title, int PageCount);

/// <summary>
/// An open comic - the thing File &gt; New/Open/Save act on, the way Word acts on a
/// document. On disk it's a Stanley project folder (see <see cref="ProjectRepository"/>);
/// the editor works on the pages of one issue - the first unless told otherwise (created
/// on the fly for a project that has none yet, e.g. one fresh from <c>stanley init</c>) -
/// plus the project's characters and its title page, which every issue shares.
/// Everything else in the folder is left untouched by a save, and carried along by Save As.
/// </summary>
public sealed class ComicProject
{
    public const string UntitledTitle = "Untitled comic";
    public const double DefaultBleedMm = 3;

    private Issue _issue;
    // The comic's issues in storage order, and every one besides the current issue - so
    // Issues can be rebuilt with the current issue's live Number/Title/page count on every
    // access without re-reading the others from disk.
    private readonly List<IssueId> _issueOrder;
    private readonly Dictionary<IssueId, IssueSummary> _otherIssues;
    // The template the comic was made from, if any: new pages' panels and the export width.
    private readonly ComicFormat? _format;
    // What's on disk (at Location) as of the last open/save: page records (kept so a
    // page's label and trim override survive a save) and each page's panel ids, so a
    // save can delete the files of pages and panels removed since.
    private Dictionary<PageId, Page> _pageRecords;
    private Dictionary<PageId, HashSet<PanelId>> _savedPanels;
    // Characters on disk as of the last open/save, so a save can delete removed ones.
    private HashSet<CharacterId> _savedCharacters;
    // Pictures in the issue's art folder the pages used as of the last open/save, so a save
    // can delete the ones no page uses any more (and never touches other files there).
    private HashSet<string> _savedPictures;
    // The comic's title page on disk as of the last open/save, so a save can prune it.
    private SavedTitlePage? _savedTitlePage;

    /// <summary>The comic's title page as last written: its record (the label survives), panels and the pictures in its art folder.</summary>
    private sealed record SavedTitlePage(Page Record, HashSet<PanelId> Panels, HashSet<string> Pictures);

    private ComicProject(string? location, string title, PageTrim trim, ComicFormat? format, Issue issue, IReadOnlyList<ComicPage> pages,
        Dictionary<PageId, Page> pageRecords, Dictionary<PageId, HashSet<PanelId>> savedPanels,
        IReadOnlyList<CharacterDefinition> characters, HashSet<CharacterId> savedCharacters,
        IReadOnlyDictionary<string, ArtFile> pictures, HashSet<string> savedPictures,
        ComicPage? titlePage = null, SavedTitlePage? savedTitlePage = null,
        IReadOnlyList<IssueId>? issueOrder = null, IReadOnlyDictionary<IssueId, IssueSummary>? otherIssues = null,
        IReadOnlyList<ObjectGroup>? objectGroups = null)
    {
        TitlePage = titlePage;
        ObjectGroups = objectGroups ?? [];
        _savedTitlePage = savedTitlePage;
        _format = format;
        Grid = GridOf(format);
        Characters = characters;
        _savedCharacters = savedCharacters;
        Pictures = pictures;
        _savedPictures = savedPictures;
        Location = location;
        Title = title;
        Trim = trim;
        _issue = issue;
        // Without an explicit order (a brand-new or never-saved comic), the current issue is the only one.
        _issueOrder = issueOrder is null ? [issue.Id] : [.. issueOrder];
        _otherIssues = otherIssues is null ? [] : new Dictionary<IssueId, IssueSummary>(otherIssues);
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

    /// <summary>
    /// What the comic was set up as (<see cref="SeriesManifest.Format"/>): a strip or
    /// webcomic template's, with the comic's current <see cref="Grid"/>; for a comic book,
    /// just the spacing - or null while that's the default, so its file stays as it was.
    /// </summary>
    public ComicFormat? Format =>
        _format is { } format && (format.PanelsPerRow != null || format.ExportWidthPx != null) ? format with { MarginMm = Grid.MarginMm, GutterMm = Grid.GutterMm }
        : Grid == PanelGrid.Default ? null
        : new ComicFormat(Grid.MarginMm, Grid.GutterMm);

    /// <summary>The margin and gutter every page is laid out and snapped with (Layout tab), saved with the comic. Kept in step with the page navigator's by whoever runs the session.</summary>
    public PanelGrid Grid { get; set; }

    /// <summary>The panels a new page starts with (a strip's row of four); null for one panel filling the page.</summary>
    public PanelLayoutPreset? NewPageLayout => LayoutOf(_format);

    /// <summary>For a comic read on screen, the width in pixels a page is exported at as a PNG; null for print resolution.</summary>
    public int? ExportWidthPx => _format?.ExportWidthPx;

    /// <summary>The open issue's id (<c>issues/&lt;id&gt;-slug/</c>); which of <see cref="Issues"/> is current.</summary>
    public IssueId IssueId => _issue.Id;

    /// <summary>The issue's number as displayed ("1", "0", "1.5") - free text, editable from File › Info, shown wherever a text has <c>{issue}</c>.</summary>
    public string IssueNumber
    {
        get => _issue.Number;
        set => _issue = _issue with { Number = value };
    }

    /// <summary>The issue's title, editable from File › Info alongside its number - e.g. "Annual" or "The Long Way Home". Saved with the issue, separately from the comic's own <see cref="Title"/>.</summary>
    public string IssueTitle
    {
        get => _issue.Title;
        set => _issue = _issue with { Title = value };
    }

    /// <summary>What the fields in the comic's texts show: its title and issue number.</summary>
    public TextFields Fields => new(Title, IssueNumber);

    /// <summary>
    /// The comic's issues in storage order (<see cref="SeriesManifest.IssueIds"/>) - for a
    /// comic that's never been saved, just the one issue being edited. The current issue's
    /// entry reflects live, unsaved edits to its number/title and its opened page count; the
    /// others are as of the last time this project was opened or a new issue was added.
    /// </summary>
    public IReadOnlyList<IssueSummary> Issues =>
        _issueOrder.Select(id => id == _issue.Id
            ? new IssueSummary(id, _issue.Number, _issue.Title, Pages.Count)
            : _otherIssues.TryGetValue(id, out var summary) ? summary : new IssueSummary(id, "", "", 0))
        .ToList();

    public Rect2D PageBounds => new(0, 0, Trim.Size.WidthMm, Trim.Size.HeightMm);

    /// <summary>The issue's pages as they were opened, in reading order - the editor's starting point. The comic's title page isn't among them (<see cref="TitlePage"/>); an issue's own title page is.</summary>
    public IReadOnlyList<ComicPage> Pages { get; }

    /// <summary>The comic's title page as opened (<see cref="TitlePageScope.Comic"/>), which every issue without one of its own opens with - or null if it has none.</summary>
    public ComicPage? TitlePage { get; }

    /// <summary>The project's characters as they were opened, sorted by name.</summary>
    public IReadOnlyList<CharacterDefinition> Characters { get; }

    /// <summary>The comic's own copies of the object groups it kept in or took from My Assets (<c>objects/</c>, docs/asset-packs.md §7.1), as opened.</summary>
    public IReadOnlyList<ObjectGroup> ObjectGroups { get; }

    /// <summary>The pictures the pages use (background pictures, picture elements) as they were opened, by art file name. One a page names but that isn't on disk is missing here and draws as a placeholder.</summary>
    public IReadOnlyDictionary<string, ArtFile> Pictures { get; }

    /// <summary>The issue's printed page numbers, as opened.</summary>
    public PageNumbering PageNumbering => _issue.PageNumbering ?? PageNumbering.Off;

    /// <summary>The issue's look per character (<see cref="Issue.CharacterRevisions"/>): what its panels show a character in unless a panel picks its own.</summary>
    public IReadOnlyDictionary<CharacterId, CharacterRevisionId> IssueLooks => _issue.CharacterRevisions;

    /// <summary>A brand-new, unsaved comic with one page: either one panel filling the live area or tiled with <paramref name="layout"/>.</summary>
    public static ComicProject CreateNew(PageTrim trim, PanelLayoutPreset? layout = null, PanelGrid? grid = null) =>
        CreateNew(trim, layout, grid, format: null);

    public static ComicProject CreateNew(MetricPaperSize paper = MetricPaperSize.A4, PanelLayoutPreset? layout = null) =>
        CreateNew(new PageTrim(MetricPaperSizes.Size(paper), DefaultBleedMm), layout);

    /// <summary>A brand-new, unsaved comic from a strip or webcomic template: its page size and first page, and its format - spacing, new pages' panels, export width - kept with the comic.</summary>
    public static ComicProject CreateNew(ComicTemplate template) =>
        CreateNew(template.Trim, template.Layout, template.Grid, template.Format);

    private static ComicProject CreateNew(PageTrim trim, PanelLayoutPreset? layout, PanelGrid? grid, ComicFormat? format)
    {
        var issue = BlankIssue();
        var page = new ComicPage(PageId.New(), trim, BlankDocument(new Rect2D(0, 0, trim.Size.WidthMm, trim.Size.HeightMm), grid ?? PanelGrid.Default, layout));
        return new ComicProject(null, UntitledTitle, trim, format, issue, [page], [], [], [], [], NoPictures, []);
    }

    /// <summary>Opens the project in <paramref name="folder"/> at <paramref name="issueId"/> - its first issue if null. Throws <see cref="InvalidDataException"/> if the folder isn't a Stanley project.</summary>
    public static ComicProject Open(string folder, IssueId? issueId = null)
    {
        if (!ProjectRepository.IsInitialized(folder))
            throw new InvalidDataException($"'{folder}' isn't a Stanley project (it has no stanley.json).");

        var repository = new ProjectRepository(folder);
        var manifest = repository.LoadManifest();
        var issue = issueId is { } id ? repository.LoadIssue(id)
            : manifest.IssueIds.Count > 0 ? repository.LoadIssue(manifest.IssueIds[0])
            : BlankIssue();

        var pages = new List<ComicPage>();
        var records = new Dictionary<PageId, Page>();
        var saved = new Dictionary<PageId, HashSet<PanelId>>();
        foreach (var pageId in issue.PageIds)
        {
            var page = repository.LoadPage(issue.Id, pageId);
            var panels = page.PanelIds.ToDictionary(id => id, id => repository.LoadPanel(issue.Id, page.Id, id));
            pages.Add(new ComicPage(page.Id, page.TrimOverride ?? manifest.DefaultPageTrim,
                new PageDocument(page.PanelIds, panels, page.LayoutLocked, page.TitlePage ? TitlePageScope.Issue : TitlePageScope.None)));
            records[page.Id] = page;
            saved[page.Id] = [.. page.PanelIds];
        }

        var characters = repository.ListCharacters();
        var pictures = new Dictionary<string, ArtFile>(StringComparer.Ordinal);
        if (manifest.IssueIds.Contains(issue.Id))
        {
            foreach (var name in pages.SelectMany(p => p.Document.Panels.Values).SelectMany(PanelElements.ArtFileNames).Distinct())
            {
                if (repository.LoadIssueArt(issue.Id, name) is { } file)
                    pictures[name] = file;
            }
        }
        var issuePictures = pictures.Keys.ToHashSet(StringComparer.Ordinal);

        ComicPage? titlePage = null;
        SavedTitlePage? savedTitlePage = null;
        if (repository.LoadTitlePage() is { } titleRecord)
        {
            var panels = titleRecord.PanelIds.ToDictionary(id => id, repository.LoadTitlePagePanel);
            titlePage = new ComicPage(titleRecord.Id, titleRecord.TrimOverride ?? manifest.DefaultPageTrim,
                new PageDocument(titleRecord.PanelIds, panels, titleRecord.LayoutLocked, TitlePageScope.Comic));
            var titlePictures = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in panels.Values.SelectMany(PanelElements.ArtFileNames).Distinct())
            {
                if (repository.LoadTitlePageArt(name) is { } file)
                {
                    pictures.TryAdd(name, file);
                    titlePictures.Add(name);
                }
            }
            savedTitlePage = new SavedTitlePage(titleRecord, [.. titleRecord.PanelIds], titlePictures);
        }

        // An issue with no pages of its own still needs one to open on - unless the comic's
        // title page will stand in for it (an issue can be just its title page - #65).
        if (pages.Count == 0 && titlePage is null)
            pages.Add(NewPage(manifest.DefaultPageTrim, manifest.Format));

        // Every other issue's summary, for Issues - not this one's, so its live edits always win.
        var otherIssues = manifest.IssueIds.Where(id => id != issue.Id).ToDictionary(id => id, id =>
        {
            var other = repository.LoadIssue(id);
            return new IssueSummary(id, other.Number, other.Title, other.PageIds.Count);
        });
        var issueOrder = manifest.IssueIds.Count > 0 ? manifest.IssueIds : (IReadOnlyList<IssueId>)[issue.Id];

        return new ComicProject(repository.RootDirectory, manifest.Title, manifest.DefaultPageTrim, manifest.Format, issue, pages, records, saved,
            characters, [.. characters.Select(c => c.Id)], pictures, issuePictures, titlePage, savedTitlePage, issueOrder, otherIssues,
            repository.ListObjectGroups());
    }

    /// <summary>A new page at the project's size: one panel filling the live area, or the format's panels.</summary>
    public ComicPage CreateBlankPage() => NewPage(Trim, _format);

    /// <summary>
    /// Adds a new issue to this (already-saved) comic: one blank page laid out like
    /// <see cref="CreateBlankPage"/>, its own folder under <c>issues/</c>, appended to the
    /// manifest's issue order and numbered <paramref name="number"/> - or, left null, one
    /// past the highest numeric issue number so far (<see cref="IssueNumbering.NextNumber"/>).
    /// Never touches another issue's files. Returns the new issue's id, to switch to with
    /// <see cref="Open"/>; this project keeps editing the issue it already had open.
    /// </summary>
    public IssueId NewIssue(string? number = null, string? title = null)
    {
        if (Location is null)
            throw new InvalidOperationException("This comic hasn't been saved yet - save it first.");

        var repository = new ProjectRepository(Location);
        var manifest = repository.LoadManifest();
        var existingNumbers = manifest.IssueIds.Select(id => repository.LoadIssue(id).Number).ToList();

        var page = CreateBlankPage();
        var issue = new Issue(IssueId.New(), number ?? IssueNumbering.NextNumber(existingNumbers), title ?? "", [page.Id],
            new SortedDictionary<CharacterId, CharacterRevisionId>());
        repository.SaveIssue(issue);
        repository.SavePage(issue.Id, new Page(page.Id, "Page 1", TrimOverride: null, page.Document.PanelOrder));
        foreach (var panelId in page.Document.PanelOrder)
            repository.SavePanel(issue.Id, page.Id, page.Document.Panels[panelId]);

        repository.SaveManifest(manifest with { IssueIds = [.. manifest.IssueIds, issue.Id] });
        return issue.Id;
    }

    /// <summary>
    /// Writes <paramref name="pages"/> (in this order) back to <see cref="Location"/>: the
    /// manifest title, the issue's page list, every page and panel - and deletes the
    /// folders/files of pages and panels removed since the last save. A page that's the
    /// comic's title page (<see cref="TitlePageScope.Comic"/>) goes to the project's
    /// <c>title-page/</c> folder instead of the issue; with none among the pages, the comic's
    /// title page is deleted. With <paramref name="characters"/>, writes every character too
    /// and deletes the ones removed since; without, leaves the characters on disk alone.
    /// </summary>
    /// <param name="issueLooks">The issue's look per character; null leaves the issue's as it was.</param>
    /// <param name="pictures">The comic's pictures by art file name: the ones the pages use are written to the issue's art folder, and ones a page used at the last save but none uses now are deleted. Null leaves the art folder alone.</param>
    /// <param name="objectGroups">The comic's copies of kept object groups, written to <c>objects/</c> (a file whose content is unchanged stays as it is). None is ever deleted - another issue may use it. Null leaves <c>objects/</c> alone.</param>
    public void Save(IReadOnlyList<(PageId Id, PageDocument Document)> pages, PageNumbering? pageNumbering = null,
        IReadOnlyList<CharacterDefinition>? characters = null, IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null,
        IReadOnlyDictionary<string, ArtFile>? pictures = null, IReadOnlyCollection<ObjectGroup>? objectGroups = null)
    {
        if (Location is null)
            throw new InvalidOperationException("This comic hasn't been saved yet - use SaveAs.");

        var repository = ProjectRepository.IsInitialized(Location)
            ? new ProjectRepository(Location)
            : ProjectRepository.Initialize(Location, Title, Trim, Format);

        var (issuePages, titlePage) = Split(pages);
        (_issue, _pageRecords, _savedPanels) = WritePages(repository, issuePages, pageNumbering ?? PageNumbering, issueLooks, prune: true);
        _savedTitlePage = WriteTitlePage(repository, titlePage, pictures, prune: true);
        if (pictures != null)
            _savedPictures = WritePictures(repository, issuePages, pictures, prune: true);
        if (characters != null)
            _savedCharacters = WriteCharacters(repository, Tidied(characters, pages), prune: true);
        WriteObjectGroups(repository, objectGroups);
    }

    private static void WriteObjectGroups(ProjectRepository repository, IReadOnlyCollection<ObjectGroup>? groups)
    {
        foreach (var group in groups ?? [])
            repository.SaveObjectGroup(group);
    }

    /// <summary>The issue's own pages, and the comic's title page if it's among them.</summary>
    private static (List<(PageId Id, PageDocument Document)> Issue, (PageId Id, PageDocument Document)? Title) Split(IReadOnlyList<(PageId Id, PageDocument Document)> pages) =>
        (pages.Where(p => p.Document.TitlePage != TitlePageScope.Comic).ToList(),
         pages.Where(p => p.Document.TitlePage == TitlePageScope.Comic).Cast<(PageId, PageDocument)?>().FirstOrDefault());

    /// <summary>
    /// Writes the comic's title page into the project's <c>title-page/</c> folder - its
    /// panels, and with <paramref name="pictures"/> the pictures it uses - and, with
    /// <paramref name="prune"/>, deletes what it no longer has; with no title page, the whole
    /// folder goes. Returns what's on disk now.
    /// </summary>
    private SavedTitlePage? WriteTitlePage(ProjectRepository repository, (PageId Id, PageDocument Document)? page,
        IReadOnlyDictionary<string, ArtFile>? pictures, bool prune)
    {
        if (page is not { } titlePage)
        {
            if (prune && _savedTitlePage != null)
                repository.DeleteTitlePage();
            return null;
        }

        var (id, document) = titlePage;
        var previous = _savedTitlePage is { } saved && saved.Record.Id == id ? saved : null;
        if (prune && _savedTitlePage != null && previous is null)
            repository.DeleteTitlePage(); // a different page took its place

        var panelIds = document.PanelOrder.Where(document.Panels.ContainsKey).ToList();
        var record = (previous?.Record ?? new Page(id, "Title page", TrimOverride: null, []))
            with
        { PanelIds = panelIds, LayoutLocked = document.LayoutLocked, TitlePage = true };
        repository.SaveTitlePage(record);
        foreach (var panelId in panelIds)
            repository.SaveTitlePagePanel(document.Panels[panelId]);
        if (prune && previous != null)
        {
            foreach (var removed in previous.Panels.Except(panelIds))
                repository.DeleteTitlePagePanel(removed);
        }

        var used = document.Panels.Values.SelectMany(PanelElements.ArtFileNames).Where(IssueArt.IsValidName).ToHashSet(StringComparer.Ordinal);
        if (pictures != null)
        {
            foreach (var name in used)
            {
                if (pictures.TryGetValue(name, out var file))
                    repository.SaveTitlePageArt(name, file);
            }
            if (prune && previous != null)
            {
                foreach (var removed in previous.Pictures.Except(used))
                    repository.DeleteTitlePageArt(removed);
            }
        }
        return new SavedTitlePage(record, [.. panelIds], used);
    }

    /// <summary>
    /// The characters as they're written: without library stickers that were tried on but
    /// aren't worn anywhere - not by the character, a named look or any panel - and without
    /// library pattern tiles no fabric uses, so trying things on leaves no files behind
    /// (docs/sticker-system.md §11).
    /// </summary>
    private static IReadOnlyList<CharacterDefinition> Tidied(IReadOnlyList<CharacterDefinition> characters, IReadOnlyList<(PageId Id, PageDocument Document)> pages)
    {
        var instances = pages.SelectMany(p => p.Document.Panels.Values.SelectMany(panel => panel.CharacterInstances)).ToList();
        return characters.Select(c =>
        {
            var tidied = LookEditing.TidyWardrobe(c, LookEditing.WornElsewhere(c, instances));
            return LookEditing.TidyTiles(tidied, LookEditing.TilesInUse(tidied, instances), IsLibraryTile);
        }).ToList();
    }

    private static bool IsLibraryTile(string name, ArtFile file) =>
        Stanley.StickerLibrary.StickerLibrary.PatternTiles.TryGetValue(name, out var shipped) && shipped.SameContent(file);

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
        IReadOnlyList<CharacterDefinition>? characters = null, IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null,
        IReadOnlyDictionary<string, ArtFile>? pictures = null, IReadOnlyCollection<ObjectGroup>? objectGroups = null)
    {
        var repository = ProjectRepository.Initialize(folder, Title, Trim, Format);
        var (issuePages, titlePage) = Split(pages);
        WritePages(repository, issuePages, pageNumbering ?? PageNumbering, issueLooks, prune: false);
        WriteTitlePage(repository, titlePage, pictures ?? Pictures, prune: false);
        WritePictures(repository, issuePages, pictures ?? Pictures, prune: false);
        WriteCharacters(repository, Tidied(characters ?? Characters, pages), prune: false);
        WriteObjectGroups(repository, objectGroups ?? ObjectGroups);
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
            // The same issue the snapshot is of, if it's there already (a comic's first issue is written on its first save).
            var issueId = new ProjectRepository(originalLocation).LoadManifest().IssueIds.Contains(copy._issue.Id) ? copy._issue.Id : (IssueId?)null;
            var original = Open(originalLocation, issueId);
            // If the snapshot's issue isn't on disk yet, it still belongs in the list - tacked on the end.
            List<IssueId> issueOrder = issueId != null ? original._issueOrder : [.. original._issueOrder, copy._issue.Id];
            return new ComicProject(original.Location, copy.Title, original.Trim, copy._format, copy._issue, copy.Pages, original._pageRecords, original._savedPanels,
                copy.Characters, original._savedCharacters, copy.Pictures, original._savedPictures, copy.TitlePage, original._savedTitlePage,
                issueOrder, original._otherIssues, copy.ObjectGroups);
        }
        return new ComicProject(null, copy.Title, copy.Trim, copy._format, copy._issue, copy.Pages, [], [], copy.Characters, [], copy.Pictures, [], copy.TitlePage,
            objectGroups: copy.ObjectGroups);
    }

    private (Issue Issue, Dictionary<PageId, Page> Records, Dictionary<PageId, HashSet<PanelId>> Saved) WritePages(
        ProjectRepository repository,
        IReadOnlyList<(PageId Id, PageDocument Document)> pages,
        PageNumbering pageNumbering,
        IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks,
        bool prune)
    {
        var manifest = repository.LoadManifest();
        var issueIds = manifest.IssueIds.Contains(_issue.Id) ? manifest.IssueIds : [.. manifest.IssueIds, _issue.Id];
        repository.SaveManifest(manifest with { Title = Title, IssueIds = issueIds, Format = Format });

        var issue = _issue with
        {
            PageIds = pages.Select(p => p.Id).ToList(),
            // "Off" is stored as absent, so turning numbers off leaves the file as it was before they existed.
            PageNumbering = pageNumbering is { Position: not PageNumberPosition.None } ? pageNumbering : null,
            CharacterRevisions = issueLooks is null ? _issue.CharacterRevisions : new SortedDictionary<CharacterId, CharacterRevisionId>(issueLooks.ToDictionary())
        };
        repository.SaveIssue(issue);

        var records = new Dictionary<PageId, Page>();
        var saved = new Dictionary<PageId, HashSet<PanelId>>();
        for (var i = 0; i < pages.Count; i++)
        {
            var (id, document) = pages[i];
            var panelIds = document.PanelOrder.Where(document.Panels.ContainsKey).ToList();
            var record = (_pageRecords.TryGetValue(id, out var existing) ? existing : new Page(id, document.IsTitlePage ? "Title page" : $"Page {i + 1}", TrimOverride: null, []))
                with
            { PanelIds = panelIds, LayoutLocked = document.LayoutLocked, TitlePage = document.IsTitlePage };
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
    /// Writes the pictures <paramref name="pages"/> use into the issue's art folder (a file
    /// already holding the same content is left alone) and, with <paramref name="prune"/>,
    /// deletes the ones used at the last save that no page uses now. Returns the names in use.
    /// </summary>
    private HashSet<string> WritePictures(ProjectRepository repository, IReadOnlyList<(PageId Id, PageDocument Document)> pages,
        IReadOnlyDictionary<string, ArtFile> pictures, bool prune)
    {
        var used = pages.SelectMany(p => p.Document.Panels.Values).SelectMany(PanelElements.ArtFileNames)
            .Where(IssueArt.IsValidName).ToHashSet(StringComparer.Ordinal);
        foreach (var name in used)
        {
            if (pictures.TryGetValue(name, out var file))
                repository.SaveIssueArt(_issue.Id, name, file);
        }
        if (prune)
        {
            foreach (var removed in _savedPictures.Except(used))
                repository.DeleteIssueArt(_issue.Id, removed);
        }
        return used;
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
        IReadOnlyList<CharacterDefinition>? characters = null, IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null,
        IReadOnlyDictionary<string, ArtFile>? pictures = null, IReadOnlyCollection<ObjectGroup>? objectGroups = null)
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
        Save(pages, pageNumbering, characters, issueLooks, pictures, objectGroups);
        return target;
    }

    /// <summary>Every page, in order, as one PDF at trim size (bleed isn't drawn yet).</summary>
    public static void ExportPdf(string path, IEnumerable<(Rect2D Bounds, PageDocument Document, PageFolio? Folio)> pages,
        IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters = null, IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null,
        IReadOnlyDictionary<string, ArtFile>? pictures = null, TextFields? fields = null)
    {
        using var stream = File.Create(path);
        PageRenderer.ExportPdf(stream, pages.Select(p => (p.Bounds, InOrder(p.Document), p.Folio)).ToList(), characters, issueLooks, pictures, fields);
    }

    /// <summary>One page as a PNG: at <paramref name="dpi"/>, or exactly <paramref name="widthPx"/> pixels wide when given (a webcomic's <see cref="ExportWidthPx"/>).</summary>
    public static void ExportPng(string path, Rect2D bounds, PageDocument document, int dpi = 300, PageFolio? folio = null,
        IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters = null, IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null,
        IReadOnlyDictionary<string, ArtFile>? pictures = null, int? widthPx = null, TextFields? fields = null)
    {
        using var stream = File.Create(path);
        if (widthPx is { } width)
            PageRenderer.ExportPngAtWidth(stream, bounds, InOrder(document), width, folio, characters, issueLooks, pictures, fields);
        else
            PageRenderer.ExportPng(stream, bounds, InOrder(document), dpi, folio, characters, issueLooks, pictures, fields);
    }

    private static IEnumerable<PanelModel> InOrder(PageDocument document) =>
        document.PanelOrder.Where(document.Panels.ContainsKey).Select(id => document.Panels[id]).ToList();

    private static readonly IReadOnlyDictionary<string, ArtFile> NoPictures = new Dictionary<string, ArtFile>();

    private static Issue BlankIssue() => new(IssueId.New(), "1", "", [], new SortedDictionary<CharacterId, CharacterRevisionId>());

    private static ComicPage NewPage(PageTrim trim, ComicFormat? format) =>
        new(PageId.New(), trim, BlankDocument(new Rect2D(0, 0, trim.Size.WidthMm, trim.Size.HeightMm), GridOf(format), LayoutOf(format)));

    private static PanelGrid GridOf(ComicFormat? format) => format is null ? PanelGrid.Default : new PanelGrid(format.MarginMm, format.GutterMm);

    private static PanelLayoutPreset? LayoutOf(ComicFormat? format) =>
        format?.PanelsPerRow is { Count: > 0 } rows ? new PanelLayoutPreset("New page", rows) : null;

    /// <summary>
    /// <paramref name="path"/> if nothing is there yet (or only an empty folder), else the
    /// first free "name (2)", "name (3)"... beside it: where a comic saved under a name that's
    /// already taken goes, so saving never mixes into or replaces what's there.
    /// </summary>
    public static string FreeFolder(string path)
    {
        static bool IsFree(string candidate) =>
            !File.Exists(candidate) && (!Directory.Exists(candidate) || !Directory.EnumerateFileSystemEntries(candidate).Any());

        if (IsFree(path))
            return path;
        for (var n = 2; ; n++)
        {
            var candidate = $"{path} ({n})";
            if (IsFree(candidate))
                return candidate;
        }
    }

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
