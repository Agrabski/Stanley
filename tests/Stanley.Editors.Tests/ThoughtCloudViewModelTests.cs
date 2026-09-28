using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>Insert › Thought cloud (issue #68): inserting, selecting, layout interactions and copy/paste, at the view-model level.</summary>
public class ThoughtCloudViewModelTests
{
    private static readonly Rect2D PageBounds = new(0, 0, 210, 297);

    private static (EditorHistory History, PageEditorViewModel ViewModel, PanelId PanelId) NewEditor(Rect2D? panelBounds = null)
    {
        var history = new EditorHistory();
        var panelId = PanelId.New();
        var bounds = panelBounds ?? new Rect2D(10, 10, 190, 277);
        var panel = new Panel(panelId, PanelShapes.Rectangle(bounds), Background: null, CharacterInstances: [], Bubbles: []);
        var document = new PageDocument([panelId], new Dictionary<PanelId, Panel> { [panelId] = panel });
        return (history, new PageEditorViewModel(history, PageBounds, document), panelId);
    }

    [Fact]
    public void InsertThoughtCloud_AddsACloudSelectsItAndIsOneUndoStep()
    {
        var (history, vm, panelId) = NewEditor();
        vm.Select(panelId);

        vm.InsertThoughtCloud();

        Assert.Equal(2, vm.Working.PanelOrder.Count);
        var cloudId = vm.SelectedPanelId;
        Assert.NotNull(cloudId);
        Assert.NotEqual(panelId, cloudId);
        var cloud = vm.Working.Panels[cloudId!.Value];
        Assert.Equal(PanelKind.Cloud, cloud.Kind);
        // A cloud with nothing leading out of it doesn't read as one - it gets a default trail.
        Assert.NotNull(cloud.Trail);
        // Last in PanelOrder: draws, and hit-tests, on top of the panel it overlaps.
        Assert.Equal(cloudId, vm.Working.PanelOrder[^1]);

        history.Undo();
        Assert.Single(vm.Working.PanelOrder);
        Assert.Equal(panelId, vm.Working.PanelOrder[0]);
    }

    [Fact]
    public void InsertThoughtCloud_SizesItAboutAThirdOfTheSelectedPanelNearItsTop()
    {
        var reference = new Rect2D(20, 20, 150, 90);
        var (_, vm, panelId) = NewEditor(reference);
        vm.Select(panelId);

        vm.InsertThoughtCloud();

        var cloudBounds = vm.PanelBounds(vm.SelectedPanelId!.Value);
        Assert.Equal(50, cloudBounds.Width, 3);
        Assert.Equal(30, cloudBounds.Height, 3);
        Assert.True(cloudBounds.Top >= reference.Top && cloudBounds.Top < reference.Top + reference.Height / 2);
    }

    [Fact]
    public void InsertThoughtCloud_WithNoSelection_SitsNearTheTopOfThePage()
    {
        var (_, vm, _) = NewEditor();
        vm.ClearSelection();

        vm.InsertThoughtCloud();

        var live = vm.Grid.LiveArea(PageBounds);
        var cloudBounds = vm.PanelBounds(vm.SelectedPanelId!.Value);
        Assert.Equal(live.Top, cloudBounds.Top, 3);
    }

    [Fact]
    public void InsertThoughtCloud_WhileLocked_DoesNothing()
    {
        var (_, vm, panelId) = NewEditor();
        vm.Select(panelId);
        vm.IsLayoutLocked = true;

        vm.InsertThoughtCloud();

        Assert.Single(vm.Working.PanelOrder);
        Assert.False(vm.InsertThoughtCloudCommand.CanExecute(null));
    }

    [Fact]
    public void MoveAndResizePanel_RegenerateTheCloudsOutline()
    {
        var (_, vm, panelId) = NewEditor();
        vm.Select(panelId);
        vm.InsertThoughtCloud();
        var cloudId = vm.SelectedPanelId!.Value;
        var before = vm.Working.Panels[cloudId].Shape;

        vm.BeginMovePanel(cloudId);
        vm.UpdateMovePanel(cloudId, 5, 5, snapTolerance: 0);
        vm.EndGesture(commit: true);

        var afterMove = vm.Working.Panels[cloudId];
        Assert.Equal(PanelKind.Cloud, afterMove.Kind);
        var beforeBounds = AnchorRing.BoundingBox(before.Anchors);
        var expectedAfterMove = PanelShapes.Cloud(beforeBounds with { X = beforeBounds.X + 5, Y = beforeBounds.Y + 5 });
        Assert.Equivalent(expectedAfterMove, afterMove.Shape, strict: true);

        var beforeResizeBounds = AnchorRing.BoundingBox(afterMove.Shape.Anchors);
        var grownBounds = beforeResizeBounds with { Width = beforeResizeBounds.Width + 20, Height = beforeResizeBounds.Height + 15 };
        vm.BeginResizePanel(cloudId);
        vm.UpdateResizePanel(cloudId, grownBounds);
        vm.EndGesture(commit: true);

        var afterResize = vm.Working.Panels[cloudId];
        Assert.Equal(PanelKind.Cloud, afterResize.Kind);
        Assert.Equivalent(PanelShapes.Cloud(grownBounds), afterResize.Shape, strict: true);
    }

    [Fact]
    public void SplitCommands_AreDisabledForASelectedCloud_AndSplittingItDoesNothing()
    {
        var (_, vm, panelId) = NewEditor();
        vm.Select(panelId);
        Assert.True(vm.SplitColumnsCommand.CanExecute(null));

        vm.InsertThoughtCloud();
        var cloudId = vm.SelectedPanelId!.Value;

        Assert.False(vm.SplitColumnsCommand.CanExecute(null));
        Assert.False(vm.SplitRowsCommand.CanExecute(null));

        vm.SplitPanel(cloudId, BoundaryOrientation.Vertical, 0.5);
        Assert.Equal(2, vm.Working.PanelOrder.Count); // unchanged: the panel and the cloud, no split-off half
        Assert.Equal(PanelKind.Cloud, vm.Working.Panels[cloudId].Kind);
    }

    [Fact]
    public void ApplyLayoutPreset_KeepsACloudFloatingAndUnchangedOnTopOfTheNewGrid()
    {
        var (_, vm, panelId) = NewEditor();
        vm.Select(panelId);
        vm.InsertThoughtCloud();
        var cloudId = vm.SelectedPanelId!.Value;
        var cloudBefore = vm.Working.Panels[cloudId];

        vm.ApplyLayoutPreset(PanelLayoutPresets.All.First(p => p.ColumnsPerRow.Sum() == 6));

        Assert.Equal(7, vm.Working.PanelOrder.Count); // 6 new grid panels + the untouched cloud
        Assert.Equal(cloudBefore, vm.Working.Panels[cloudId]); // byte-for-byte unchanged
        Assert.Equal(cloudId, vm.Working.PanelOrder[^1]); // still floats on top
    }

    [Fact]
    public void CopyAndPaste_KeepsTheCloudACloud()
    {
        var (_, vm, panelId) = NewEditor();
        vm.Select(panelId);
        vm.InsertThoughtCloud();
        var cloudId = vm.SelectedPanelId!.Value;
        vm.Select(cloudId);

        Assert.True(vm.Copy());
        Assert.True(vm.Paste());

        var pastedId = vm.SelectedPanelId!.Value;
        Assert.NotEqual(cloudId, pastedId);
        var pasted = vm.Working.Panels[pastedId];
        Assert.Equal(PanelKind.Cloud, pasted.Kind);
        Assert.Equal(PanelShapes.Lobes * PanelShapes.AnchorsPerLobe, pasted.Shape.Anchors.Count);
    }

    [Fact]
    public void DuplicatePanel_KeepsTheCloudACloud()
    {
        var (_, vm, panelId) = NewEditor();
        vm.Select(panelId);
        vm.InsertThoughtCloud();
        var cloudId = vm.SelectedPanelId!.Value;

        var copyId = vm.BeginDuplicatePanel(cloudId);
        Assert.NotNull(copyId);
        vm.UpdateMovePanel(copyId!.Value, 5, 5, snapTolerance: 0);
        vm.EndGesture(commit: true);

        Assert.Equal(PanelKind.Cloud, vm.Working.Panels[copyId.Value].Kind);
        Assert.Equal(PanelKind.Cloud, vm.Working.Panels[cloudId].Kind); // the original is untouched
    }

    [Fact]
    public void DeletePanel_RemovesTheCloud()
    {
        var (history, vm, panelId) = NewEditor();
        vm.Select(panelId);
        vm.InsertThoughtCloud();
        var cloudId = vm.SelectedPanelId!.Value;

        vm.DeletePanel(cloudId);

        Assert.Single(vm.Working.PanelOrder);
        Assert.DoesNotContain(cloudId, vm.Working.Panels.Keys);

        history.Undo();
        Assert.Contains(cloudId, vm.Working.Panels.Keys);
    }

    // ---------------------------------------------------------------- reaching the trail from the UI

    [Fact]
    public void InsertThoughtCloud_AimsTheDefaultTrailAtTheLowerPartOfTheReferencePanel()
    {
        var reference = new Rect2D(20, 20, 150, 90);
        var (_, vm, panelId) = NewEditor(reference);
        vm.Select(panelId);

        vm.InsertThoughtCloud();

        var trail = vm.Working.Panels[vm.SelectedPanelId!.Value].Trail;
        Assert.NotNull(trail);
        Assert.True(trail!.Target.Y > reference.Top + reference.Height / 2, "should aim towards the lower part of the reference panel, not its middle or top");
        Assert.True(trail.Target.X >= reference.Left && trail.Target.X <= reference.Right);
    }

    [Fact]
    public void InsertThoughtCloud_WithNoSelection_AimsTheDefaultTrailStraightDown()
    {
        var (_, vm, _) = NewEditor();
        vm.ClearSelection();

        vm.InsertThoughtCloud();

        var cloudId = vm.SelectedPanelId!.Value;
        var cloudBounds = vm.PanelBounds(cloudId);
        var trail = vm.Working.Panels[cloudId].Trail;
        Assert.NotNull(trail);
        Assert.Equal(cloudBounds.MidX, trail!.Target.X, 3);
        Assert.True(trail.Target.Y > cloudBounds.Bottom, "should point straight down, below the cloud itself");
    }

    [Fact]
    public void MoveTrailTarget_ThroughTheViewModel_DragsOnlyTheTipAsOneUndoStep()
    {
        var (history, vm, panelId) = NewEditor();
        vm.Select(panelId);
        vm.InsertThoughtCloud();
        var cloudId = vm.SelectedPanelId!.Value;
        var attachmentBefore = vm.Working.Panels[cloudId].Trail!.AttachmentT;

        vm.BeginMoveTrailTarget(cloudId);
        vm.UpdateMoveTrailTarget(cloudId, new Point2D(15, 15));
        vm.EndGesture(commit: true);

        var trail = vm.Working.Panels[cloudId].Trail;
        Assert.Equal(new Point2D(15, 15), trail!.Target);
        Assert.Equal(attachmentBefore, trail.AttachmentT, 6); // the base didn't move

        // Undoing the drag alone (not the insert too) proves it was its own, separate step.
        history.Undo();
        Assert.NotEqual(new Point2D(15, 15), vm.Working.Panels[cloudId].Trail!.Target);
        Assert.Contains(cloudId, vm.Working.Panels.Keys); // the cloud itself is still there
    }

    [Fact]
    public void SlideTrailAttachment_ThroughTheViewModel_MovesOnlyTheBase()
    {
        var (_, vm, panelId) = NewEditor();
        vm.Select(panelId);
        vm.InsertThoughtCloud();
        var cloudId = vm.SelectedPanelId!.Value;
        var trailBefore = vm.Working.Panels[cloudId].Trail!;
        var cloudBounds = vm.PanelBounds(cloudId);

        vm.BeginSlideTrailAttachment(cloudId);
        vm.UpdateSlideTrailAttachment(cloudId, new Point2D(cloudBounds.Left, cloudBounds.MidY));
        vm.EndGesture(commit: true);

        var trail = vm.Working.Panels[cloudId].Trail;
        Assert.Equal(trailBefore.Target, trail!.Target); // the tip stayed put
        Assert.NotEqual(trailBefore.AttachmentT, trail.AttachmentT); // only the base slid
    }

    [Fact]
    public void RemoveThoughtTrail_ThenAddThoughtTrail_EachOneUndoStep()
    {
        var (history, vm, panelId) = NewEditor();
        vm.Select(panelId);
        vm.InsertThoughtCloud();
        var cloudId = vm.SelectedPanelId!.Value;

        vm.RemoveThoughtTrail(cloudId);
        Assert.Null(vm.Working.Panels[cloudId].Trail);

        history.Undo();
        Assert.NotNull(vm.Working.Panels[cloudId].Trail);

        vm.RemoveThoughtTrail(cloudId);
        Assert.Null(vm.Working.Panels[cloudId].Trail);

        vm.AddThoughtTrail(cloudId);
        Assert.NotNull(vm.Working.Panels[cloudId].Trail);

        history.Undo(); // undoes the add
        Assert.Null(vm.Working.Panels[cloudId].Trail);
        history.Undo(); // undoes the remove
        Assert.NotNull(vm.Working.Panels[cloudId].Trail);
    }
}
