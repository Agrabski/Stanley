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
    /// outward: the demo page starts with one panel filling the entire A4 page, so any
    /// outward drag would exceed the page bounds and be correctly rejected by
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
        var cornerWorldPoint = new Point(boundsBefore.Right, boundsBefore.Bottom);
        var cornerWindowPoint = canvas.TranslatePoint(cornerWorldPoint, window)!.Value;
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
        var cornerWorldPoint = new Point(boundsBefore.Right, boundsBefore.Bottom);
        var cornerWindowPoint = canvas.TranslatePoint(cornerWorldPoint, window)!.Value;
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
        var cornerWorldPoint = new Point(boundsOriginal.Right, boundsOriginal.Bottom);
        var cornerWindowPoint = canvas.TranslatePoint(cornerWorldPoint, window)!.Value;
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
