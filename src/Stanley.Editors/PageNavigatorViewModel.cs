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
    private int _number;

    public PageItem(PageId id, PageEditorViewModel editor)
    {
        Id = id;
        Editor = editor;
    }

    public PageId Id { get; }

    public PageEditorViewModel Editor { get; }

    public int Number
    {
        get => _number;
        set
        {
            if (SetProperty(ref _number, value))
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
    public PageNavigatorViewModel(EditorHistory history, IEnumerable<ComicPage> pages, PageNumbering? pageNumbering = null, ICharacterCatalog? characters = null,
        IReadOnlyDictionary<CharacterId, CharacterRevisionId>? issueLooks = null, PictureLibrary? pictures = null, PanelGrid? grid = null,
        PanelLayoutPreset? newPageLayout = null)
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
        if (Pages.Count == 0)
            throw new ArgumentException("A comic needs at least one page.", nameof(pages));
        SyncPages();
        _currentPage = Pages[0];

        AddPageCommand = new RelayCommand(() => AddPageAfter(CurrentPage));
        DuplicatePageCommand = new RelayCommand<PageItem?>(item => DuplicatePage(item ?? CurrentPage));
        DeletePageCommand = new RelayCommand<PageItem?>(item => DeletePage(item ?? CurrentPage), _ => Pages.Count > 1);
        MovePageUpCommand = new RelayCommand<PageItem?>(item => MoveBy(item ?? CurrentPage, -1), item => Pages.IndexOf(item ?? CurrentPage) > 0);
        MovePageDownCommand = new RelayCommand<PageItem?>(item => MoveBy(item ?? CurrentPage, 1), item => Pages.IndexOf(item ?? CurrentPage) is var i && i >= 0 && i < Pages.Count - 1);

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
                foreach (var page in Pages)
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

    /// <summary>Every page's committed content, in order - what Save writes.</summary>
    public IReadOnlyList<(PageId Id, PageDocument Document)> Snapshot() =>
        Pages.Select(p => (p.Id, p.Editor.Committed)).ToList();

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

    /// <summary>A copy of <paramref name="source"/> (fresh panel ids, same content) right after it. A copy of the title page is an ordinary page.</summary>
    public PageItem DuplicatePage(PageItem source)
    {
        var document = source.Editor.Committed;
        var newIds = document.PanelOrder.Where(document.Panels.ContainsKey).ToDictionary(id => id, _ => PanelId.New());
        var copy = new PageDocument(
            newIds.Values.ToList(),
            newIds.ToDictionary(kvp => kvp.Value, kvp => document.Panels[kvp.Key] with { Id = kvp.Value }),
            document.LayoutLocked,
            IsTitlePage: false);

        var id = PageId.New();
        var item = new PageItem(id, CreateEditor(id, source.Editor.PageBounds, copy, source.Editor));
        var order = Pages.ToList();
        order.Insert(order.IndexOf(source) + 1, item);
        ChangePages("Duplicate page", order, item);
        return item;
    }

    /// <summary>Removes a page (never the last one). The next page - or the previous, at the end - becomes current.</summary>
    public void DeletePage(PageItem page)
    {
        if (Pages.Count <= 1 || !Pages.Contains(page))
            return;

        var index = Pages.IndexOf(page);
        var order = Pages.ToList();
        order.RemoveAt(index);
        var current = ReferenceEquals(page, CurrentPage) ? order[Math.Min(index, order.Count - 1)] : CurrentPage;
        ChangePages("Delete page", order, current);
    }

    /// <summary>Moves the page at <paramref name="from"/> so it ends up at index <paramref name="to"/> (drag-and-drop in the navigator).</summary>
    public void MovePage(int from, int to)
    {
        if (from < 0 || from >= Pages.Count)
            return;
        to = Math.Clamp(to, 0, Pages.Count - 1);
        if (from == to)
            return;

        var order = Pages.ToList();
        var item = order[from];
        order.RemoveAt(from);
        order.Insert(to, item);
        ChangePages("Move page", order, CurrentPage);
    }

    // ---------------------------------------------------------------- title page

    /// <summary>The comic's title page (Insert › Title page), if it has one.</summary>
    public PageItem? TitlePage => Pages.FirstOrDefault(p => p.Editor.Committed.IsTitlePage);

    public bool HasTitlePage => TitlePage is not null;

    /// <summary>Whether the title page can go: it can't if it's the comic's only page.</summary>
    public bool CanRemoveTitlePage => HasTitlePage && Pages.Count > 1;

    public event Action? TitlePageChanged;

    /// <summary>
    /// Insert › Title page: a title page in <paramref name="design"/> at the front of the
    /// comic, shown. If the comic has one already, that page is redone in the new design
    /// instead, keeping the words typed into it - like Word's cover pages. One undo step.
    /// </summary>
    public PageItem InsertTitlePage(TitlePageDesign design)
    {
        if (TitlePage is { } existing)
        {
            var editor = existing.Editor;
            var words = TitlePages.WordsOn(editor.Committed.Panels.Values, TitlePages.DefaultWords);
            var redone = TitlePageDocument(design, editor.PageBounds, editor.Grid, words) with { LayoutLocked = editor.Committed.LayoutLocked };
            editor.Apply(EditResult<PageDocument>.Success(redone));
            Reveal(existing);
            return existing;
        }

        var bounds = Pages[0].Editor.PageBounds;
        var id = PageId.New();
        var item = new PageItem(id, CreateEditor(id, bounds, TitlePageDocument(design, bounds, _grid, TitlePages.DefaultWords), CurrentPage.Editor));
        var order = Pages.ToList();
        order.Insert(0, item);
        ChangePages("Insert title page", order, item);
        return item;
    }

    /// <summary>Deletes the title page (never the comic's only page).</summary>
    public void RemoveTitlePage()
    {
        if (TitlePage is { } page)
            DeletePage(page);
    }

    private static PageDocument TitlePageDocument(TitlePageDesign design, Rect2D bounds, PanelGrid grid, TitlePageWords words)
    {
        var panels = TitlePages.Compose(design, bounds, grid, words);
        return new PageDocument(panels.Select(p => p.Id).ToList(), panels.ToDictionary(p => p.Id), IsTitlePage: true);
    }

    private void MoveBy(PageItem item, int delta)
    {
        var index = Pages.IndexOf(item);
        if (index >= 0)
            MovePage(index, index + delta);
    }

    private void ChangePages(string description, List<PageItem> order, PageItem current)
    {
        var beforeOrder = Pages.ToList();
        var beforeCurrent = CurrentPage;
        SetPages(order, current);
        _history.Push(description, () => SetPages(beforeOrder, beforeCurrent), () => SetPages(order, current), this);
    }

    /// <summary>Brings <see cref="Pages"/> to <paramref name="order"/> with moves/inserts/removes rather than a reset, so the list keeps its item containers (and a drag feels like a move, not a flicker).</summary>
    private void SetPages(IReadOnlyList<PageItem> order, PageItem current)
    {
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
            Pages[i].Editor.Grid = _grid;
            Pages[i].Editor.Fields = _fields;
        }
    }

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
