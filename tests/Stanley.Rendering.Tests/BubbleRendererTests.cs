using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Xunit;

namespace Stanley.Rendering.Tests;

public class BubbleRendererTests
{
    [Theory]
    [InlineData(BubbleStylePreset.Speech)]
    [InlineData(BubbleStylePreset.Shout)]
    [InlineData(BubbleStylePreset.Whisper)]
    public void BuildRenderPath_UnionsShapeWithEveryTail(BubbleStylePreset preset)
    {
        var bounds = new Rect2D(100, 100, 200, 120);
        var target1 = new Point2D(50, 400);
        var target2 = new Point2D(350, 420);
        var bubble = new Bubble(
            BubbleId.New(),
            BubbleStylePresets.GenerateShape(preset, bounds),
            preset,
            [
                new BubbleTail(0.75, target1, BubbleStylePresets.TailKindFor(preset)),
                new BubbleTail(0.25, target2, BubbleStylePresets.TailKindFor(preset))
            ],
            "");

        using var renderPath = BubbleRenderer.BuildRenderPath(bubble);
        var pathBounds = renderPath.TightBounds;

        Assert.False(renderPath.IsEmpty);
        AssertWithinBounds(pathBounds, AnchorRingPath.ToSk(target1), "the first tail's target");
        AssertWithinBounds(pathBounds, AnchorRingPath.ToSk(target2), "the second tail's target");
        AssertWithinBounds(pathBounds, new SkiaSharp.SKPoint((float)bounds.MidX, (float)bounds.MidY), "the shape's centre");
    }

    private static void AssertWithinBounds(SkiaSharp.SKRect bounds, SkiaSharp.SKPoint point, string description)
    {
        // SKRect.Contains excludes the right/bottom edge, and a tail's target is often
        // exactly the extreme point of the unioned shape, so check inclusive bounds instead.
        Assert.True(point.X >= bounds.Left - 0.01f && point.X <= bounds.Right + 0.01f, $"render path should reach {description} (X)");
        Assert.True(point.Y >= bounds.Top - 0.01f && point.Y <= bounds.Bottom + 0.01f, $"render path should reach {description} (Y)");
    }
}
