using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Ids;
using Xunit;

namespace Stanley.Editing.Tests;

public class PanelLayoutEditingTests
{
    private static readonly Rect2D PageBounds = new(0, 0, 210, 297);

    private static Panel NewPanel(Rect2D bounds) =>
        new(PanelId.New(), PanelShapes.Rectangle(bounds), Background: null, CharacterInstances: [], Bubbles: []);

    [Fact]
    public void Resize_WithinPage_Succeeds()
    {
        var panel = NewPanel(new Rect2D(0, 0, 100, 100));
        var result = PanelLayoutEditing.Resize(panel, new Rect2D(0, 0, 150, 150), PageBounds);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Resize_BelowMinimumSize_Fails()
    {
        var panel = NewPanel(new Rect2D(0, 0, 100, 100));
        var result = PanelLayoutEditing.Resize(panel, new Rect2D(0, 0, 5, 5), PageBounds);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Resize_PastPageBounds_Fails()
    {
        var panel = NewPanel(new Rect2D(0, 0, 100, 100));
        var result = PanelLayoutEditing.Resize(panel, new Rect2D(0, 0, 300, 300), PageBounds);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Split_Vertical_ProducesTwoAdjacentPanels()
    {
        var panel = NewPanel(new Rect2D(0, 0, 200, 100));
        var result = PanelLayoutEditing.Split(panel, BoundaryOrientation.Vertical, 0.5);

        Assert.True(result.IsValid);
        var firstBounds = AnchorRing.BoundingBox(result.Value.First.Shape.Anchors);
        var secondBounds = AnchorRing.BoundingBox(result.Value.Second.Shape.Anchors);
        Assert.Equal(100, firstBounds.Width, 3);
        Assert.Equal(100, secondBounds.Width, 3);
        Assert.Equal(firstBounds.Right, secondBounds.Left, 3);
        Assert.Equal(panel.Id, result.Value.First.Id);
        Assert.NotEqual(panel.Id, result.Value.Second.Id);
    }

    [Fact]
    public void Split_WouldLeaveAPanelBelowMinimum_Fails()
    {
        var panel = NewPanel(new Rect2D(0, 0, 200, 100));
        var result = PanelLayoutEditing.Split(panel, BoundaryOrientation.Vertical, 0.02);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void DragBoundary_MovesBothAdjacentPanelsEdgesTogether()
    {
        var left = NewPanel(new Rect2D(0, 0, 100, 100));
        var right = NewPanel(new Rect2D(100, 0, 100, 100));
        var panels = new Dictionary<PanelId, Panel> { [left.Id] = left, [right.Id] = right };
        var boundary = new PanelBoundaryDrag(BoundaryOrientation.Vertical, [left.Id], [right.Id]);

        var result = PanelLayoutEditing.DragBoundary(panels, boundary, 130, PageBounds);

        Assert.True(result.IsValid);
        var newLeftBounds = AnchorRing.BoundingBox(result.Value[left.Id].Shape.Anchors);
        var newRightBounds = AnchorRing.BoundingBox(result.Value[right.Id].Shape.Anchors);
        Assert.Equal(130, newLeftBounds.Right, 3);
        Assert.Equal(130, newRightBounds.Left, 3);
    }

    [Fact]
    public void DragBoundary_WouldCollapseAPanel_FailsAndLeavesInputUntouched()
    {
        var left = NewPanel(new Rect2D(0, 0, 100, 100));
        var right = NewPanel(new Rect2D(100, 0, 100, 100));
        var panels = new Dictionary<PanelId, Panel> { [left.Id] = left, [right.Id] = right };
        var boundary = new PanelBoundaryDrag(BoundaryOrientation.Vertical, [left.Id], [right.Id]);

        var result = PanelLayoutEditing.DragBoundary(panels, boundary, 5, PageBounds);

        Assert.False(result.IsValid);
        Assert.Equal(left, panels[left.Id]);
    }
}
