using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.App.Documents;
using Stanley.Editors;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// Opens a window in the canvas layout (File › Options › Layout › Canvas) for headless UI tests,
/// and finds its parts: the top bar, the inspector, a named control in either. The setting is
/// kept in memory, so no test touches another's profile.
/// </summary>
internal static class CanvasLayoutHarness
{
    public static MainWindow Open()
    {
        var settings = new AppSettings(null) { InterfaceLayout = InterfaceLayout.Canvas };
        var window = new MainWindow(new MainWindowViewModel(new NoDialogs(), new RecentProjects(null), settings: settings));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    public static CanvasTopBar TopBar(MainWindow window) => window.CanvasTopBarControl ?? throw new InvalidOperationException("not the canvas layout");

    public static InspectorColumn InspectorColumn(MainWindow window) => window.InspectorControl ?? throw new InvalidOperationException("not the canvas layout");

    /// <summary>The page inspector showing in the window now (the active editor is a page).</summary>
    public static PageInspector PageInspector(MainWindow window) =>
        InspectorColumn(window).GetVisualDescendants().OfType<PageInspector>().Single();

    /// <summary>The character inspector showing in the window now (the active editor is a character).</summary>
    public static CharacterInspector CharacterInspector(MainWindow window) =>
        InspectorColumn(window).GetVisualDescendants().OfType<CharacterInspector>().Single();

    /// <summary>The page toolbar in the top bar now (the active editor is a page).</summary>
    public static PageToolbar PageToolbar(MainWindow window) =>
        TopBar(window).GetVisualDescendants().OfType<PageToolbar>().Single();

    /// <summary>A named control anywhere in the inspector or the top bar, looking inside every section's and toolbar's own name scope.</summary>
    public static T? Find<T>(MainWindow window, string name) where T : Control =>
        InspectorColumn(window).GetVisualDescendants().Concat(TopBar(window).GetVisualDescendants())
            .OfType<T>().FirstOrDefault(control => control.Name == name);

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
