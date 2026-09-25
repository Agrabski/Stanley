using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Rendering.Tests;

/// <summary>Fields in texts and bubbles draw as the comic's values - pixel for pixel what typing the value would draw.</summary>
public class TextFieldRenderingTests
{
    private static readonly Rect2D Page = new(0, 0, 120, 80);
    private static readonly TextFields Fields = new("Moon Pie", "7");

    private static Panel PanelWith(string text)
    {
        var element = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(15, 15, 90, 14), text, new TextStyle(14, ColorValue.FromHex("#000000")));
        var bubble = new Bubble(BubbleId.New(), new BubbleShape(AnchorRing.Rectangle(new Rect2D(20, 40, 80, 30))), BubbleStylePreset.Speech, [], text);
        return new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 10, 100, 65)), null, [], [bubble], [element]);
    }

    private static byte[] Render(string text, TextFields? fields)
    {
        using var bitmap = new SKBitmap((int)Page.Width * 4, (int)Page.Height * 4);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Scale(4);
            PageRenderer.Draw(canvas, Page, [PanelWith(text)], fields: fields);
        }
        return bitmap.Bytes;
    }

    [Fact]
    public void A_field_draws_as_its_value()
    {
        Assert.Equal(Render("Wydanie #7", null), Render("Wydanie #{issue}", Fields));
        Assert.Equal(Render("Moon Pie", null), Render("{title}", Fields));
    }

    [Fact]
    public void Without_the_comics_values_a_field_draws_as_typed() =>
        Assert.NotEqual(Render("Wydanie #7", null), Render("Wydanie #{issue}", null));
}
