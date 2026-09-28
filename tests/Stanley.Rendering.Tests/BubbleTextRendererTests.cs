using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Rendering.Tests;

/// <summary>Issue #66: a bubble's font size is absolute - the letters never shrink to fit; only <see cref="BubbleTextRenderer.NeededScale"/> (and the bubble growing to it) makes room for them.</summary>
public class BubbleTextRendererTests
{
    private static readonly Rect2D SmallBounds = new(0, 0, 40, 20);

    private static Bubble MakeBubble(string text, double fontSizePt, Rect2D? bounds = null) =>
        new(BubbleId.New(), new BubbleShape(PanelShapes.Rectangle(bounds ?? SmallBounds).Anchors), BubbleStylePreset.Speech, [], text, FontSizePt: fontSizePt);

    [Fact]
    public void Draw_keeps_the_same_letter_size_no_matter_how_long_the_text_is()
    {
        // Eight repeats of the same word, one per line, overflow this bubble badly (the old
        // shrink-to-fit would have clamped the font to a third of its size); one repeat doesn't
        // overflow it at all. Every line says the same word, so its ink should measure the same
        // width wherever it's drawn - if it doesn't, the font size changed with the text's length.
        var shortBubble = MakeBubble("Hello", fontSizePt: 20);
        var longBubble = shortBubble with { Text = string.Join("\n", Enumerable.Repeat("Hello", 8)) };

        var (shortLeft, shortRight) = InkColumnRange(shortBubble, width: 400, height: 120);
        var (longLeft, longRight) = InkColumnRange(longBubble, width: 400, height: 120, translateY: 50);

        Assert.True(shortRight > shortLeft, "the short bubble should have drawn something");
        Assert.True(longRight > longLeft, "the long bubble should have drawn something");
        Assert.Equal(shortRight - shortLeft, longRight - longLeft);
    }

    [Fact]
    public void Draw_lets_a_long_bubble_spill_past_its_outline_instead_of_shrinking()
    {
        var bubble = MakeBubble(string.Join("\n", Enumerable.Repeat("Hello", 8)), fontSizePt: 20);

        using var bitmap = new SKBitmap(400, 80);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        BubbleTextRenderer.Draw(canvas, bubble);

        // The bubble's own bounds are (0, 0, 40, 20); ink below y = 20 means the text spilled
        // past the outline rather than being squeezed down to fit inside it.
        var spillsPastTheBubble = false;
        for (var y = 21; y < bitmap.Height && !spillsPastTheBubble; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Red < 128) { spillsPastTheBubble = true; break; }

        Assert.True(spillsPastTheBubble, "text this long should spill past the outline, not shrink to fit inside it");
    }

    [Theory]
    [InlineData("Hi")]
    [InlineData("A short line of dialogue")]
    public void NeededScale_is_1_when_the_text_already_fits(string text)
    {
        var roomy = MakeBubble(text, fontSizePt: 10, bounds: new Rect2D(0, 0, 150, 100));

        Assert.Equal(1f, BubbleTextRenderer.NeededScale(roomy));
    }

    [Fact]
    public void NeededScale_grows_with_how_long_the_text_is()
    {
        var shortBubble = MakeBubble("Hi", fontSizePt: 10);
        var longBubble = shortBubble with { Text = string.Join(" ", Enumerable.Repeat("word", 40)) };

        Assert.Equal(1f, BubbleTextRenderer.NeededScale(shortBubble));
        Assert.True(BubbleTextRenderer.NeededScale(longBubble) > 1f);
    }

    [Fact]
    public void NeededScale_grows_further_for_even_longer_text()
    {
        var medium = MakeBubble(string.Join(" ", Enumerable.Repeat("word", 20)), fontSizePt: 10);
        var longer = medium with { Text = string.Join(" ", Enumerable.Repeat("word", 60)) };

        Assert.True(BubbleTextRenderer.NeededScale(longer) > BubbleTextRenderer.NeededScale(medium));
    }

    [Fact]
    public void NeededScale_grows_with_font_size()
    {
        var text = string.Join(" ", Enumerable.Repeat("word", 15));
        var small = MakeBubble(text, fontSizePt: 10);
        var big = small with { FontSizePt = 28 };

        Assert.True(BubbleTextRenderer.NeededScale(big) > BubbleTextRenderer.NeededScale(small));
    }

    [Fact]
    public void NeededScale_makes_room_for_one_long_word_that_cant_wrap()
    {
        // One line tall enough, but one unbreakable word far wider than the bubble.
        var bubble = MakeBubble("Aaaaaaaaaaaaaaaaaaaaaaaaaaah!", fontSizePt: 10, bounds: new Rect2D(0, 0, 20, 40));

        var scale = BubbleTextRenderer.NeededScale(bubble);

        Assert.True(scale > 1f);
        using var font = Lettering.Font((float)FontPoints.ToMm(10));
        using var paint = new SKPaint();
        var textWidth = 20 * scale * (1 - 2 * 0.18f);
        Assert.True(font.MeasureText(bubble.Text, paint) <= textWidth + 1e-3f);
    }

    [Fact]
    public void NeededScale_is_never_less_than_1()
    {
        var tiny = MakeBubble(string.Join(" ", Enumerable.Repeat("word", 100)), fontSizePt: 40, bounds: new Rect2D(0, 0, 10, 5));

        Assert.True(BubbleTextRenderer.NeededScale(tiny) >= 1f);
    }

    [Fact]
    public void NeededScale_is_1_for_a_bubble_with_no_text()
    {
        Assert.Equal(1f, BubbleTextRenderer.NeededScale(MakeBubble("", fontSizePt: 40)));
        Assert.Equal(1f, BubbleTextRenderer.NeededScale(MakeBubble("   ", fontSizePt: 40)));
    }

    /// <summary>The leftmost and rightmost dark-pixel columns anywhere on the canvas - the ink width of a word, wherever (and however many times) it was drawn.</summary>
    private static (int Left, int Right) InkColumnRange(Bubble bubble, int width, int height, float translateY = 0)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        if (translateY != 0)
            canvas.Translate(0, translateY);
        BubbleTextRenderer.Draw(canvas, bubble);

        int left = -1, right = -1;
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (bitmap.GetPixel(x, y).Red < 128)
                {
                    if (left < 0)
                        left = x;
                    right = x;
                    break;
                }
            }
        }
        return (left, right);
    }
}
