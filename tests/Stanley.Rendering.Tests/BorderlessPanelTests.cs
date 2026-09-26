using SkiaSharp;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Rendering.Tests;

public class BorderlessPanelTests
{
    private static readonly Rect2D Page = new(0, 0, 100, 60);

    private static Panel APanel(bool borderless) =>
        new(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 80, 40)), null, [], [], Borderless: borderless);

    /// <summary>The colour at the middle of the panel's top edge, drawn at 4 px/mm.</summary>
    private static SKColor TopEdge(Panel panel)
    {
        const float scale = 4;
        using var bitmap = new SKBitmap((int)(Page.Width * scale), (int)(Page.Height * scale));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Scale(scale);
            PageRenderer.Draw(canvas, Page, [panel]);
        }
        return bitmap.GetPixel(50 * 4, 10 * 4);
    }

    [Fact]
    public void A_panel_draws_its_border_unless_it_is_borderless()
    {
        Assert.True(TopEdge(APanel(borderless: false)).Red < 64, "a panel's border is inked");
        Assert.Equal(SKColors.White, TopEdge(APanel(borderless: true)));
    }

    [Fact]
    public void A_panel_with_a_border_style_draws_its_border_in_that_colour()
    {
        var red = APanel(borderless: false) with { BorderStyle = new PanelBorderStyle(ColorValue.FromHex("#ff0000"), 1.5) };

        var edge = TopEdge(red);

        Assert.True(edge.Red > 200 && edge.Green < 64 && edge.Blue < 64, $"the border is red, not {edge}");
    }

    [Fact]
    public void ExportPngAtWidth_comes_out_exactly_that_wide_with_its_height_in_proportion()
    {
        using var stream = new MemoryStream();
        PageRenderer.ExportPngAtWidth(stream, new Rect2D(0, 0, 200, 320), [], 800);

        stream.Position = 0;
        using var image = SKBitmap.Decode(stream);
        Assert.Equal((800, 1280), (image.Width, image.Height));
    }
}
