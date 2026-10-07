using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Rendering.Tests;

public class PictureRendererTests
{
    /// <summary>A PNG, <paramref name="width"/> by <paramref name="height"/>: its left half red, its right half blue.</summary>
    internal static ArtFile SplitPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, x < width / 2 ? SKColors.Red : SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return ArtFile.Png(data.ToArray());
    }

    private static SKBitmap Render(Panel panel, IReadOnlyDictionary<string, ArtFile>? pictures)
    {
        var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        PageRenderer.Draw(canvas, new Rect2D(0, 0, 100, 100), [panel], pictures: pictures);
        return bitmap;
    }

    [Fact]
    public void Size_reads_a_png_and_an_svg_and_refuses_anything_else()
    {
        Assert.Equal((40.0, 10.0), PictureRenderer.Size(SplitPng(40, 10)));
        Assert.Equal((200.0, 100.0), PictureRenderer.Size(ArtFile.Svg("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 100\"><g id=\"all\"><rect width=\"200\" height=\"100\" fill=\"#00ff00\"/></g></svg>")));
        Assert.Null(PictureRenderer.Size(ArtFile.Png([1, 2, 3])));
        Assert.Null(PictureRenderer.Size(ArtFile.Svg("not svg")));
    }

    [Fact]
    public void A_background_picture_covers_the_whole_panel_keeping_its_shape()
    {
        // A wide picture in a square-ish panel: scaled to the panel's height, centred, its sides cropped.
        var picture = SplitPng(40, 10);
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 80, 80)), new InlineBackground("wide.png"), [], []);

        using var bitmap = Render(panel, new Dictionary<string, ArtFile> { ["wide.png"] = picture });

        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(15, 50)); // all of the left edge red
        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(15, 15));
        Assert.Equal(new SKColor(0, 0, 255), bitmap.GetPixel(85, 85)); // right edge blue, to the corner
        Assert.Equal(SKColors.White, bitmap.GetPixel(5, 50)); // nothing outside the panel
    }

    [Fact]
    public void A_picture_element_is_drawn_into_its_box_in_its_layer()
    {
        var picture = new PictureElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(20, 20, 40, 20), "p.png");
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 80, 80)), null, [], [], [picture]);

        using var bitmap = Render(panel, new Dictionary<string, ArtFile> { ["p.png"] = SplitPng(40, 20) });

        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(25, 30));
        Assert.Equal(new SKColor(0, 0, 255), bitmap.GetPixel(55, 30));
        Assert.Equal(SKColors.White, bitmap.GetPixel(25, 50));
        Assert.True(ElementRenderer.Hits(picture, new Point2D(30, 30), 0));
        Assert.False(ElementRenderer.Hits(picture, new Point2D(30, 50), 0));
    }

    [Fact]
    public void A_missing_picture_draws_a_placeholder_instead_of_failing()
    {
        var picture = new PictureElement(ElementId.New(), ElementLayer.Background, new Rect2D(20, 20, 40, 20), "gone.png");
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 80, 80)), new InlineBackground("also-gone.png"), [], [], [picture]);

        using var bitmap = Render(panel, pictures: null);

        Assert.NotEqual(SKColors.White, bitmap.GetPixel(15, 50)); // grey placeholder behind everything
        Assert.NotEqual(SKColors.White, bitmap.GetPixel(30, 25));
    }

    [Fact]
    public void An_svg_picture_draws_as_vectors()
    {
        var svg = ArtFile.Svg("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><g id=\"all\"><rect width=\"10\" height=\"10\" fill=\"#00ff00\"/></g></svg>");
        var picture = new PictureElement(ElementId.New(), ElementLayer.Background, new Rect2D(20, 20, 40, 40), "g.svg");
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 80, 80)), null, [], [], [picture]);

        using var bitmap = Render(panel, new Dictionary<string, ArtFile> { ["g.svg"] = svg });

        Assert.Equal(new SKColor(0, 255, 0), bitmap.GetPixel(40, 40));
    }

    /// <summary>
    /// #112: icon sites save an SVG sized in pixels over a much smaller view box (800px over a
    /// 24-unit icon). The reader drew it scaled up to that size, so the picture's box showed an
    /// empty corner of it - the picture was invisible. Whatever size it declares, it fills its box.
    /// </summary>
    [Theory]
    [InlineData("width=\"800px\" height=\"800px\" viewBox=\"0 0 24 24\"")]
    [InlineData("width=\"10\" height=\"10\" viewBox=\"0 0 24 24\"")]
    [InlineData("width=\"100%\" height=\"100%\" viewBox=\"0 0 24 24\"")]
    [InlineData("width=\"24\" height=\"24\"")]
    [InlineData("width=\"0.25in\" height=\"0.25in\"")]
    public void An_svg_picture_fills_its_box_whatever_size_it_declares(string size)
    {
        // A 24-unit square (24 px is a quarter inch), green but for a red top-left quarter.
        var svg = ArtFile.Svg($"<svg xmlns=\"http://www.w3.org/2000/svg\" {size} fill=\"none\"><rect width=\"24\" height=\"24\" fill=\"#00ff00\"/><rect width=\"12\" height=\"12\" fill=\"#ff0000\"/></svg>");
        var picture = new PictureElement(ElementId.New(), ElementLayer.Background, new Rect2D(20, 20, 40, 40), "icon.svg");
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 80, 80)), null, [], [], [picture]);

        using var bitmap = Render(panel, new Dictionary<string, ArtFile> { ["icon.svg"] = svg });

        Assert.Equal((24.0, 24.0), PictureRenderer.Size(svg));
        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(30, 30));
        Assert.Equal(new SKColor(0, 255, 0), bitmap.GetPixel(50, 50));
        Assert.Equal(new SKColor(0, 255, 0), bitmap.GetPixel(58, 58)); // to its far corner
    }
}
