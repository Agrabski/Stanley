using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.App.HeadlessTests;

/// <summary>Copy, paste, duplicate and Alt+drag through the real window - keyboard, pointer, menus and ribbon - and the shortcuts shown in tooltips.</summary>
[Collection("Page Editor Tests")]
public class ClipboardAndShortcutTests
{
    private static (MainWindow Window, PageCanvasControl Canvas, PanelId Panel, Rect2D Bounds) Open()
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var canvas = window.GetVisualDescendants().OfType<PageCanvasControl>().First();
        var panelId = window.Editor.Working.PanelOrder[0];
        window.Editor.Clipboard = new PageClipboard();
        return (window, canvas, panelId, window.Editor.PanelBounds(panelId));
    }

    private static Point At(MainWindow window, PageCanvasControl canvas, double x, double y) =>
        canvas.TranslatePoint(canvas.PageToControl(new Point2D(x, y)), window)!.Value;

    private static Rect2D BubbleBox(MainWindow window, PanelId panel, int index) =>
        AnchorRing.BoundingBox(window.Editor.Working.Panels[panel].Bubbles[index].Shape.Anchors);

    private static void Ctrl(MainWindow window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.Control, physical, null);
        window.KeyRelease(key, RawInputModifiers.Control, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static PageEditorRibbon Ribbon(MainWindow window) => window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();

    [Fact]
    public void Ctrl_C_then_Ctrl_V_on_the_page_pastes_a_copy_of_the_selected_bubble_and_Ctrl_D_duplicates()
    {
        var (window, canvas, panelId, bounds) = Open();
        window.Editor.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.MidY));
        window.Editor.SetBubbleText(panelId, 0, "Psst");
        canvas.Focus();

        Ctrl(window, Key.C, PhysicalKey.C);
        Ctrl(window, Key.V, PhysicalKey.V);

        var bubbles = window.Editor.Working.Panels[panelId].Bubbles;
        Assert.Equal(2, bubbles.Count);
        Assert.Equal("Psst", bubbles[1].Text);
        Assert.Equal(1, window.Editor.SelectedBubbleIndex);
        Assert.Equal(PageEditorTool.Select, window.Editor.Tool); // Ctrl+V isn't V, the Select tool's key

        Ctrl(window, Key.D, PhysicalKey.D);
        Assert.Equal(3, window.Editor.Working.Panels[panelId].Bubbles.Count);
        Assert.Equal(PageEditorTool.Select, window.Editor.Tool); // nor is Ctrl+D the pen's D
    }

    [Fact]
    public void Alt_dragging_a_bubble_pulls_a_copy_away_under_the_copy_cursor()
    {
        var (window, canvas, panelId, bounds) = Open();
        window.Editor.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.MidY));
        window.Editor.ClearSelection();
        Dispatcher.UIThread.RunJobs();
        var original = BubbleBox(window, panelId, 0);
        var start = At(window, canvas, original.MidX, original.MidY);

        // Holding Alt over it already says a drag will copy it.
        window.MouseMove(start, RawInputModifiers.Alt);
        Assert.Equal(StandardCursorType.DragCopy, canvas.CursorType);
        window.MouseMove(start);
        Assert.Equal(StandardCursorType.SizeAll, canvas.CursorType);

        window.MouseDown(start, MouseButton.Left, RawInputModifiers.Alt);
        window.MouseMove(new Point(start.X + 40, start.Y + 30), RawInputModifiers.Alt);
        Assert.Equal(StandardCursorType.DragCopy, canvas.CursorType);
        window.MouseMove(new Point(start.X + 60, start.Y + 50), RawInputModifiers.Alt);
        window.MouseUp(new Point(start.X + 60, start.Y + 50), MouseButton.Left, RawInputModifiers.Alt);
        Dispatcher.UIThread.RunJobs();

        var bubbles = window.Editor.Working.Panels[panelId].Bubbles;
        Assert.Equal(2, bubbles.Count);
        Assert.Equal(original, BubbleBox(window, panelId, 0));
        Assert.True(BubbleBox(window, panelId, 1).Left > original.Left + 5);
        Assert.Equal(1, window.Editor.SelectedBubbleIndex);
        Assert.Equal(window.Editor.Working, window.Editor.Committed);

        window.History.Undo();
        Assert.Single(window.Editor.Working.Panels[panelId].Bubbles);
    }

    [Fact]
    public void Right_clicking_a_bubble_offers_cut_copy_paste_and_duplicate_with_their_keys()
    {
        var (window, canvas, panelId, bounds) = Open();
        window.Editor.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.MidY));
        var box = BubbleBox(window, panelId, 0);

        var items = canvas.ContextMenuItems(new Point2D(box.MidX, box.MidY)).OfType<MenuItem>().ToList();

        MenuItem Item(string header) => items.Single(i => i.Header as string == header);
        Assert.Equal(KeyGesture.Parse("Ctrl+X"), Item("Cut").InputGesture);
        Assert.Equal(KeyGesture.Parse("Ctrl+C"), Item("Copy").InputGesture);
        Assert.Equal(KeyGesture.Parse("Ctrl+V"), Item("Paste").InputGesture);
        Assert.False(Item("Paste").IsEnabled); // nothing copied yet
        Assert.Equal(KeyGesture.Parse("Ctrl+D"), Item("Duplicate").InputGesture);
        Assert.Equal(KeyGesture.Parse("Delete"), Item("Delete bubble").InputGesture);

        Item("Copy").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var again = canvas.ContextMenuItems(new Point2D(box.MidX, box.MidY)).OfType<MenuItem>().ToList();
        Assert.True(again.Single(i => i.Header as string == "Paste").IsEnabled);
    }

    [Fact]
    public void The_home_tabs_clipboard_buttons_copy_and_paste()
    {
        var (window, _, panelId, bounds) = Open();
        window.Editor.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.MidY));
        Dispatcher.UIThread.RunJobs();
        var buttons = Ribbon(window).GetVisualDescendants().OfType<Button>().ToList();
        Button Named(string name) => buttons.Single(b => b.Name == name);
        Assert.False(Named("PasteButton").IsEffectivelyEnabled);

        Named("CopyButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named("PasteButton").IsEffectivelyEnabled);
        Named("PasteButton").Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, window.Editor.Working.Panels[panelId].Bubbles.Count);
        LookTabTests.Snapshot(window, "home-clipboard-group");
    }

    private sealed class ExportDialogs(string exportPath) : Stanley.App.Documents.IFileDialogs
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveLocationAsync(string title, string suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName) => Task.FromResult<string?>(exportPath);
        public Task<Stanley.App.Documents.SaveChangesChoice> AskSaveChangesAsync(string documentTitle) => Task.FromResult(Stanley.App.Documents.SaveChangesChoice.Cancel);
        public Task<string?> PickSvgEditorAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> AskInstallUpdateAsync(string version, string? notes) => Task.FromResult(false);
        public Task<bool> AskDeleteIssueAsync(string caption) => Task.FromResult(false);
    }

    private sealed class RecordingLauncher : Stanley.App.Documents.IFileLauncher
    {
        public List<string> Calls { get; } = [];
        public Task<bool> OpenAsync(string path) { Calls.Add("open " + path); return Task.FromResult(true); }
        public Task<bool> ShowInFolderAsync(string path) { Calls.Add("show " + path); return Task.FromResult(true); }
    }

    /// <summary>File › Export › PDF: a note in the corner says what was written, with Open and Show in folder.</summary>
    [Fact]
    public void After_exporting_a_note_offers_to_open_the_file_or_show_it_in_its_folder()
    {
        var path = Path.Combine(Path.GetTempPath(), "stanley-export-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            var launcher = new RecordingLauncher();
            var window = new MainWindow(new MainWindowViewModel(new ExportDialogs(path), new Stanley.App.Documents.RecentProjects(null), launcher: launcher));
            window.Show();
            var notice = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ExportNotice");
            Assert.False(notice.IsVisible);

            window.ViewModel.ShowBackstage(BackstagePage.Export);
            window.ViewModel.ExportPdfCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.ViewModel.IsBackstageOpen);
            Assert.True(notice.IsVisible);
            var text = notice.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ExportNoticeText");
            Assert.Equal($"Exported {Path.GetFileName(path)}", text.Text);
            LookTabTests.Snapshot(window, "export-notice");

            Button Named(string name) => notice.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            Named("OpenExportButton").Command!.Execute(null);
            Named("ShowExportInFolderButton").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["open " + path, "show " + path], launcher.Calls);

            Named("DismissExportNoticeButton").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(notice.IsVisible);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CtrlDown(MainWindow window) =>
        window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);

    private static void CtrlUp(MainWindow window) =>
        window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);

    /// <summary>Like Office's KeyTips: hold Ctrl and every button on screen shows its shortcut right by it; let go and they're gone - and nothing on the ribbon moves.</summary>
    [Fact]
    public void Holding_Ctrl_shows_every_buttons_shortcut_beside_it_and_letting_go_hides_them()
    {
        var delay = Shortcut.RevealDelay;
        Shortcut.RevealDelay = TimeSpan.Zero;
        try
        {
            var (window, _, _, _) = Open();
            var ribbon = Ribbon(window);
            var select = ribbon.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "SelectToolButton");
            var selectBounds = select.Bounds;
            Assert.Empty(Shortcut.Showing(window));

            CtrlDown(window);
            Dispatcher.UIThread.RunJobs();

            var showing = Shortcut.Showing(window);
            string KeysOn(string name) => showing.Single(s => s.Host.Name == name).Cap.Keys;
            Assert.Equal("V", KeysOn("SelectToolButton"));
            Assert.Equal("Ctrl+V", KeysOn("PasteButton"));
            Assert.Equal("Ctrl+C", KeysOn("CopyButton"));
            Assert.Equal("Del", KeysOn("DeleteButton"));
            Assert.Equal("Ctrl+Z", KeysOn("QuickUndoButton"));
            Assert.Equal("Alt+F", KeysOn("FileButton"));
            Assert.DoesNotContain(showing, s => s.Host.Name == "SaveButton"); // the File view is closed: nothing of it shows
            Assert.Equal(selectBounds, select.Bounds);

            // A big button's keys sit centred under it; a small one's just past its end.
            var cap = showing.Single(s => s.Host.Name == "SelectToolButton").Cap;
            var capCentre = cap.TranslatePoint(new Point(cap.Bounds.Width / 2, cap.Bounds.Height / 2), window)!.Value;
            var selectBottom = select.TranslatePoint(new Point(select.Bounds.Width / 2, select.Bounds.Height), window)!.Value;
            Assert.InRange(capCentre.X - selectBottom.X, -1.5, 1.5);
            Assert.InRange(capCentre.Y - selectBottom.Y, -1.5, 1.5);
            var copy = ribbon.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CopyButton");
            var copyCap = showing.Single(s => s.Host.Name == "CopyButton").Cap;
            Assert.True(copyCap.TranslatePoint(default, window)!.Value.X >= copy.TranslatePoint(new Point(copy.Bounds.Width, 0), window)!.Value.X);

            // Keycaps wider than their buttons (the title bar's Save, Undo, Redo) step aside rather than cover each other.
            var boxes = showing.Select(s => new Rect(s.Cap.TranslatePoint(default, window)!.Value, s.Cap.Bounds.Size)).ToList();
            for (var i = 0; i < boxes.Count; i++)
            for (var j = i + 1; j < boxes.Count; j++)
                Assert.False(boxes[i].Intersects(boxes[j]), $"{showing[i].Cap.Keys} and {showing[j].Cap.Keys} overlap");
            LookTabTests.Snapshot(window, "ctrl-held");

            CtrlUp(window);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Shortcut.Showing(window));

            // The File view's commands show theirs too, while it's open.
            window.ViewModel.ShowBackstage(BackstagePage.Info);
            Dispatcher.UIThread.RunJobs();
            CtrlDown(window);
            Dispatcher.UIThread.RunJobs();
            showing = Shortcut.Showing(window);
            Assert.Equal("Ctrl+S", KeysOn("SaveButton"));
            Assert.Equal("Esc", KeysOn("BackButton"));
            Assert.DoesNotContain(showing, s => s.Host.Name is "SelectToolButton" or "QuickUndoButton"); // under the File view
            var save = window.BackstageControl.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SaveButton");
            var saveCap = showing.Single(s => s.Host.Name == "SaveButton").Cap;
            Assert.True(saveCap.TranslatePoint(new Point(saveCap.Bounds.Width, 0), window)!.Value.X <= save.TranslatePoint(new Point(save.Bounds.Width, 0), window)!.Value.X);
            var saveAs = window.BackstageControl.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "SaveAsPageButton");
            var saveAsCap = showing.Single(s => s.Host.Name == "SaveAsPageButton").Cap;
            Assert.True(saveAsCap.TranslatePoint(new Point(saveAsCap.Bounds.Width, 0), window)!.Value.X <= saveAs.TranslatePoint(new Point(saveAs.Bounds.Width, 0), window)!.Value.X);
            LookTabTests.Snapshot(window, "ctrl-held-backstage");
            CtrlUp(window);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            Shortcut.RevealDelay = delay;
        }
    }

    /// <summary>Using a shortcut isn't looking for one: a quick Ctrl+C never shows the keycaps, and pressing a key while they show puts them away.</summary>
    [Fact]
    public void Using_a_Ctrl_shortcut_doesnt_leave_the_keycaps_up()
    {
        var (window, canvas, panelId, bounds) = Open();
        window.Editor.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.MidY));
        canvas.Focus();

        CtrlDown(window); // with the usual delay, nothing yet
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Shortcut.Showing(window));
        window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
        window.KeyRelease(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
        CtrlUp(window);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Shortcut.Showing(window));
        Assert.True(window.Editor.CanPaste); // the Ctrl+C itself went through

        var delay = Shortcut.RevealDelay;
        Shortcut.RevealDelay = TimeSpan.Zero;
        try
        {
            CtrlDown(window);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(Shortcut.Showing(window));
            window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Shortcut.Showing(window));
            Assert.Equal(2, window.Editor.Working.Panels[panelId].Bubbles.Count);
            window.KeyRelease(Key.V, RawInputModifiers.Control, PhysicalKey.V, null);
            CtrlUp(window);
        }
        finally
        {
            Shortcut.RevealDelay = delay;
        }
    }
}
