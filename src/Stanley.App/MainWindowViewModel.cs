using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanley.App.Documents;
using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.Editors;
using Stanley.ProjectModel;

namespace Stanley.App;

/// <summary>The pages of the File (backstage) view, in Word's order.</summary>
public enum BackstagePage
{
    New,
    Open,
    Info,
    SaveAs,
    Export
}

public sealed record RecentProjectEntry(string Name, string Path);

/// <summary>
/// The window's document lifecycle, the way Word runs it: one open comic at a time, New /
/// Open / Save / Save As / Close / Export from the File view, an "unsaved changes" marker
/// in the title, and a save prompt before anything would throw unsaved work away. The
/// editing itself belongs to the <see cref="EditorWorkspace"/> for the open comic.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IFileDialogs _dialogs;
    private readonly RecentProjects _recent;
    private ComicProject? _project;
    private EditorWorkspace? _workspace;
    private PageNavigatorViewModel? _navigator;
    private bool _titleDirty;
    private bool _isBackstageOpen;
    private BackstagePage _backstagePage = BackstagePage.New;
    private MetricPaperSize _newPaperSize = MetricPaperSize.A4;
    private string? _message;

    public MainWindowViewModel(IFileDialogs dialogs, RecentProjects recent, bool startWithBlankComic = true)
    {
        _dialogs = dialogs;
        _recent = recent;

        OpenBackstageCommand = new RelayCommand<BackstagePage?>(page => ShowBackstage(page ?? (HasDocument ? BackstagePage.Info : BackstagePage.New)));
        CloseBackstageCommand = new RelayCommand(() => IsBackstageOpen = false, () => HasDocument);
        NewCommand = new AsyncRelayCommand<PanelLayoutPreset?>(NewAsync);
        OpenCommand = new AsyncRelayCommand(BrowseAndOpenAsync);
        OpenRecentCommand = new AsyncRelayCommand<string>(path => path is null ? Task.CompletedTask : OpenAsync(path));
        SaveCommand = new AsyncRelayCommand(async () => await SaveAsync(), () => HasDocument);
        SaveAsCommand = new AsyncRelayCommand(async () => await SaveAsAsync(), () => HasDocument);
        CloseDocumentCommand = new AsyncRelayCommand(CloseDocumentAsync, () => HasDocument);
        ExportPdfCommand = new AsyncRelayCommand(() => ExportAsync("pdf"), () => HasDocument);
        ExportPngCommand = new AsyncRelayCommand(() => ExportAsync("png"), () => HasDocument);
        UndoCommand = new RelayCommand(() => _workspace?.History.Undo(), () => _workspace?.History.CanUndo ?? false);
        RedoCommand = new RelayCommand(() => _workspace?.History.Redo(), () => _workspace?.History.CanRedo ?? false);

        RefreshRecent();
        if (startWithBlankComic)
            Load(ComicProject.CreateNew(_newPaperSize));
        else
            ShowBackstage(BackstagePage.New);
    }

    // ---------------------------------------------------------------- document state

    public ComicProject? Project => _project;
    public EditorWorkspace? Workspace => _workspace;
    public PageNavigatorViewModel? Navigator => _navigator;

    /// <summary>The page being edited (the navigator's current page).</summary>
    public PageEditorViewModel? Editor => _navigator?.CurrentPage.Editor;
    public bool HasDocument => _project is not null;

    /// <summary>Unsaved edits (anything undoable since the last save) or an unsaved title change.</summary>
    public bool IsDirty => HasDocument && ((_workspace?.History.IsDirty ?? false) || _titleDirty);

    public string WindowTitle => HasDocument ? $"{DocumentTitle}{(IsDirty ? " •" : "")} - Stanley" : "Stanley";

    /// <summary>The title bar caption, Word-style: the name, and whether it's saved.</summary>
    public string DocumentCaption => !HasDocument ? "Stanley"
        : _project!.IsUntitled ? $"{DocumentTitle} - not saved yet"
        : IsDirty ? $"{DocumentTitle} - unsaved changes"
        : $"{DocumentTitle} - saved";

    /// <summary>The comic's title, editable from File &gt; Info. Saved with the project.</summary>
    public string DocumentTitle
    {
        get => _project?.Title ?? "";
        set
        {
            if (_project is null || value == _project.Title || string.IsNullOrWhiteSpace(value))
                return;
            _project.Title = value.Trim();
            _titleDirty = true;
            RaiseDocumentChanged();
        }
    }

    public string LocationText => _project?.Location ?? "Not saved yet - Save picks a folder for it.";

    public string PageSizeText
    {
        get
        {
            if (_project is null)
                return "";
            var size = _project.Trim.Size;
            var paper = Enum.GetValues<MetricPaperSize>().Cast<MetricPaperSize?>()
                .FirstOrDefault(p => MetricPaperSizes.Size(p!.Value) == size);
            return $"{(paper is { } p ? p + " · " : "")}{size.WidthMm:0.#} × {size.HeightMm:0.#} mm, {_project.Trim.BleedMm:0.#} mm bleed";
        }
    }

    public string PanelCountText
    {
        get
        {
            if (_navigator is null)
                return "";
            var pages = _navigator.Pages.Count;
            var documents = _navigator.Pages.Select(p => p.Editor.Working).ToList();
            var panels = documents.Sum(d => d.PanelOrder.Count);
            var bubbles = documents.Sum(d => d.Panels.Values.Sum(p => p.Bubbles.Count));
            return $"{Plural(pages, "page")}, {Plural(panels, "panel")}, {Plural(bubbles, "bubble")}";
        }
    }

    /// <summary>The latest problem (or confirmation) from a file operation, shown in the title bar and the File view.</summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    // ---------------------------------------------------------------- backstage (File view)

    public bool IsBackstageOpen
    {
        get => _isBackstageOpen;
        set
        {
            // With nothing open, the File view *is* the window - there's nothing to go back to.
            if (!value && !HasDocument)
                return;
            SetProperty(ref _isBackstageOpen, value);
        }
    }

    public BackstagePage BackstagePage
    {
        get => _backstagePage;
        set
        {
            SetProperty(ref _backstagePage, value);
            OnPropertyChanged(nameof(IsNewPage));
            OnPropertyChanged(nameof(IsOpenPage));
            OnPropertyChanged(nameof(IsInfoPage));
            OnPropertyChanged(nameof(IsSaveAsPage));
            OnPropertyChanged(nameof(IsExportPage));
        }
    }

    public bool IsNewPage { get => BackstagePage == BackstagePage.New; set => SetPageFlag(BackstagePage.New, value); }
    public bool IsOpenPage { get => BackstagePage == BackstagePage.Open; set => SetPageFlag(BackstagePage.Open, value); }
    public bool IsInfoPage { get => BackstagePage == BackstagePage.Info; set => SetPageFlag(BackstagePage.Info, value); }
    public bool IsSaveAsPage { get => BackstagePage == BackstagePage.SaveAs; set => SetPageFlag(BackstagePage.SaveAs, value); }
    public bool IsExportPage { get => BackstagePage == BackstagePage.Export; set => SetPageFlag(BackstagePage.Export, value); }

    private void SetPageFlag(BackstagePage page, bool value)
    {
        if (value)
            BackstagePage = page;
    }

    public void ShowBackstage(BackstagePage page)
    {
        BackstagePage = page;
        Message = null;
        RefreshRecent();
        SetProperty(ref _isBackstageOpen, true, nameof(IsBackstageOpen));
    }

    public IReadOnlyList<MetricPaperSize> PaperSizes { get; } = Enum.GetValues<MetricPaperSize>();

    /// <summary>Paper for File &gt; New; A4 unless changed.</summary>
    public MetricPaperSize NewPaperSize
    {
        get => _newPaperSize;
        set => SetProperty(ref _newPaperSize, value);
    }

    public ObservableCollection<RecentProjectEntry> RecentEntries { get; } = [];

    public bool HasRecent => RecentEntries.Count > 0;

    // ---------------------------------------------------------------- commands

    public IRelayCommand<BackstagePage?> OpenBackstageCommand { get; }
    public IRelayCommand CloseBackstageCommand { get; }
    public IAsyncRelayCommand<PanelLayoutPreset?> NewCommand { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand<string> OpenRecentCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand SaveAsCommand { get; }
    public IAsyncRelayCommand CloseDocumentCommand { get; }
    public IAsyncRelayCommand ExportPdfCommand { get; }
    public IAsyncRelayCommand ExportPngCommand { get; }
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }

    // ---------------------------------------------------------------- operations

    /// <summary>File &gt; New: a blank comic on <see cref="NewPaperSize"/>, optionally pre-tiled with a layout.</summary>
    public async Task NewAsync(PanelLayoutPreset? layout)
    {
        if (!await ConfirmDiscardAsync())
            return;
        Load(ComicProject.CreateNew(NewPaperSize, layout));
    }

    public async Task BrowseAndOpenAsync()
    {
        var folder = await _dialogs.PickFolderAsync("Open a Stanley comic (its project folder)");
        if (folder != null)
            await OpenAsync(folder);
    }

    public async Task OpenAsync(string folder)
    {
        if (_project?.Location is { } current && PathsEqual(current, folder))
        {
            IsBackstageOpen = false; // already open - Word just switches to it
            return;
        }

        if (!Directory.Exists(folder))
        {
            _recent.Remove(folder);
            RefreshRecent();
            ShowError($"\"{folder}\" couldn't be found - it may have been moved or deleted.");
            return;
        }

        if (!await ConfirmDiscardAsync())
            return;

        try
        {
            var project = ComicProject.Open(folder);
            Load(project);
            _recent.Add(project.Location!);
            RefreshRecent();
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            ShowError($"Couldn't open \"{folder}\": {e.Message}");
        }
    }

    /// <summary>Ctrl+S. An untitled comic goes through Save As, like Word's first save. Returns whether it was saved.</summary>
    public async Task<bool> SaveAsync()
    {
        if (_project is null || _navigator is null)
            return false;
        if (_project.IsUntitled)
            return await SaveAsAsync();

        try
        {
            _project.Save(_navigator.Snapshot(), _navigator.PageNumbering);
            MarkSaved();
            return true;
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            ShowError($"Couldn't save: {e.Message}");
            return false;
        }
    }

    public async Task<bool> SaveAsAsync()
    {
        if (_project is null || _navigator is null)
            return false;

        var folder = await _dialogs.PickFolderAsync("Save the comic in a folder");
        if (folder is null)
            return false;

        try
        {
            var saved = _project.SaveAs(folder, _navigator.Snapshot(), _navigator.PageNumbering);
            MarkSaved();
            Message = $"Saved to {saved}";
            return true;
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            ShowError($"Couldn't save: {e.Message}");
            return false;
        }
    }

    /// <summary>File &gt; Close: after the save prompt, the window is left on the File view to start or open something else.</summary>
    public async Task CloseDocumentAsync()
    {
        if (!await ConfirmDiscardAsync())
            return;
        Unload();
        ShowBackstage(BackstagePage.New);
    }

    public async Task ExportAsync(string format)
    {
        if (_project is null || _navigator is null)
            return;

        var isPdf = format == "pdf";
        var current = _navigator.CurrentPage;
        var path = await _dialogs.PickExportFileAsync(
            isPdf ? "Export all pages as PDF" : "Export this page as PNG",
            isPdf ? $"{DocumentTitle}.pdf" : $"{DocumentTitle} - page {current.Number}.png",
            format,
            isPdf ? "PDF document" : "PNG image");
        if (path is null)
            return;

        try
        {
            if (isPdf)
                ComicProject.ExportPdf(path, _navigator.Pages.Select(p => (p.Editor.PageBounds, p.Editor.Committed, p.Editor.Folio)));
            else
                ComicProject.ExportPng(path, current.Editor.PageBounds, current.Editor.Committed, folio: current.Editor.Folio);
            Message = $"Exported to {path}";
            IsBackstageOpen = false;
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            ShowError($"Couldn't export: {e.Message}");
        }
    }

    /// <summary>Before discarding the open comic: nothing to ask if it's clean; otherwise Save / Don't Save / Cancel. Returns whether it's OK to go ahead.</summary>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsDirty)
            return true;

        return await _dialogs.AskSaveChangesAsync(DocumentTitle) switch
        {
            SaveChangesChoice.Save => await SaveAsync(),
            SaveChangesChoice.DontSave => true,
            _ => false
        };
    }

    // ---------------------------------------------------------------- helpers

    private void Load(ComicProject project)
    {
        Unload();
        _project = project;
        (_workspace, _navigator) = PageEditorHost.CreateWorkspace(project);
        _workspace.History.PropertyChanged += OnHistoryChanged;
        _navigator.CurrentPageChanged += OnCurrentPageChanged;
        _titleDirty = false;
        Message = null;
        SetProperty(ref _isBackstageOpen, false, nameof(IsBackstageOpen));
        RaiseDocumentChanged();
        OnPropertyChanged(nameof(Workspace));
        OnPropertyChanged(nameof(Editor));
        OnPropertyChanged(nameof(Navigator));
        OnPropertyChanged(nameof(Project));
    }

    private void Unload()
    {
        if (_workspace != null)
            _workspace.History.PropertyChanged -= OnHistoryChanged;
        if (_navigator != null)
            _navigator.CurrentPageChanged -= OnCurrentPageChanged;
        _project = null;
        _workspace = null;
        _navigator = null;
        _titleDirty = false;
        RaiseDocumentChanged();
        OnPropertyChanged(nameof(Workspace));
        OnPropertyChanged(nameof(Editor));
        OnPropertyChanged(nameof(Navigator));
        OnPropertyChanged(nameof(Project));
    }

    private void MarkSaved()
    {
        _workspace?.History.MarkSaved();
        _titleDirty = false;
        Message = null;
        if (_project?.Location is { } location)
            _recent.Add(location);
        RefreshRecent();
        RaiseDocumentChanged();
    }

    private void OnHistoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorHistory.IsDirty))
            RaiseDocumentChanged();
        if (e.PropertyName is nameof(EditorHistory.CanUndo))
            OnPropertyChanged(nameof(PanelCountText)); // every edit, undo and redo passes through here
        if (e.PropertyName is nameof(EditorHistory.CanUndo) or nameof(EditorHistory.CanRedo))
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnCurrentPageChanged(PageItem page) => OnPropertyChanged(nameof(Editor));

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private void RaiseDocumentChanged()
    {
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(DocumentCaption));
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(LocationText));
        OnPropertyChanged(nameof(PageSizeText));
        OnPropertyChanged(nameof(PanelCountText));
        CloseBackstageCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        CloseDocumentCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
        ExportPngCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void RefreshRecent()
    {
        RecentEntries.Clear();
        foreach (var path in _recent.Paths)
            RecentEntries.Add(new RecentProjectEntry(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), path));
        OnPropertyChanged(nameof(HasRecent));
    }

    /// <summary>Errors surface in the File view (opened on the relevant page if it was closed) rather than as a modal.</summary>
    private void ShowError(string message)
    {
        if (!IsBackstageOpen)
            ShowBackstage(HasDocument ? BackstagePage.Info : BackstagePage.Open);
        Message = message;
    }

    private static bool IsFileProblem(Exception e) =>
        e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException or ArgumentException;

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
}
