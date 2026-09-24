using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Xunit;

namespace Stanley.Rendering.Tests;

public class AnchorRingPathTests
{
    [Theory]
    [InlineData(BubbleStylePreset.Speech)]
    [InlineData(BubbleStylePreset.Shout)]
    [InlineData(BubbleStylePreset.Whisper)]
    public void ToSkPath_ProducesAClosedNonDegeneratePath(BubbleStylePreset preset)
    {
        var bounds = new Rect2D(0, 0, 200, 120);
        var shape = BubbleStylePresets.GenerateShape(preset, bounds);

        using var path = AnchorRingPath.ToSkPath(shape.Anchors);

        Assert.False(path.IsEmpty);
        var pathBounds = path.TightBounds;
        Assert.True(pathBounds.Width > bounds.Width * 0.5f, "path should roughly fill its bounds horizontally");
        Assert.True(pathBounds.Height > bounds.Height * 0.5f, "path should roughly fill its bounds vertically");
    }
}
