using Stanley.ProjectModel.Bubbles;
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

    private static Bubble NewBubble(Rect2D bounds) => BubbleEditing.Create(bounds, BubbleStylePreset.Speech).Value;

    [Fact]
    public void DragBoundary_WithAGap_KeepsTheGutterWidth()
    {
        var left = NewPanel(new Rect2D(10, 10, 93, 100));
        var right = NewPanel(new Rect2D(107, 10, 93, 100));
        var panels = new Dictionary<PanelId, Panel> { [left.Id] = left, [right.Id] = right };
        var boundary = new PanelBoundaryDrag(BoundaryOrientation.Vertical, [left.Id], [right.Id], Gap: 4);

        var result = PanelLayoutEditing.DragBoundary(panels, boundary, 80, PageBounds);

        Assert.True(result.IsValid);
        Assert.Equal(80, AnchorRing.BoundingBox(result.Value[left.Id].Shape.Anchors).Right, 6);
        Assert.Equal(84, AnchorRing.BoundingBox(result.Value[right.Id].Shape.Anchors).Left, 6);
    }

    [Fact]
    public void Split_WithGutter_LeavesAGapAndSendsEachBubbleToTheHalfItSitsIn()
    {
        var leftBubble = NewBubble(new Rect2D(20, 20, 30, 20));
        var rightBubble = NewBubble(new Rect2D(150, 20, 30, 20));
        var panel = NewPanel(new Rect2D(0, 0, 200, 100)) with { Bubbles = [leftBubble, rightBubble] };

        var result = PanelLayoutEditing.Split(panel, BoundaryOrientation.Vertical, 0.5, gutter: 4);

        Assert.True(result.IsValid);
        var first = AnchorRing.BoundingBox(result.Value.First.Shape.Anchors);
        var second = AnchorRing.BoundingBox(result.Value.Second.Shape.Anchors);
        Assert.Equal(98, first.Right, 6);
        Assert.Equal(102, second.Left, 6);
        Assert.Equal(leftBubble.Id, Assert.Single(result.Value.First.Bubbles).Id);
        Assert.Equal(rightBubble.Id, Assert.Single(result.Value.Second.Bubbles).Id);
    }

    [Fact]
    public void Resize_ScalesBubblesAlongInsideThePanel()
    {
        var bubble = NewBubble(new Rect2D(60, 60, 30, 20));
        var panel = NewPanel(new Rect2D(0, 0, 100, 100)) with { Bubbles = [bubble] };

        var result = PanelLayoutEditing.Resize(panel, new Rect2D(100, 100, 50, 50), PageBounds);

        Assert.True(result.IsValid);
        var bubbleBounds = AnchorRing.BoundingBox(result.Value.Bubbles[0].Shape.Anchors);
        Assert.Equal(130, bubbleBounds.Left, 6);
        Assert.Equal(130, bubbleBounds.Top, 6);
        Assert.Equal(15, bubbleBounds.Width, 6);
        Assert.Equal(10, bubbleBounds.Height, 6);
        Assert.Equal(Bubble.DefaultFontSizePt / 2, result.Value.Bubbles[0].FontSizePt);
    }

    [Fact]
    public void Move_ClampsToThePageAndTakesBubblesAlong()
    {
        var bubble = NewBubble(new Rect2D(10, 10, 30, 20));
        var panel = NewPanel(new Rect2D(0, 0, 100, 100)) with { Bubbles = [bubble] };

        var result = PanelLayoutEditing.Move(panel, 500, 20, PageBounds);

        Assert.True(result.IsValid);
        var bounds = AnchorRing.BoundingBox(result.Value.Shape.Anchors);
        Assert.Equal(PageBounds.Right, bounds.Right, 6);
        Assert.Equal(20, bounds.Top, 6);
        var bubbleBounds = AnchorRing.BoundingBox(result.Value.Bubbles[0].Shape.Anchors);
        Assert.Equal(bounds.Left + 10, bubbleBounds.Left, 6);
        Assert.Equal(30, bubbleBounds.Top, 6);
    }

    [Fact]
    public void GridLayout_TilesTheLiveAreaWithGutters()
    {
        var result = PanelLayoutEditing.GridLayout(PageBounds, new PanelGrid(10, 4), [1, 2]);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Value.Count);
        var (top, bottomLeft, bottomRight) = (result.Value[0], result.Value[1], result.Value[2]);
        Assert.Equal(10, top.Left, 6);
        Assert.Equal(200, top.Right, 6);
        Assert.Equal(4, bottomLeft.Top - top.Bottom, 6);
        Assert.Equal(4, bottomRight.Left - bottomLeft.Right, 6);
        Assert.Equal(287, bottomRight.Bottom, 6);
    }

    [Fact]
    public void GridLayout_TooManyPanels_Fails()
    {
        var result = PanelLayoutEditing.GridLayout(PageBounds, new PanelGrid(10, 4), [20]);
        Assert.False(result.IsValid);
    }
}
