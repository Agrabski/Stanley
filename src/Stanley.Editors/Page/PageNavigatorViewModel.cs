using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>One entry in the page navigator: a page's id, its editor (which holds its content and undo), and its 1-based position.</summary>
public sealed class PageItem : ObservableObject
{

    public PageItem(PageId id, PageEditorViewModel editor)
    {
        Id = id;
        Editor = editor;
    }

    public PageId Id { get; }

    public PageEditorViewModel Editor { get; }

    public int Number
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Editor.Title = $"Page {value}";
                OnPropertyChanged(nameof(Caption));
            }
        }
    }

    /// <summary>What the navigator shows under the thumbnail: the page's number, or "Title" for the title page.</summary>
    public string Caption => Editor.Committed.IsTitlePage ? "Title" : Number.ToString(System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>
/// The page navigator side pane: the comic's pages in reading order, which one is being
/// edited, and the page-level operations (add, duplicate, delete, reorder). A dock
/// <see cref="Tool"/>, not an editor pane - so working in it never swaps the ribbon.
///
/// Each page has its own <see cref="PageEditorViewModel"/>, all committing into the one
/// shared <see cref="EditorHistory"/>; page-list changes go into that same history, so
/// Ctrl+Z undoes "deleted page 3" as naturally as "moved a bubble". Undoing an edit made
/// on another page switches to that page first, so the change is never invisible.
/// </summary>
public sealed class PageNavigatorViewModel : Tool, IPageNumberingHost, IIssueLooksHost, ITitlePageHost, IPageSpacingHost
{
    private IReadOnlyDictionary<CharacterId, CharacterRevisionId> _issueLooks;
    private readonly EditorHistory _history;
    private PageItem _currentPage;
    private PageNumbering _pageNumbering;
    private readonly ICharacterCatalog? _characters;
    private readonly PictureLibrary? _pictures;
    private PanelGrid _grid;
    private readonly PanelLayoutPreset? _newPageLayout;
    private TextFields? _fields;

    /// <param name="characters">What every page draws its placed characters from (the Characters pane); null for a comic edited without one.</param>
    /// <param name="issueLooks">The issue's look per character (<see cref="ComicProject.IssueLooks"/>).</param>
    /// <param name="pictures">The comic's pictures, shared by every page; null for a comic edited without them.</param>
    /// <param name="grid">The margin and gutter the pages start with (<see cref="ComicProject.Grid"/>); the comic book default if null.</param>
    /// <param name="newPageLayout">The panels a new page starts with (<see cref="ComicProject.NewPageLayout"/>); one panel if null.</param>
    /// <param name="titlePage">The comic's title page (<see cref="ComicProject.TitlePage"/>), shown first unless the issue has its own; none if null.</param>
    public PageNavigatorViewModel(EditorHistory history, IEnumerable<ComicPage> pages, PageNumbering? pageNumbering = null, ICharacterCatalog? characters = null,
        IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null, PictureLibrary? pictures = null, PanelGrid? grid = null,
        PanelLayoutPreset? newPageLayout = null, ComicPage? titlePage = null)
    {
        _history = history;
        _pictures = pictures;
        _grid = grid ?? PanelGrid.Default;
        _newPageLayout = newPageLayout;
        _issueLooks = new Dictionary<CharacterId, CharacterRevisionId>(issueLooks ?? new Dictionary<CharacterId, CharacterRevisionId>());
        _characters = characters;
        _pageNumbering = pageNumbering ?? PageNumbering.Off;
        Id = "Pages";
        Title = "Pages";
        CanClose = false;
        CanFloat = false;

        Pages = new ObservableCollection<PageItem>(pages.Select(p => new PageItem(p.Id, CreateEditor(p.Id, p.Bounds, p.Document, null))));
        if (titlePage != null)
        {
            _comicTitlePage = new PageItem(titlePage.Id, CreateEditor(titlePage.Id, titlePage.Bounds, titlePage.Document with { TitlePage = TitlePageScope.Comic }, null));
            if (OwnTitlePage is null)
                Pages.Insert(0, _comicTitlePage);
        }
        if (Pages.Count == 0)
            throw new ArgumentException("A comic needs at least one page.", nameof(pages));
        SyncPages();
        _currentPage = Pages[0];

        AddPageCommand = new RelayCommand(() => AddPageAfter(CurrentPage));
        DuplicatePageCommand = new RelayCommand<PageItem?>(item => DuplicatePage(item ?? CurrentPage));
        DeletePageCommand = new RelayCommand<PageItem?>(item => DeletePage(item ?? CurrentPage), item => CanDeletePage(item ?? CurrentPage));
        MovePageUpCommand = new RelayCommand<PageItem?>(item => MoveBy(item ?? CurrentPage, -1), item => CanMoveBy(item ?? CurrentPage, -1));
        MovePageDownCommand = new RelayCommand<PageItem?>(item => MoveBy(item ?? CurrentPage, 1), item => CanMoveBy(item ?? CurrentPage, 1));

        _history.Restored += OnHistoryRestored;
    }

    public ObservableCollection<PageItem> Pages { get; }

    /// <summary>The page shown in the editor area. Bound two-way to the navigator list's selection.</summary>
    public PageItem CurrentPage
    {
        get => _currentPage;
        set
        {
            // A list box briefly reports "no selection" while its items shuffle; the
            // current page never goes away that way.
            if (value is null || ReferenceEquals(value, _currentPage) || !Pages.Contains(value))
            {
                OnPropertyChanged();
                return;
            }
            _currentPage = value;
            OnPropertyChanged();
            NotifyCommands();
            CurrentPageChanged?.Invoke(value);
        }
    }

    /// <summary>Raised when a different page becomes current - or the current one is asked for again (<see cref="Reveal"/>); the workspace shows its editor.</summary>
    public event Action<PageItem>? CurrentPageChanged;

    /// <summary>Makes <paramref name="page"/> current and shows it even if it already was current - e.g. clicking the current page while a character is being edited brings the page back.</summary>
    public void Reveal(PageItem page)
    {
        if (!Pages.Contains(page))
            return;
        if (ReferenceEquals(page, _currentPage))
            CurrentPageChanged?.Invoke(page);
        else
            CurrentPage = page;
    }

    public IRelayCommand AddPageCommand { get; }
    public IRelayCommand<PageItem?> DuplicatePageCommand { get; }
    public IRelayCommand<PageItem?> DeletePageCommand { get; }
    public IRelayCommand<PageItem?> MovePageUpCommand { get; }
    public IRelayCommand<PageItem?> MovePageDownCommand { get; }

    /// <summary>The issue's look per character; each page draws with it.</summary>
    public IReadOnlyDictionary<CharacterId, CharacterRevisionId> IssueLooks => _issueLooks;

    public event Action? IssueLooksChanged;

    public void SetIssueLook(CharacterId character, CharacterRevisionId? look)
    {
        var current = _issueLooks.TryGetValue(character, out var existing) ? existing : (CharacterRevisionId?)null;
        if (current == look)
            return;
        var before = _issueLooks;
        var after = new Dictionary<CharacterId, CharacterRevisionId>(_issueLooks);
        if (look is { } id)
            after[character] = id;
        else
            after.Remove(character);
        ApplyIssueLooks(after);
        _history.Push("Look for the issue", () => ApplyIssueLooks(before), () => ApplyIssueLooks(after), this);
    }

    private void ApplyIssueLooks(IReadOnlyDictionary<CharacterId, CharacterRevisionId> looks)
    {
        _issueLooks = looks;
        OnPropertyChanged(nameof(IssueLooks));
        IssueLooksChanged?.Invoke();
    }

    // ---------------------------------------------------------------- spacing and fields

    /// <summary>The margin and gutter every page lays out and snaps with (Layout tab); one setting for the whole comic, saved with it.</summary>
    public PanelGrid Spacing => _grid;

    public event Action? SpacingChanged;

    /// <summary>
    /// Layout tab › Margin / Gutter, for the whole comic: every page lays out and snaps to
    /// it. A new margin also moves the panel edges that sat on the old one onto it, on every
    /// page whose layout isn't locked, so the layouts keep hugging the margin. One undo step.
    /// </summary>
    public void SetSpacing(PanelGrid grid)
    {
        grid = new PanelGrid(Math.Max(0, grid.MarginMm), Math.Max(0, grid.GutterMm));
        if (grid == _grid)
            return;

        var before = _grid;
        using (_history.Group("Margins", this))
        {
            ApplySpacing(grid);
            _history.Push("Margins", () => ApplySpacing(before), () => ApplySpacing(grid), this);
            if (Math.Abs(grid.MarginMm - before.MarginMm) > 1e-9)
            {
                foreach (var page in AllPages)
                    page.Editor.MoveMargin(before.MarginMm, grid.MarginMm);
            }
        }
    }

    private void ApplySpacing(PanelGrid grid)
    {
        _grid = grid;
        SyncPages();
        OnPropertyChanged(nameof(Spacing));
        SpacingChanged?.Invoke();
    }

    /// <summary>What the fields in texts show on every page (the comic's title and issue number); the session keeps it up to date.</summary>
    public TextFields? Fields
    {
        get => _fields;
        set
        {
            if (Equals(value, _fields))
                return;
            _fields = value;
            SyncPages();
            OnPropertyChanged();
        }
    }

    /// <summary>The comic's page numbering; each page's <see cref="PageEditorViewModel.Folio"/> follows it and the page order.</summary>
    public PageNumbering PageNumbering => _pageNumbering;

    public event Action? PageNumberingChanged;

    public void SetPageNumbering(PageNumbering numbering)
    {
        numbering = numbering with { StartAt = Math.Max(0, numbering.StartAt) };
        if (numbering == _pageNumbering)
            return;

        var before = _pageNumbering;
        ApplyPageNumbering(numbering);
        _history.Push("Page numbers", () => ApplyPageNumbering(before), () => ApplyPageNumbering(numbering), this);
    }

    private void ApplyPageNumbering(PageNumbering numbering)
    {
        _pageNumbering = numbering;
        SyncPages();
        OnPropertyChanged(nameof(PageNumbering));
        PageNumberingChanged?.Invoke();
    }

    /// <summary>
    /// Every page's committed content, in order - what Save writes: the comic's title page
    /// first (also while this issue shows its own instead, so it isn't lost), then the issue's.
    /// </summary>
    public IReadOnlyList<(PageId Id, PageDocument Document)> Snapshot()
    {
        var issuePages = Pages.Where(p => !ReferenceEquals(p, _comicTitlePage)).Select(p => (p.Id, p.Editor.Committed));
        return _comicTitlePage is { } title ? [(title.Id, title.Editor.Committed), .. issuePages] : [.. issuePages];
    }

    /// <summary>
    /// Adds a new page (same size and spacing as <paramref name="after"/>, with one panel
    /// filling the live area - or, for a strip or webcomic, its format's panels) right after
    /// it, and shows it.
    /// </summary>
    public PageItem AddPageAfter(PageItem after)
    {
        var bounds = after.Editor.PageBounds;
        var id = PageId.New();
        var item = new PageItem(id, CreateEditor(id, bounds, ComicProject.BlankDocument(bounds, _grid, _newPageLayout), after.Editor));
        var order = Pages.ToList();
        order.Insert(order.IndexOf(after) + 1, item);
        ChangePages("Add page", order, item);
        return item;
    }

    /// <summary>A copy of <paramref name="source"/> (fresh panel ids, same content) right after it. A copy of a title page is an ordinary page.</summary>
    public PageItem DuplicatePage(PageItem source)
    {
        var id = PageId.New();
        var item = new PageItem(id, CreateEditor(id, source.Editor.PageBounds, Copy(source.Editor.Committed, TitlePageScope.None), source.Editor));
        var order = Pages.ToList();
        order.Insert(order.IndexOf(source) + 1, item);
        ChangePages("Duplicate page", order, item);
        return item;
    }

    /// <summary>
    /// Removes a page - never the navigator's last one, of any kind, so an issue can end up
    /// with only its title page (#65) but never with none at all. The next page - or the
    /// previous, at the end - becomes current. Deleting the comic's title page takes it away
    /// from every issue; deleting this issue's own swaps the comic's back in its place, which
    /// never loses a page and so is always allowed.
    /// </summary>
    public void DeletePage(PageItem page)
    {
        if (!Pages.Contains(page))
            return;

        var index = Pages.IndexOf(page);
        var order = Pages.ToList();
        if (page.Editor.Committed.TitlePage == TitlePageScope.Issue && _comicTitlePage is { } comic && !order.Contains(comic))
        {
            order[index] = comic;
            ChangePages("Use the comic's title page", order, ReferenceEquals(page, CurrentPage) ? comic : CurrentPage);
            return;
        }

        if (Pages.Count <= 1)
            return;
        order.RemoveAt(index);
        var current = ReferenceEquals(page, CurrentPage) ? order[Math.Min(index, order.Count - 1)] : CurrentPage;
        var comicTitlePage = ReferenceEquals(page, _comicTitlePage) ? null : _comicTitlePage;
        ChangePages(comicTitlePage == _comicTitlePage ? "Delete page" : "Remove the title page", order, current, comicTitlePage);
    }

    /// <summary>
    /// Whether <see cref="DeletePage"/> would do anything: swapping the issue's own title page
    /// for the comic's (hidden while the issue has its own) never loses a page, so that's
    /// always allowed; otherwise only while another page is left.
    /// </summary>
    public bool CanDeletePage(PageItem page) =>
        Pages.Contains(page) &&
        ((page.Editor.Committed.TitlePage == TitlePageScope.Issue && _comicTitlePage is { } comic && !Pages.Contains(comic)) || Pages.Count > 1);

    /// <summary>The first index a page can be moved to or from: a title page stays first.</summary>
    private int FirstMovable => Pages[0].Editor.Committed.IsTitlePage ? 1 : 0;

    private bool CanMoveBy(PageItem page, int delta) =>
        Pages.IndexOf(page) is var i && i >= FirstMovable && i + delta >= FirstMovable && i + delta < Pages.Count;

    /// <summary>Moves the page at <paramref name="from"/> so it ends up at index <paramref name="to"/> (drag-and-drop in the navigator). A title page stays first.</summary>
    public void MovePage(int from, int to)
    {
        if (from < 0 || from >= Pages.Count)
            return;
        var first = FirstMovable;
        if (from < first)
            return;
        to = Math.Clamp(to, first, Pages.Count - 1);
        if (from == to)
            return;

        var order = Pages.ToList();
        var item = order[from];
        order.RemoveAt(from);
        order.Insert(to, item);
        ChangePages("Move page", order, CurrentPage);
    }

    // ---------------------------------------------------------------- title page

    // The comic's title page, shared by every issue and kept in the project folder: first in
    // Pages unless this issue has its own - and held on to even then, so dropping the
    // issue's own brings it back, and a save never loses it.
    private PageItem? _comicTitlePage;

    /// <summary>The comic's title page (<see cref="TitlePageScope.Comic"/>): every issue opens with it unless it has its own. Null if the comic has none.</summary>
    public PageItem? ComicTitlePage => _comicTitlePage;

    /// <summary>This issue's own title page (<see cref="TitlePageScope.Issue"/>), shown instead of the comic's; null if it has none.</summary>
    public PageItem? OwnTitlePage => Pages.FirstOrDefault(p => p.Editor.Committed.TitlePage == TitlePageScope.Issue);

    /// <summary>The title page this issue opens with: its own, else the comic's.</summary>
    public PageItem? TitlePage => OwnTitlePage ?? (_comicTitlePage is { } comic && Pages.Contains(comic) ? comic : null);

    public bool HasTitlePage => TitlePage is not null;

    public bool HasOwnTitlePage => OwnTitlePage is not null;

    /// <summary>Whether Remove title page can do anything: the same rule as deleting the title page it shows (<see cref="CanDeletePage"/>) - never the navigator's last page.</summary>
    public bool CanRemoveTitlePage => TitlePage is { } shown && CanDeletePage(shown);

    public event Action? TitlePageChanged;

    /// <summary>
    /// Insert › Title page: the title page this issue opens with - its own, or the comic's,
    /// which changes it for every issue - redone in <paramref name="design"/>, keeping the
    /// words typed into it, like Word's cover pages. A comic without one gets one, at the
    /// front of every issue. Shown; one undo step.
    /// </summary>
    public PageItem InsertTitlePage(TitlePageDesign design)
    {
        if (TitlePage is { } shown)
        {
            var editor = shown.Editor;
            var words = TitlePages.WordsOn(editor.Committed.Panels.Values, TitlePages.DefaultWords);
            var redone = TitlePageDocument(design, editor.PageBounds, editor.Grid, words, editor.Committed.TitlePage) with { LayoutLocked = editor.Committed.LayoutLocked };
            editor.Apply(EditResult<PageDocument>.Success(redone));
            Reveal(shown);
            return shown;
        }

        var bounds = Pages[0].Editor.PageBounds;
        var id = PageId.New();
        var item = new PageItem(id, CreateEditor(id, bounds, TitlePageDocument(design, bounds, _grid, TitlePages.DefaultWords, TitlePageScope.Comic), CurrentPage.Editor));
        ChangePages("Insert title page", [item, .. Pages], item, comicTitlePage: item);
        return item;
    }

    /// <summary>
    /// Insert › Title page › Only this issue. On: this issue gets its own title page - a copy
    /// of the comic's, to change without touching the other issues'. Off: this issue's own
    /// goes and the comic's shows again - or, if the comic has none, this one becomes the
    /// comic's, for every issue. One undo step either way.
    /// </summary>
    public void SetOwnTitlePage(bool own)
    {
        if (own)
        {
            if (HasOwnTitlePage || _comicTitlePage is not { } comic || !Pages.Contains(comic))
                return;
            var id = PageId.New();
            var copy = new PageItem(id, CreateEditor(id, comic.Editor.PageBounds, Copy(comic.Editor.Committed, TitlePageScope.Issue), comic.Editor));
            var order = Pages.ToList();
            order[order.IndexOf(comic)] = copy;
            ChangePages("Title page for this issue only", order, copy, _comicTitlePage);
            return;
        }

        if (OwnTitlePage is not { } mine)
            return;
        if (_comicTitlePage != null)
        {
            DeletePage(mine);
            return;
        }
        var pages = Pages.ToList();
        pages.Remove(mine);
        var promotedId = PageId.New();
        var promoted = new PageItem(promotedId, CreateEditor(promotedId, mine.Editor.PageBounds, Copy(mine.Editor.Committed, TitlePageScope.Comic), mine.Editor));
        ChangePages("Title page for every issue", [promoted, .. pages], promoted, promoted);
    }

    /// <summary>Removes the title page this issue opens with: its own (then the comic's shows, if it has one), else the comic's - from every issue. Never the navigator's last page.</summary>
    public void RemoveTitlePage()
    {
        if (!CanRemoveTitlePage)
            return;
        if (TitlePage is { } shown)
            DeletePage(shown);
    }

    private static PageDocument TitlePageDocument(TitlePageDesign design, Rect2D bounds, PanelGrid grid, TitlePageWords words, TitlePageScope scope)
    {
        var panels = TitlePages.Compose(design, bounds, grid, words);
        return new PageDocument(panels.Select(p => p.Id).ToList(), panels.ToDictionary(p => p.Id), TitlePage: scope);
    }

    /// <summary>The same content under fresh panel ids, as a page of <paramref name="scope"/>.</summary>
    private static PageDocument Copy(PageDocument document, TitlePageScope scope)
    {
        var newIds = document.PanelOrder.Where(document.Panels.ContainsKey).ToDictionary(id => id, _ => PanelId.New());
        return new PageDocument(
            newIds.Values.ToList(),
            newIds.ToDictionary(kvp => kvp.Value, kvp => document.Panels[kvp.Key] with { Id = kvp.Value }),
            document.LayoutLocked,
            scope);
    }

    private void MoveBy(PageItem item, int delta)
    {
        var index = Pages.IndexOf(item);
        if (index >= 0)
            MovePage(index, index + delta);
    }

    private void ChangePages(string description, List<PageItem> order, PageItem current) =>
        ChangePages(description, order, current, _comicTitlePage);

    private void ChangePages(string description, List<PageItem> order, PageItem current, PageItem? comicTitlePage)
    {
        var beforeOrder = Pages.ToList();
        var beforeCurrent = CurrentPage;
        var beforeTitlePage = _comicTitlePage;
        SetPages(order, current, comicTitlePage);
        _history.Push(description, () => SetPages(beforeOrder, beforeCurrent, beforeTitlePage), () => SetPages(order, current, comicTitlePage), this);
    }

    /// <summary>Brings <see cref="Pages"/> to <paramref name="order"/> with moves/inserts/removes rather than a reset, so the list keeps its item containers (and a drag feels like a move, not a flicker).</summary>
    private void SetPages(IReadOnlyList<PageItem> order, PageItem current, PageItem? comicTitlePage)
    {
        _comicTitlePage = comicTitlePage;
        for (var i = 0; i < order.Count; i++)
        {
            if (i < Pages.Count && ReferenceEquals(Pages[i], order[i]))
                continue;
            var existing = Pages.IndexOf(order[i]);
            if (existing >= 0)
                Pages.Move(existing, i);
            else
                Pages.Insert(i, order[i]);
        }
        while (Pages.Count > order.Count)
            Pages.RemoveAt(Pages.Count - 1);

        SyncPages();
        if (ReferenceEquals(current, _currentPage))
        {
            OnPropertyChanged(nameof(CurrentPage)); // re-assert the selection the list may have dropped
            NotifyCommands();
        }
        else
        {
            CurrentPage = current;
        }
        OnPropertyChanged(nameof(HasTitlePage));
        OnPropertyChanged(nameof(HasOwnTitlePage));
        OnPropertyChanged(nameof(CanRemoveTitlePage));
        TitlePageChanged?.Invoke();
    }

    /// <summary>Brings every page up to date with its position and the comic-wide settings - including a page an undo just brought back, which missed changes made while it was gone.</summary>
    private void SyncPages()
    {
        for (var i = 0; i < Pages.Count; i++)
        {
            Pages[i].Number = i + 1;
            Pages[i].Editor.Folio = PageFolios.For(_pageNumbering, i);
        }
        foreach (var page in AllPages)
        {
            page.Editor.Grid = _grid;
            page.Editor.Fields = _fields;
        }
    }

    /// <summary>The pages shown, plus the comic's title page while this issue shows its own instead - it's still the comic's, kept up to date and saved.</summary>
    public IEnumerable<PageItem> AllPages =>
        _comicTitlePage is { } comic && !Pages.Contains(comic) ? [.. Pages, comic] : Pages;

    private void OnHistoryRestored(object? source)
    {
        if (source is PageEditorViewModel editor && Pages.FirstOrDefault(p => ReferenceEquals(p.Editor, editor)) is { } page)
            Reveal(page);
    }

    private void NotifyCommands()
    {
        DeletePageCommand.NotifyCanExecuteChanged();
        MovePageUpCommand.NotifyCanExecuteChanged();
        MovePageDownCommand.NotifyCanExecuteChanged();
    }

    private PageEditorViewModel CreateEditor(PageId id, Rect2D bounds, PageDocument document, PageEditorViewModel? settingsFrom)
    {
        var editor = new PageEditorViewModel(_history, bounds, document)
        {
            Id = $"page-{id.Value}",
            CanClose = false,
            CanFloat = false,
            NumberingHost = this,
            LooksHost = this,
            TitlePageHost = this,
            SpacingHost = this,
            Characters = _characters,
            Pictures = _pictures,
            Grid = _grid,
            Fields = _fields
        };
        if (settingsFrom != null)
        {
            editor.SnapEnabled = settingsFrom.SnapEnabled;
            editor.ShowMarginGuides = settingsFrom.ShowMarginGuides;
        }
        return editor;
    }
}
