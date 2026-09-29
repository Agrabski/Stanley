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
    public void SetStyle_RegeneratesShapeAndRestylesTails()
    {
        var bubble = NewBubble();
        bubble = BubbleEditing.AddTail(bubble, new Point2D(100, 300)).Value;
        var targetBefore = bubble.Tails[0].Target;

        var result = BubbleEditing.SetStyle(bubble, BubbleStylePreset.Shout);

        Assert.True(result.IsValid);
        Assert.Equal(BubbleStylePreset.Shout, result.Value.Style);
        Assert.Equal(BubbleStylePresets.GenerateShape(BubbleStylePreset.Shout, new Rect2D(0, 0, 60, 40)), result.Value.Shape, ShapeComparer);
        Assert.Equal(TailKind.JaggedTriangle, result.Value.Tails[0].Kind);
        Assert.Equal(targetBefore, result.Value.Tails[0].Target);
    }

    // #122: the four switches that used to throw a tail's base somewhere else on the bubble.
    [Theory]
    [InlineData(BubbleStylePreset.Speech, BubbleStylePreset.Shout)]
    [InlineData(BubbleStylePreset.Whisper, BubbleStylePreset.Shout)]
    [InlineData(BubbleStylePreset.Shout, BubbleStylePreset.Speech)]
    [InlineData(BubbleStylePreset.Shout, BubbleStylePreset.Whisper)]
    [InlineData(BubbleStylePreset.Speech, BubbleStylePreset.Whisper)]
    public void SetStyle_KeepsEachTailLeavingTheBubbleWhereItDid(BubbleStylePreset from, BubbleStylePreset to)
    {
        var bubble = NewBubble(style: from);
        bubble = bubble with
        {
            Tails = [.. new[] { 0, 0.05, 0.3, 0.5, 0.55, 0.75, 0.9 }.Select(t => new BubbleTail(t, new Point2D(100, 300), BubbleStylePresets.TailKindFor(from)))]
        };
        var centre = new Point2D(30, 20);

        var restyled = BubbleEditing.SetStyle(bubble, to).Value;

        for (var i = 0; i < bubble.Tails.Count; i++)
        {
            var before = AnchorRing.PointAt(bubble.Shape.Anchors, bubble.Tails[i].AttachmentT);
            var after = AnchorRing.PointAt(restyled.Shape.Anchors, restyled.Tails[i].AttachmentT);
            var turn = Math.IEEERemainder(Direction(centre, after) - Direction(centre, before), 2 * Math.PI);
            Assert.True(Math.Abs(turn) < 1e-6, $"the tail at t={bubble.Tails[i].AttachmentT} swung {turn} rad round the bubble");
        }
    }

    [Fact]
    public void SetStyle_ThereAndBackAgain_GivesBackTheSameBubble()
    {
        var bubble = NewBubble();
        bubble = BubbleEditing.AddTail(bubble, new Point2D(100, 300)).Value;
        bubble = bubble with { Tails = [bubble.Tails[0] with { AttachmentT = 0.62 }] };

        var roundTrip = bubble;
        for (var i = 0; i < 5; i++)
            roundTrip = BubbleEditing.SetStyle(BubbleEditing.SetStyle(roundTrip, BubbleStylePreset.Shout).Value, BubbleStylePreset.Speech).Value;

        Assert.Equal(bubble.Shape, roundTrip.Shape, ShapeComparer);
        Assert.Equal(bubble.Tails[0].AttachmentT, roundTrip.Tails[0].AttachmentT, 6);
        Assert.Equal(bubble.Tails[0].Target, roundTrip.Tails[0].Target);
    }

    private static double Direction(Point2D centre, Point2D p) => Math.Atan2(p.Y - centre.Y, p.X - centre.X);

    private static readonly IEqualityComparer<BubbleShape> ShapeComparer = EqualityComparer<BubbleShape>.Create((a, b) =>
        a!.Anchors.Count == b!.Anchors.Count && a.Anchors.Zip(b.Anchors).All(p =>
            Math.Abs(p.First.Point.X - p.Second.Point.X) < 1e-6 && Math.Abs(p.First.Point.Y - p.Second.Point.Y) < 1e-6));

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

    [Fact]
    public void GrowToFit_AtScale1_ChangesNothing()
    {
        var bubble = NewBubble(new Rect2D(10, 10, 60, 40));

        var grown = BubbleEditing.GrowToFit(bubble, 1);

        Assert.Same(bubble, grown);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.999)]
    public void GrowToFit_NeverShrinks(double scale)
    {
        var bubble = NewBubble(new Rect2D(10, 10, 60, 40));

        var grown = BubbleEditing.GrowToFit(bubble, scale);

        Assert.Same(bubble, grown);
    }

    [Fact]
    public void GrowToFit_ScalesAroundItsOwnCentreKeepingAspectRatio()
    {
        var bounds = new Rect2D(10, 20, 60, 40);
        var bubble = NewBubble(bounds);

        var grown = BubbleEditing.GrowToFit(bubble, 1.5);

        var newBounds = AnchorRing.BoundingBox(grown.Shape.Anchors);
        Assert.Equal(bounds.MidX, newBounds.MidX, 6); // same centre
        Assert.Equal(bounds.MidY, newBounds.MidY, 6);
        Assert.Equal(90, newBounds.Width, 6); // 60 * 1.5
        Assert.Equal(60, newBounds.Height, 6); // 40 * 1.5, so a 60x40 oval stays 3:2
        Assert.Equal(bounds.Width / bounds.Height, newBounds.Width / newBounds.Height, 6);
    }

    [Fact]
    public void GrowToFit_KeepsEveryTailsTargetAndItsFractionAlongTheOutline()
    {
        var bubble = NewBubble(new Rect2D(0, 0, 60, 40));
        bubble = BubbleEditing.AddTail(bubble, new Point2D(-40, 100)).Value;
        var attachmentT = bubble.Tails[0].AttachmentT;
        var target = bubble.Tails[0].Target;
        var attachmentPoint = AnchorRing.PointAt(bubble.Shape.Anchors, attachmentT);

        var grown = BubbleEditing.GrowToFit(bubble, 2);

        Assert.Equal(target, grown.Tails[0].Target); // untouched - resizing shouldn't drag whoever it points at
        Assert.Equal(attachmentT, grown.Tails[0].AttachmentT, 9); // same fraction along the ring
        var grownAttachmentPoint = AnchorRing.PointAt(grown.Shape.Anchors, attachmentT);
        Assert.NotEqual(attachmentPoint, grownAttachmentPoint); // but it slid out to the bigger outline
    }

    [Fact]
    public void GrowToFit_LargerScaleGrowsFurther()
    {
        var bubble = NewBubble(new Rect2D(0, 0, 60, 40));

        var grownALittle = AnchorRing.BoundingBox(BubbleEditing.GrowToFit(bubble, 1.2).Shape.Anchors);
        var grownALot = AnchorRing.BoundingBox(BubbleEditing.GrowToFit(bubble, 2).Shape.Anchors);

        Assert.True(grownALot.Width > grownALittle.Width);
        Assert.True(grownALot.Height > grownALittle.Height);
    }
}
