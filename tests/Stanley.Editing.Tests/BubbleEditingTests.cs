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

    [Fact]
    public void Move_TranslatesTheShapeButNotTailTargets()
    {
        var bubble = BubbleEditing.AddTail(NewBubble(new Rect2D(0, 0, 60, 40)), new Point2D(30, 100)).Value;

        var moved = BubbleEditing.Move(bubble, 10, 5).Value;

        var bounds = AnchorRing.BoundingBox(moved.Shape.Anchors);
        Assert.Equal(10, bounds.Left, 6);
        Assert.Equal(5, bounds.Top, 6);
        Assert.Equal(new Point2D(30, 100), moved.Tails[0].Target);
    }

    [Fact]
    public void Move_WithTails_CarriesTheTipsAlong()
    {
        var bubble = BubbleEditing.AddTail(NewBubble(new Rect2D(0, 0, 60, 40)), new Point2D(30, 100)).Value;

        var moved = BubbleEditing.Move(bubble, 10, 5, withTails: true).Value;

        Assert.Equal(10, AnchorRing.BoundingBox(moved.Shape.Anchors).Left, 6);
        Assert.Equal(new Point2D(40, 105), moved.Tails[0].Target);
        Assert.Equal(bubble.Tails[0].AttachmentT, moved.Tails[0].AttachmentT);
    }

    [Fact]
    public void OutOfTheWay_StepsANewBubbleAsideFromOneInTheSameSpot()
    {
        var panel = new Rect2D(0, 0, 200, 150);
        var spot = new Rect2D(50, 30, 42, 26);

        Assert.Equal(spot, BubbleEditing.OutOfTheWay(spot, [new Rect2D(120, 30, 42, 26)], panel)); // clear already
        var second = BubbleEditing.OutOfTheWay(spot, [spot], panel);
        Assert.Equal(spot with { X = 56, Y = 36 }, second);
        var third = BubbleEditing.OutOfTheWay(spot, [spot, second], panel);
        Assert.Equal(spot with { X = 62, Y = 42 }, third);
    }

    [Fact]
    public void OutOfTheWay_StepsBackUpWhenThereIsNoRoomBelow()
    {
        var panel = new Rect2D(0, 0, 100, 60);
        var corner = new Rect2D(58, 34, 42, 26); // in the bottom right corner

        Assert.Equal(corner with { X = 52, Y = 28 }, BubbleEditing.OutOfTheWay(corner, [corner], panel));
        Assert.Equal(panel, BubbleEditing.OutOfTheWay(panel, [panel], panel)); // no room anywhere
    }

    [Fact]
    public void KeepInside_SlidesTheBubbleAndClampsTailTargetsIntoTheContainer()
    {
        var bubble = BubbleEditing.AddTail(NewBubble(new Rect2D(90, -10, 60, 40)), new Point2D(300, 300)).Value;
        var container = new Rect2D(0, 0, 120, 120);

        var kept = BubbleEditing.KeepInside(bubble, container);

        var bounds = AnchorRing.BoundingBox(kept.Shape.Anchors);
        Assert.Equal(60, bounds.Left, 6);
        Assert.Equal(0, bounds.Top, 6);
        Assert.Equal(60, bounds.Width, 6);
        Assert.Equal(new Point2D(120, 120), kept.Tails[0].Target);
    }

    [Fact]
    public void KeepInside_ShrinksABubbleBiggerThanTheContainer()
    {
        var kept = BubbleEditing.KeepInside(NewBubble(new Rect2D(0, 0, 200, 40)), new Rect2D(0, 0, 100, 100));

        Assert.Equal(100, AnchorRing.BoundingBox(kept.Shape.Anchors).Width, 6);
    }

    [Fact]
    public void Refit_KeepsTheBubblesRelativePositionInItsPanel()
    {
        var bubble = NewBubble(new Rect2D(40, 40, 20, 20));

        var refit = BubbleEditing.Refit(bubble, new Rect2D(0, 0, 100, 100), new Rect2D(0, 0, 200, 100));

        var bounds = AnchorRing.BoundingBox(refit.Shape.Anchors);
        Assert.Equal(100, bounds.MidX, 6);
        Assert.Equal(50, bounds.MidY, 6);
        Assert.Equal(20, bounds.Width, 6);
    }
}
