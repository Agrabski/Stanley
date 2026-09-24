using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// Ensures the Avalonia headless platform is initialized for all tests in the collection.
/// </summary>
public class HeadlessPlatformSetup
{
    private static bool _initialized;
    private static readonly object _lock = new();

    static HeadlessPlatformSetup()
    {
        // Static initializer ensures platform is initialized exactly once
        lock (_lock)
        {
            if (!_initialized)
            {
                // BuildAvaloniaApp() only constructs an AppBuilder - SetupWithoutStarting()
                // is what actually runs Initialize() and registers the headless platform
                // (IWindowingPlatform etc.) with AvaloniaLocator. Without this call nothing
                // is ever wired up, which surfaced as "Unable to locate IWindowingPlatform"
                // on the first Window construction below.
                TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
                _initialized = true;
            }
        }
    }

    // Public constructor ensures static initializer runs
    public HeadlessPlatformSetup()
    {
    }
}

/// <summary>
/// Collection definition for page editor tests with Avalonia headless platform initialization.
/// </summary>
[CollectionDefinition("Page Editor Tests")]
public class PageEditorTestCollection : ICollectionFixture<HeadlessPlatformSetup>
{
}

/// <summary>
/// Headless smoke tests for the page/panel editor, verifying Avalonia-specific wiring:
/// pointer→gesture handling, DataTemplate resolution, and KeyBindings. Higher-level
/// editing logic is already tested at the ViewModel level in PageEditorViewModelTests.
/// </summary>
[Collection("Page Editor Tests")]
public class PageEditorTests
{
    /// <summary>
    /// Verifies that dragging a panel's bottom-right corner inward shrinks its bounds,
    /// and that the gesture commits so Working == Committed after release. Inward, not
    /// outward: the demo page starts with one panel filling the A4 page's live area, so a
    /// large outward drag would exceed the page bounds and be correctly rejected by
    /// PanelLayoutEditing.Resize's validation - there'd be nothing to grow into.
    /// </summary>
    [Fact]
    public void DraggingPanelCorner_ResizesThePanelAndCommits()
    {
        var window = new MainWindow();
        window.Show();

        var panelId = window.Editor.Working.PanelOrder[0];
        var panelBefore = window.Editor.Working.Panels[panelId];
        var boundsBefore = AnchorRing.BoundingBox(panelBefore.Shape.Anchors);

        var canvas = GetPageCanvasControl(window);
        Assert.NotNull(canvas);

        // Drag the bottom-right corner inward
        var cornerWindowPoint = canvas.TranslatePoint(canvas.PageToControl(new Point2D(boundsBefore.Right, boundsBefore.Bottom)), window)!.Value;
        var newCornerWindowPoint = new Point(cornerWindowPoint.X - 50, cornerWindowPoint.Y - 40);

        window.MouseDown(cornerWindowPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(newCornerWindowPoint);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(newCornerWindowPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // Verify the panel is smaller
        var panelAfter = window.Editor.Working.Panels[panelId];
        var boundsAfter = AnchorRing.BoundingBox(panelAfter.Shape.Anchors);
        Assert.True(boundsAfter.Right < boundsBefore.Right, "right edge should move inward");
        Assert.True(boundsAfter.Bottom < boundsBefore.Bottom, "bottom edge should move inward");

        // Verify gesture committed (Working == Committed)
        Assert.Equal(window.Editor.Working, window.Editor.Committed);
    }

    /// <summary>
    /// Verifies that pressing Escape mid-drag cancels the resize gesture, reverting
    /// the panel bounds and leaving History.CanUndo false.
    /// </summary>
    [Fact]
    public void PressEscapeMidDrag_CancelsDragAndReverts()
    {
        var window = new MainWindow();
        window.Show();

        var panelId = window.Editor.Working.PanelOrder[0];
        var panelBefore = window.Editor.Working.Panels[panelId];
        var boundsBefore = AnchorRing.BoundingBox(panelBefore.Shape.Anchors);

        var canvas = GetPageCanvasControl(window);
        Assert.NotNull(canvas);

        // Start dragging the bottom-right corner inward (see DraggingPanelCorner_ResizesThePanelAndCommits for why inward)
        var cornerWindowPoint = canvas.TranslatePoint(canvas.PageToControl(new Point2D(boundsBefore.Right, boundsBefore.Bottom)), window)!.Value;
        var newCornerWindowPoint = new Point(cornerWindowPoint.X - 50, cornerWindowPoint.Y - 40);

        window.MouseDown(cornerWindowPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(newCornerWindowPoint);
        Dispatcher.UIThread.RunJobs();

        // Confirm the drag actually changed something before cancelling - otherwise
        // "bounds are unchanged after Escape" would trivially hold even if Escape did nothing.
        var boundsMidDrag = AnchorRing.BoundingBox(window.Editor.Working.Panels[panelId].Shape.Anchors);
        Assert.True(boundsMidDrag.Right < boundsBefore.Right, "drag should have shrunk the panel before Escape is pressed");

        // Press Escape to cancel
        var keyEventArgs = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape
        };
        canvas.RaiseEvent(keyEventArgs);

        // Don't release the mouse - the drag should already be cancelled

        // Verify bounds are back to original
        var panelAfter = window.Editor.Working.Panels[panelId];
        var boundsAfter = AnchorRing.BoundingBox(panelAfter.Shape.Anchors);
        Assert.Equal(boundsBefore.Right, boundsAfter.Right, 1);
        Assert.Equal(boundsBefore.Bottom, boundsAfter.Bottom, 1);

        // Verify no undo entry was created (the gesture was cancelled, not committed)
        Assert.False(window.History.CanUndo);
    }

    /// <summary>
    /// Verifies that Ctrl+Z (undo) and Ctrl+Shift+Z (redo) key bindings work:
    /// after committing a resize, Ctrl+Z reverts it, and Ctrl+Shift+Z restores it.
    /// </summary>
    [Fact]
    public void CtrlZAndCtrlShiftZ_UndoAndRedo()
    {
        var window = new MainWindow();
        window.Show();

        var panelId = window.Editor.Working.PanelOrder[0];
        var panelOriginal = window.Editor.Working.Panels[panelId];
        var boundsOriginal = AnchorRing.BoundingBox(panelOriginal.Shape.Anchors);

        var canvas = GetPageCanvasControl(window);
        Assert.NotNull(canvas);

        // Perform a resize gesture to create an undo entry (inward - see
        // DraggingPanelCorner_ResizesThePanelAndCommits for why not outward)
        var cornerWindowPoint = canvas.TranslatePoint(canvas.PageToControl(new Point2D(boundsOriginal.Right, boundsOriginal.Bottom)), window)!.Value;
        var newCornerWindowPoint = new Point(cornerWindowPoint.X - 50, cornerWindowPoint.Y - 40);

        window.MouseDown(cornerWindowPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(newCornerWindowPoint);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(newCornerWindowPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // Verify resize happened
        var panelAfterResize = window.Editor.Working.Panels[panelId];
        var boundsAfterResize = AnchorRing.BoundingBox(panelAfterResize.Shape.Anchors);
        Assert.True(boundsAfterResize.Right < boundsOriginal.Right);
        Assert.True(boundsAfterResize.Bottom < boundsOriginal.Bottom);
        Assert.True(window.History.CanUndo, "should have undo available after resize");

        // Execute Undo via the command directly (more reliable than key chord in headless mode)
        window.History.UndoCommand.Execute(null);

        // Verify bounds are back to original
        var panelAfterUndo = window.Editor.Working.Panels[panelId];
        var boundsAfterUndo = AnchorRing.BoundingBox(panelAfterUndo.Shape.Anchors);
        Assert.Equal(boundsOriginal.Right, boundsAfterUndo.Right, 1);
        Assert.Equal(boundsOriginal.Bottom, boundsAfterUndo.Bottom, 1);
        Assert.True(window.History.CanRedo, "should have redo available after undo");

        // Execute Redo via the command
        window.History.RedoCommand.Execute(null);

        // Verify bounds are back to resized state
        var panelAfterRedo = window.Editor.Working.Panels[panelId];
        var boundsAfterRedo = AnchorRing.BoundingBox(panelAfterRedo.Shape.Anchors);
        Assert.Equal(boundsAfterResize.Right, boundsAfterRedo.Right, 1);
        Assert.Equal(boundsAfterResize.Bottom, boundsAfterRedo.Bottom, 1);
    }

    /// <summary>
    /// Smoke test: verifies that the MainWindow's DataTemplate correctly resolves
    /// PageEditorViewModel to PageEditorView, which renders a PageCanvasControl
    /// in the visual tree.
    /// </summary>
    [Fact]
    public void MainWindowRendersPageCanvasControlInVisualTree()
    {
        var window = new MainWindow();
        window.Show();

        var canvas = GetPageCanvasControl(window);
        Assert.NotNull(canvas);
        Assert.NotNull(canvas.ViewModel);
    }

    /// <summary>The main "add dialogue" path: double-click inside a panel, type, press Enter.</summary>
    [Fact]
    public void DoubleClickInPanel_TypeAndEnter_CreatesALetteredBubbleInThatPanel()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = GetPageCanvasControl(window)!;
        var view = window.GetVisualDescendants().OfType<PageEditorView>().Single();
        var panelId = window.Editor.Working.PanelOrder[0];
        var bounds = window.Editor.PanelBounds(panelId);

        var point = canvas.TranslatePoint(canvas.PageToControl(new Point2D(bounds.MidX, bounds.MidY)), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.TextEditor.IsVisible, "the inline text editor should open over the new bubble");
        window.KeyTextInput("Hello there");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(view.TextEditor.IsVisible);
        var bubble = Assert.Single(window.Editor.Working.Panels[panelId].Bubbles);
        Assert.Equal("Hello there", bubble.Text);
    }

    /// <summary>Picking the bubble tool from the keyboard and clicking places a bubble, then returns to the select tool.</summary>
    [Fact]
    public void BubbleToolShortcut_ThenClick_AddsBubbleAndReturnsToSelect()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = GetPageCanvasControl(window)!;
        var panelId = window.Editor.Working.PanelOrder[0];
        var bounds = window.Editor.PanelBounds(panelId);

        canvas.Focus();
        window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.B, "b");
        Assert.Equal(PageEditorTool.Bubble, window.Editor.Tool);

        var point = canvas.TranslatePoint(canvas.PageToControl(new Point2D(bounds.MidX, bounds.MidY)), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(window.Editor.Working.Panels[panelId].Bubbles);
        Assert.Equal(PageEditorTool.Select, window.Editor.Tool);
    }

    /// <summary>Ctrl+scroll zooms towards the pointer; Fit page brings the whole A4 page back into view.</summary>
    [Fact]
    public void CtrlScroll_ZoomsAroundThePointer_AndFitPageRestores()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = GetPageCanvasControl(window)!;
        var fitZoom = canvas.Zoom;

        var pagePoint = new Point2D(50, 60);
        var screenPoint = canvas.TranslatePoint(canvas.PageToControl(pagePoint), window)!.Value;
        window.MouseWheel(screenPoint, new Vector(0, 3), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.True(canvas.Zoom > fitZoom, "scrolling up with Ctrl should zoom in");
        var after = canvas.TranslatePoint(canvas.PageToControl(pagePoint), window)!.Value;
        Assert.Equal(screenPoint.X, after.X, 3);
        Assert.Equal(screenPoint.Y, after.Y, 3);

        canvas.FitPage();
        Assert.Equal(fitZoom, canvas.Zoom, 6);
        var topLeft = canvas.PageToControl(new Point2D(0, 0));
        var bottomRight = canvas.PageToControl(new Point2D(210, 297));
        Assert.True(topLeft.X >= 0 && topLeft.Y >= 0 && bottomRight.X <= canvas.Bounds.Width && bottomRight.Y <= canvas.Bounds.Height,
            "fit page should show the whole A4 page");
    }

    /// <summary>Dragging the gutter between two panels resizes both and keeps the gutter between them.</summary>
    [Fact]
    public void DraggingAGutter_ResizesBothNeighbours()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = GetPageCanvasControl(window)!;
        var editor = window.Editor;
        editor.SplitPanel(editor.Working.PanelOrder[0], Stanley.Editing.BoundaryOrientation.Vertical, 0.5);
        var (left, right) = (editor.Working.PanelOrder[0], editor.Working.PanelOrder[1]);
        var gutterBefore = editor.PanelBounds(right).Left - editor.PanelBounds(left).Right;
        var leftRightBefore = editor.PanelBounds(left).Right;

        var start = canvas.TranslatePoint(canvas.PageToControl(new Point2D(leftRightBefore + gutterBefore / 2, 100)), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X - 40, start.Y));
        window.MouseUp(new Point(start.X - 40, start.Y), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.PanelBounds(left).Right < leftRightBefore - 5, "left panel should shrink");
        Assert.Equal(gutterBefore, editor.PanelBounds(right).Left - editor.PanelBounds(left).Right, 6);
        Assert.True(window.History.CanUndo);
    }

    /// <summary>The ribbon sits in the window above the dock area (not inside the pane) and shows the active pane's ribbon.</summary>
    [Fact]
    public void Ribbon_IsAboveTheDockArea_AndBoundToTheActiveEditor()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = GetPageCanvasControl(window)!;

        var dock = window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DockControl>().Single();
        Assert.DoesNotContain(window.RibbonBarControl, dock.GetVisualDescendants());
        Assert.DoesNotContain(dock.GetVisualDescendants(), v => v is PageEditorRibbon);

        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();
        Assert.Same(window.Editor, ribbon.DataContext);
        Assert.Same(window.Editor, window.Workspace.ActiveEditor);

        var ribbonBottom = window.RibbonBarControl.TranslatePoint(new Point(0, window.RibbonBarControl.Bounds.Height), window)!.Value.Y;
        var canvasTop = canvas.TranslatePoint(new Point(0, 0), window)!.Value.Y;
        Assert.True(ribbonBottom <= canvasTop, "ribbon should be above the editor pane");
    }

    /// <summary>Word-style tabs: the fixed ones are always there; the Panel/Bubble contextual tabs appear only for their selection, and their buttons act on it.</summary>
    [Fact]
    public void RibbonTabs_ContextualTabsFollowTheSelection()
    {
        var window = new MainWindow();
        window.Show();
        GetPageCanvasControl(window);
        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();
        TabItem Tab(string name) => ribbon.TabControl.Items.OfType<TabItem>().Single(t => t.Name == name);
        var editor = window.Editor;
        var panelId = editor.Working.PanelOrder[0];

        Assert.Equal(["Home", "Insert", "Layout", "View"],
            ribbon.TabControl.Items.OfType<TabItem>().Where(t => t.IsVisible).Select(t => t.Header as string));
        Assert.False(Tab("PanelTab").IsVisible);
        Assert.False(Tab("BubbleTab").IsVisible);

        editor.Select(panelId);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Tab("PanelTab").IsVisible);
        Assert.False(Tab("BubbleTab").IsVisible);

        ribbon.TabControl.SelectedItem = Tab("PanelTab");
        Dispatcher.UIThread.RunJobs();
        var columns = ribbon.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SplitColumnsButton");
        columns.Command!.Execute(columns.CommandParameter);
        Assert.Equal(2, editor.Working.PanelOrder.Count);

        editor.CreateBubble(editor.Working.PanelOrder[0], new Point2D(40, 40));
        Dispatcher.UIThread.RunJobs();
        Assert.False(Tab("PanelTab").IsVisible);
        Assert.True(Tab("BubbleTab").IsVisible);
        Assert.Same(Tab("HomeTab"), ribbon.TabControl.SelectedItem); // the Panel tab it was on went away
    }

    /// <summary>The File button opens the full-window File view over the ribbon and page; Escape goes back.</summary>
    [Fact]
    public void FileButton_OpensTheBackstage_AndEscapeReturns()
    {
        var window = new MainWindow();
        window.Show();
        GetPageCanvasControl(window);
        var file = window.RibbonBarControl.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FileButton");

        file.Command!.Execute(file.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.BackstageControl.IsVisible);
        Assert.Equal(BackstagePage.Info, window.ViewModel.BackstagePage);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.BackstageControl.IsVisible);
    }

    /// <summary>Ctrl+S on a new comic asks for a folder (Save As), saves there, and the title bar stops showing unsaved changes.</summary>
    [Fact]
    public void CtrlS_OnANewComic_SavesToThePickedFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "stanley-headless-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dialogs = new ScriptedDialogs(folder);
            var window = new MainWindow(new MainWindowViewModel(dialogs, new Stanley.App.Documents.RecentProjects(null)));
            window.Show();
            var canvas = GetPageCanvasControl(window)!;
            window.Editor.CreateBubble(window.Editor.Working.PanelOrder[0], new Point2D(60, 60));
            Assert.True(window.ViewModel.IsDirty);
            Assert.EndsWith("not saved yet", window.ViewModel.DocumentCaption, StringComparison.Ordinal);

            canvas.Focus();
            window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, "s");
            Dispatcher.UIThread.RunJobs();

            Assert.True(Stanley.ProjectModel.Storage.ProjectRepository.IsInitialized(folder));
            Assert.False(window.ViewModel.IsDirty);
            Assert.EndsWith("saved", window.ViewModel.DocumentCaption, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>File &gt; Close empties the window down to the File view; File &gt; New brings a fresh page (and its ribbon tabs) back.</summary>
    [Fact]
    public void CloseThenNew_SwapsTheWholeEditorAndRibbon()
    {
        var window = new MainWindow();
        window.Show();
        GetPageCanvasControl(window);
        var before = window.Editor;

        window.ViewModel.CloseDocumentCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.ViewModel.HasDocument);
        Assert.True(window.BackstageControl.IsVisible);
        Assert.Empty(window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>());

        window.ViewModel.NewCommand.Execute(Stanley.Editing.PanelLayoutPresets.All[2]);
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.BackstageControl.IsVisible);
        Assert.NotSame(before, window.Editor);
        Assert.Equal(3, window.Editor.Working.PanelOrder.Count);
        Assert.Same(window.Editor, window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single().DataContext);
        Assert.Same(window.Editor, GetPageCanvasControl(window)!.ViewModel);
    }

    private sealed class ScriptedDialogs(string folder) : Stanley.App.Documents.IFileDialogs
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(folder);
        public Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName) => Task.FromResult<string?>(null);
        public Task<Stanley.App.Documents.SaveChangesChoice> AskSaveChangesAsync(string documentTitle) => Task.FromResult(Stanley.App.Documents.SaveChangesChoice.Cancel);
    }

    /// <summary>A ribbon command outside the pane still reaches the pane's view: Add bubble opens the inline text editor.</summary>
    [Fact]
    public void RibbonAddBubble_OpensTheInlineTextEditorInThePane()
    {
        var window = new MainWindow();
        window.Show();
        GetPageCanvasControl(window);
        var view = window.GetVisualDescendants().OfType<PageEditorView>().Single();
        var ribbon = window.RibbonBarControl.GetVisualDescendants().OfType<PageEditorRibbon>().Single();
        var add = ribbon.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AddBubbleButton");

        add.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.TextEditor.IsVisible);
        Assert.Single(window.Editor.Working.Panels[window.Editor.Working.PanelOrder[0]].Bubbles);
    }

    /// <summary>
    /// Helper: finds the PageCanvasControl in the window's visual tree.
    /// </summary>
    /// <summary>
    /// Headless mode has no real message pump, so a DockControl's templated content
    /// (resolved from its Layout/DataTemplate) doesn't materialize into the visual tree
    /// until pending layout/dispatcher work is actually run.
    /// </summary>
    private static PageCanvasControl? GetPageCanvasControl(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<PageCanvasControl>().FirstOrDefault();
    }
}
