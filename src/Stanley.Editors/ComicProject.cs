using Stanley.Editing;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Storage;
using Stanley.Rendering;
using PanelModel = Stanley.ProjectModel.Issues.Panel;

namespace Stanley.Editors;

/// <summary>
/// An open comic - the thing File &gt; New/Open/Save act on, the way Word acts on a
/// document. On disk it's a Stanley project folder (see <see cref="ProjectRepository"/>);
/// the page editor edits one page of it (for now, the first page of the first issue -
/// created on the fly for a project that has none yet, e.g. one fresh from
/// <c>stanley init</c>). Everything else in the folder is left untouched by a save, and
/// carried along by Save As.
/// </summary>
public sealed class ComicProject
{
    public const string UntitledTitle = "Untitled comic";
    public const double DefaultBleedMm = 3;

    private Issue _issue;
    private Page _page;
    private HashSet<PanelId> _savedPanelIds;

    private ComicProject(string? location, string title, PageTrim trim, Issue issue, Page page, PageDocument document, IEnumerable<PanelId> savedPanelIds)
    {
        Location = location;
        Title = title;
        Trim = trim;
        _issue = issue;
        _page = page;
        Document = document;
        _savedPanelIds = [.. savedPanelIds];
    }

    /// <summary>The project folder, or null for a comic that has never been saved (Save then behaves as Save As).</summary>
    public string? Location { get; private set; }

    public bool IsUntitled => Location is null;

    /// <summary>The series title (<c>stanley.json</c>'s title); shown in the window title and editable from File &gt; Info.</summary>
    public string Title { get; set; }

    public PageTrim Trim { get; }

    /// <summary>The page as it was opened - the editor's starting document.</summary>
    public PageDocument Document { get; }

    public Rect2D PageBounds => new(0, 0, Trim.Size.WidthMm, Trim.Size.HeightMm);

    /// <summary>A brand-new, unsaved comic: one page, either one panel filling the live area or tiled with <paramref name="layout"/>.</summary>
    public static ComicProject CreateNew(PageTrim trim, PanelLayoutPreset? layout = null, PanelGrid? grid = null)
    {
        var issue = new Issue(IssueId.New(), "1", "", [], new SortedDictionary<CharacterId, CharacterRevisionId>());
        var page = new Page(PageId.New(), "Page 1", TrimOverride: null, []);
        var bounds = new Rect2D(0, 0, trim.Size.WidthMm, trim.Size.HeightMm);
        return new ComicProject(null, UntitledTitle, trim, issue, page, BlankDocument(bounds, grid ?? PanelGrid.Default, layout), []);
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

        var issue = manifest.IssueIds.Count > 0
            ? repository.LoadIssue(manifest.IssueIds[0])
            : new Issue(IssueId.New(), "1", "", [], new SortedDictionary<CharacterId, CharacterRevisionId>());

        if (issue.PageIds.Count == 0)
        {
            var newPage = new Page(PageId.New(), "Page 1", TrimOverride: null, []);
            var bounds = new Rect2D(0, 0, manifest.DefaultPageTrim.Size.WidthMm, manifest.DefaultPageTrim.Size.HeightMm);
            return new ComicProject(repository.RootDirectory, manifest.Title, manifest.DefaultPageTrim, issue, newPage,
                BlankDocument(bounds, PanelGrid.Default, null), []);
        }

        var page = repository.LoadPage(issue.Id, issue.PageIds[0]);
        var panels = page.PanelIds.ToDictionary(id => id, id => repository.LoadPanel(issue.Id, page.Id, id));
        return new ComicProject(repository.RootDirectory, manifest.Title, page.TrimOverride ?? manifest.DefaultPageTrim,
            issue, page, new PageDocument(page.PanelIds, panels), page.PanelIds);
    }

    /// <summary>Writes the edited page (and the manifest/issue entries pointing at it) back to <see cref="Location"/>.</summary>
    public void Save(PageDocument document)
    {
        if (Location is null)
            throw new InvalidOperationException("This comic hasn't been saved yet - use SaveAs.");

        var repository = ProjectRepository.IsInitialized(Location)
            ? new ProjectRepository(Location)
            : ProjectRepository.Initialize(Location, Title, Trim);

        var manifest = repository.LoadManifest();
        var issueIds = manifest.IssueIds.Contains(_issue.Id) ? manifest.IssueIds : [.. manifest.IssueIds, _issue.Id];
        repository.SaveManifest(manifest with { Title = Title, IssueIds = issueIds });

        if (!_issue.PageIds.Contains(_page.Id))
            _issue = _issue with { PageIds = [.. _issue.PageIds, _page.Id] };
        repository.SaveIssue(_issue);

        _page = _page with { PanelIds = document.PanelOrder.Where(document.Panels.ContainsKey).ToList() };
        repository.SavePage(_issue.Id, _page);
        foreach (var id in _page.PanelIds)
            repository.SavePanel(_issue.Id, _page.Id, document.Panels[id]);

        foreach (var removed in _savedPanelIds.Except(_page.PanelIds))
            repository.DeletePanel(_issue.Id, _page.Id, removed);
        _savedPanelIds = [.. _page.PanelIds];
    }

    /// <summary>
    /// Saves to a new location and makes it this comic's home, like Word's Save As. If
    /// <paramref name="folder"/> isn't empty the project goes into a new subfolder named
    /// after the title instead, so Save As never mixes files into (or overwrites) an
    /// existing folder. A comic that was already saved somewhere is copied over whole first
    /// (other issues, characters, art), since a project is a folder, not one file.
    /// Returns the folder actually saved to.
    /// </summary>
    public string SaveAs(string folder, PageDocument document)
    {
        var target = ChooseTargetFolder(Path.GetFullPath(folder));
        if (Title == UntitledTitle)
            Title = Path.GetFileName(target);

        // The copy carries this page's panel files as last saved, so _savedPanelIds still
        // describes what's on disk and Save prunes deleted panels in the new copy too.
        // (An untitled comic has nothing to copy, and no saved panels.)
        if (Location is { } source && Directory.Exists(source))
            CopyProject(source, target);

        Location = target;
        Save(document);
        return target;
    }

    /// <summary>Exports the page at its trim size (bleed isn't drawn yet).</summary>
    public void ExportPdf(string path, PageDocument document)
    {
        using var stream = File.Create(path);
        PageRenderer.ExportPdf(stream, PageBounds, InOrder(document));
    }

    public void ExportPng(string path, PageDocument document, int dpi = 300)
    {
        using var stream = File.Create(path);
        PageRenderer.ExportPng(stream, PageBounds, InOrder(document), dpi);
    }

    private static IEnumerable<PanelModel> InOrder(PageDocument document) =>
        document.PanelOrder.Where(document.Panels.ContainsKey).Select(id => document.Panels[id]).ToList();

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

        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dir);
            if (IsGitPath(relative))
                continue;
            Directory.CreateDirectory(Path.Combine(target, relative));
        }
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (IsGitPath(relative))
                continue;
            File.Copy(file, Path.Combine(target, relative), overwrite: true);
        }
    }

    /// <summary>A copy is a new project, not a second checkout of the old one's history.</summary>
    private static bool IsGitPath(string relative) =>
        relative == ".git" || relative.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static PageDocument BlankDocument(Rect2D pageBounds, PanelGrid grid, PanelLayoutPreset? layout)
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
