using SkiaSharp;
using Stanley.Bubbles;
using Xunit;

namespace Stanley.Bubbles.Tests;

public class BubbleTailTests
{
    [Theory]
    [InlineData(0.02f, -150, -80)]
    [InlineData(0.13f, 480, 410)]
    [InlineData(0.37f, 600, 50)]
    [InlineData(0.55f, 620, 480)]
    [InlineData(0.61f, -100, 300)]
    [InlineData(0.75f, 50, 400)]
    [InlineData(0.9f, 400, -120)]
    public void GeneratePath_JaggedTriangle_ProducesASimplePolygon(float attachmentT, float targetX, float targetY)
    {
        var outline = new BubbleOutline(BubbleStylePresets.GenerateOutline(BubbleStylePreset.Shout, new SKRect(100, 100, 300, 220)));
        var tail = new BubbleTail(attachmentT, new SKPoint(targetX, targetY), TailKind.JaggedTriangle);

        using var path = tail.GeneratePath(outline);
        var vertices = path.Points;

        Assert.Equal(5, vertices.Length);
        Assert.False(HasSelfIntersection(vertices), "the jagged tail's own edges should not cross each other");
    }

    [Fact]
    public void GeneratePath_JaggedTriangle_BaseSpansMoreThanASingleZigzagTooth()
    {
        // A jagged outline is made of many short straight edges; the tail's base
        // (its two attachment points either side of AttachmentT) needs to span wide
        // enough to blend across that jaggedness instead of pinching into a sliver
        // inside one tooth.
        var outline = new BubbleOutline(BubbleStylePresets.GenerateOutline(BubbleStylePreset.Shout, new SKRect(100, 100, 300, 220)));
        var tail = new BubbleTail(0.75f, new SKPoint(50, 400), TailKind.JaggedTriangle);

        using var path = tail.GeneratePath(outline);
        var left = path.Points[0];
        var right = path.Points[^1];
        var chord = MathF.Sqrt(MathF.Pow(right.X - left.X, 2) + MathF.Pow(right.Y - left.Y, 2));

        Assert.True(chord > 40f, $"tail base should be a wide chord, was only {chord}px");
    }

    private static bool HasSelfIntersection(SKPoint[] polygon)
    {
        var n = polygon.Length;
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                // Skip edges that share a vertex (adjacent, or the pair that meets at the polygon's closing edge).
                if (j == i + 1 || (i == 0 && j == n - 1))
                    continue;

                if (SegmentsIntersect(polygon[i], polygon[(i + 1) % n], polygon[j], polygon[(j + 1) % n]))
                    return true;
            }
        }
        return false;
    }

    private static bool SegmentsIntersect(SKPoint p1, SKPoint p2, SKPoint p3, SKPoint p4)
    {
        var d1 = Cross(Sub(p4, p3), Sub(p1, p3));
        var d2 = Cross(Sub(p4, p3), Sub(p2, p3));
        var d3 = Cross(Sub(p2, p1), Sub(p3, p1));
        var d4 = Cross(Sub(p2, p1), Sub(p4, p1));
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static SKPoint Sub(SKPoint a, SKPoint b) => new(a.X - b.X, a.Y - b.Y);
    private static float Cross(SKPoint a, SKPoint b) => a.X * b.Y - a.Y * b.X;
}
