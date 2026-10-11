using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.App.Documents;
using Stanley.Editing;
using Stanley.Editors;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// The canvas layout's top bar (docs/ui-shell.md §6): the page tools and their menus, Insert ▾,
/// the zoom menu and the main menu - each doing what its ribbon counterpart does, and the tools
/// agreeing with the keys pressed on the page.
/// </summary>
[Collection("Page Editor Tests")]
public class CanvasTopBarTests
{
    private static PageCanvasControl Canvas(MainWindow window) => window.GetVisualDescendants().OfType<PageCanvasControl>().Single();

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    /// <summary>A real click in the middle of <paramref name="control"/> - in a popup too.</summary>
    private static void Click(MainWindow window, Control control)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(MainWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, modifiers == RawInputModifiers.None ? key.ToString().ToLowerInvariant() : null);
        window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The way to a menu item: the submenus it's in, then the item.</summary>
    private static IEnumerable<IReadOnlyList<MenuItem>> Paths(IEnumerable<object?> items, IReadOnlyList<MenuItem> above) =>
        items.OfType<MenuItem>().SelectMany(item => Paths(item.Items, [.. above, item]).Prepend([.. above, item]));

    /// <summary>
    /// A menu item, with its menu - and any submenu it's in - open, as a user would get to it: a
    /// closed flyout's or submenu's items have no DataContext to bind their commands through.
    /// </summary>
    private static MenuItem ItemOf(MenuFlyout menu, Control anchor, string name)
    {
        if (!menu.IsOpen)
        {
            menu.ShowAt(anchor);
            Dispatcher.UIThread.RunJobs();
        }
        var path = Paths(menu.Items, []).Single(p => p[^1].Name == name);
        foreach (var submenu in path.SkipLast(1))
        {
            submenu.Open();
            Dispatcher.UIThread.RunJobs();
        }
        return path[^1];
    }

    /// <summary>Clicks a menu item with the pointer (which, unlike a bare Click event, also ticks a checkable one) and sees the menu closed after it.</summary>
    private static void Choose(MenuFlyout menu, Control anchor, string name)
    {
        var item = ItemOf(menu, anchor, name);
        Assert.True(item.IsEffectivelyEnabled, $"{name} is disabled");
        Click((MainWindow)TopLevel.GetTopLevel(anchor)!, item);
        menu.Hide();
        Dispatcher.UIThread.RunJobs();
    }

    private static MenuFlyout MenuOf(Button button) => (MenuFlyout)button.Flyout!;

    private static void ChooseMain(MainWindow window, string name)
    {
        var bar = CanvasLayoutHarness.TopBar(window);
        Choose(bar.MainMenu, Named<Button>(bar, "MainMenuButton"), name);
    }

    private static MenuItem MainItem(MainWindow window, string name)
    {
        var bar = CanvasLayoutHarness.TopBar(window);
        return ItemOf(bar.MainMenu, Named<Button>(bar, "MainMenuButton"), name);
    }

    [Fact]
    public void Tool_buttons_pick_the_tool_and_follow_the_keys_pressed_on_the_page()
    {
        var window = CanvasLayoutHarness.Open();
        var editor = window.Editor;
        var toolbar = CanvasLayoutHarness.PageToolbar(window);
        RadioButton Tool(string name) => Named<RadioButton>(toolbar, name);
        Canvas(window).Focus();

        foreach (var (name, tool) in new[]
                 {
                     ("PanelToolButton", PageEditorTool.Panel), ("BubbleToolButton", PageEditorTool.Bubble), ("TextToolButton", PageEditorTool.Text),
                     ("DrawToolButton", PageEditorTool.Draw), ("PanToolButton", PageEditorTool.Pan), ("SelectToolButton", PageEditorTool.Select)
                 })
        {
            Click(window, Tool(name));
            Assert.Equal(tool, editor.Tool);
            Assert.True(Tool(name).IsChecked);
        }

        // The buttons never take the keyboard: the page still has it, and its keys move the checked button.
        Click(window, Tool("PanelToolButton"));
        Assert.True(Canvas(window).IsFocused);
        foreach (var (key, physical, name) in new[]
                 {
                     (Key.B, PhysicalKey.B, "BubbleToolButton"), (Key.T, PhysicalKey.T, "TextToolButton"), (Key.H, PhysicalKey.H, "PanToolButton"),
                     (Key.P, PhysicalKey.P, "PanelToolButton"), (Key.V, PhysicalKey.V, "SelectToolButton")
                 })
        {
            Press(window, key, physical);
            Assert.True(Tool(name).IsChecked, name);
            Assert.Equal(1, new[] { "SelectToolButton", "PanToolButton", "PanelToolButton", "BubbleToolButton", "TextToolButton" }.Count(n => Tool(n).IsChecked == true));
        }

        // One shapes button: whichever shape tool is in hand, and after it's put down, the one used last.
        Press(window, Key.R, PhysicalKey.R);
        Assert.Equal(PageEditorTool.Rectangle, editor.Tool);
        Assert.True(Tool("RectangleToolButton").IsVisible);
        Assert.True(Tool("RectangleToolButton").IsChecked);
        Assert.False(Tool("DrawToolButton").IsVisible);
        Press(window, Key.V, PhysicalKey.V);
        Assert.True(Tool("RectangleToolButton").IsVisible);
        Assert.False(Tool("RectangleToolButton").IsChecked);
        Click(window, Tool("RectangleToolButton"));
        Assert.Equal(PageEditorTool.Rectangle, editor.Tool);
        Press(window, Key.E, PhysicalKey.E);
        Assert.Equal(["EllipseToolButton"], Named<Avalonia.Controls.Panel>(toolbar, "ShapeToolButtons").Children.Where(c => c.IsVisible).Select(c => c.Name));
        Assert.True(Tool("EllipseToolButton").IsChecked);

        // Back from editing a character, the page's tools come back as they were.
        window.ViewModel.Characters!.NewCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        window.Workspace.Activate(editor);
        Dispatcher.UIThread.RunJobs();
        toolbar = CanvasLayoutHarness.PageToolbar(window);
        Assert.True(Tool("EllipseToolButton").IsVisible);
        Assert.True(Tool("EllipseToolButton").IsChecked);
        Click(window, Tool("PanelToolButton"));
        Assert.Equal(PageEditorTool.Panel, editor.Tool);
    }

    [Fact]
    public void The_shapes_menu_picks_any_shape_tool_and_the_shapes_button_then_shows_it()
    {
        var window = CanvasLayoutHarness.Open();
        var editor = window.Editor;
        var toolbar = CanvasLayoutHarness.PageToolbar(window);
        var chevron = Named<Button>(toolbar, "ShapeMenuButton");
        var menu = MenuOf(chevron);

        foreach (var (item, tool, button) in new[]
                 {
                     ("FreeformToolItem", PageEditorTool.Freeform, "FreeformToolButton"), ("LineToolItem", PageEditorTool.Line, "LineToolButton"),
                     ("RectangleToolItem", PageEditorTool.Rectangle, "RectangleToolButton"), ("EllipseToolItem", PageEditorTool.Ellipse, "EllipseToolButton"),
                     ("DrawToolItem", PageEditorTool.Draw, "DrawToolButton")
                 })
        {
            Choose(menu, chevron, item);
            Assert.Equal(tool, editor.Tool);
            Assert.True(Named<RadioButton>(toolbar, button).IsVisible);
            Assert.True(Named<RadioButton>(toolbar, button).IsChecked);
            Assert.True(ItemOf(menu, chevron, item).IsChecked); // the menu marks the one in hand
            menu.Hide();
        }
    }

    [Fact]
    public void The_tools_menus_drop_bubbles_text_and_panels_into_the_page()
    {
        var window = CanvasLayoutHarness.Open();
        var editor = window.Editor;
        var toolbar = CanvasLayoutHarness.PageToolbar(window);
        var panel = editor.Working.PanelOrder[0];
        editor.Select(panel);
        Dispatcher.UIThread.RunJobs();

        var bubbles = Named<Button>(toolbar, "BubbleMenuButton");
        Choose(MenuOf(bubbles), bubbles, "InsertShoutItem");
        Assert.Equal(BubbleStylePreset.Shout, Assert.Single(editor.Working.Panels[panel].Bubbles).Style);

        editor.Select(panel);
        var texts = Named<Button>(toolbar, "TextMenuButton");
        Choose(MenuOf(texts), texts, "InsertCaptionItem");
        Assert.IsType<TextElement>(Assert.Single(editor.Working.Panels[panel].Elements));

        editor.Select(panel);
        Click(window, Named<Button>(toolbar, "InsertSpeedLinesButton"));
        Assert.Contains(editor.Working.Panels[panel].Elements, e => e is SpeedLinesElement);
        Assert.Same(editor.InsertPictureCommand, Named<Button>(toolbar, "InsertPictureButton").Command);

        var panels = Named<Button>(toolbar, "PanelMenuButton");
        Choose(MenuOf(panels), panels, "InsertThoughtCloudItem");
        Assert.Equal(2, editor.Working.PanelOrder.Count);
        Assert.Contains(editor.Working.Panels.Values, p => p.Kind == PanelKind.Cloud);
        Choose(MenuOf(panels), panels, "DrawPanelItem");
        Assert.Equal(PageEditorTool.Panel, editor.Tool);
        Assert.True(Named<RadioButton>(toolbar, "PanelToolButton").IsChecked);
    }

    [Fact]
    public void Insert_places_characters_backgrounds_and_title_pages_and_closes_behind_a_pick()
    {
        var window = CanvasLayoutHarness.Open();
        var editor = window.Editor;
        var toolbar = CanvasLayoutHarness.PageToolbar(window);
        var insert = Named<Button>(toolbar, "InsertButton");
        var panel = editor.Working.PanelOrder[0];
        Control Open()
        {
            insert.Flyout!.ShowAt(insert);
            Dispatcher.UIThread.RunJobs();
            return (Control)((Flyout)toolbar.InsertFlyout).Content!;
        }

        // Characters, to begin with: a new one, then the same one again from the gallery.
        var content = Open();
        Assert.True(Named<RadioButton>(content, "InsertCharactersPage").IsChecked);
        Click(window, Named<Button>(content, "NewCharacterButton"));
        Assert.False(toolbar.InsertFlyout.IsOpen);
        var placed = Assert.Single(editor.Working.Panels[panel].CharacterInstances);

        content = Open();
        var gallery = Named<ItemsControl>(content, "CharacterGallery");
        Click(window, gallery.GetVisualDescendants().OfType<Button>().Single());
        Assert.False(toolbar.InsertFlyout.IsOpen);
        Assert.Equal(2, editor.Working.Panels[panel].CharacterInstances.Count(i => i.CharacterId == placed.CharacterId));

        // A background, for the selected panel - and the gallery stays open to try another.
        editor.Select(panel);
        Dispatcher.UIThread.RunJobs();
        content = Open();
        Named<RadioButton>(content, "InsertBackgroundPage").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Click(window, Named<Button>(content, "BackgroundDaysky"));
        Assert.IsType<GradientBackground>(editor.Working.Panels[panel].Background);
        Assert.True(toolbar.InsertFlyout.IsOpen);
        insert.Flyout!.Hide();

        // A title page: the comic gets one at the front, and the gallery closes.
        content = Open();
        Assert.True(Named<RadioButton>(content, "InsertBackgroundPage").IsChecked); // the page last shown
        Named<RadioButton>(content, "InsertTitlePagePage").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        var cover = Named<ItemsControl>(content, "TitlePageGallery").GetVisualDescendants().OfType<Button>()
            .Single(b => b.DataContext is TitlePageChoice { Design: TitlePageDesign.Cover });
        Click(window, cover);
        Assert.False(toolbar.InsertFlyout.IsOpen);
        var navigator = window.ViewModel.Navigator!;
        Assert.Same(navigator.Pages[0], navigator.TitlePage);
    }

    [Fact]
    public void Insert_My_Assets_puts_a_kept_group_in_the_page()
    {
        var folder = Path.Combine(Path.GetTempPath(), "stanley-canvas-assets-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new AppSettings(null) { InterfaceLayout = InterfaceLayout.Canvas };
            var window = new MainWindow(new MainWindowViewModel(new RecordingDialogs(), new RecentProjects(null), settings: settings,
                myAssets: new MyAssetsLibrary(folder)));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var editor = window.Editor;
            var panel = editor.Working.PanelOrder[0];
            editor.Tool = PageEditorTool.Rectangle;
            foreach (var (x, y) in new[] { (40.0, 40.0), (70.0, 60.0) })
            {
                editor.BeginDrawShape(panel);
                editor.UpdateDrawShape(new Point2D(x, y), new Point2D(x + 20, y + 15));
                editor.CommitDrawShape();
            }
            editor.Tool = PageEditorTool.Select;
            editor.SelectElement(panel, 0);
            editor.ToggleSelect(panel, elementIndex: 1);
            editor.GroupSelectionCommand.Execute(null);
            editor.KeepGroupInMyAssetsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var toolbar = CanvasLayoutHarness.PageToolbar(window);
            var insert = Named<Button>(toolbar, "InsertButton");
            insert.Flyout!.ShowAt(insert);
            Dispatcher.UIThread.RunJobs();
            var content = (Control)((Flyout)toolbar.InsertFlyout).Content!;
            Assert.True(Named<RadioButton>(content, "InsertMyAssetsPage").IsVisible);
            Named<RadioButton>(content, "InsertMyAssetsPage").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Click(window, Named<ItemsControl>(content, "MyAssetsGroupGallery").GetVisualDescendants().OfType<Button>().Single());

            Assert.False(toolbar.InsertFlyout.IsOpen);
            Assert.Equal(2, editor.Working.Panels[panel].Elements.OfType<GroupElement>().Count());

            // The main menu's My Assets shows with a library, and opens File › My Assets.
            Assert.True(MainItem(window, "MenuMyAssetsItem").IsVisible);
            ChooseMain(window, "MenuMyAssetsItem");
            Assert.Equal(BackstagePage.MyAssets, window.ViewModel.BackstagePage);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_zoom_menu_zooms_the_page_and_shows_where_it_is()
    {
        var window = CanvasLayoutHarness.Open();
        var editor = window.Editor;
        var zoom = CanvasLayoutHarness.TopBar(window).GetVisualDescendants().OfType<PageZoomControl>().Single();
        var button = Named<Button>(zoom, "ZoomButton");
        string Shown() => Named<TextBlock>(zoom, "ZoomText").Text!;
        var fit = editor.ZoomPercent;
        Assert.Equal(editor.ZoomText, Shown());

        Choose(zoom.ZoomMenu, button, "ZoomInItem");
        Assert.True(editor.ZoomPercent > fit);
        Assert.Equal(editor.ZoomText, Shown());
        var zoomedIn = editor.ZoomPercent;
        Choose(zoom.ZoomMenu, button, "ZoomOutItem");
        Assert.True(editor.ZoomPercent < zoomedIn);
        Choose(zoom.ZoomMenu, button, "ActualSizeItem");
        Assert.NotEqual(fit, editor.ZoomPercent, 0.5);
        Choose(zoom.ZoomMenu, button, "FitPageItem");
        Assert.Equal(fit, editor.ZoomPercent, 0.5);
        Assert.Equal(editor.ZoomText, Shown());
    }

    [Fact]
    public void The_main_menu_opens_the_File_views_pages()
    {
        var window = CanvasLayoutHarness.Open();
        var vm = window.ViewModel;

        foreach (var (item, page) in new[]
                 {
                     ("MenuFileItem", BackstagePage.Info), ("MenuOpenItem", BackstagePage.Open), ("MenuNewFromTemplateItem", BackstagePage.New),
                     ("MenuExportPageItem", BackstagePage.Export), ("MenuOptionsItem", BackstagePage.Options)
                 })
        {
            ChooseMain(window, item);
            Assert.True(vm.IsBackstageOpen, item);
            Assert.Equal(page, vm.BackstagePage);
            vm.IsBackstageOpen = false;
        }
        Assert.False(MainItem(window, "MenuMyAssetsItem").IsVisible); // no My Assets library here
    }

    [Fact]
    public void The_main_menu_saves_exports_starts_and_closes_comics()
    {
        var dialogs = new RecordingDialogs();
        var settings = new AppSettings(null) { InterfaceLayout = InterfaceLayout.Canvas };
        var window = new MainWindow(new MainWindowViewModel(dialogs, new RecentProjects(null), settings: settings));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = window.ViewModel;

        ChooseMain(window, "MenuSaveItem"); // never saved: Save asks where, as Save As does
        ChooseMain(window, "MenuSaveAsItem");
        ChooseMain(window, "MenuExportPdfItem");
        ChooseMain(window, "MenuExportPngItem");
        Assert.Equal(["save", "save", "pdf", "png"], dialogs.Asked);

        var before = vm.Workspace;
        ChooseMain(window, "MenuNewItem");
        Assert.NotSame(before, vm.Workspace);
        Assert.Single(CanvasLayoutHarness.TopBar(window).GetVisualDescendants().OfType<PageToolbar>()); // the new comic's page, tools and all

        ChooseMain(window, "MenuCloseItem");
        Assert.False(vm.HasDocument);
        // Nothing open: nothing to edit or view, and nothing for Export to write.
        foreach (var name in new[] { "MenuEditItem", "MenuViewItem", "MenuExportItem", "MenuCloseItem", "MenuSaveItem" })
            Assert.False(MainItem(window, name).IsEffectivelyEnabled, name);
    }

    [Fact]
    public void The_main_menus_Edit_and_View_act_on_the_page()
    {
        var window = CanvasLayoutHarness.Open();
        var editor = window.Editor;
        editor.Clipboard = new PageClipboard();
        var panel = editor.Working.PanelOrder[0];
        var bounds = editor.PanelBounds(panel);
        editor.CreateBubble(panel, new Point2D(bounds.MidX, bounds.MidY));
        Dispatcher.UIThread.RunJobs();
        int Bubbles() => editor.Working.Panels[panel].Bubbles.Count;

        ChooseMain(window, "MenuCopyItem");
        ChooseMain(window, "MenuPasteItem");
        Assert.Equal(2, Bubbles());
        ChooseMain(window, "MenuDuplicateItem");
        Assert.Equal(3, Bubbles());
        ChooseMain(window, "MenuUndoItem");
        Assert.Equal(2, Bubbles());
        ChooseMain(window, "MenuRedoItem");
        Assert.Equal(3, Bubbles());
        editor.Select(panel, 2);
        ChooseMain(window, "MenuDeleteItem");
        Assert.Equal(2, Bubbles());
        editor.Select(panel, 1);
        ChooseMain(window, "MenuCutItem");
        Assert.Equal(1, Bubbles());

        // Two bubbles, grouped and taken apart again.
        editor.CreateBubble(panel, new Point2D(bounds.MidX, bounds.Top + 20));
        editor.Select(panel, 0);
        editor.ToggleSelect(panel, bubbleIndex: 1);
        Dispatcher.UIThread.RunJobs();
        ChooseMain(window, "MenuGroupItem");
        Assert.True(editor.UngroupSelectionCommand.CanExecute(null));
        ChooseMain(window, "MenuUngroupItem");
        Assert.False(editor.UngroupSelectionCommand.CanExecute(null));

        var typing = 0;
        editor.TextEditRequested += (_, _) => typing++;
        editor.Select(panel, 0);
        ChooseMain(window, "MenuEditTextItem");
        Assert.Equal(1, typing);

        var zoom = editor.ZoomPercent;
        ChooseMain(window, "MenuZoomInItem");
        Assert.True(editor.ZoomPercent > zoom);
        ChooseMain(window, "MenuFitPageItem");
        Assert.Equal(zoom, editor.ZoomPercent, 0.5);

        // View's switches: ticked as the page has them, and clicking one flips it.
        foreach (var (name, read) in new (string, Func<bool>)[]
                 {
                     ("MenuLockLayoutItem", () => editor.IsLayoutLocked), ("MenuMarginGuidesItem", () => editor.ShowMarginGuides),
                     ("MenuSnapItem", () => editor.SnapEnabled), ("MenuLayersItem", () => editor.ShowLayers)
                 })
        {
            var was = read();
            Assert.Equal(was, MainItem(window, name).IsChecked);
            ChooseMain(window, name);
            Assert.NotEqual(was, read());
            Assert.Equal(!was, MainItem(window, name).IsChecked);
            CanvasLayoutHarness.TopBar(window).MainMenu.Hide();
        }
    }

    [Fact]
    public void Edit_and_View_leave_the_page_alone_while_a_character_is_edited()
    {
        var window = CanvasLayoutHarness.Open();
        window.ViewModel.Characters!.NewCharacterCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<CharacterEditorViewModel>(window.Workspace.ActiveEditor);

        Assert.False(MainItem(window, "MenuViewItem").IsEffectivelyEnabled);
        foreach (var name in new[] { "MenuCutItem", "MenuCopyItem", "MenuPasteItem", "MenuDuplicateItem", "MenuDeleteItem", "MenuGroupItem", "MenuUngroupItem" })
            Assert.False(MainItem(window, name).IsEnabled, name);
        Assert.True(MainItem(window, "MenuUndoItem").IsEffectivelyEnabled); // making the character is undoable, from anywhere
        CanvasLayoutHarness.TopBar(window).MainMenu.Hide();

        window.Workspace.Activate(window.Editor);
        Dispatcher.UIThread.RunJobs();
        Assert.True(MainItem(window, "MenuViewItem").IsEffectivelyEnabled);
        Assert.True(MainItem(window, "MenuPasteItem").IsEnabled);
    }

    [Fact]
    public void The_windows_shortcuts_still_work_in_the_canvas_layout()
    {
        var dialogs = new RecordingDialogs();
        var settings = new AppSettings(null) { InterfaceLayout = InterfaceLayout.Canvas };
        var window = new MainWindow(new MainWindowViewModel(dialogs, new RecentProjects(null), settings: settings));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = window.ViewModel;

        var before = vm.Workspace;
        Canvas(window).Focus();
        Press(window, Key.N, PhysicalKey.N, RawInputModifiers.Control);
        Assert.NotSame(before, vm.Workspace);

        var editor = window.Editor;
        var panel = editor.Working.PanelOrder[0];
        editor.CreateBubble(panel, new Point2D(60, 60));
        Dispatcher.UIThread.RunJobs();
        Canvas(window).Focus();
        Press(window, Key.Z, PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Empty(editor.Working.Panels[panel].Bubbles);
        Press(window, Key.Y, PhysicalKey.Y, RawInputModifiers.Control);
        Assert.Single(editor.Working.Panels[panel].Bubbles);

        Press(window, Key.S, PhysicalKey.S, RawInputModifiers.Control);
        Press(window, Key.S, PhysicalKey.S, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal(["save", "save"], dialogs.Asked);

        Press(window, Key.O, PhysicalKey.O, RawInputModifiers.Control);
        Assert.True(vm.IsBackstageOpen);
        Assert.Equal(BackstagePage.Open, vm.BackstagePage);
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(vm.IsBackstageOpen);

        Canvas(window).Focus();
        Press(window, Key.F, PhysicalKey.F, RawInputModifiers.Alt);
        Assert.True(vm.IsBackstageOpen);
        Assert.Equal(BackstagePage.Info, vm.BackstagePage);
    }

    /// <summary>Cancels every file dialog, writing down which ones were asked for.</summary>
    private sealed class RecordingDialogs : IFileDialogs
    {
        public List<string> Asked { get; } = [];
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveLocationAsync(string title, string suggestedName)
        {
            Asked.Add("save");
            return Task.FromResult<string?>(null);
        }
        public Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName)
        {
            Asked.Add(extension);
            return Task.FromResult<string?>(null);
        }
        public Task<SaveChangesChoice> AskSaveChangesAsync(string documentTitle) => Task.FromResult(SaveChangesChoice.Cancel);
        public Task<string?> PickSvgEditorAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> AskInstallUpdateAsync(string version, string? notes) => Task.FromResult(false);
        public Task<bool> AskDeleteIssueAsync(string caption) => Task.FromResult(false);
    }
}
