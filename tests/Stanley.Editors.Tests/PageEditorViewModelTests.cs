using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

public class PageEditorViewModelTests
{
    private static PageDocument CreateSinglePanelDocument(Rect2D? bounds = null)
    {
        var pageBounds = new Rect2D(0, 0, 210, 297);
        var panelId = PanelId.New();
        var panelBounds = bounds ?? new Rect2D(10, 10, 100, 100);
        var panel = new Panel(
            panelId,
            PanelShapes.Rectangle(panelBounds),
            Background: null,
            CharacterInstances: [],
            Bubbles: []);
        return new PageDocument([panelId], new Dictionary<PanelId, Panel> { [panelId] = panel });
    }

    [Fact]
    public void InsertBubble_AddsToPanel()
    {
        var history = new EditorHistory();
        var pageBounds = new Rect2D(0, 0, 210, 297);
        var document = CreateSinglePanelDocument();
        var panelId = document.PanelOrder[0];
        var viewModel = new PageEditorViewModel(history, pageBounds, document);

        var bubbleBounds = new Rect2D(20, 20, 50, 40);
        viewModel.InsertBubble(panelId, bubbleBounds, BubbleStylePreset.Speech);

        Assert.Single(viewModel.Working.Panels[panelId].Bubbles);
        Assert.True(viewModel.Working.Panels[panelId].Bubbles[0].Text == "");
    }

    [Fact]
    public void SplitPanel_ThenUndo_RestoresOriginal()
    {
        var history = new EditorHistory();
        var pageBounds = new Rect2D(0, 0, 210, 297);
        var document = CreateSinglePanelDocument();
        var panelId = document.PanelOrder[0];
        var viewModel = new PageEditorViewModel(history, pageBounds, document);

        // Split the panel
        viewModel.SplitPanel(panelId, BoundaryOrientation.Vertical, 0.5);

        Assert.Equal(2, viewModel.Working.PanelOrder.Count);
        Assert.Equal(2, viewModel.Working.Panels.Count);

        // Undo
        history.Undo();

        Assert.Single(viewModel.Working.PanelOrder);
        Assert.Single(viewModel.Working.Panels);
        Assert.Contains(panelId, viewModel.Working.PanelOrder);
    }

    [Fact]
    public void InsertBubble_ThenSplitPanel_ThenUndo_ThenUndo_RestoresOriginal()
    {
        var history = new EditorHistory();
        var pageBounds = new Rect2D(0, 0, 210, 297);
        var document = CreateSinglePanelDocument();
        var panelId = document.PanelOrder[0];
        var viewModel = new PageEditorViewModel(history, pageBounds, document);

        // Insert bubble
        var bubbleBounds = new Rect2D(20, 20, 50, 40);
        viewModel.InsertBubble(panelId, bubbleBounds, BubbleStylePreset.Speech);
        Assert.Single(viewModel.Working.Panels[panelId].Bubbles);

        // Split
        viewModel.SplitPanel(panelId, BoundaryOrientation.Vertical, 0.5);
        Assert.Equal(2, viewModel.Working.PanelOrder.Count);

        // Undo split
        history.Undo();
        Assert.Single(viewModel.Working.PanelOrder);

        // Undo insert
        history.Undo();
        Assert.Empty(viewModel.Working.Panels[panelId].Bubbles);
    }

    [Fact]
    public void ResizePanel_Gesture_CreatesOneUndoEntry()
    {
        var history = new EditorHistory();
        var pageBounds = new Rect2D(0, 0, 210, 297);
        var document = CreateSinglePanelDocument();
        var panelId = document.PanelOrder[0];
        var viewModel = new PageEditorViewModel(history, pageBounds, document);

        var originalBounds = AnchorRing.BoundingBox(viewModel.Working.Panels[panelId].Shape.Anchors);

        // Gesture: begin, update once, update twice, commit
        viewModel.BeginResizePanel(panelId);
        viewModel.UpdateResizePanel(panelId, new Rect2D(10, 10, 80, 80));
        viewModel.UpdateResizePanel(panelId, new Rect2D(10, 10, 90, 90));
        viewModel.CommitGesture();

        // Should have exactly one undo entry
        Assert.True(history.CanUndo);

        // Undo should restore original bounds
        history.Undo();
        var restoredBounds = AnchorRing.BoundingBox(viewModel.Working.Panels[panelId].Shape.Anchors);
        Assert.Equal(originalBounds.Left, restoredBounds.Left);
        Assert.Equal(originalBounds.Top, restoredBounds.Top);
        Assert.Equal(originalBounds.Width, restoredBounds.Width);
        Assert.Equal(originalBounds.Height, restoredBounds.Height);
    }

    [Fact]
    public void CancelGesture_RestoresWorking_NoUndoEntry()
    {
        var history = new EditorHistory();
        var pageBounds = new Rect2D(0, 0, 210, 297);
        var document = CreateSinglePanelDocument();
        var panelId = document.PanelOrder[0];
        var viewModel = new PageEditorViewModel(history, pageBounds, document);

        var originalCommitted = viewModel.Committed;

        viewModel.BeginResizePanel(panelId);
        viewModel.UpdateResizePanel(panelId, new Rect2D(10, 10, 90, 90));
        Assert.NotEqual(originalCommitted, viewModel.Working);

        viewModel.CancelGesture();
        Assert.Equal(originalCommitted, viewModel.Working);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void SetBubbleText_RecordsUndoEntry()
    {
        var history = new EditorHistory();
        var pageBounds = new Rect2D(0, 0, 210, 297);
        var document = CreateSinglePanelDocument();
        var panelId = document.PanelOrder[0];
        var viewModel = new PageEditorViewModel(history, pageBounds, document);

        // Insert bubble
        var bubbleBounds = new Rect2D(20, 20, 50, 40);
        viewModel.InsertBubble(panelId, bubbleBounds, BubbleStylePreset.Speech);

        // Set text
        viewModel.SetBubbleText(panelId, 0, "Hello!");
        Assert.Equal("Hello!", viewModel.Working.Panels[panelId].Bubbles[0].Text);

        // Undo
        history.Undo();
        Assert.Equal("", viewModel.Working.Panels[panelId].Bubbles[0].Text);
    }

    [Fact]
    public void ResizePanel_BelowMinimumSize_LeavesWorkingUnchanged()
    {
        var history = new EditorHistory();
        var pageBounds = new Rect2D(0, 0, 210, 297);
        var document = CreateSinglePanelDocument();
        var panelId = document.PanelOrder[0];
        var viewModel = new PageEditorViewModel(history, pageBounds, document);

        var originalBounds = AnchorRing.BoundingBox(viewModel.Working.Panels[panelId].Shape.Anchors);

        viewModel.BeginResizePanel(panelId);
        viewModel.UpdateResizePanel(panelId, new Rect2D(10, 10, 5, 5)); // Below minimum

        // Working should remain at last valid state (still original)
        var currentBounds = AnchorRing.BoundingBox(viewModel.Working.Panels[panelId].Shape.Anchors);
        Assert.Equal(originalBounds.Left, currentBounds.Left);
        Assert.Equal(originalBounds.Top, currentBounds.Top);

        // LastError should be set
        Assert.NotNull(viewModel.LastError);
    }

    [Fact]
    public void ResizeBubble_Gesture_CreatesOneUndoEntry()
    {
        var history = new EditorHistory();
        var pageBounds = new Rect2D(0, 0, 210, 297);
        var document = CreateSinglePanelDocument();
        var panelId = document.PanelOrder[0];
        var viewModel = new PageEditorViewModel(history, pageBounds, document);

        // Insert bubble
        var bubbleBounds = new Rect2D(20, 20, 50, 40);
        viewModel.InsertBubble(panelId, bubbleBounds, BubbleStylePreset.Speech);

        var originalBubble = viewModel.Working.Panels[panelId].Bubbles[0];
        var originalBounds = AnchorRing.BoundingBox(originalBubble.Shape.Anchors);

        // Resize gesture
        viewModel.BeginResizeBubble(panelId, 0);
        viewModel.UpdateResizeBubble(panelId, 0, new Rect2D(20, 20, 70, 50));
        viewModel.CommitGesture();

        // Undo
        history.Undo();
        var restoredBubble = viewModel.Working.Panels[panelId].Bubbles[0];
        var restoredBounds = AnchorRing.BoundingBox(restoredBubble.Shape.Anchors);

        Assert.Equal(originalBounds.Width, restoredBounds.Width);
        Assert.Equal(originalBounds.Height, restoredBounds.Height);
    }

    private static (EditorHistory History, PageEditorViewModel ViewModel, PanelId PanelId) NewEditor(Rect2D? panelBounds = null)
    {
        var history = new EditorHistory();
        var document = CreateSinglePanelDocument(panelBounds);
        return (history, new PageEditorViewModel(history, new Rect2D(0, 0, 210, 297), document), document.PanelOrder[0]);
    }

    private static Rect2D BubbleBounds(PageEditorViewModel vm, PanelId panelId, int index) =>
        AnchorRing.BoundingBox(vm.Working.Panels[panelId].Bubbles[index].Shape.Anchors);

    [Fact]
    public void CreateBubble_AddsASelectedBubbleWithATailInsideItsPanel()
    {
        var (_, vm, panelId) = NewEditor();

        var index = vm.CreateBubble(panelId, new Point2D(15, 15));

        Assert.Equal(0, index);
        Assert.Equal(panelId, vm.SelectedPanelId);
        Assert.Equal(0, vm.SelectedBubbleIndex);
        var bubble = vm.Working.Panels[panelId].Bubbles[0];
        var tail = Assert.Single(bubble.Tails);
        var panelBounds = vm.PanelBounds(panelId);
        var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        Assert.True(bounds.Left >= panelBounds.Left && bounds.Top >= panelBounds.Top, "a click near the corner still lands the bubble inside the panel");
        Assert.True(tail.Target.Y > bounds.Bottom, "first tail points down, at the speaker below");
    }

    [Fact]
    public void MoveBubble_StopsAtThePanelEdge()
    {
        var (_, vm, panelId) = NewEditor();
        var index = vm.CreateBubble(panelId, new Point2D(60, 60));

        vm.BeginMoveBubble(panelId, index);
        vm.UpdateMoveBubble(panelId, index, 500, 0);
        vm.EndGesture(commit: true);

        Assert.Equal(vm.PanelBounds(panelId).Right, BubbleBounds(vm, panelId, index).Right, 6);
    }

    [Fact]
    public void ResizePanelGesture_ShrinkThenGrowBack_RestoresBubbleExactly()
    {
        var (_, vm, panelId) = NewEditor(new Rect2D(10, 10, 190, 190));
        var index = vm.CreateBubble(panelId, new Point2D(170, 170));
        var before = BubbleBounds(vm, panelId, index);

        vm.BeginResizePanel(panelId);
        vm.UpdateResizePanel(panelId, new Rect2D(10, 10, 40, 40));
        vm.UpdateResizePanel(panelId, new Rect2D(10, 10, 190, 190));
        vm.EndGesture(commit: true);

        var after = BubbleBounds(vm, panelId, index);
        Assert.Equal(before.Left, after.Left, 6);
        Assert.Equal(before.Width, after.Width, 6);
    }

    [Fact]
    public void ResizePanel_WithSnapping_LandsOnTheMarginAndReportsAGuide()
    {
        var (_, vm, panelId) = NewEditor(new Rect2D(10, 10, 100, 100));

        vm.BeginResizePanel(panelId);
        vm.UpdateResizePanel(panelId, Rect2D.FromEdges(10, 10, 198.5, 110), RectEdges.Right, snapTolerance: 3);

        Assert.Equal(200, vm.PanelBounds(panelId).Right, 6);
        Assert.NotEmpty(vm.ActiveGuides);

        vm.EndGesture(commit: true);
        Assert.Empty(vm.ActiveGuides);
    }

    [Fact]
    public void ApplyLayoutPreset_ReusesExistingPanelsSoTheirBubblesSurvive()
    {
        var (history, vm, panelId) = NewEditor();
        vm.CreateBubble(panelId, new Point2D(50, 50));

        vm.ApplyLayoutPreset(PanelLayoutPresets.All.First(p => p.ColumnsPerRow.Sum() == 6));

        Assert.Equal(6, vm.Working.PanelOrder.Count);
        Assert.Equal(panelId, vm.Working.PanelOrder[0]);
        Assert.Single(vm.Working.Panels[panelId].Bubbles);

        history.Undo();
        Assert.Single(vm.Working.PanelOrder);
    }

    [Fact]
    public void CreatePanelGesture_AddsASnappedPanelAndSelectsIt()
    {
        var (_, vm, panelId) = NewEditor(new Rect2D(10, 10, 100, 100));

        vm.BeginCreatePanel();
        vm.UpdateCreatePanel(Rect2D.FromEdges(115, 11, 199, 109), snapTolerance: 3);
        Assert.True(vm.CommitCreatePanel());

        var created = vm.SelectedPanelId!.Value;
        Assert.NotEqual(panelId, created);
        var bounds = vm.PanelBounds(created);
        Assert.Equal(114, bounds.Left, 6); // one 4mm gutter after the existing panel
        Assert.Equal(200, bounds.Right, 6); // on the right margin
        Assert.Equal(10, bounds.Top, 6); // aligned with the existing panel's top
    }

    [Fact]
    public void CurrentBubbleStyle_RestylesTheSelectedBubble()
    {
        var (_, vm, panelId) = NewEditor();
        var index = vm.CreateBubble(panelId, new Point2D(50, 50));

        vm.IsShoutStyle = true;

        Assert.Equal(BubbleStylePreset.Shout, vm.Working.Panels[panelId].Bubbles[index].Style);
        Assert.True(vm.IsShoutStyle);
        Assert.False(vm.IsSpeechStyle);
    }

    [Fact]
    public void DeleteSelection_ThenUndo_KeepsSelectionValid()
    {
        var (history, vm, panelId) = NewEditor();
        vm.CreateBubble(panelId, new Point2D(50, 50));

        vm.DeleteSelection();
        Assert.Empty(vm.Working.Panels[panelId].Bubbles);
        Assert.False(vm.HasSelectedBubble);
        Assert.True(vm.HasSelectedPanel);

        history.Undo();
        history.Undo(); // back past the bubble's creation too
        Assert.Empty(vm.Working.Panels[panelId].Bubbles);
        Assert.Equal(-1, vm.SelectedBubbleIndex);
    }

    [Fact]
    public void AddBubbleTail_WithoutATarget_AimsAwayFromTheExistingTail()
    {
        var (_, vm, panelId) = NewEditor(new Rect2D(10, 10, 190, 190));
        var index = vm.CreateBubble(panelId, new Point2D(100, 60));

        vm.AddBubbleTail(panelId, index);

        var tails = vm.Working.Panels[panelId].Bubbles[index].Tails;
        Assert.Equal(2, tails.Count);
        var distance = Math.Sqrt(Math.Pow(tails[0].Target.X - tails[1].Target.X, 2) + Math.Pow(tails[0].Target.Y - tails[1].Target.Y, 2));
        Assert.True(distance > 15, $"second tail should point somewhere else, but its tip is only {distance:0.0}mm from the first");
    }

    [Fact]
    public void RibbonCommands_AreOnlyEnabledForTheMatchingSelection()
    {
        var (_, vm, panelId) = NewEditor();

        Assert.False(vm.SplitColumnsCommand.CanExecute(null));
        Assert.False(vm.EditTextCommand.CanExecute(null));
        Assert.False(vm.IsPanelContext);
        Assert.False(vm.IsBubbleContext);

        vm.Select(panelId);
        Assert.True(vm.SplitColumnsCommand.CanExecute(null));
        Assert.False(vm.EditTextCommand.CanExecute(null));
        Assert.True(vm.IsPanelContext);

        vm.CreateBubble(panelId, new Point2D(50, 50));
        Assert.True(vm.EditTextCommand.CanExecute(null));
        Assert.True(vm.IsBubbleContext);
        Assert.False(vm.IsPanelContext);
    }

    [Fact]
    public void AddBubbleCommand_CreatesABubbleAndAsksForTheTextEditor()
    {
        var (_, vm, panelId) = NewEditor();
        (PanelId Panel, int Index)? requested = null;
        vm.TextEditRequested += (p, i) => requested = (p, i);

        vm.AddBubbleCommand.Execute(null);

        Assert.Single(vm.Working.Panels[panelId].Bubbles);
        Assert.Equal((panelId, 0), requested);
    }

    [Fact]
    public void ZoomCommands_AreForwardedToTheView()
    {
        var (_, vm, _) = NewEditor();
        var requests = new List<ViewportRequest>();
        vm.ViewportRequested += requests.Add;

        vm.ZoomInCommand.Execute(null);
        vm.FitPageCommand.Execute(null);

        Assert.Equal([ViewportRequest.ZoomIn, ViewportRequest.FitPage], requests);
    }

    // ---------------------------------------------------------------- layout lock

    [Fact]
    public void IsLayoutLocked_TogglesAsOneUndoEntry()
    {
        var (history, vm, _) = NewEditor();

        vm.IsLayoutLocked = true;
        Assert.True(vm.Working.LayoutLocked);
        Assert.True(vm.IsLayoutLocked);

        history.Undo();
        Assert.False(vm.Working.LayoutLocked);
        Assert.False(vm.IsLayoutLocked);

        history.Redo();
        Assert.True(vm.IsLayoutLocked);
    }

    [Fact]
    public void ResizePanel_WhileLocked_LeavesWorkingUnchanged()
    {
        var (_, vm, panelId) = NewEditor();
        vm.IsLayoutLocked = true;
        var originalBounds = vm.PanelBounds(panelId);

        vm.BeginResizePanel(panelId);
        vm.UpdateResizePanel(panelId, new Rect2D(10, 10, 90, 90));
        vm.EndGesture(commit: true);

        Assert.Equal(originalBounds, vm.PanelBounds(panelId));
        Assert.False(vm.IsGestureActive);
    }

    [Fact]
    public void MovePanel_WhileLocked_LeavesWorkingUnchanged()
    {
        var (_, vm, panelId) = NewEditor();
        vm.IsLayoutLocked = true;
        var originalBounds = vm.PanelBounds(panelId);

        vm.BeginMovePanel(panelId);
        vm.UpdateMovePanel(panelId, 20, 20, snapTolerance: 0);
        vm.EndGesture(commit: true);

        Assert.Equal(originalBounds, vm.PanelBounds(panelId));
    }

    [Fact]
    public void DragBoundary_WhileLocked_LeavesWorkingUnchanged()
    {
        var (_, vm, panelId) = NewEditor();
        vm.SplitPanel(panelId, BoundaryOrientation.Vertical, 0.5);
        var firstId = vm.Working.PanelOrder[0];
        var secondId = vm.Working.PanelOrder[1];
        var boundary = new PanelBoundaryDrag(BoundaryOrientation.Vertical, [firstId], [secondId]);
        var originalFirstBounds = vm.PanelBounds(firstId);
        var originalSecondBounds = vm.PanelBounds(secondId);

        vm.IsLayoutLocked = true;
        vm.BeginDragBoundary(boundary);
        vm.UpdateDragBoundary(boundary, originalFirstBounds.Right + 10);
        vm.EndGesture(commit: true);

        Assert.Equal(originalFirstBounds, vm.PanelBounds(firstId));
        Assert.Equal(originalSecondBounds, vm.PanelBounds(secondId));
    }

    [Fact]
    public void SplitPanel_WhileLocked_DoesNothingAndSetsLastError()
    {
        var (_, vm, panelId) = NewEditor();
        vm.IsLayoutLocked = true;

        vm.SplitPanel(panelId, BoundaryOrientation.Vertical, 0.5);

        Assert.Single(vm.Working.Panels);
        Assert.NotNull(vm.LastError);
    }

    [Fact]
    public void DeletePanel_WhileLocked_DoesNothing()
    {
        var (_, vm, panelId) = NewEditor();
        vm.IsLayoutLocked = true;

        vm.DeletePanel(panelId);

        Assert.True(vm.Working.Panels.ContainsKey(panelId));
    }

    [Fact]
    public void ApplyLayoutPreset_WhileLocked_DoesNothingAndSetsLastError()
    {
        var (_, vm, _) = NewEditor();
        vm.IsLayoutLocked = true;

        vm.ApplyLayoutPreset(PanelLayoutPresets.All.First(p => p.ColumnsPerRow.Sum() == 6));

        Assert.Single(vm.Working.PanelOrder);
        Assert.NotNull(vm.LastError);
    }

    [Fact]
    public void CreatePanelGesture_WhileLocked_DoesNothing()
    {
        var (_, vm, panelId) = NewEditor(new Rect2D(10, 10, 100, 100));
        vm.IsLayoutLocked = true;

        vm.BeginCreatePanel();
        vm.UpdateCreatePanel(Rect2D.FromEdges(115, 11, 199, 109), snapTolerance: 0);
        var committed = vm.CommitCreatePanel();

        Assert.False(committed);
        Assert.Single(vm.Working.Panels);
        Assert.True(vm.Working.Panels.ContainsKey(panelId));
        Assert.Null(vm.SelectedPanelId);
    }

    [Fact]
    public void NudgeSelection_WhileLocked_DoesNotMoveThePanelButStillMovesABubble()
    {
        var (_, vm, panelId) = NewEditor();
        var bubbleIndex = vm.CreateBubble(panelId, new Point2D(60, 60));
        var bubbleBefore = BubbleBounds(vm, panelId, bubbleIndex);
        vm.Select(panelId);
        var panelBoundsBefore = vm.PanelBounds(panelId);
        vm.IsLayoutLocked = true;

        vm.NudgeSelection(5, 5);
        Assert.Equal(panelBoundsBefore, vm.PanelBounds(panelId));

        vm.Select(panelId, bubbleIndex);
        vm.NudgeSelection(5, 5);
        var bubbleAfter = BubbleBounds(vm, panelId, bubbleIndex);
        Assert.NotEqual(bubbleBefore.Left, bubbleAfter.Left);
    }

    [Fact]
    public void BubbleAndCharacterEditing_StillWorkWhileLayoutIsLocked()
    {
        var (_, vm, panelId) = NewEditor();
        vm.IsLayoutLocked = true;

        var bubbleIndex = vm.CreateBubble(panelId, new Point2D(60, 60));
        Assert.True(bubbleIndex >= 0);
        vm.SetBubbleText(panelId, bubbleIndex, "Still works");
        Assert.Equal("Still works", vm.Working.Panels[panelId].Bubbles[bubbleIndex].Text);

        vm.BeginMoveBubble(panelId, bubbleIndex);
        vm.UpdateMoveBubble(panelId, bubbleIndex, 5, 5);
        vm.EndGesture(commit: true);
    }

    [Fact]
    public void SplitAndApplyLayoutCommands_AreDisabledWhileLocked()
    {
        var (_, vm, panelId) = NewEditor();
        vm.Select(panelId);
        Assert.True(vm.SplitColumnsCommand.CanExecute(null));
        Assert.True(vm.ApplyLayoutCommand.CanExecute(PanelLayoutPresets.All[0]));

        vm.IsLayoutLocked = true;

        Assert.False(vm.SplitColumnsCommand.CanExecute(null));
        Assert.False(vm.SplitRowsCommand.CanExecute(null));
        Assert.False(vm.ApplyLayoutCommand.CanExecute(PanelLayoutPresets.All[0]));
    }

    [Fact]
    public void DeleteSelectionCommand_StaysEnabledForABubbleButNotAPanelWhileLocked()
    {
        var (_, vm, panelId) = NewEditor();
        var bubbleIndex = vm.CreateBubble(panelId, new Point2D(60, 60));
        vm.IsLayoutLocked = true;

        vm.Select(panelId, bubbleIndex);
        Assert.True(vm.DeleteSelectionCommand.CanExecute(null));

        vm.Select(panelId);
        Assert.False(vm.DeleteSelectionCommand.CanExecute(null));
    }
}
