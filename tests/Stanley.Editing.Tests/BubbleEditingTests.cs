using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Xunit;

namespace Stanley.Editing.Tests;

public class BubbleEditingTests
{
    private static Bubble NewBubble(Rect2D? bounds = null, BubbleStylePreset style = BubbleStylePreset.Speech) =>
        BubbleEditing.Create(bounds ?? new Rect2D(0, 0, 60, 40), style).Value;

    [Fact]
    public void Create_BelowMinimumSize_Fails()
    {
        var result = BubbleEditing.Create(new Rect2D(0, 0, 5, 5), BubbleStylePreset.Speech);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Resize_ScalesShapeButLeavesTailTargetsFixed()
    {
        var bubble = NewBubble();
        var addResult = BubbleEditing.AddTail(bubble, new Point2D(100, 300));
        bubble = addResult.Value;

        var result = BubbleEditing.Resize(bubble, new Rect2D(0, 0, 120, 80));

        Assert.True(result.IsValid);
        Assert.Equal(100, result.Value.Tails[0].Target.X);
        Assert.Equal(300, result.Value.Tails[0].Target.Y);
        var newBounds = AnchorRing.BoundingBox(result.Value.Shape.Anchors);
        Assert.True(newBounds.Width > 100);
    }

    [Fact]
    public void Resize_BelowMinimumSize_Fails()
    {
        var bubble = NewBubble();
        var result = BubbleEditing.Resize(bubble, new Rect2D(0, 0, 1, 1));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void AddTail_SpreadsDefaultAttachmentPointsApart()
    {
        var bubble = NewBubble();
        bubble = BubbleEditing.AddTail(bubble, new Point2D(0, 300)).Value;
        var t1 = bubble.Tails[0].AttachmentT;
        bubble = BubbleEditing.AddTail(bubble, new Point2D(300, 300)).Value;
        var t2 = bubble.Tails[1].AttachmentT;
        bubble = BubbleEditing.AddTail(bubble, new Point2D(-100, 300)).Value;
        var t3 = bubble.Tails[2].AttachmentT;

        Assert.Equal(3, bubble.Tails.Count);
        Assert.NotEqual(t1, t2, 3);
        Assert.NotEqual(t2, t3, 3);
    }

    [Fact]
    public void SetStyle_RegeneratesShapeButKeepsTailPlacement()
    {
        var bubble = NewBubble();
        bubble = BubbleEditing.AddTail(bubble, new Point2D(100, 300)).Value;
        var attachmentBefore = bubble.Tails[0].AttachmentT;
        var targetBefore = bubble.Tails[0].Target;

        var result = BubbleEditing.SetStyle(bubble, BubbleStylePreset.Shout);

        Assert.True(result.IsValid);
        Assert.Equal(BubbleStylePreset.Shout, result.Value.Style);
        Assert.Equal(TailKind.JaggedTriangle, result.Value.Tails[0].Kind);
        Assert.Equal(attachmentBefore, result.Value.Tails[0].AttachmentT);
        Assert.Equal(targetBefore, result.Value.Tails[0].Target);
    }

    [Fact]
    public void RemoveTail_TakesItOutOfTheList()
    {
        var bubble = NewBubble();
        bubble = BubbleEditing.AddTail(bubble, new Point2D(100, 300)).Value;

        var result = BubbleEditing.RemoveTail(bubble, 0);

        Assert.True(result.IsValid);
        Assert.Empty(result.Value.Tails);
    }

    [Fact]
    public void RemoveTail_UnknownIndex_Fails()
    {
        var bubble = NewBubble();
        var result = BubbleEditing.RemoveTail(bubble, 0);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void SlideTailAttachment_MovesToNearestPointOnShape()
    {
        var bubble = NewBubble();
        bubble = BubbleEditing.AddTail(bubble, new Point2D(100, 300)).Value;
        var knownPoint = AnchorRing.PointAt(bubble.Shape.Anchors, 0.37);

        var result = BubbleEditing.SlideTailAttachment(bubble, 0, knownPoint);

        Assert.True(result.IsValid);
        var found = AnchorRing.PointAt(result.Value.Shape.Anchors, result.Value.Tails[0].AttachmentT);
        var dist = Math.Sqrt(Math.Pow(found.X - knownPoint.X, 2) + Math.Pow(found.Y - knownPoint.Y, 2));
        Assert.True(dist < 3.0);
    }

    [Fact]
    public void SetText_TooLong_Fails()
    {
        var bubble = NewBubble();
        var result = BubbleEditing.SetText(bubble, new string('x', BubbleEditing.MaxTextLength + 1));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void SetText_WithinLimit_Succeeds()
    {
        var bubble = NewBubble();
        var result = BubbleEditing.SetText(bubble, "Hello!");
        Assert.True(result.IsValid);
        Assert.Equal("Hello!", result.Value.Text);
    }
}
