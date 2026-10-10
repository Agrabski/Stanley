using Stanley.App.Documents;
using Xunit;

namespace Stanley.App.Tests;

/// <summary>File › Options › Layout: the ribbon or the canvas layout, an application-level setting that applies on restart.</summary>
public sealed class InterfaceLayoutSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-layout-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void The_ribbon_is_the_default_and_a_choice_is_remembered()
    {
        var path = Path.Combine(_root, "settings.txt");
        Assert.Equal(InterfaceLayout.Ribbon, new AppSettings(path).InterfaceLayout);

        new AppSettings(path).InterfaceLayout = InterfaceLayout.Canvas;

        Assert.Equal(InterfaceLayout.Canvas, new AppSettings(path).InterfaceLayout);
    }

    [Fact]
    public void An_unknown_value_falls_back_to_the_ribbon()
    {
        var path = Path.Combine(_root, "settings.txt");
        Directory.CreateDirectory(_root);
        File.WriteAllLines(path, ["InterfaceLayout=Holographic"]);

        Assert.Equal(InterfaceLayout.Ribbon, new AppSettings(path).InterfaceLayout);
    }

    [Fact]
    public void Changing_the_layout_in_Options_keeps_this_window_as_it_is_until_a_restart()
    {
        var settings = new AppSettings(null);
        var vm = new MainWindowViewModel(new NoDialogs(), new RecentProjects(null), settings: settings);
        var restarts = 0;
        vm.RestartRequested += (_, _) => restarts++;

        Assert.True(vm.IsRibbonLayout);
        Assert.False(vm.IsLayoutRestartPending);
        Assert.False(vm.RestartCommand.CanExecute(null));

        vm.IsCanvasLayout = true;

        Assert.Equal(InterfaceLayout.Canvas, settings.InterfaceLayout);
        Assert.Equal(InterfaceLayout.Ribbon, vm.ActiveInterfaceLayout);
        Assert.False(vm.IsCanvasActive);
        Assert.True(vm.IsLayoutRestartPending);
        Assert.True(vm.RestartCommand.CanExecute(null));
        vm.RestartCommand.Execute(null);
        Assert.Equal(1, restarts);

        vm.IsRibbonLayout = true; // changed their mind: nothing to restart for
        Assert.False(vm.IsLayoutRestartPending);
    }

    [Fact]
    public void A_window_started_with_the_canvas_layout_stacks_the_side_panes()
    {
        var vm = new MainWindowViewModel(new NoDialogs(), new RecentProjects(null), settings: new AppSettings(null) { InterfaceLayout = InterfaceLayout.Canvas });

        Assert.True(vm.IsCanvasActive);
        Assert.Equal(EditorFramework.SidePaneArrangement.Stacked, vm.Workspace!.Arrangement);
    }

    private sealed class NoDialogs : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveLocationAsync(string title, string suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName) => Task.FromResult<string?>(null);
        public Task<SaveChangesChoice> AskSaveChangesAsync(string documentTitle) => Task.FromResult(SaveChangesChoice.Cancel);
        public Task<string?> PickSvgEditorAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> AskInstallUpdateAsync(string version, string? notes) => Task.FromResult(false);
        public Task<bool> AskDeleteIssueAsync(string caption) => Task.FromResult(false);
    }
}
