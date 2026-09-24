using Stanley.App.Documents;
using Stanley.App.Updates;
using Stanley.ProjectModel.Geometry;

namespace Stanley.App.Tests;

/// <summary>Scripted results instead of a real Velopack install, so update-check flows are testable without network or a packaged build.</summary>
public sealed class FakeUpdateService : IUpdateService
{
    public bool IsInstalled { get; set; } = true;
    public AvailableUpdate? NextResult { get; set; }
    public int CheckCount { get; private set; }
    public int InstallCount { get; private set; }

    public Task<AvailableUpdate?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        CheckCount++;
        return Task.FromResult(NextResult);
    }

    public Task DownloadAndApplyAsync(CancellationToken cancellationToken = default)
    {
        InstallCount++;
        return Task.CompletedTask;
    }
}

public sealed class UpdatesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-updates-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeFileDialogs _dialogs = new();
    private readonly ManualScheduler _scheduler = new();

    public UpdatesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private MainWindowViewModel NewViewModel(FakeUpdateService? updates, AppSettings? settings = null, GithubTokenStore? tokenStore = null) =>
        new(_dialogs, new RecentProjects(null),
            settings: settings ?? new AppSettings(null),
            tokenStore: tokenStore ?? new GithubTokenStore(null),
            updates: updates,
            scheduler: _scheduler);

    // ---------------------------------------------------------------- GithubTokenStore

    [Fact]
    public void GithubTokenStore_PersistsAcrossInstances()
    {
        var path = Path.Combine(_root, "token.txt");
        Assert.Null(new GithubTokenStore(path).Token);

        _ = new GithubTokenStore(path) { Token = " ghp_abc123 " };

        Assert.Equal("ghp_abc123", new GithubTokenStore(path).Token);
    }

    [Fact]
    public void GithubTokenStore_SettingBlankClearsTheFile()
    {
        var path = Path.Combine(_root, "token.txt");
        _ = new GithubTokenStore(path) { Token = "ghp_abc123" };
        Assert.True(File.Exists(path));

        _ = new GithubTokenStore(path) { Token = "  " };

        Assert.False(File.Exists(path));
        Assert.Null(new GithubTokenStore(path).Token);
    }

    [Fact]
    public void GithubTokenStore_WithNoPath_KeepsTheTokenInMemoryOnly()
    {
        var store = new GithubTokenStore(null) { Token = "ghp_abc123" };
        Assert.Equal("ghp_abc123", store.Token);
    }

    // ---------------------------------------------------------------- AppSettings

    [Fact]
    public void Settings_AutoCheckForUpdates_DefaultsOffAndPersists()
    {
        var path = Path.Combine(_root, "settings.txt");
        Assert.False(new AppSettings(path).AutoCheckForUpdates);

        _ = new AppSettings(path) { AutoCheckForUpdates = true };

        Assert.True(new AppSettings(path).AutoCheckForUpdates);
    }

    [Fact]
    public void Settings_UpdateChannel_DefaultsToStableAndPersists()
    {
        var path = Path.Combine(_root, "settings.txt");
        Assert.Equal(AppUpdateChannel.Stable, new AppSettings(path).UpdateChannel);

        _ = new AppSettings(path) { UpdateChannel = AppUpdateChannel.Nightly };

        Assert.Equal(AppUpdateChannel.Nightly, new AppSettings(path).UpdateChannel);
    }

    // ---------------------------------------------------------------- channel naming

    [Fact]
    public void ResolveChannel_NightlyIsTheStableChannelWithASuffix_SoTheyNeverCrossUpdate()
    {
        var stable = VelopackUpdateService.ResolveChannel(AppUpdateChannel.Stable);
        var nightly = VelopackUpdateService.ResolveChannel(AppUpdateChannel.Nightly);

        Assert.Equal($"{stable}-nightly", nightly);
    }

    // ---------------------------------------------------------------- MainWindowViewModel wiring

    [Fact]
    public void CanCheckForUpdates_NeedsBothAnInstalledServiceAndAToken()
    {
        var vm = NewViewModel(new FakeUpdateService { IsInstalled = true });
        Assert.False(vm.CanCheckForUpdates); // no token yet

        vm.GithubToken = "ghp_abc123";
        Assert.True(vm.CanCheckForUpdates);
    }

    [Fact]
    public void CanCheckForUpdates_FalseWithoutAnUpdateService()
    {
        var vm = NewViewModel(updates: null);
        vm.GithubToken = "ghp_abc123";
        Assert.False(vm.CanCheckForUpdates);
    }

    [Fact]
    public async Task CheckForUpdatesCommand_FindsNothing_ReportsUpToDate()
    {
        var updates = new FakeUpdateService();
        var vm = NewViewModel(updates);
        vm.GithubToken = "ghp_abc123";

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal(1, updates.CheckCount);
        Assert.False(vm.HasUpdateAvailable);
        Assert.Equal("Stanley is up to date.", vm.UpdateStatus);
    }

    [Fact]
    public async Task CheckForUpdatesCommand_FindsOne_OffersToInstall()
    {
        var updates = new FakeUpdateService { NextResult = new AvailableUpdate("0.9.0", "Notes") };
        var vm = NewViewModel(updates);
        vm.GithubToken = "ghp_abc123";

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.True(vm.HasUpdateAvailable);
        Assert.Contains("0.9.0", vm.UpdateStatus);
        Assert.True(vm.InstallUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task InstallUpdateCommand_AsksToSaveFirst_AndCancellingSkipsTheInstall()
    {
        var updates = new FakeUpdateService { NextResult = new AvailableUpdate("0.9.0", null) };
        var vm = NewViewModel(updates);
        vm.GithubToken = "ghp_abc123";
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        var editor = vm.Editor!;
        editor.CreateBubble(editor.Working.PanelOrder[0], new Point2D(50, 50));
        Assert.True(vm.IsDirty);
        _dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.Cancel);

        await vm.InstallUpdateCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogs.SaveChangesPrompts);
        Assert.Equal(0, updates.InstallCount);
    }

    [Fact]
    public async Task InstallUpdateCommand_NoUnsavedChanges_InstallsDirectly()
    {
        var updates = new FakeUpdateService { NextResult = new AvailableUpdate("0.9.0", null) };
        var vm = NewViewModel(updates);
        vm.GithubToken = "ghp_abc123";
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        await vm.InstallUpdateCommand.ExecuteAsync(null);

        Assert.Equal(1, updates.InstallCount);
    }

    [Fact]
    public void AutoCheckForUpdates_OnStartup_SchedulesACheck_IfATokenIsAlreadyConfigured()
    {
        var settings = new AppSettings(null) { AutoCheckForUpdates = true };
        var tokenStore = new GithubTokenStore(null) { Token = "ghp_abc123" };
        var updates = new FakeUpdateService();

        _ = NewViewModel(updates, settings, tokenStore);

        Assert.Equal(1, _scheduler.PendingCount);
        _scheduler.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, updates.CheckCount);
    }

    [Fact]
    public void AutoCheckForUpdates_Off_SchedulesNothing()
    {
        var settings = new AppSettings(null) { AutoCheckForUpdates = false };
        var tokenStore = new GithubTokenStore(null) { Token = "ghp_abc123" };

        _ = NewViewModel(new FakeUpdateService(), settings, tokenStore);

        Assert.Equal(0, _scheduler.PendingCount);
    }

    [Fact]
    public void AutoCheckForUpdates_On_ButNoTokenYet_SchedulesNothing()
    {
        var settings = new AppSettings(null) { AutoCheckForUpdates = true };

        _ = NewViewModel(new FakeUpdateService(), settings);

        Assert.Equal(0, _scheduler.PendingCount);
    }
}
