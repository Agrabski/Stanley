using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanley.App.Diagnostics;
using Stanley.App.Documents;
using Stanley.App.Updates;
using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.Editors;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Ids;

namespace Stanley.App;

/// <summary>The pages of the File (backstage) view, in Word's order.</summary>
public enum BackstagePage
{
    New,
    Open,
    Info,
    SaveAs,
    Export,
    Options
}

public sealed record RecentProjectEntry(string Name, string Path);

/// <summary>One of the comic's issues, as shown in File &gt; Info's Issues section and the quick switcher.</summary>
public sealed record IssueEntry(IssueId Id, string Number, string Title, int PageCount, bool IsCurrent)
{
    public string Caption => string.IsNullOrEmpty(Title) ? $"#{Number}" : $"#{Number} - {Title}";
    public string PageCountText => PageCount == 1 ? "1 page" : $"{PageCount} pages";
}

/// <summary>
/// The window's document lifecycle, the way Word runs it: one open comic at a time, New /
/// Open / Save / Save As / Close / Export from the File view, an "unsaved changes" marker
/// in the title, and a save prompt before anything would throw unsaved work away. The
/// editing itself belongs to the <see cref="EditorWorkspace"/> for the open comic.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    /// <summary>How long after the last change AutoSave waits, so a burst of edits is one save, not twenty.</summary>
    public static readonly TimeSpan AutoSaveDelay = TimeSpan.FromSeconds(2);

    /// <summary>At most this long between a change and its crash-recovery snapshot.</summary>
    public static readonly TimeSpan RecoveryDelay = TimeSpan.FromSeconds(5);

    /// <summary>How long the notice after File › Export stays up, offering to open what was exported.</summary>
    public static readonly TimeSpan ExportNoticeDuration = TimeSpan.FromSeconds(30);

    private readonly IFileDialogs _dialogs;
    private readonly RecentProjects _recent;
    private readonly AppSettings _settings;
    private readonly RecoveryStore? _recovery;
    private readonly IDelayScheduler? _scheduler;
    private readonly GithubTokenStore _tokenStore;
    private readonly IUpdateService? _updates;
    private readonly IFileLauncher? _launcher;
    private IDisposable? _pendingAutoSave;
    private IDisposable? _pendingNoticeHide;
    private IDisposable? _pendingRecovery;
    private IDisposable? _pendingUpdateCheck;
    private bool _recoveredUnsaved;
    private bool _isCheckingForUpdates;
    private AvailableUpdate? _availableUpdate;
    private ComicProject? _project;
    private EditorWorkspace? _workspace;
    private PageNavigatorViewModel? _navigator;
    private CharacterLibraryViewModel? _characters;
    private PictureLibrary? _pictures;
    private bool _infoDirty;
    private bool _isBackstageOpen;
    private MetricPaperSize _newPaperSize = MetricPaperSize.A4;

    /// <param name="settings">User preferences (AutoSave); in-memory defaults if null.</param>
    /// <param name="recovery">Crash recovery; none if null.</param>
    /// <param name="scheduler">Runs AutoSave, recovery snapshots and the startup update check after a delay; without one, none of them run on their own (tests drive them directly).</param>
    /// <param name="tokenStore">The user's optional GitHub token for update checks; in-memory-only default if null.</param>
    /// <param name="updates">Checks for/applies app updates; update controls are hidden entirely if null.</param>
    /// <param name="launcher">Opens exported files and shows them in their folder; the export notice offers neither if null.</param>
    public MainWindowViewModel(
        IFileDialogs dialogs,
        RecentProjects recent,
        bool startWithBlankComic = true,
        AppSettings? settings = null,
        RecoveryStore? recovery = null,
        IDelayScheduler? scheduler = null,
        GithubTokenStore? tokenStore = null,
        IUpdateService? updates = null,
        IFileLauncher? launcher = null)
    {
        _dialogs = dialogs;
        _launcher = launcher;
        _recent = recent;
        _settings = settings ?? new AppSettings(null);
        _recovery = recovery;
        _scheduler = scheduler;
        _tokenStore = tokenStore ?? new GithubTokenStore(null);
        _updates = updates;
        ThemeSwitcher.Apply(_settings.Theme);

        OpenBackstageCommand = new RelayCommand<BackstagePage?>(page => ShowBackstage(page ?? (HasDocument ? BackstagePage.Info : BackstagePage.New)));
        CloseBackstageCommand = new RelayCommand(() => IsBackstageOpen = false, () => HasDocument);
        NewCommand = new AsyncRelayCommand<PanelLayoutPreset?>(NewAsync);
        NewFromTemplateCommand = new AsyncRelayCommand<ComicTemplate?>(NewFromTemplateAsync);
        OpenCommand = new AsyncRelayCommand(BrowseAndOpenAsync);
        OpenRecentCommand = new AsyncRelayCommand<string>(path => path is null ? Task.CompletedTask : OpenAsync(path));
        SaveCommand = new AsyncRelayCommand(async () => await SaveAsync(), () => HasDocument);
        SaveAsCommand = new AsyncRelayCommand(async () => await SaveAsAsync(), () => HasDocument);
        CloseDocumentCommand = new AsyncRelayCommand(CloseDocumentAsync, () => HasDocument);
        SwitchIssueCommand = new AsyncRelayCommand<IssueId>(SwitchIssueAsync, _ => HasDocument);
        NewIssueCommand = new AsyncRelayCommand(NewIssueAsync, () => HasDocument);
        ExportPdfCommand = new AsyncRelayCommand(() => ExportAsync("pdf"), () => HasDocument);
        ExportPngCommand = new AsyncRelayCommand(() => ExportAsync("png"), () => HasDocument);
        OpenExportCommand = new AsyncRelayCommand(() => LaunchExportAsync(open: true), () => CanLaunchExport);
        ShowExportInFolderCommand = new AsyncRelayCommand(() => LaunchExportAsync(open: false), () => CanLaunchExport);
        DismissExportNoticeCommand = new RelayCommand(DismissExportNotice);
        UndoCommand = new RelayCommand(() => _workspace?.History.Undo(), () => _workspace?.History.CanUndo ?? false);
        RedoCommand = new RelayCommand(() => _workspace?.History.Redo(), () => _workspace?.History.CanRedo ?? false);
        OpenRecoveredCommand = new AsyncRelayCommand<RecoveredComic>(comic => comic is null ? Task.CompletedTask : OpenRecoveredAsync(comic));
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => HasUpdateAvailable);
        ChooseSvgEditorCommand = new AsyncRelayCommand(ChooseSvgEditorAsync);
        DiscardRecoveredCommand = new RelayCommand<RecoveredComic>(comic =>
        {
            if (comic is null)
                return;
            _recovery?.Discard(comic);
            AppLog.Info($"Discarded recovered work \"{comic.Title}\" from {comic.SavedAtText}.");
            RefreshRecovered();
        });

        RefreshRecent();
        _recovery?.StartSession();
        if (startWithBlankComic)
            Load(ComicProject.CreateNew(_newPaperSize));
        else
            ShowBackstage(BackstagePage.New);

        // Unsaved work from a session that crashed: say so up front, like Word's Document Recovery.
        RefreshRecovered();
        if (HasRecovered)
        {
            AppLog.Warn($"Found {RecoveredEntries.Count} recovered comic(s) from a session that didn't shut down properly.");
            ShowBackstage(BackstagePage.Open);
        }

        if (_scheduler is not null && AutoCheckForUpdates && CanCheckForUpdates)
            _pendingUpdateCheck = _scheduler.Schedule(TimeSpan.FromSeconds(5), () => _ = CheckForUpdatesOnStartupAsync());
    }

    // ---------------------------------------------------------------- document state

    public ComicProject? Project => _project;
    public EditorWorkspace? Workspace => _workspace;
    public PageNavigatorViewModel? Navigator => _navigator;

    /// <summary>The open comic's Characters pane (its characters and their editors), or null with no comic open.</summary>
    public CharacterLibraryViewModel? Characters => _characters;

    /// <summary>The page being edited (the navigator's current page).</summary>
    public PageEditorViewModel? Editor => _navigator?.CurrentPage.Editor;
    public bool HasDocument => _project is not null;

    /// <summary>Unsaved edits (anything undoable since the last save), an unsaved File › Info change (title, issue number), or recovered work not yet saved back.</summary>
    public bool IsDirty => HasDocument && ((_workspace?.History.IsDirty ?? false) || _infoDirty || _recoveredUnsaved);

    // ---------------------------------------------------------------- appearance

    /// <summary>File &gt; Options &gt; Appearance: light, dark, or follow the system. Remembered, and applied straight away.</summary>
    public AppTheme Theme
    {
        get => _settings.Theme;
        set
        {
            if (value == _settings.Theme)
                return;
            _settings.Theme = value;
            AppLog.Info($"Theme set to {value}.");
            ThemeSwitcher.Apply(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
        }
    }

    public bool IsSystemTheme { get => Theme == AppTheme.System; set { if (value) Theme = AppTheme.System; } }
    public bool IsLightTheme { get => Theme == AppTheme.Light; set { if (value) Theme = AppTheme.Light; } }
    public bool IsDarkTheme { get => Theme == AppTheme.Dark; set { if (value) Theme = AppTheme.Dark; } }

    // ---------------------------------------------------------------- AutoSave

    /// <summary>
    /// The title bar's AutoSave switch: a remembered preference, on by default. It saves a
    /// moment after each change once the comic has a folder; switching it on for a comic
    /// that has never been saved asks where to save it first (and stays off if that's
    /// cancelled), like Word.
    /// </summary>
    public bool AutoSaveEnabled
    {
        get => _settings.AutoSave && !(_project?.IsUntitled ?? false);
        set
        {
            if (value == AutoSaveEnabled)
                return;
            if (value && _project is { IsUntitled: true })
            {
                OnPropertyChanged(); // stays off until the first save succeeds
                _ = EnableAutoSaveForUntitledAsync();
                return;
            }
            SetAutoSavePreference(value);
        }
    }

    private async Task EnableAutoSaveForUntitledAsync()
    {
        if (await SaveAsAsync())
            SetAutoSavePreference(true);
    }

    private void SetAutoSavePreference(bool on)
    {
        _settings.AutoSave = on;
        AppLog.Info($"AutoSave turned {(on ? "on" : "off")}.");
        OnPropertyChanged(nameof(AutoSaveEnabled));
        OnPropertyChanged(nameof(AutoSaveTip));
        if (on && IsDirty)
            ScheduleBackgroundSaves();
    }

    public string AutoSaveTip => _project is { IsUntitled: true }
        ? "AutoSave - switch on to pick a folder for this comic; after that every change is saved automatically."
        : AutoSaveEnabled
            ? "AutoSave is on: changes are saved a moment after you make them."
            : "AutoSave is off: save with Ctrl+S.";

    /// <summary>Saves now if AutoSave is on and there's anything to save; failures are logged and shown, never prompted. Called by the timer (and by tests).</summary>
    public void AutoSaveNow()
    {
        _pendingAutoSave = null;
        if (!AutoSaveEnabled || !IsDirty || _project is null || _navigator is null)
            return;

        try
        {
            _project.Save(_navigator.Snapshot(), _navigator.PageNumbering, _characters?.Snapshot(), _navigator.IssueLooks, _pictures?.Files);
            AppLog.Info($"AutoSaved \"{DocumentTitle}\" to {_project.Location}.");
            MarkSaved();
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            AppLog.Error($"AutoSave of \"{DocumentTitle}\" to {_project.Location} failed.", e);
            Message = $"AutoSave failed: {e.Message}";
        }
    }

    // ---------------------------------------------------------------- crash recovery

    public ObservableCollection<RecoveredComic> RecoveredEntries { get; } = [];

    public bool HasRecovered => RecoveredEntries.Count > 0;

    public IAsyncRelayCommand<RecoveredComic> OpenRecoveredCommand { get; }
    public IRelayCommand<RecoveredComic> DiscardRecoveredCommand { get; }

    /// <summary>Writes the crash-recovery snapshot now if there's unsaved work. Called by the timer, before an unhandled crash takes the process down, and by tests.</summary>
    public void WriteRecoverySnapshot()
    {
        _pendingRecovery = null;
        if (_recovery is null || _project is null || _navigator is null || !IsDirty)
            return;

        try
        {
            _recovery.Write(_project, _navigator.Snapshot(), _navigator.PageNumbering, _characters?.Snapshot(), _navigator.IssueLooks, _pictures?.Files);
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            AppLog.Error("Couldn't write the crash-recovery snapshot.", e);
        }
    }

    /// <summary>Opens recovered work as the current comic: back at its original folder if it had one (so Save puts it where it belongs), with unsaved changes pending.</summary>
    public async Task OpenRecoveredAsync(RecoveredComic comic)
    {
        if (!await ConfirmDiscardAsync())
            return;

        try
        {
            var project = ComicProject.OpenRecovered(comic.SnapshotDirectory, comic.OriginalLocation);
            Load(project);
            _recoveredUnsaved = true;
            AppLog.Info($"Opened recovered work \"{comic.Title}\" from {comic.SavedAtText} (originally {comic.LocationText}).");
            // It's in this session's hands now: snapshot it here before dropping the old copy.
            WriteRecoverySnapshot();
            _recovery?.Discard(comic);
            RefreshRecovered();
            RaiseDocumentChanged();
            ScheduleBackgroundSaves();
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            AppLog.Error($"Couldn't open recovered work from {comic.SnapshotDirectory}.", e);
            ShowError($"Couldn't open the recovered comic: {e.Message}");
        }
    }

    /// <summary>Clean shutdown: nothing is left to recover. (A crash never gets here, which is the point.)</summary>
    public void EndSession()
    {
        _pendingAutoSave?.Dispose();
        _pendingRecovery?.Dispose();
        _pendingUpdateCheck?.Dispose();
        _recovery?.Dispose();
        AppLog.Info("Session ended cleanly.");
    }

    private void RefreshRecovered()
    {
        RecoveredEntries.Clear();
        foreach (var comic in _recovery?.FindAbandoned() ?? [])
            RecoveredEntries.Add(comic);
        OnPropertyChanged(nameof(HasRecovered));
    }

    /// <summary>After any change: AutoSave (debounced - each change restarts the wait) and a recovery snapshot (throttled - at most one pending).</summary>
    private void ScheduleBackgroundSaves()
    {
        if (_scheduler is null || !IsDirty)
            return;

        if (AutoSaveEnabled)
        {
            _pendingAutoSave?.Dispose();
            _pendingAutoSave = _scheduler.Schedule(AutoSaveDelay, AutoSaveNow);
        }
        _pendingRecovery ??= _scheduler.Schedule(RecoveryDelay, WriteRecoverySnapshot);
    }

    // ---------------------------------------------------------------- updates

    /// <summary>
    /// An optional GitHub personal access token: Stanley is public, so update checks work
    /// anonymously without one. Only useful to get past GitHub's anonymous rate limit or to
    /// point at a private fork. Kept on this machine only (<see cref="Updates.GithubTokenStore"/>),
    /// never in <see cref="AppSettings"/>.
    /// </summary>
    public string? GithubToken
    {
        get => _tokenStore.Token;
        set
        {
            if (value == _tokenStore.Token)
                return;
            _tokenStore.Token = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Stable (tagged releases) or Nightly (every change to `develop`, for testers).</summary>
    public AppUpdateChannel UpdateChannel
    {
        get => _settings.UpdateChannel;
        set
        {
            if (value == _settings.UpdateChannel)
                return;
            _settings.UpdateChannel = value;
            AppLog.Info($"Update channel set to {value}.");
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsStableChannel));
            OnPropertyChanged(nameof(IsNightlyChannel));
        }
    }

    public bool IsStableChannel { get => UpdateChannel == AppUpdateChannel.Stable; set { if (value) UpdateChannel = AppUpdateChannel.Stable; } }
    public bool IsNightlyChannel { get => UpdateChannel == AppUpdateChannel.Nightly; set { if (value) UpdateChannel = AppUpdateChannel.Nightly; } }

    /// <summary>File &gt; Options &gt; Updates: check automatically on startup, a moment after the window opens.</summary>
    public bool AutoCheckForUpdates
    {
        get => _settings.AutoCheckForUpdates;
        set
        {
            if (value == AutoCheckForUpdates)
                return;
            _settings.AutoCheckForUpdates = value;
            AppLog.Info($"Automatic update checks turned {(value ? "on" : "off")}.");
            OnPropertyChanged();
        }
    }

    /// <summary>Whether there's anything to check with: an update service (a real install, not a dev/test build). No token is required - Stanley is public.</summary>
    public bool CanCheckForUpdates => _updates is { IsInstalled: true };

    public bool IsCheckingForUpdates { get => _isCheckingForUpdates; private set => SetProperty(ref _isCheckingForUpdates, value); }

    public string? UpdateStatus
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool HasUpdateAvailable => _availableUpdate is not null;

    public IAsyncRelayCommand CheckForUpdatesCommand { get; }
    public IAsyncRelayCommand InstallUpdateCommand { get; }

    /// <summary>
    /// File &gt; Options &gt; SVG editor: what "Draw your own..."/"Edit drawing..." opens sticker
    /// art in - never guessed from the OS's file association, which is often just a viewer
    /// (see <see cref="Editors.IArtEditing"/>). Null until set; the character editor asks for
    /// one itself the first time it's needed, through the same picker.
    /// </summary>
    public string? SvgEditorPath => _settings.SvgEditorPath;

    public IAsyncRelayCommand ChooseSvgEditorCommand { get; }

    private async Task ChooseSvgEditorAsync()
    {
        if (await _dialogs.PickSvgEditorAsync(_settings.SvgEditorPath) is not { Length: > 0 } chosen)
            return;
        _settings.SvgEditorPath = chosen;
        AppLog.Info($"SVG editor set to {chosen}.");
        OnPropertyChanged(nameof(SvgEditorPath));
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_updates is null || _isCheckingForUpdates)
            return;

        IsCheckingForUpdates = true;
        UpdateStatus = null;
        try
        {
            _availableUpdate = await _updates.CheckForUpdatesAsync();
            OnPropertyChanged(nameof(HasUpdateAvailable));
            InstallUpdateCommand.NotifyCanExecuteChanged();
            UpdateStatus = _availableUpdate is null ? "Stanley is up to date." : $"Version {_availableUpdate.Version} is available.";
            AppLog.Info(_availableUpdate is null ? "Checked for updates: up to date." : $"Checked for updates: {_availableUpdate.Version} available.");
        }
        catch (Exception e)
        {
            AppLog.Error("Checking for updates failed.", e);
            UpdateStatus = $"Couldn't check for updates: {e.Message}";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>
    /// The startup check, unlike a manual one from File &gt; Options: finding an update pops
    /// up an offer to install right away, rather than leaving it for the user to notice on
    /// the Options page. Declining leaves it available there for later.
    /// </summary>
    private async Task CheckForUpdatesOnStartupAsync()
    {
        await CheckForUpdatesAsync();
        if (_availableUpdate is { } update && await _dialogs.AskInstallUpdateAsync(update.Version, update.Notes))
            await InstallUpdateAsync();
    }

    /// <summary>Goes through the same Save / Don't Save / Cancel gate as Close - never restarts out from under unsaved work.</summary>
    private async Task InstallUpdateAsync()
    {
        if (_updates is null || !HasUpdateAvailable || !await ConfirmDiscardAsync())
            return;

        UpdateStatus = "Downloading the update...";
        try
        {
            await _updates.DownloadAndApplyAsync();
        }
        catch (Exception e)
        {
            AppLog.Error("Installing the update failed.", e);
            UpdateStatus = $"Couldn't install the update: {e.Message}";
        }
    }

    public string WindowTitle => HasDocument ? $"{DocumentTitle}{IssueSuffix}{(IsDirty ? " •" : "")} - Stanley" : "Stanley";

    /// <summary>The title bar caption, Word-style: the name, and whether it's saved.</summary>
    public string DocumentCaption => !HasDocument ? "Stanley"
        : _project!.IsUntitled ? $"{DocumentTitle}{IssueSuffix} - not saved yet"
        : IsDirty ? $"{DocumentTitle}{IssueSuffix} - unsaved changes"
        : $"{DocumentTitle}{IssueSuffix} - saved";

    /// <summary>" - Issue N" once the comic has more than one issue, so the title bar and File &gt; Info's caption always say which one's open.</summary>
    private string IssueSuffix => HasMultipleIssues ? $" - Issue {IssueNumber}" : "";

    /// <summary>The comic's title, editable from File &gt; Info. Saved with the project; texts show it wherever they have <c>{title}</c>.</summary>
    public string DocumentTitle
    {
        get => _project?.Title ?? "";
        set
        {
            if (_project is null || value == _project.Title || string.IsNullOrWhiteSpace(value))
                return;
            _project.Title = value.Trim();
            InfoChanged();
        }
    }

    /// <summary>The issue's number, editable from File &gt; Info - free text ("1", "0", "1.5"). Saved with the issue; texts show it wherever they have <c>{issue}</c>.</summary>
    public string IssueNumber
    {
        get => _project?.IssueNumber ?? "";
        set
        {
            if (_project is null || value.Trim() == _project.IssueNumber || string.IsNullOrWhiteSpace(value))
                return;
            _project.IssueNumber = value.Trim();
            InfoChanged();
        }
    }

    /// <summary>The issue's title, editable from File &gt; Info next to its number - e.g. "Annual" or "The Long Way Home". Saved with the issue, separately from the comic's own <see cref="DocumentTitle"/>.</summary>
    public string IssueTitle
    {
        get => _project?.IssueTitle ?? "";
        set
        {
            if (_project is null || value.Trim() == _project.IssueTitle)
                return;
            _project.IssueTitle = value.Trim();
            InfoChanged();
        }
    }

    // ---------------------------------------------------------------- issues

    /// <summary>
    /// The comic's issues in storage order (File &gt; Info's Issues section, and the quick
    /// switcher), current one marked - just the one issue for a comic that's never been saved.
    /// </summary>
    public IReadOnlyList<IssueEntry> Issues =>
        _project is null ? [] : _project.Issues.Select(i => new IssueEntry(i.Id, i.Number, i.Title, i.PageCount, i.Id == _project.IssueId)).ToList();

    public bool HasMultipleIssues => Issues.Count > 1;

    /// <summary>
    /// The quick switcher's selection. Picking another issue kicks off <see cref="SwitchIssueAsync"/>
    /// in the background (like a menu command); the getter always reflects which issue is
    /// actually open, so the switcher snaps back if a switch is cancelled.
    /// </summary>
    public IssueEntry? SelectedIssue
    {
        get => Issues.FirstOrDefault(i => i.IsCurrent);
        set
        {
            if (value is null || value.IsCurrent)
                return;
            _ = SwitchIssueAsync(value.Id);
        }
    }

    public IAsyncRelayCommand<IssueId> SwitchIssueCommand { get; }
    public IAsyncRelayCommand NewIssueCommand { get; }

    /// <summary>
    /// Switches to another of the comic's issues: the same Save / Don't Save / Cancel gate as
    /// opening a different comic, then loads it, staying on the editor. Opening the current
    /// issue again is a no-op.
    /// </summary>
    public async Task SwitchIssueAsync(IssueId issueId)
    {
        if (_project is null || issueId == _project.IssueId)
        {
            IsBackstageOpen = false; // already open - Word just switches to it
            return;
        }

        if (_project.Location is { } folder && await ConfirmDiscardAsync())
        {
            try
            {
                var project = ComicProject.Open(folder, issueId);
                Load(project);
                AppLog.Info($"Switched to issue #{project.IssueNumber} of \"{project.Title}\".");
            }
            catch (Exception e) when (IsFileProblem(e))
            {
                AppLog.Error($"Couldn't switch to issue {issueId} of \"{DocumentTitle}\".", e);
                ShowError($"Couldn't switch issues: {e.Message}");
            }
        }
        OnPropertyChanged(nameof(SelectedIssue)); // snaps the switcher back to the issue that's actually open
    }

    /// <summary>
    /// File &gt; Info's "New issue": an untitled comic is saved first (like Word's first
    /// Save), then the current issue is saved (the same gate as switching), a new issue -
    /// one blank page, numbered one past the highest so far - is added on disk and opened.
    /// </summary>
    public async Task NewIssueAsync()
    {
        if (_project is null)
            return;

        if (_project.IsUntitled && !await SaveAsAsync())
        {
            Message = "Save the comic before adding an issue to it.";
            return;
        }
        if (!await ConfirmDiscardAsync())
            return;

        var folder = _project.Location!;
        try
        {
            var newIssueId = _project.NewIssue();
            var project = ComicProject.Open(folder, newIssueId);
            Load(project);
            Message = $"Issue #{project.IssueNumber} added";
            AppLog.Info($"Added issue #{project.IssueNumber} to \"{project.Title}\".");
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            AppLog.Error($"Couldn't add a new issue to \"{DocumentTitle}\".", e);
            ShowError($"Couldn't add a new issue: {e.Message}");
        }
    }

    /// <summary>A File › Info edit: not an undo step (like Word's document properties), but unsaved work - and the pages' fields show it straight away.</summary>
    private void InfoChanged()
    {
        _infoDirty = true;
        RefreshFields();
        RaiseDocumentChanged();
        ScheduleBackgroundSaves();
    }

    private void RefreshFields()
    {
        if (_navigator != null && _project != null)
            _navigator.Fields = _project.Fields;
    }

    public string LocationText => _project?.Location ?? "Not saved yet - Save picks a folder for it.";

    /// <summary>File › Info's page size: the paper or template it matches, the size, the bleed and - for a webcomic - the size its pictures export at.</summary>
    public string PageSizeText
    {
        get
        {
            if (_project is null)
                return "";
            var size = _project.Trim.Size;
            var paper = Enum.GetValues<MetricPaperSize>().Cast<MetricPaperSize?>()
                .FirstOrDefault(p => MetricPaperSizes.Size(p!.Value) == size);
            var name = paper?.ToString() ?? ComicTemplates.Matching(size)?.Name;
            var bleed = _project.Trim.BleedMm > 0 ? $"{_project.Trim.BleedMm:0.#} mm bleed" : "no bleed";
            var pixels = _project.ExportWidthPx is { } width ? $", exported at {width} × {ComicTemplates.ExportHeightPx(width, size)} px" : "";
            return $"{(name is null ? "" : name + " · ")}{size.WidthMm:0.#} × {size.HeightMm:0.#} mm, {bleed}{pixels}";
        }
    }

    /// <summary>File › Export's PNG tile: print resolution, or a webcomic's own picture size.</summary>
    public string PngExportText =>
        _project?.ExportWidthPx is { } width
            ? $"The current page at {width} × {ComicTemplates.ExportHeightPx(width, _project.Trim.Size)} px, ready to post online."
            : "The current page at 300 dpi. For the web and social media.";

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
        get;
        private set => SetProperty(ref field, value);
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
        get;
        set
        {
            SetProperty(ref field, value);
            OnPropertyChanged(nameof(IsNewPage));
            OnPropertyChanged(nameof(IsOpenPage));
            OnPropertyChanged(nameof(IsInfoPage));
            OnPropertyChanged(nameof(IsSaveAsPage));
            OnPropertyChanged(nameof(IsExportPage));
            OnPropertyChanged(nameof(IsOptionsPage));
        }
    } = BackstagePage.New;

    public bool IsNewPage { get => BackstagePage == BackstagePage.New; set => SetPageFlag(BackstagePage.New, value); }
    public bool IsOpenPage { get => BackstagePage == BackstagePage.Open; set => SetPageFlag(BackstagePage.Open, value); }
    public bool IsInfoPage { get => BackstagePage == BackstagePage.Info; set => SetPageFlag(BackstagePage.Info, value); }
    public bool IsSaveAsPage { get => BackstagePage == BackstagePage.SaveAs; set => SetPageFlag(BackstagePage.SaveAs, value); }
    public bool IsExportPage { get => BackstagePage == BackstagePage.Export; set => SetPageFlag(BackstagePage.Export, value); }
    public bool IsOptionsPage { get => BackstagePage == BackstagePage.Options; set => SetPageFlag(BackstagePage.Options, value); }

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

    /// <summary>File › New › a comic strip or webcomic template.</summary>
    public IAsyncRelayCommand<ComicTemplate?> NewFromTemplateCommand { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand<string> OpenRecentCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand SaveAsCommand { get; }
    public IAsyncRelayCommand CloseDocumentCommand { get; }
    public IAsyncRelayCommand ExportPdfCommand { get; }
    public IAsyncRelayCommand ExportPngCommand { get; }

    /// <summary>The export notice's Open: the exported file, in whatever the computer opens that kind of file with.</summary>
    public IAsyncRelayCommand OpenExportCommand { get; }

    /// <summary>The export notice's Show in folder: the file manager, on the folder it went to.</summary>
    public IAsyncRelayCommand ShowExportInFolderCommand { get; }

    public IRelayCommand DismissExportNoticeCommand { get; }
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

    /// <summary>File &gt; New from a strip or webcomic template: its page size, panels and spacing, and for a webcomic its export size.</summary>
    public async Task NewFromTemplateAsync(ComicTemplate? template)
    {
        if (template is null || !await ConfirmDiscardAsync())
            return;
        Load(ComicProject.CreateNew(template));
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
            AppLog.Info($"Opened \"{project.Title}\" from {project.Location} ({project.Pages.Count} pages).");
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            AppLog.Error($"Couldn't open {folder}.", e);
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
            _project.Save(_navigator.Snapshot(), _navigator.PageNumbering, _characters?.Snapshot(), _navigator.IssueLooks, _pictures?.Files);
            AppLog.Info($"Saved \"{DocumentTitle}\" to {_project.Location}.");
            MarkSaved();
            return true;
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            AppLog.Error($"Saving \"{DocumentTitle}\" to {_project.Location} failed.", e);
            ShowError($"Couldn't save: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// File › Save As (and a new comic's first Save): the system's Save dialog, with a box for
    /// the comic's name and a place to put it. The comic is saved as a folder of that name
    /// there ("name (2)" if the name is taken), and a comic's first save names it, as it
    /// does a document in Word. Returns whether it was saved.
    /// </summary>
    public async Task<bool> SaveAsAsync()
    {
        if (_project is null || _navigator is null)
            return false;

        var chosen = await _dialogs.PickSaveLocationAsync("Save the comic", DocumentTitle);
        if (chosen is null)
            return false;
        if (_project.Location is { } current && PathsEqual(current, chosen))
            return await SaveAsync(); // its own folder, under its own name: nothing to copy

        var title = _project.Title;
        try
        {
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chosen));
            if (_project.IsUntitled && Path.GetFileName(path).Trim() is { Length: > 0 } name)
                _project.Title = name;
            var saved = _project.SaveAs(ComicProject.FreeFolder(path), _navigator.Snapshot(), _navigator.PageNumbering, _characters?.Snapshot(), _navigator.IssueLooks,
                _pictures?.Files);
            AppLog.Info($"Saved \"{DocumentTitle}\" as {saved}.");
            RefreshFields(); // a new comic took the name it was saved under
            MarkSaved();
            Message = $"Saved to {saved}";
            OnPropertyChanged(nameof(AutoSaveEnabled)); // no longer untitled - AutoSave can apply
            OnPropertyChanged(nameof(AutoSaveTip));
            return true;
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            _project.Title = title;
            AppLog.Error($"Save As of \"{DocumentTitle}\" to {chosen} failed.", e);
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
                ComicProject.ExportPdf(path, _navigator.Pages.Select(p => (p.Editor.PageBounds, p.Editor.Committed, p.Editor.Folio)), CommittedCharacters(), _navigator.IssueLooks,
                    _pictures?.Files, _project.Fields);
            else
                ComicProject.ExportPng(path, current.Editor.PageBounds, current.Editor.Committed, folio: current.Editor.Folio, characters: CommittedCharacters(),
                    issueLooks: _navigator.IssueLooks, pictures: _pictures?.Files, widthPx: _project.ExportWidthPx, fields: _project.Fields);
            Message = null;
            AppLog.Info($"Exported \"{DocumentTitle}\" as {format.ToUpperInvariant()} to {path}.");
            IsBackstageOpen = false;
            ShowExportNotice(path);
        }
        catch (Exception e) when (IsFileProblem(e))
        {
            AppLog.Error($"Export to {path} failed.", e);
            ShowError($"Couldn't export: {e.Message}");
        }
    }

    // ---------------------------------------------------------------- after exporting

    /// <summary>
    /// The file File › Export just wrote, while the notice about it is up - "Exported
    /// comic.pdf" with Open and Show in folder, as a browser or Office offers after saving
    /// something - or null. It goes after <see cref="ExportNoticeDuration"/>, or when closed.
    /// </summary>
    public string? ExportedPath
    {
        get;
        private set
        {
            if (!SetProperty(ref field, value))
                return;
            OnPropertyChanged(nameof(HasExportNotice));
            OnPropertyChanged(nameof(ExportNoticeText));
            OpenExportCommand.NotifyCanExecuteChanged();
            ShowExportInFolderCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasExportNotice => ExportedPath is not null;

    public string ExportNoticeText => ExportedPath is { } path ? $"Exported {Path.GetFileName(path)}" : "";

    /// <summary>Whether the notice can offer Open and Show in folder - not without a way to launch things (tests).</summary>
    public bool CanLaunchExport => ExportedPath is not null && _launcher is not null;

    private void ShowExportNotice(string path)
    {
        _pendingNoticeHide?.Dispose();
        ExportedPath = path;
        _pendingNoticeHide = _scheduler?.Schedule(ExportNoticeDuration, DismissExportNotice);
    }

    public void DismissExportNotice()
    {
        _pendingNoticeHide?.Dispose();
        _pendingNoticeHide = null;
        ExportedPath = null;
    }

    private async Task LaunchExportAsync(bool open)
    {
        if (ExportedPath is not { } path || _launcher is null)
            return;
        if (!File.Exists(path))
        {
            Message = $"\"{Path.GetFileName(path)}\" isn't there any more - it may have been moved or deleted.";
            DismissExportNotice();
            return;
        }
        var launched = open ? await _launcher.OpenAsync(path) : await _launcher.ShowInFolderAsync(path);
        if (!launched)
            Message = open
                ? $"Couldn't open \"{Path.GetFileName(path)}\" - there may be no app set up to open {Path.GetExtension(path).TrimStart('.').ToUpperInvariant()} files."
                : $"Couldn't show the folder \"{Path.GetFileName(path)}\" is in.";
    }

    /// <summary>Before discarding the open comic: nothing to ask if it's clean; otherwise Save / Don't Save / Cancel. Returns whether it's OK to go ahead.</summary>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!IsDirty)
            return true;

        // With AutoSave on there's nothing to ask - just save, as Word does.
        if (AutoSaveEnabled)
        {
            AutoSaveNow();
            if (!IsDirty)
                return true;
        }

        return await _dialogs.AskSaveChangesAsync(DocumentTitle) switch
        {
            SaveChangesChoice.Save => await SaveAsync(),
            SaveChangesChoice.DontSave => true,
            _ => false
        };
    }

    // ---------------------------------------------------------------- helpers

    private IReadOnlyDictionary<ProjectModel.Ids.CharacterId, ProjectModel.Characters.CharacterDefinition>? CommittedCharacters() =>
        _characters?.Snapshot().ToDictionary(c => c.Id);

    private void Load(ComicProject project)
    {
        Unload();
        AppLog.Info($"Loaded \"{project.Title}\" ({(project.IsUntitled ? "new, unsaved" : project.Location)}).");
        _project = project;
        (_workspace, _navigator, _characters, _pictures) = PageEditorHost.CreateWorkspace(project);
        _characters.ArtEditing = new SystemArtEditing(AppPaths.ArtEditingDirectory, () => _settings.SvgEditorPath, path =>
        {
            _settings.SvgEditorPath = path;
            OnPropertyChanged(nameof(SvgEditorPath));
        });
        _workspace.History.PropertyChanged += OnHistoryChanged;
        _navigator.CurrentPageChanged += OnCurrentPageChanged;
        _navigator.SpacingChanged += OnSpacingChanged;
        _infoDirty = false;
        Message = null;
        SetProperty(ref _isBackstageOpen, false, nameof(IsBackstageOpen));
        RaiseDocumentChanged();
        OnPropertyChanged(nameof(Workspace));
        OnPropertyChanged(nameof(Editor));
        OnPropertyChanged(nameof(Navigator));
        OnPropertyChanged(nameof(Characters));
        OnPropertyChanged(nameof(Project));
    }

    private void Unload()
    {
        if (_workspace != null)
            _workspace.History.PropertyChanged -= OnHistoryChanged;
        if (_navigator != null)
        {
            _navigator.CurrentPageChanged -= OnCurrentPageChanged;
            _navigator.SpacingChanged -= OnSpacingChanged;
        }
        _project = null;
        _workspace = null;
        _navigator = null;
        _characters = null;
        _pictures = null;
        _infoDirty = false;
        _recoveredUnsaved = false;
        _pendingAutoSave?.Dispose();
        _pendingAutoSave = null;
        _pendingRecovery?.Dispose();
        _pendingRecovery = null;
        DismissExportNotice();
        _recovery?.Clear(); // the comic is being put down on purpose - saved or deliberately discarded
        RaiseDocumentChanged();
        OnPropertyChanged(nameof(Workspace));
        OnPropertyChanged(nameof(Editor));
        OnPropertyChanged(nameof(Navigator));
        OnPropertyChanged(nameof(Characters));
        OnPropertyChanged(nameof(Project));
    }

    private void MarkSaved()
    {
        _workspace?.History.MarkSaved();
        _infoDirty = false;
        _recoveredUnsaved = false;
        _recovery?.Clear();
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
            ScheduleBackgroundSaves(); // every edit, undo and redo passes through here
        if (e.PropertyName is nameof(EditorHistory.CanUndo))
            OnPropertyChanged(nameof(PanelCountText)); // every edit, undo and redo passes through here
        if (e.PropertyName is nameof(EditorHistory.CanUndo) or nameof(EditorHistory.CanRedo))
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnCurrentPageChanged(PageItem page) => OnPropertyChanged(nameof(Editor));

    /// <summary>The Layout tab's margin and gutter are the comic's, saved with it (an undoable edit, so the history already says it's unsaved).</summary>
    private void OnSpacingChanged()
    {
        if (_project != null && _navigator != null)
            _project.Grid = _navigator.Spacing;
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private void RaiseDocumentChanged()
    {
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(DocumentCaption));
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(IssueNumber));
        OnPropertyChanged(nameof(IssueTitle));
        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(HasMultipleIssues));
        OnPropertyChanged(nameof(SelectedIssue));
        OnPropertyChanged(nameof(LocationText));
        OnPropertyChanged(nameof(PageSizeText));
        OnPropertyChanged(nameof(PngExportText));
        OnPropertyChanged(nameof(PanelCountText));
        OnPropertyChanged(nameof(AutoSaveEnabled));
        OnPropertyChanged(nameof(AutoSaveTip));
        CloseBackstageCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        CloseDocumentCommand.NotifyCanExecuteChanged();
        SwitchIssueCommand.NotifyCanExecuteChanged();
        NewIssueCommand.NotifyCanExecuteChanged();
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
