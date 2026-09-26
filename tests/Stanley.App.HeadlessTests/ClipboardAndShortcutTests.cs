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
    public void The_home_tabs_clipboard_buttons_copy_and_paste_and_say_their_keys()
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
        var tip = Assert.IsType<ShortcutTip>(ToolTip.GetTip(Named("PasteButton")));
        Assert.Equal("Ctrl+V", tip.Keys);
        ToolTip.SetIsOpen(Named("CopyButton"), true);
        Dispatcher.UIThread.RunJobs();
        LookTabTests.Snapshot(window, "home-clipboard-group");
        ToolTip.SetIsOpen(Named("CopyButton"), false);
    }

    private sealed class ExportDialogs(string exportPath) : Stanley.App.Documents.IFileDialogs
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveLocationAsync(string title, string suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName) => Task.FromResult<string?>(exportPath);
        public Task<Stanley.App.Documents.SaveChangesChoice> AskSaveChangesAsync(string documentTitle) => Task.FromResult(Stanley.App.Documents.SaveChangesChoice.Cancel);
        public Task<string?> PickSvgEditorAsync(string? currentPath) => Task.FromResult<string?>(null);
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

    [Fact]
    public void F1_on_the_page_opens_the_list_of_every_shortcut()
    {
        var (window, canvas, _, _) = Open();
        canvas.Focus();

        window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, null);
        Dispatcher.UIThread.RunJobs();

        var ribbon = Ribbon(window);
        Assert.Equal("ViewTab", ((TabItem)ribbon.TabControl.SelectedItem!).Name);
        var button = ribbon.GetVisualDescendants().OfType<DropDownButton>().Single(b => b.Name == "ShortcutsButton");
        Assert.True(button.Flyout!.IsOpen);
        var list = ((Control)((Flyout)button.Flyout).Content!).GetVisualDescendants().OfType<ItemsControl>().First(i => i.Name == "ShortcutGroups");
        Assert.Equal(PageShortcuts.All.Count, list.ItemCount);
        var keys = ((Control)((Flyout)button.Flyout).Content!).GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Alt+drag", keys);
        Assert.Contains("Space+drag", keys);
        LookTabTests.Snapshot(TopLevel.GetTopLevel((Control)((Flyout)button.Flyout).Content!) ?? window, "keyboard-shortcuts");
        button.Flyout.Hide();
    }

    [Fact]
    public void Tooltips_show_the_shortcut_apart_from_the_words()
    {
        var (window, _, _, _) = Open();
        var ribbon = Ribbon(window);

        var select = ribbon.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "SelectToolButton");
        var tip = Assert.IsType<ShortcutTip>(ToolTip.GetTip(select));
        Assert.Equal("V", tip.Keys);
        Assert.DoesNotContain("(V)", tip.Text, StringComparison.Ordinal);
        var keycap = tip.Children.OfType<Border>().Single(b => b.Classes.Contains("shortcutKeys"));
        Assert.Equal("V", ((TextBlock)keycap.Child!).Text);

        var undo = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "QuickUndoButton");
        Assert.Equal(("Undo", "Ctrl+Z"), (((ShortcutTip)ToolTip.GetTip(undo)!).Text, ((ShortcutTip)ToolTip.GetTip(undo)!).Keys));

        // A tip changed later keeps its keys.
        ToolTip.SetTip(select, "Pick things");
        tip = Assert.IsType<ShortcutTip>(ToolTip.GetTip(select));
        Assert.Equal(("Pick things", "V"), (tip.Text, tip.Keys));
    }
}
