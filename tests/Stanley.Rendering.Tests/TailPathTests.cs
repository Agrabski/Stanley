using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Xunit;

namespace Stanley.Rendering.Tests;

public class TailPathTests
{
    [Theory]
    [InlineData(0.02, -150, -80)]
    [InlineData(0.13, 480, 410)]
    [InlineData(0.37, 600, 50)]
    [InlineData(0.55, 620, 480)]
    [InlineData(0.61, -100, 300)]
    [InlineData(0.75, 50, 400)]
    [InlineData(0.9, 400, -120)]
    public void Generate_JaggedTriangle_ProducesASimplePolygon(double attachmentT, double targetX, double targetY)
    {
        var shape = BubbleStylePresets.GenerateShape(BubbleStylePreset.Shout, new Rect2D(100, 100, 200, 120));
        var tail = new BubbleTail(attachmentT, new Point2D(targetX, targetY), TailKind.JaggedTriangle);

        using var path = TailPath.Generate(tail, shape);
        var vertices = path.Points;

        Assert.True(vertices.Length >= 3, "left, target, right is the fallback minimum; more once the jag/absorbed-outline-anchor detail fits without crossing itself");
        Assert.False(HasSelfIntersection(vertices), "the jagged tail's own edges should not cross each other");
    }

    [Fact]
    public void Generate_JaggedTriangle_BaseSpansMoreThanASingleZigzagTooth()
    {
        // A jagged outline is made of many short straight edges; the tail's base
        // (its two attachment points either side of AttachmentT) needs to span wide
        // enough to blend across that jaggedness instead of pinching into a sliver
        // inside one tooth.
        var shape = BubbleStylePresets.GenerateShape(BubbleStylePreset.Shout, new Rect2D(100, 100, 200, 120));
        var tail = new BubbleTail(0.75, new Point2D(50, 400), TailKind.JaggedTriangle);

        using var path = TailPath.Generate(tail, shape);
        var left = path.Points[0];
        // Generate's JaggedTriangle point order is fixed: left, kink+jag, target,
        // kink-jag, right, then any absorbed outline anchors - so index 4 is always
        // "right", not Points[^1] (which is the last absorbed anchor when there is one).
        var right = path.Points[4];
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
