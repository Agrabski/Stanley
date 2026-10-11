using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Stanley.App.Documents;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// The line a file operation leaves in the File view and the title bar (#85): a red "Saved to ..."
/// read as a failure, so only a real problem is red now, in both themes.
/// </summary>
[Collection("Page Editor Tests")]
public sealed class FileMessageColourTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-message-colour-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>Answers Save As with one folder under the test's own temp folder; nothing else is ever asked.</summary>
    private sealed class SaveHereDialogs(string folder) : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveLocationAsync(string title, string suggestedName) => Task.FromResult<string?>(folder);
        public Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName) => Task.FromResult<string?>(null);
        public Task<SaveChangesChoice> AskSaveChangesAsync(string documentTitle) => Task.FromResult(SaveChangesChoice.Cancel);
        public Task<string?> PickSvgEditorAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> AskDeleteIssueAsync(string caption) => Task.FromResult(false);
        public Task<bool> AskInstallUpdateAsync(string version, string? notes) => Task.FromResult(false);
    }

    private static Color ColourOf(TextBlock text) => Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color;

    private static Color ThemeColour(Control from, string key, ThemeVariant theme)
    {
        Assert.True(from.TryFindResource(key, theme, out var value), $"{key} should exist in the {theme} theme");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>WCAG contrast ratio; 4.5 is the bar for ordinary-size text.</summary>
    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    [Fact]
    public async Task Good_news_is_not_shown_in_the_red_of_an_error_but_a_problem_still_is()
    {
        var folder = Path.Combine(_root, "Comic");
        var vm = new MainWindowViewModel(new SaveHereDialogs(folder), new RecentProjects(storePath: null));
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var fileViewMessage = window.BackstageControl.FindControl<TextBlock>("MessageText")!;
        var titleBarMessage = window.FindControl<TextBlock>("TitleMessageText")!;

        vm.ShowBackstage(BackstagePage.SaveAs);
        Assert.True(await vm.SaveAsAsync());
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Saved to ", vm.Message, StringComparison.Ordinal);
        Assert.False(vm.IsMessageProblem);
        Assert.Equal(ThemeColour(window, "StanleySuccessBrush", window.ActualThemeVariant), ColourOf(fileViewMessage));
        Assert.NotEqual(ThemeColour(window, "StanleyErrorBrush", window.ActualThemeVariant), ColourOf(fileViewMessage));
        Assert.Equal(Colors.White, ColourOf(titleBarMessage));
        LookTabTests.Snapshot(window, "backstage-message-good-news");

        await vm.DeleteIssueAsync(vm.Project!.IssueId); // the issue that's open can't go
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsMessageProblem);
        Assert.Equal(ThemeColour(window, "StanleyErrorBrush", window.ActualThemeVariant), ColourOf(fileViewMessage));
        Assert.NotEqual(Colors.White, ColourOf(titleBarMessage));
        LookTabTests.Snapshot(window, "backstage-message-problem");
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_good_news_colour_is_legible_on_the_File_view_in_both_themes(string themeName)
    {
        var theme = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var window = new MainWindow(new MainWindowViewModel(new SaveHereDialogs(Path.Combine(_root, "Comic")), new RecentProjects(storePath: null)));
        window.Show();
        window.RequestedThemeVariant = theme;
        Dispatcher.UIThread.RunJobs();

        var success = ThemeColour(window, "StanleySuccessBrush", theme);
        var background = ThemeColour(window, "StanleyBackstageBrush", theme);

        Assert.True(Contrast(success, background) >= 4.5, $"{themeName}: {success} on {background} is {Contrast(success, background):0.0}:1");
        Assert.True(success.G > success.R, "it should read as green, not as the error red");
    }
}
