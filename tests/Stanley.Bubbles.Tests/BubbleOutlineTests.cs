using SkiaSharp;
using Stanley.Bubbles;
using Xunit;

namespace Stanley.Bubbles.Tests;

public class BubbleOutlineTests
{
    [Theory]
    [InlineData(BubbleStylePreset.Speech)]
    [InlineData(BubbleStylePreset.Shout)]
    [InlineData(BubbleStylePreset.Whisper)]
    public void GeneratedOutline_ProducesClosedNonDegeneratePath(BubbleStylePreset preset)
    {
        var bounds = new SKRect(0, 0, 200, 120);
        var outline = new BubbleOutline(BubbleStylePresets.GenerateOutline(preset, bounds));

        using var path = outline.ToPath();

        Assert.False(path.IsEmpty);
        var pathBounds = path.TightBounds;
        Assert.True(pathBounds.Width > bounds.Width * 0.5f, "outline should roughly fill its bounds horizontally");
        Assert.True(pathBounds.Height > bounds.Height * 0.5f, "outline should roughly fill its bounds vertically");
    }

    [Theory]
    [InlineData(BubbleStylePreset.Speech)]
    [InlineData(BubbleStylePreset.Shout)]
    public void Rescale_MapsAnchorsAffinelyAndPreservesCount(BubbleStylePreset preset)
    {
        var from = new SKRect(0, 0, 200, 120);
        var to = new SKRect(50, 30, 350, 330);

        var outline = new BubbleOutline(BubbleStylePresets.GenerateOutline(preset, from));
        var originalCount = outline.Anchors.Count;
        var originalPoints = outline.Anchors.Select(a => a.Point).ToList();

        outline.Rescale(from, to);

        Assert.Equal(originalCount, outline.Anchors.Count);

        var sx = to.Width / from.Width;
        var sy = to.Height / from.Height;
        for (var i = 0; i < originalCount; i++)
        {
            var expectedX = to.Left + (originalPoints[i].X - from.Left) * sx;
            var expectedY = to.Top + (originalPoints[i].Y - from.Top) * sy;
            Assert.Equal(expectedX, outline.Anchors[i].Point.X, 3);
            Assert.Equal(expectedY, outline.Anchors[i].Point.Y, 3);
        }
    }

    [Fact]
    public void PointAt_WrapsAroundAndStaysWithinOutlineBounds()
    {
        var bounds = new SKRect(0, 0, 200, 120);
        var outline = new BubbleOutline(BubbleStylePresets.GenerateOutline(BubbleStylePreset.Speech, bounds));

        for (var i = 0; i <= 20; i++)
        {
            var p = outline.PointAt(i / 20f);
            Assert.InRange(p.X, bounds.Left - 1, bounds.Right + 1);
            Assert.InRange(p.Y, bounds.Top - 1, bounds.Bottom + 1);
        }

        var p0 = outline.PointAt(0f);
        var p1 = outline.PointAt(1f);
        Assert.Equal(p0.X, p1.X, 3);
        Assert.Equal(p0.Y, p1.Y, 3);
    }

    [Fact]
    public void NearestT_FindsCloseApproximationOfAKnownPoint()
    {
        var bounds = new SKRect(0, 0, 200, 120);
        var outline = new BubbleOutline(BubbleStylePresets.GenerateOutline(BubbleStylePreset.Speech, bounds));

        var knownPoint = outline.PointAt(0.37f);
        var foundPoint = outline.PointAt(outline.NearestT(knownPoint));

        var dx = foundPoint.X - knownPoint.X;
        var dy = foundPoint.Y - knownPoint.Y;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        Assert.True(dist < 3f, $"nearest-point search should land within a few pixels of the known point, was {dist}px off");
    }
}
