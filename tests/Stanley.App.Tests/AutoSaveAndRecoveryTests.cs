using Stanley.App.Diagnostics;
using Stanley.App.Documents;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;

namespace Stanley.App.Tests;

/// <summary>Collects scheduled actions instead of running them on a timer, so a test decides when "later" is.</summary>
public sealed class ManualScheduler : IDelayScheduler
{
    private readonly List<(TimeSpan Delay, Action Action, Cancel Handle)> _pending = [];

    public int PendingCount => _pending.Count(p => !p.Handle.Cancelled);

    public IDisposable Schedule(TimeSpan delay, Action action)
    {
        var handle = new Cancel();
        _pending.Add((delay, action, handle));
        return handle;
    }

    /// <summary>Runs everything scheduled with at most <paramref name="elapsed"/> delay that hasn't been cancelled.</summary>
    public void Advance(TimeSpan elapsed)
    {
        var due = _pending.Where(p => p.Delay <= elapsed && !p.Handle.Cancelled).ToList();
        _pending.RemoveAll(p => due.Contains(p) || p.Handle.Cancelled);
        foreach (var (_, action, _) in due)
            action();
    }

    public sealed class Cancel : IDisposable
    {
        public bool Cancelled { get; private set; }
        public void Dispose() => Cancelled = true;
    }
}

public sealed class AutoSaveAndRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-autosave-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeFileDialogs _dialogs = new();
    private readonly ManualScheduler _scheduler = new();

    public AutoSaveAndRecoveryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string RecoveryRoot => Path.Combine(_root, "Recovery");

    private MainWindowViewModel NewViewModel(AppSettings? settings = null, RecoveryStore? recovery = null) =>
        new(_dialogs, new RecentProjects(null), settings: settings ?? new AppSettings(null), recovery: recovery, scheduler: _scheduler);

    private static void AddBubble(MainWindowViewModel vm, string text = "")
    {
        var editor = vm.Editor!;
        var panel = editor.Working.PanelOrder[0];
        var index = editor.CreateBubble(panel, new Point2D(50, 50));
        if (text.Length > 0)
            editor.SetBubbleText(panel, index, text);
    }

    private async Task<(MainWindowViewModel Vm, string Folder)> SavedComicAsync(AppSettings? settings = null, RecoveryStore? recovery = null)
    {
        var vm = NewViewModel(settings, recovery);
        var folder = Path.Combine(_root, "Comic");
        _dialogs.Folders.Enqueue(folder);
        Assert.True(await vm.SaveAsync());
        return (vm, folder);
    }

    // ---------------------------------------------------------------- AutoSave

    [Fact]
    public async Task AutoSave_SavesAMomentAfterTheLastChange_NotBefore()
    {
        var (vm, folder) = await SavedComicAsync();
        Assert.True(vm.AutoSaveEnabled);

        AddBubble(vm, "Hi");
        Assert.True(vm.IsDirty);
        _scheduler.Advance(TimeSpan.FromSeconds(1));
        Assert.True(vm.IsDirty); // not yet

        _scheduler.Advance(MainWindowViewModel.AutoSaveDelay);
        Assert.False(vm.IsDirty);
        Assert.Contains(ComicProject.Open(folder).Pages[0].Document.Panels.Values.SelectMany(p => p.Bubbles), b => b.Text == "Hi");
    }

    [Fact]
    public async Task AutoSave_Off_LeavesChangesUnsaved()
    {
        var (vm, _) = await SavedComicAsync();
        vm.AutoSaveEnabled = false;

        AddBubble(vm);
        _scheduler.Advance(TimeSpan.FromMinutes(1));

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task AutoSave_RepeatedEdits_RestartTheWait()
    {
        var (vm, _) = await SavedComicAsync();
        AddBubble(vm);
        AddBubble(vm);
        AddBubble(vm);

        // One AutoSave (each edit cancelled the previous one) plus one recovery snapshot (throttled, not restarted).
        Assert.Equal(2, _scheduler.PendingCount);
    }

    [Fact]
    public async Task AutoSave_On_ClosingDoesntAsk_ItJustSaves()
    {
        var (vm, folder) = await SavedComicAsync();
        AddBubble(vm, "Keep me");

        await vm.CloseDocumentAsync();

        Assert.Equal(0, _dialogs.SaveChangesPrompts);
        Assert.False(vm.HasDocument);
        Assert.Contains(ComicProject.Open(folder).Pages[0].Document.Panels.Values.SelectMany(p => p.Bubbles), b => b.Text == "Keep me");
    }

    [Fact]
    public async Task AutoSave_SwitchedOnForAnUntitledComic_AsksWhereToSaveFirst()
    {
        var settings = new AppSettings(null) { AutoSave = false };
        var vm = NewViewModel(settings);
        Assert.False(vm.AutoSaveEnabled);

        vm.AutoSaveEnabled = true; // cancelled folder picker
        await Task.Yield();
        Assert.False(vm.AutoSaveEnabled);
        Assert.True(vm.Project!.IsUntitled);

        _dialogs.Folders.Enqueue(Path.Combine(_root, "Now saved"));
        vm.AutoSaveEnabled = true;
        await Task.Yield();
        Assert.False(vm.Project!.IsUntitled);
        Assert.True(vm.AutoSaveEnabled);
        Assert.True(settings.AutoSave);
    }

    [Fact]
    public void Settings_AutoSave_DefaultsOnAndPersists()
    {
        var path = Path.Combine(_root, "settings.txt");
        Assert.True(new AppSettings(path).AutoSave);

        var settings = new AppSettings(path);
        settings.AutoSave = false;

        Assert.False(new AppSettings(path).AutoSave);
    }

    [Fact]
    public void Settings_ShowLayers_DefaultsOnAndRemembersBeingSwitchedOff()
    {
        var path = Path.Combine(_root, "settings.txt");
        Assert.True(new AppSettings(path).ShowLayers);

        _ = new AppSettings(path) { ShowLayers = false };
        Assert.False(new AppSettings(path).ShowLayers);

        _ = new AppSettings(path) { ShowLayers = true };
        Assert.True(new AppSettings(path).ShowLayers);
    }

    [Fact]
    public void Settings_Theme_DefaultsToSystemAndPersists()
    {
        var path = Path.Combine(_root, "settings.txt");
        Assert.Equal(AppTheme.System, new AppSettings(path).Theme);

        var vm = NewViewModel(new AppSettings(path));
        vm.IsDarkTheme = true;

        Assert.Equal(AppTheme.Dark, vm.Theme);
        Assert.False(vm.IsSystemTheme);
        Assert.Equal(AppTheme.Dark, new AppSettings(path).Theme);
    }

    // ---------------------------------------------------------------- crash recovery

    [Fact]
    public async Task Crash_ThenRestart_OffersTheUnsavedWork_AndOpeningItSavesBackToTheOriginalFolder()
    {
        var crashedStore = new RecoveryStore(RecoveryRoot);
        var (crashed, folder) = await SavedComicAsync(new AppSettings(null) { AutoSave = false }, crashedStore);
        AddBubble(crashed, "Don't lose me");
        _scheduler.Advance(MainWindowViewModel.RecoveryDelay);
        Assert.True(crashedStore.HasSnapshot);
        crashedStore.AbandonForTests(); // the process dies: lock released, folder left behind

        var restarted = NewViewModel(recovery: new RecoveryStore(RecoveryRoot));

        var recovered = Assert.Single(restarted.RecoveredEntries);
        Assert.Equal("Comic", recovered.Title);
        Assert.Equal(folder, recovered.OriginalLocation);
        Assert.True(restarted.IsBackstageOpen);
        Assert.Equal(BackstagePage.Open, restarted.BackstagePage);

        await restarted.OpenRecoveredAsync(recovered);
        Assert.Empty(restarted.RecoveredEntries);
        Assert.True(restarted.IsDirty);
        Assert.Equal(folder, restarted.Project!.Location);
        Assert.Contains(restarted.Editor!.Working.Panels.Values.SelectMany(p => p.Bubbles), b => b.Text == "Don't lose me");

        Assert.True(await restarted.SaveAsync());
        Assert.Contains(ComicProject.Open(folder).Pages[0].Document.Panels.Values.SelectMany(p => p.Bubbles), b => b.Text == "Don't lose me");
    }

    [Fact]
    public void RecoveryStore_IgnoresLiveSessions_AndACleanExitLeavesNothing()
    {
        var running = new RecoveryStore(RecoveryRoot);
        running.StartSession();
        var project = ComicProject.CreateNew();
        var navigator = new PageNavigatorViewModel(new EditorFramework.EditorHistory(), project.Pages);
        running.Write(project, navigator.Snapshot(), navigator.PageNumbering);

        var other = new RecoveryStore(RecoveryRoot);
        other.StartSession();
        Assert.Empty(other.FindAbandoned()); // still running - its lock is held

        running.Dispose();
        other.Dispose();
        Assert.Empty(Directory.EnumerateDirectories(RecoveryRoot));
    }

    [Fact]
    public async Task Saving_ClearsTheRecoverySnapshot_AndDiscardRemovesAbandonedWork()
    {
        var store = new RecoveryStore(RecoveryRoot);
        var (vm, _) = await SavedComicAsync(new AppSettings(null) { AutoSave = false }, store);
        AddBubble(vm);
        vm.WriteRecoverySnapshot();
        Assert.True(store.HasSnapshot);

        await vm.SaveAsync();
        Assert.False(store.HasSnapshot);

        AddBubble(vm);
        vm.WriteRecoverySnapshot();
        store.AbandonForTests();
        var restarted = NewViewModel(recovery: new RecoveryStore(RecoveryRoot));
        restarted.DiscardRecoveredCommand.Execute(Assert.Single(restarted.RecoveredEntries));
        Assert.Empty(restarted.RecoveredEntries);
        Assert.Empty(new RecoveryStore(RecoveryRoot).FindAbandoned());
    }

    // ---------------------------------------------------------------- logging

    [Fact]
    public async Task FileOperations_AreLogged()
    {
        var logs = Path.Combine(_root, "Logs");
        AppLog.Initialize(logs);
        try
        {
            await SavedComicAsync();
            var text = File.ReadAllText(AppLog.CurrentFile!);
            Assert.Contains("INFO  Saved \"Comic\" as", text, StringComparison.Ordinal);

            AppLog.Error("Something broke.", new InvalidOperationException("boom"));
            text = File.ReadAllText(AppLog.CurrentFile!);
            Assert.Contains("ERROR Something broke.", text, StringComparison.Ordinal);
            Assert.Contains("System.InvalidOperationException: boom", text, StringComparison.Ordinal);
        }
        finally
        {
            AppLog.Disable();
        }
    }
}
