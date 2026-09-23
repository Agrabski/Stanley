using SkiaSharp;
using Stanley.Bubbles;
using Xunit;

namespace Stanley.Bubbles.Tests;

public class SpeechBubbleTests
{
    [Theory]
    [InlineData(BubbleStylePreset.Speech)]
    [InlineData(BubbleStylePreset.Shout)]
    [InlineData(BubbleStylePreset.Whisper)]
    public void BuildRenderPath_UnionsOutlineWithEveryTail(BubbleStylePreset preset)
    {
        var bubble = new SpeechBubble(new SKRect(100, 100, 300, 220), preset);
        var target1 = new SKPoint(50, 400);
        var target2 = new SKPoint(350, 420);
        bubble.AddTail(target1);
        bubble.AddTail(target2);

        using var renderPath = bubble.BuildRenderPath();
        var bounds = renderPath.TightBounds;

        Assert.False(renderPath.IsEmpty);
        // SKRect.Contains excludes the right/bottom edge, and a tail's target is often
        // exactly the extreme point of the unioned shape, so check inclusive bounds instead.
        AssertWithinBounds(bounds, target1, "the first tail's target");
        AssertWithinBounds(bounds, target2, "the second tail's target");
        AssertWithinBounds(bounds, new SKPoint(bubble.Bounds.MidX, bubble.Bounds.MidY), "the outline's centre");
    }

    private static void AssertWithinBounds(SKRect bounds, SKPoint point, string description)
    {
        Assert.True(point.X >= bounds.Left - 0.01f && point.X <= bounds.Right + 0.01f, $"render path should reach {description} (X)");
        Assert.True(point.Y >= bounds.Top - 0.01f && point.Y <= bounds.Bottom + 0.01f, $"render path should reach {description} (Y)");
    }

    [Fact]
    public void AddTail_SpreadsDefaultAttachmentPointsApart()
    {
        var bubble = new SpeechBubble(new SKRect(0, 0, 200, 120), BubbleStylePreset.Speech);
        var tail1 = bubble.AddTail(new SKPoint(0, 300));
        var tail2 = bubble.AddTail(new SKPoint(300, 300));
        var tail3 = bubble.AddTail(new SKPoint(-100, 300));

        Assert.Equal(3, bubble.Tails.Count);
        Assert.NotEqual(tail1.AttachmentT, tail2.AttachmentT, 3);
        Assert.NotEqual(tail2.AttachmentT, tail3.AttachmentT, 3);
    }

    [Fact]
    public void SetStyle_RegeneratesOutlineButKeepsTailPlacement()
    {
        var bubble = new SpeechBubble(new SKRect(0, 0, 200, 120), BubbleStylePreset.Speech);
        var tail = bubble.AddTail(new SKPoint(100, 300));
        var attachmentBefore = tail.AttachmentT;
        var targetBefore = tail.Target;

        bubble.SetStyle(BubbleStylePreset.Shout);

        Assert.Equal(BubbleStylePreset.Shout, bubble.Style);
        Assert.Equal(TailKind.JaggedTriangle, tail.Kind);
        Assert.Equal(attachmentBefore, tail.AttachmentT);
        Assert.Equal(targetBefore.X, tail.Target.X);
        Assert.Equal(targetBefore.Y, tail.Target.Y);
    }

    [Fact]
    public void Resize_ScalesOutlineButLeavesTailTargetsFixed()
    {
        var bubble = new SpeechBubble(new SKRect(0, 0, 200, 120), BubbleStylePreset.Speech);
        var tail = bubble.AddTail(new SKPoint(100, 300));

        bubble.Resize(new SKRect(0, 0, 400, 240));

        Assert.Equal(new SKRect(0, 0, 400, 240), bubble.Bounds);
        Assert.Equal(100, tail.Target.X);
        Assert.Equal(300, tail.Target.Y);
    }

    [Fact]
    public void RemoveTail_TakesItOutOfTheList()
    {
        var bubble = new SpeechBubble(new SKRect(0, 0, 200, 120), BubbleStylePreset.Speech);
        var tail = bubble.AddTail(new SKPoint(100, 300));

        var removed = bubble.RemoveTail(tail);

        Assert.True(removed);
        Assert.Empty(bubble.Tails);
    }
}
