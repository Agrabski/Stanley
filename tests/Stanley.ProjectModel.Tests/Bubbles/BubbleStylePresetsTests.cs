using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Xunit;

namespace Stanley.ProjectModel.Tests.Bubbles;

public class BubbleStylePresetsTests
{
    [Theory]
    [InlineData(BubbleStylePreset.Speech)]
    [InlineData(BubbleStylePreset.Shout)]
    [InlineData(BubbleStylePreset.Whisper)]
    [InlineData(BubbleStylePreset.Thought)]
    public void GenerateShape_FillsItsBoundsRoughly(BubbleStylePreset preset)
    {
        var bounds = new Rect2D(0, 0, 200, 120);
        var shape = BubbleStylePresets.GenerateShape(preset, bounds);

        var shapeBounds = AnchorRing.BoundingBox(shape.Anchors);
        Assert.True(shapeBounds.Width > bounds.Width * 0.5, "shape should roughly fill its bounds horizontally");
        Assert.True(shapeBounds.Height > bounds.Height * 0.5, "shape should roughly fill its bounds vertically");
    }

    // Switching style regenerates the shape from the old one's bounding box, so a shape that
    // fell short of its box would shrink the bubble a little on every switch (#122).
    [Theory]
    [InlineData(BubbleStylePreset.Speech)]
    [InlineData(BubbleStylePreset.Shout)]
    [InlineData(BubbleStylePreset.Whisper)]
    [InlineData(BubbleStylePreset.Thought)]
    public void GenerateShape_ReachesEveryEdgeOfItsBounds(BubbleStylePreset preset)
    {
        var bounds = new Rect2D(10, 20, 200, 120);
        var shapeBounds = AnchorRing.BoundingBox(BubbleStylePresets.GenerateShape(preset, bounds).Anchors);

        Assert.Equal(bounds.Left, shapeBounds.Left, 9);
        Assert.Equal(bounds.Top, shapeBounds.Top, 9);
        Assert.Equal(bounds.Right, shapeBounds.Right, 9);
        Assert.Equal(bounds.Bottom, shapeBounds.Bottom, 9);
    }

    [Theory]
    [InlineData(BubbleStylePreset.Speech, 0.37)]
    [InlineData(BubbleStylePreset.Shout, 0.37)]
    [InlineData(BubbleStylePreset.Shout, 0.0)]
    [InlineData(BubbleStylePreset.Speech, 0.75)]
    public void TowardsT_FindsTheOutlineInThatDirectionFromTheCentre(BubbleStylePreset preset, double knownT)
    {
        var bounds = new Rect2D(0, 0, 200, 120);
        var shape = BubbleStylePresets.GenerateShape(preset, bounds);
        var centre = new Point2D(100, 60);
        var known = AnchorRing.PointAt(shape.Anchors, knownT);
        // Anywhere along the ray will do - halfway out, or well beyond the outline.
        foreach (var reach in new[] { 0.5, 3.0 })
        {
            var towards = new Point2D(centre.X + (known.X - centre.X) * reach, centre.Y + (known.Y - centre.Y) * reach);

            var found = AnchorRing.PointAt(shape.Anchors, AnchorRing.TowardsT(shape.Anchors, centre, towards));

            Assert.Equal(known.X, found.X, 6);
            Assert.Equal(known.Y, found.Y, 6);
        }
    }

    [Fact]
    public void TowardsT_FromOutsideTheRing_FallsBackToTheNearestPoint()
    {
        var shape = BubbleStylePresets.GenerateShape(BubbleStylePreset.Speech, new Rect2D(0, 0, 200, 120));
        var towards = new Point2D(250, 60);

        var t = AnchorRing.TowardsT(shape.Anchors, new Point2D(500, 500), towards);

        Assert.Equal(AnchorRing.NearestT(shape.Anchors, towards), t);
    }

    [Theory]
    [InlineData(BubbleStylePreset.Speech)]
    [InlineData(BubbleStylePreset.Shout)]
    public void Rescale_MapsAnchorsAffinelyAndPreservesCount(BubbleStylePreset preset)
    {
        var from = new Rect2D(0, 0, 200, 120);
        var to = new Rect2D(50, 30, 300, 300);

        var shape = BubbleStylePresets.GenerateShape(preset, from);
        var originalPoints = shape.Anchors.Select(a => a.Point).ToList();

        var rescaled = AnchorRing.Rescale(shape.Anchors, from, to);

        Assert.Equal(originalPoints.Count, rescaled.Count);

        var sx = to.Width / from.Width;
        var sy = to.Height / from.Height;
        for (var i = 0; i < originalPoints.Count; i++)
        {
            var expectedX = to.Left + (originalPoints[i].X - from.Left) * sx;
            var expectedY = to.Top + (originalPoints[i].Y - from.Top) * sy;
            Assert.Equal(expectedX, rescaled[i].Point.X, 3);
            Assert.Equal(expectedY, rescaled[i].Point.Y, 3);
        }
    }

    [Fact]
    public void PointAt_WrapsAroundAndStaysWithinBounds()
    {
        var bounds = new Rect2D(0, 0, 200, 120);
        var shape = BubbleStylePresets.GenerateShape(BubbleStylePreset.Speech, bounds);

        for (var i = 0; i <= 20; i++)
        {
            var p = AnchorRing.PointAt(shape.Anchors, i / 20.0);
            Assert.InRange(p.X, bounds.Left - 1, bounds.Right + 1);
            Assert.InRange(p.Y, bounds.Top - 1, bounds.Bottom + 1);
        }

        var p0 = AnchorRing.PointAt(shape.Anchors, 0);
        var p1 = AnchorRing.PointAt(shape.Anchors, 1);
        Assert.Equal(p0.X, p1.X, 3);
        Assert.Equal(p0.Y, p1.Y, 3);
    }

    [Fact]
    public void NearestT_FindsCloseApproximationOfAKnownPoint()
    {
        var bounds = new Rect2D(0, 0, 200, 120);
        var shape = BubbleStylePresets.GenerateShape(BubbleStylePreset.Speech, bounds);

        var knownPoint = AnchorRing.PointAt(shape.Anchors, 0.37);
        var foundPoint = AnchorRing.PointAt(shape.Anchors, AnchorRing.NearestT(shape.Anchors, knownPoint));

        var dx = foundPoint.X - knownPoint.X;
        var dy = foundPoint.Y - knownPoint.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        Assert.True(dist < 3.0, $"nearest-point search should land within a few pixels of the known point, was {dist}px off");
    }
}
