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
}
