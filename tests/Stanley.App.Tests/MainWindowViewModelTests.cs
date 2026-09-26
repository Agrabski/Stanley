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
    public string? SvgEditorAnswer { get; set; }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folders.Count > 0 ? Folders.Dequeue() : null);

    /// <summary>The names Save As offered in its name box.</summary>
    public List<string> SuggestedNames { get; } = [];

    /// <summary>Answers with the next of <see cref="Folders"/>: the place and the name typed, as one path.</summary>
    public Task<string?> PickSaveLocationAsync(string title, string suggestedName)
    {
        SuggestedNames.Add(suggestedName);
        return PickFolderAsync(title);
    }

    public Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName) =>
        Task.FromResult(ExportPath);

    public Task<SaveChangesChoice> AskSaveChangesAsync(string documentTitle)
    {
        SaveChangesPrompts++;
        return Task.FromResult(SaveChangesAnswers.Count > 0 ? SaveChangesAnswers.Dequeue() : SaveChangesChoice.Cancel);
    }

    public Task<string?> PickSvgEditorAsync(string? currentPath) => Task.FromResult(SvgEditorAnswer);
}

/// <summary>Records what it was asked to open or show, and answers as told.</summary>
public sealed class FakeFileLauncher : IFileLauncher
{
    public List<string> Opened { get; } = [];
    public List<string> Shown { get; } = [];
    public bool Succeeds { get; set; } = true;

    public Task<bool> OpenAsync(string path)
    {
        Opened.Add(path);
        return Task.FromResult(Succeeds);
    }

    public Task<bool> ShowInFolderAsync(string path)
    {
        Shown.Add(path);
        return Task.FromResult(Succeeds);
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
    public async Task SavingANewComic_AsksForItsName_AndTheNameTypedBecomesItsTitle()
    {
        var vm = NewViewModel();
        MakeAnEdit(vm);

        var folder = Path.Combine(_root, "Kot Filemon");
        _dialogs.Folders.Enqueue(folder); // the place picked, and the name typed over the suggestion
        Assert.True(await vm.SaveAsync());

        Assert.Equal(["Untitled comic"], _dialogs.SuggestedNames);
        Assert.Equal(folder, vm.Project!.Location);
        Assert.Equal("Kot Filemon", vm.DocumentTitle);
        Assert.Equal("Kot Filemon - saved", vm.DocumentCaption);
        Assert.Equal(new Stanley.ProjectModel.Issues.TextFields("Kot Filemon", "1"), vm.Editor!.Fields);
        Assert.Equal("Kot Filemon", Stanley.Editors.ComicProject.Open(folder).Title);
    }

    [Fact]
    public async Task SavingUnderANameThatsTaken_SavesBesideIt_AndLeavesWhatsThereAlone()
    {
        var taken = Path.Combine(_root, "Comic");
        Directory.CreateDirectory(taken);
        File.WriteAllText(Path.Combine(taken, "notes.txt"), "mine");
        var vm = NewViewModel();

        _dialogs.Folders.Enqueue(taken);
        Assert.True(await vm.SaveAsync());

        Assert.Equal(Path.Combine(_root, "Comic (2)"), vm.Project!.Location);
        Assert.Equal("Comic", vm.DocumentTitle);
        Assert.Equal(["notes.txt"], Directory.GetFileSystemEntries(taken).Select(Path.GetFileName));
    }

    [Fact]
    public async Task SaveAs_OfASavedComic_OffersItsTitle_AndKeepsIt()
    {
        var vm = NewViewModel();
        _dialogs.Folders.Enqueue(Path.Combine(_root, "First"));
        await vm.SaveAsync();
        vm.DocumentTitle = "The Big Heist";

        var copy = Path.Combine(_root, "Heist backup");
        _dialogs.Folders.Enqueue(copy);
        Assert.True(await vm.SaveAsAsync());

        Assert.Equal("The Big Heist", _dialogs.SuggestedNames[^1]);
        Assert.Equal(copy, vm.Project!.Location);
        Assert.Equal("The Big Heist", vm.DocumentTitle); // it's printed on the title page - a copy's folder name doesn't change it
    }

    [Fact]
    public async Task SaveAs_ToItsOwnFolderUnderItsOwnName_JustSaves()
    {
        var vm = NewViewModel();
        var folder = Path.Combine(_root, "Comic");
        _dialogs.Folders.Enqueue(folder);
        await vm.SaveAsync();
        MakeAnEdit(vm);

        _dialogs.Folders.Enqueue(folder);
        Assert.True(await vm.SaveAsAsync());

        Assert.Equal(folder, vm.Project!.Location);
        Assert.False(vm.IsDirty);
        Assert.False(Directory.Exists(Path.Combine(_root, "Comic (2)")));
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
    public async Task AfterExporting_ANoticeOffersToOpenTheFile_OrShowItInItsFolder()
    {
        var launcher = new FakeFileLauncher();
        var vm = new MainWindowViewModel(_dialogs, _recent, launcher: launcher);
        _dialogs.ExportPath = Path.Combine(_root, "Moon Pie.pdf");
        Assert.False(vm.HasExportNotice);

        await vm.ExportAsync("pdf");

        Assert.True(vm.HasExportNotice);
        Assert.Equal("Exported Moon Pie.pdf", vm.ExportNoticeText);
        Assert.Equal(_dialogs.ExportPath, vm.ExportedPath);
        Assert.Null(vm.Message);

        await vm.OpenExportCommand.ExecuteAsync(null);
        await vm.ShowExportInFolderCommand.ExecuteAsync(null);
        Assert.Equal([_dialogs.ExportPath], launcher.Opened);
        Assert.Equal([_dialogs.ExportPath], launcher.Shown);
        Assert.True(vm.HasExportNotice); // still there for the other one

        vm.DismissExportNoticeCommand.Execute(null);
        Assert.False(vm.HasExportNotice);
    }

    [Fact]
    public async Task TheExportNotice_GoesAwayOnItsOwn_AndACancelledExportShowsNone()
    {
        var scheduler = new ManualScheduler();
        var vm = new MainWindowViewModel(_dialogs, _recent, scheduler: scheduler, launcher: new FakeFileLauncher());

        await vm.ExportAsync("png"); // cancelled: no path picked
        Assert.False(vm.HasExportNotice);

        _dialogs.ExportPath = Path.Combine(_root, "page.png");
        await vm.ExportAsync("png");
        Assert.True(vm.HasExportNotice);
        scheduler.Advance(MainWindowViewModel.ExportNoticeDuration);
        Assert.False(vm.HasExportNotice);
    }

    [Fact]
    public async Task WhenTheExportedFileCantBeOpened_TheTitleBarSaysSo()
    {
        var launcher = new FakeFileLauncher { Succeeds = false };
        var vm = new MainWindowViewModel(_dialogs, _recent, launcher: launcher);
        _dialogs.ExportPath = Path.Combine(_root, "out.pdf");
        await vm.ExportAsync("pdf");

        await vm.OpenExportCommand.ExecuteAsync(null);
        Assert.Contains("Couldn't open \"out.pdf\"", vm.Message, StringComparison.Ordinal);

        File.Delete(_dialogs.ExportPath);
        await vm.ShowExportInFolderCommand.ExecuteAsync(null);
        Assert.Empty(launcher.Shown); // never asked: the file's gone
        Assert.Contains("isn't there any more", vm.Message, StringComparison.Ordinal);
        Assert.False(vm.HasExportNotice);
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

    [Fact]
    public async Task NewIssue_OnAnUntitledComic_GoesThroughSaveAs_ThenAddsAndOpensIt()
    {
        var vm = NewViewModel();
        var folder = Path.Combine(_root, "Fresh Comic");
        _dialogs.Folders.Enqueue(folder);

        await vm.NewIssueAsync();

        Assert.False(vm.Project!.IsUntitled);
        Assert.Equal(folder, vm.Project.Location);
        Assert.Equal(2, vm.Issues.Count);
        Assert.Equal("2", vm.IssueNumber); // now editing the issue just added
        Assert.Equal("Issue #2 added", vm.Message);
    }

    [Fact]
    public async Task NewIssue_OnAnUntitledComic_CancellingSaveAs_AddsNothing()
    {
        var vm = NewViewModel(); // no folder queued - the Save As dialog is cancelled

        await vm.NewIssueAsync();

        Assert.True(vm.Project!.IsUntitled);
        Assert.Single(vm.Issues);
        Assert.Equal("Save the comic before adding an issue to it.", vm.Message);
    }

    [Fact]
    public async Task NewIssue_AddsToTheIssuesList_CurrentOneMarked()
    {
        var vm = NewViewModel();
        var folder = Path.Combine(_root, "Series");
        _dialogs.Folders.Enqueue(folder);
        await vm.SaveAsync();
        Assert.Single(vm.Issues);

        await vm.NewIssueAsync();

        Assert.Equal(2, vm.Issues.Count);
        var current = Assert.Single(vm.Issues, i => i.IsCurrent);
        Assert.Equal("2", current.Number);
        Assert.Equal(current.Id, vm.Project!.IssueId);
    }

    [Fact]
    public async Task SwitchingIssue_WithUnsavedEdits_AsksToSaveFirst()
    {
        var vm = NewViewModel();
        var folder = Path.Combine(_root, "Multi");
        _dialogs.Folders.Enqueue(folder);
        await vm.SaveAsync();
        var firstIssueId = vm.Project!.IssueId;
        await vm.NewIssueAsync();
        var secondIssueId = vm.Project!.IssueId;
        Assert.NotEqual(firstIssueId, secondIssueId);

        vm.AutoSaveEnabled = false; // with AutoSave on there'd be nothing to ask (see below)
        MakeAnEdit(vm);
        Assert.True(vm.IsDirty);

        _dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.Cancel);
        await vm.SwitchIssueAsync(firstIssueId);
        Assert.Equal(secondIssueId, vm.Project!.IssueId); // cancelled - stayed on the issue being edited
        Assert.Equal(1, _dialogs.SaveChangesPrompts);

        _dialogs.SaveChangesAnswers.Enqueue(SaveChangesChoice.DontSave);
        await vm.SwitchIssueAsync(firstIssueId);
        Assert.Equal(firstIssueId, vm.Project!.IssueId);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task SwitchingIssue_WithAutoSaveOn_SavesTheEditsAndAsksNothing()
    {
        var vm = NewViewModel();
        _dialogs.Folders.Enqueue(Path.Combine(_root, "Multi"));
        await vm.SaveAsync();
        var firstIssueId = vm.Project!.IssueId;
        await vm.NewIssueAsync();
        Assert.True(vm.AutoSaveEnabled);
        MakeAnEdit(vm);
        var edited = vm.Project!.IssueId;

        await vm.SwitchIssueAsync(firstIssueId);

        Assert.Equal(firstIssueId, vm.Project!.IssueId);
        Assert.Equal(0, _dialogs.SaveChangesPrompts);
        Assert.False(vm.IsDirty);
        // The bubble was saved with the issue it was added to.
        var reopened = Stanley.Editors.ComicProject.Open(vm.Project.Location!, edited);
        Assert.Single(reopened.Pages[0].Document.Panels.Values.SelectMany(panel => panel.Bubbles));
    }

    [Fact]
    public async Task SwitchingToTheCurrentIssue_IsANoOp_AndClosesTheFileView()
    {
        var vm = NewViewModel();
        var editor = vm.Editor;
        vm.IsBackstageOpen = true;

        await vm.SwitchIssueAsync(vm.Project!.IssueId);

        Assert.Same(editor, vm.Editor);
        Assert.False(vm.IsBackstageOpen);
    }

    [Fact]
    public async Task IssueTitle_IsEditableFromInfo_AndIsSaved()
    {
        var vm = NewViewModel();
        Assert.Equal("", vm.IssueTitle);

        vm.IssueTitle = " Annual ";
        Assert.True(vm.IsDirty);

        var folder = Path.Combine(_root, "Annual");
        _dialogs.Folders.Enqueue(folder);
        Assert.True(await vm.SaveAsync());

        Assert.Equal("Annual", Stanley.Editors.ComicProject.Open(folder).IssueTitle);
    }
}
