using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.Editors;
using Stanley.ProjectModel.Geometry;

namespace Stanley.App.HeadlessTests;

/// <summary>
/// Shift+click multi-selection through real pointer events, on top of the same MainWindow the
/// other page editor headless tests drive. The view-model-level behaviour (add/toggle/remove,
/// group move as one undo step, nudge, multi-delete, stale-index clean-up) is already covered
/// in PageEditorViewModelTests/MultiSelectionTests at the view-model level; this file checks
/// that Avalonia's own pointer pipeline reports Shift correctly and wires up to it.
/// </summary>
[Collection("Page Editor Tests")]
public class MultiSelectionHeadlessTests
{
    /// <summary>Creates a bubble by double-clicking, types nothing and presses Enter to close the inline editor - the same steps DoubleClickInPanel_TypeAndEnter_CreatesALetteredBubbleInThatPanel uses, just without the typing.</summary>
    private static void CreateBubbleAt(MainWindow window, PageCanvasControl canvas, Point2D pagePoint)
    {
        var point = canvas.TranslatePoint(canvas.PageToControl(pagePoint), window)!.Value;
        window.DoubleClick(point);
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void ShiftClick_AddsASecondBubbleToTheSelection()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = GetPageCanvasControl(window)!;
        var panelId = window.Editor.Working.PanelOrder[0];
        var bounds = window.Editor.PanelBounds(panelId);
        var leftPoint = new Point2D(bounds.Left + bounds.Width * 0.25, bounds.MidY);
        var rightPoint = new Point2D(bounds.Left + bounds.Width * 0.75, bounds.MidY);

        CreateBubbleAt(window, canvas, leftPoint); // becomes bubble 0, selected alone
        CreateBubbleAt(window, canvas, rightPoint); // becomes bubble 1, selected alone
        Assert.Equal(2, window.Editor.Working.Panels[panelId].Bubbles.Count);
        Assert.False(window.Editor.HasMultiSelection);
        Assert.Equal(1, window.Editor.SelectedBubbleIndex);

        // Shift+click on the first bubble adds it to the selection. (A second Shift+click on the
        // very same spot to test removal is left to the view-model tests: consecutive presses at
        // the same point land within Avalonia's own double-click window in a headless run with no
        // real gap between them, which would exercise double-click handling instead of ours.)
        var leftScreen = canvas.TranslatePoint(canvas.PageToControl(leftPoint), window)!.Value;
        window.MouseDown(leftScreen, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(leftScreen, MouseButton.Left, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.Editor.HasMultiSelection);
        Assert.Equal(2, window.Editor.SelectionCount);
        Assert.Equal(0, window.Editor.SelectedBubbleIndex); // the one just Shift+clicked is now primary
        Assert.True(window.Editor.IsPartOfSelection(panelId, bubbleIndex: 1));
    }

    /// <summary>The point of the issue this feature is for: drag a multi-selection and everything in it moves together, as one undo step.</summary>
    [Fact]
    public void DraggingAMultiSelectedBubble_MovesTheWholeGroupTogether()
    {
        var window = new MainWindow();
        window.Show();
        var canvas = GetPageCanvasControl(window)!;
        var panelId = window.Editor.Working.PanelOrder[0];
        var bounds = window.Editor.PanelBounds(panelId);
        var leftPoint = new Point2D(bounds.Left + bounds.Width * 0.25, bounds.MidY);
        var rightPoint = new Point2D(bounds.Left + bounds.Width * 0.75, bounds.MidY);

        CreateBubbleAt(window, canvas, leftPoint);
        CreateBubbleAt(window, canvas, rightPoint);
        // Built directly rather than with a Shift+click, so the drag below is the only press at
        // this point in the whole test - seeing exactly one press keeps Avalonia's click counter
        // from folding it into a double-click, the way two presses at the same spot would.
        window.Editor.ToggleSelect(panelId, bubbleIndex: 0);
        Assert.True(window.Editor.HasMultiSelection);

        var leftBefore = AnchorRing.BoundingBox(window.Editor.Working.Panels[panelId].Bubbles[0].Shape.Anchors);
        var rightBefore = AnchorRing.BoundingBox(window.Editor.Working.Panels[panelId].Bubbles[1].Shape.Anchors);

        // A plain press-and-drag on either bubble - both are already selected - moves the whole group.
        var leftScreen = canvas.TranslatePoint(canvas.PageToControl(leftPoint), window)!.Value;
        var dragTo = new Point(leftScreen.X + 20, leftScreen.Y + 15);
        window.MouseDown(leftScreen, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(dragTo);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(dragTo, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var leftAfter = AnchorRing.BoundingBox(window.Editor.Working.Panels[panelId].Bubbles[0].Shape.Anchors);
        var rightAfter = AnchorRing.BoundingBox(window.Editor.Working.Panels[panelId].Bubbles[1].Shape.Anchors);
        var movedX = leftAfter.Left - leftBefore.Left;
        var movedY = leftAfter.Top - leftBefore.Top;
        Assert.True(movedX > 5, $"the dragged bubble should have moved right, moved {movedX:0.0}mm");
        Assert.Equal(movedX, rightAfter.Left - rightBefore.Left, 3);
        Assert.Equal(movedY, rightAfter.Top - rightBefore.Top, 3);

        // Still a multi-selection - the drag didn't collapse it - and it committed as one gesture.
        Assert.True(window.Editor.HasMultiSelection);
        Assert.Equal(window.Editor.Working, window.Editor.Committed);
        Assert.True(window.History.CanUndo);

        window.History.UndoCommand.Execute(null);
        Assert.Equal(leftBefore, AnchorRing.BoundingBox(window.Editor.Working.Panels[panelId].Bubbles[0].Shape.Anchors));
        Assert.Equal(rightBefore, AnchorRing.BoundingBox(window.Editor.Working.Panels[panelId].Bubbles[1].Shape.Anchors));
    }

    private static PageCanvasControl? GetPageCanvasControl(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<PageCanvasControl>().FirstOrDefault();
    }
}
