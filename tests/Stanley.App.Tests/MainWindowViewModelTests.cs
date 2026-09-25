using Stanley.App.Documents;
using Stanley.ProjectModel.Geometry;

namespace Stanley.App.Tests;

/// <summary>Scripted answers for the file dialogs, recording what was asked.</summary>
public sealed class FakeFileDialogs : IFileDialogs
{
    public Queue<string?> Folders { get; } = new();
    public Queue<SaveChangesChoice> SaveChangesAnswers { get; } = new();
    public string? ExportPath { get; set; }
    public int SaveChangesPrompts { get; private set; }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folders.Count > 0 ? Folders.Dequeue() : null);

    public Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName) =>
        Task.FromResult(ExportPath);

    public Task<SaveChangesChoice> AskSaveChangesAsync(string documentTitle)
    {
        SaveChangesPrompts++;
        return Task.FromResult(SaveChangesAnswers.Count > 0 ? SaveChangesAnswers.Dequeue() : SaveChangesChoice.Cancel);
    }
}

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-vm-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeFileDialogs _dialogs = new();
    private readonly RecentProjects _recent = new(storePath: null);

    public MainWindowViewModelTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private MainWindowViewModel NewViewModel() => new(_dialogs, _recent);

    private static void MakeAnEdit(MainWindowViewModel vm)
    {
        var editor = vm.Editor!;
        editor.CreateBubble(editor.Working.PanelOrder[0], new Point2D(50, 50));
    }

    [Fact]
    public void StartsWithABlankUntitledComic_ThatIsNotDirty()
    {
        var vm = NewViewModel();

        Assert.True(vm.HasDocument);
        Assert.False(vm.IsDirty);
        Assert.Equal("Untitled comic - Stanley", vm.WindowTitle);
    }

    [Fact]
    public async Task Edit_MarksDirty_AndSavingAnUntitledComicAsksForAFolder()
    {
        var vm = NewViewModel();
        MakeAnEdit(vm);
        Assert.True(vm.IsDirty);
        Assert.Contains("•", vm.WindowTitle);

        var folder = Path.Combine(_root, "Comic");
        _dialogs.Folders.Enqueue(folder);
        Assert.True(await vm.SaveAsync());

        Assert.False(vm.IsDirty);
        Assert.Equal(folder, vm.Project!.Location);
        Assert.Equal("Comic - Stanley", vm.WindowTitle);
        Assert.Equal(folder, Assert.Single(_recent.Paths));
    }

    [Fact]
    public async Task Saving_writes_an_imported_picture_and_reopening_draws_it_again()
    {
        var vm = NewViewModel();
        var editor = vm.Editor!;
        var panel = editor.Working.PanelOrder[0];
        using (var bitmap = new SkiaSharp.SKBitmap(8, 4))
        {
            bitmap.Erase(SkiaSharp.SKColors.Orange);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            Assert.True(editor.ImportPicture(new Stanley.Editors.PictureImportRequest(panel, AsBackground: true), "sunset.png",
                Stanley.ProjectModel.Characters.ArtFile.Png(data.ToArray())));
        }
        var name = ((Stanley.ProjectModel.Issues.InlineBackground)editor.Working.Panels[panel].Background!).ArtFileName;

        var folder = Path.Combine(_root, "Comic");
        _dialogs.Folders.Enqueue(folder);
        Assert.True(await vm.SaveAsync());

        Assert.Single(Directory.GetFiles(folder, name, SearchOption.AllDirectories));
        var reopened = Stanley.Editors.ComicProject.Open(folder);
        Assert.True(reopened.Pictures.ContainsKey(name));
    }

    [Fact]
    public async Task SaveAs_Cancelled_LeavesTheComicUntitledAndDirty()
    {
        var vm = NewViewModel();
        MakeAnEdit(vm);

        Assert.False(await vm.SaveAsync());

        Assert.True(vm.Project!.IsUntitled);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public async Task New_WithUnsavedChanges_CancelKeepsTheComic_DontSaveReplacesIt()
    {
        var vm = NewViewModel();
        MakeAnEdit(vm);
        var original = vm.Editor;

        _dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.Cancel);
        await vm.NewAsync(null);
        Assert.Same(original, vm.Editor);

        _dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.DontSave);
        await vm.NewAsync(null);
        Assert.NotSame(original, vm.Editor);
        Assert.False(vm.IsDirty);
        Assert.Equal(2, _dialogs.SaveChangesPrompts);
    }

    [Fact]
    public async Task Open_LoadsTheSavedComic_AndIsListedInRecent()
    {
        var first = NewViewModel();
        MakeAnEdit(first);
        var folder = Path.Combine(_root, "Saved");
        _dialogs.Folders.Enqueue(folder);
        await first.SaveAsync();

        var second = NewViewModel();
        await second.OpenAsync(folder);

        Assert.Equal(folder, second.Project!.Location);
        Assert.Single(second.Editor!.Working.Panels.Values.SelectMany(p => p.Bubbles));
        Assert.Equal(folder, Assert.Single(second.RecentEntries).Path);
    }

    [Fact]
    public async Task Open_AMissingFolder_ShowsAnErrorAndKeepsTheCurrentComic()
    {
        var vm = NewViewModel();
        var editor = vm.Editor;

        await vm.OpenAsync(Path.Combine(_root, "nope"));

        Assert.Same(editor, vm.Editor);
        Assert.NotNull(vm.Message);
        Assert.True(vm.IsBackstageOpen);
    }

    [Fact]
    public async Task Close_LeavesTheFileViewOpenWithNoComic()
    {
        var vm = NewViewModel();

        await vm.CloseDocumentAsync();

        Assert.False(vm.HasDocument);
        Assert.True(vm.IsBackstageOpen);
        Assert.Equal(BackstagePage.New, vm.BackstagePage);
        vm.IsBackstageOpen = false;
        Assert.True(vm.IsBackstageOpen); // nothing to go back to
    }

    [Fact]
    public async Task RenamingTheTitle_MarksDirty_AndIsSaved()
    {
        var vm = NewViewModel();
        var folder = Path.Combine(_root, "Named");
        _dialogs.Folders.Enqueue(folder);
        await vm.SaveAsync();

        vm.DocumentTitle = "The Big Heist";
        Assert.True(vm.IsDirty);
        await vm.SaveAsync();

        Assert.False(vm.IsDirty);
        Assert.Equal("The Big Heist", Stanley.Editors.ComicProject.Open(folder).Title);
    }

    [Fact]
    public async Task ExportPdf_WritesTheFile()
    {
        var vm = NewViewModel();
        _dialogs.ExportPath = Path.Combine(_root, "out.pdf");

        await vm.ExportAsync("pdf");

        Assert.True(File.Exists(_dialogs.ExportPath));
    }

    [Fact]
    public async Task NewFromATemplate_OpensThatKindOfComic_AndInfoNamesIt()
    {
        var vm = NewViewModel();
        var daily = Stanley.Editing.ComicTemplates.All.Single(t => t.Name == "Daily strip");

        await vm.NewFromTemplateAsync(daily);

        Assert.Equal(new Rect2D(0, 0, 330, 105), vm.Editor!.PageBounds);
        Assert.Equal(4, vm.Editor.Working.PanelOrder.Count);
        Assert.False(vm.IsDirty);
        Assert.Equal("Daily strip · 330 × 105 mm, no bleed", vm.PageSizeText);
        Assert.Equal("The current page at 300 dpi. For the web and social media.", vm.PngExportText);
    }

    [Fact]
    public async Task AWebcomicsPng_IsExportedAtItsPixelSize()
    {
        var vm = NewViewModel();
        await vm.NewFromTemplateAsync(Stanley.Editing.ComicTemplates.All.Single(t => t.Name == "Vertical scroll"));
        _dialogs.ExportPath = Path.Combine(_root, "episode.png");

        await vm.ExportAsync("png");

        Assert.Equal("Vertical scroll · 200 × 320 mm, no bleed, exported at 800 × 1280 px", vm.PageSizeText);
        Assert.Contains("800 × 1280 px", vm.PngExportText, StringComparison.Ordinal);
        using var image = SkiaSharp.SKBitmap.Decode(_dialogs.ExportPath);
        Assert.Equal((800, 1280), (image.Width, image.Height));
    }

    [Fact]
    public async Task InfosTitleAndIssueNumber_ShowWhereverAPageHasTheirFields_AndAreSaved()
    {
        var vm = NewViewModel();
        Assert.Equal("1", vm.IssueNumber);

        vm.DocumentTitle = "Moon Pie";
        vm.IssueNumber = " 4 ";

        Assert.True(vm.IsDirty);
        Assert.Equal(new Stanley.ProjectModel.Issues.TextFields("Moon Pie", "4"), vm.Editor!.Fields);
        var folder = Path.Combine(_root, "Moon Pie");
        _dialogs.Folders.Enqueue(folder);
        Assert.True(await vm.SaveAsync());
        Assert.False(vm.IsDirty);
        Assert.Equal("4", Stanley.Editors.ComicProject.Open(folder).IssueNumber);
    }

    [Fact]
    public async Task AMarginSetOnTheLayoutTab_IsSavedWithTheComic()
    {
        var vm = NewViewModel();
        vm.Editor!.MarginMm = 14;
        Assert.True(vm.IsDirty);

        var folder = Path.Combine(_root, "Margins");
        _dialogs.Folders.Enqueue(folder);
        Assert.True(await vm.SaveAsync());

        Assert.Equal(14, Stanley.Editors.ComicProject.Open(folder).Grid.MarginMm);
    }

    [Fact]
    public void AComicBooksInfo_StillNamesItsPaper() =>
        Assert.Equal("A4 · 210 × 297 mm, 3 mm bleed", NewViewModel().PageSizeText);
}
