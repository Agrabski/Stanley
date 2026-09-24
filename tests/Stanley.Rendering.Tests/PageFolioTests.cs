using SkiaSharp;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Rendering.Tests;

public class PageFolioTests
{
    private static readonly Rect2D Page = new(0, 0, 210, 297);

    /// <summary>Renders an otherwise blank page with just the folio at 4 px/mm and returns where ink landed.</summary>
    private static SKRectI InkBounds(PageFolio folio)
    {
        const float scale = 4;
        using var bitmap = new SKBitmap((int)(Page.Width * scale), (int)(Page.Height * scale));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Scale(scale);
            PageRenderer.Draw(canvas, Page, [], folio);
        }

        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (bitmap.GetPixel(x, y).Red > 128)
                continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        return new SKRectI(left, top, right, bottom);
    }

    [Fact]
    public void BottomOuter_OnARightHandPage_PrintsInTheBottomRightMargin()
    {
        var ink = InkBounds(new PageFolio("3", PageNumberPosition.BottomOuter, IsRightHandPage: true));

        Assert.True(ink.Left > 180 * 4, "should be at the right");
        Assert.True(ink.Top > 287 * 4, "should be below the 10mm margin line");
        Assert.True(ink.Right <= 200 * 4 + 1, "should end at the margin, not the trim edge");
    }

    [Fact]
    public void BottomOuter_OnALeftHandPage_PrintsInTheBottomLeftMargin()
    {
        var ink = InkBounds(new PageFolio("4", PageNumberPosition.BottomOuter, IsRightHandPage: false));

        Assert.True(ink.Right < 30 * 4, "should be at the left");
        Assert.True(ink.Left >= 10 * 4 - 1, "should start at the margin");
    }

    [Fact]
    public void TopOuter_PrintsInTheTopMargin_AndNoneDrawsNothing()
    {
        Assert.True(InkBounds(new PageFolio("7", PageNumberPosition.TopOuter, true)).Bottom < 10 * 4);
        Assert.Equal(-1, InkBounds(new PageFolio("7", PageNumberPosition.None, true)).Right);
    }
}
