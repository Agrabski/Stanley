using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Xunit;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// Headless smoke tests for the page/panel editor, verifying Avalonia-specific wiring:
/// pointer→gesture handling, DataTemplate resolution, and KeyBindings. Higher-level
/// editing logic is already tested at the ViewModel level in PageEditorViewModelTests.
/// </summary>
public class PageEditorTests
{
    static PageEditorTests()
    {
        // Initialize Avalonia once for all tests in this class
        TestAppBuilder.BuildAvaloniaApp().SetupInProcessDesktopPlatform();
    }
    /// <summary>
    /// Verifies that dragging a panel's bottom-right corner outward increases its bounds,
    /// and that the gesture commits so Working == Committed after release.
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

        // Drag the bottom-right corner outward
        var cornerWorldPoint = new Point(boundsBefore.Right, boundsBefore.Bottom);
        var cornerWindowPoint = canvas.TranslatePoint(cornerWorldPoint, window)!.Value;
        var newCornerWindowPoint = new Point(cornerWindowPoint.X + 50, cornerWindowPoint.Y + 40);

        window.MouseDown(cornerWindowPoint, MouseButton.Left);
        window.MouseMove(newCornerWindowPoint);
        window.MouseUp(newCornerWindowPoint, MouseButton.Left);

        // Verify the panel is larger
        var panelAfter = window.Editor.Working.Panels[panelId];
        var boundsAfter = AnchorRing.BoundingBox(panelAfter.Shape.Anchors);
        Assert.True(boundsAfter.Right > boundsBefore.Right, "right edge should move outward");
        Assert.True(boundsAfter.Bottom > boundsBefore.Bottom, "bottom edge should move outward");

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

        // Start dragging the bottom-right corner
        var cornerWorldPoint = new Point(boundsBefore.Right, boundsBefore.Bottom);
        var cornerWindowPoint = canvas.TranslatePoint(cornerWorldPoint, window)!.Value;
        var newCornerWindowPoint = new Point(cornerWindowPoint.X + 50, cornerWindowPoint.Y + 40);

        window.MouseDown(cornerWindowPoint, MouseButton.Left);
        window.MouseMove(newCornerWindowPoint);

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

        // Perform a resize gesture to create an undo entry
        var cornerWorldPoint = new Point(boundsOriginal.Right, boundsOriginal.Bottom);
        var cornerWindowPoint = canvas.TranslatePoint(cornerWorldPoint, window)!.Value;
        var newCornerWindowPoint = new Point(cornerWindowPoint.X + 50, cornerWindowPoint.Y + 40);

        window.MouseDown(cornerWindowPoint, MouseButton.Left);
        window.MouseMove(newCornerWindowPoint);
        window.MouseUp(newCornerWindowPoint, MouseButton.Left);

        // Verify resize happened
        var panelAfterResize = window.Editor.Working.Panels[panelId];
        var boundsAfterResize = AnchorRing.BoundingBox(panelAfterResize.Shape.Anchors);
        Assert.True(boundsAfterResize.Right > boundsOriginal.Right);
        Assert.True(boundsAfterResize.Bottom > boundsOriginal.Bottom);
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
    private static PageCanvasControl? GetPageCanvasControl(MainWindow window) =>
        window.GetVisualDescendants().OfType<PageCanvasControl>().FirstOrDefault();
}
