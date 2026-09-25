using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Rendering.Tests;

/// <summary>Choosing a font: bundled ones, the computer's own, and falling back when a comic names one this computer lacks.</summary>
public class LetteringTests
{
    private static float Width(string? family, string text = "Hello there") =>
        Lettering.Font(10, family: family).MeasureText(text);

    [Fact]
    public void A_font_this_computer_doesnt_have_draws_in_the_default()
    {
        const string missing = "No Such Font 5f2c";

        Assert.False(Lettering.IsAvailable(missing));
        Assert.Equal(Lettering.DefaultFamily, Lettering.Resolve(missing));
        Assert.Same(Lettering.Typeface(null), Lettering.Typeface(missing));
        Assert.Equal(Width(null), Width(missing));
    }

    [Fact]
    public void No_font_is_the_default_and_always_available()
    {
        Assert.True(Lettering.IsAvailable(null));
        Assert.True(Lettering.IsAvailable("  "));
        Assert.Equal(Lettering.DefaultFamily, Lettering.Resolve(null));
    }

    [Fact]
    public void Installed_fonts_are_listed_sorted_and_drawn_in_their_own_face()
    {
        var families = Lettering.SystemFamilies;
        Assert.Equal(families.Order(StringComparer.OrdinalIgnoreCase), families);
        var faces = families.Select(f => SKFontManager.Default.MatchFamily(f)?.FamilyName ?? f).ToList();
        Assert.Equal(faces.Count, faces.Distinct(StringComparer.OrdinalIgnoreCase).Count()); // each font once, not under its aliases too

        var other = families.FirstOrDefault(f => Lettering.Typeface(f).FamilyName != Lettering.Typeface(null).FamilyName);
        Assert.SkipWhen(other is null, "this computer has only one font family");

        Assert.True(Lettering.IsAvailable(other));
        Assert.True(Lettering.IsAvailable(other!.ToUpperInvariant()));
        Assert.Equal(other, Lettering.Typeface(other).FamilyName, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_fonts_other_names_still_count_as_installed()
    {
        var alias = SKFontManager.Default.FontFamilies.FirstOrDefault(f => !f.StartsWith('.') && !Lettering.SystemFamilies.Contains(f, StringComparer.OrdinalIgnoreCase) && !Lettering.IsBundled(f));
        Assert.SkipWhen(alias is null, "no font here goes by a second name");

        Assert.True(Lettering.IsAvailable(alias));
        Assert.Equal(SKFontManager.Default.MatchFamily(alias)!.FamilyName, Lettering.Typeface(alias).FamilyName);
    }

    [Fact]
    public void A_bundled_font_is_used_by_name_and_listed_apart_from_the_computers_own()
    {
        const string family = "Stanley Test Bundle";
        using var stream = SKTypeface.Default.OpenStream();
        var data = new byte[stream.Length];
        stream.Read(data, data.Length);

        Assert.True(Lettering.AddBundledFace(family, data));
        Assert.False(Lettering.AddBundledFace("Not A Font", [1, 2, 3]));

        Assert.True(Lettering.IsBundled("stanley test bundle"));
        Assert.True(Lettering.IsAvailable(family));
        Assert.Contains(family, Lettering.BundledFamilies);
        Assert.DoesNotContain(family, Lettering.SystemFamilies);
        Assert.NotEqual(family, Lettering.DefaultFamily); // only the app's own default font is the default
        Assert.NotSame(Lettering.Typeface(null), Lettering.Typeface(family));
        Assert.Equal(Width(null), Width(family), 3); // the same letters, so the same widths

        // It has no bold or italic face: they're faked, so they still show.
        using var bold = Lettering.Font(10, bold: true, family: family);
        using var italic = Lettering.Font(10, italic: true, family: family);
        Assert.True(bold.Embolden || bold.Typeface.FontStyle.Weight >= (int)SKFontStyleWeight.SemiBold);
        Assert.True(italic.SkewX != 0 || italic.Typeface.FontStyle.Slant != SKFontStyleSlant.Upright);
    }

    [Fact]
    public void Bubbles_and_text_letter_in_their_own_font()
    {
        var other = Lettering.SystemFamilies.FirstOrDefault(f => Math.Abs(Width(f, "WWWW iiii") - Width(null, "WWWW iiii")) > 0.5);
        Assert.SkipWhen(other is null, "no installed font with different widths from the default");

        var text = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(0, 0, 30, 5), "WWWW iiii WWWW iiii WWWW iiii",
            new TextStyle(4, ColorValue.FromHex("#000000")));
        Assert.NotEqual(ElementRenderer.NeededHeight(text), ElementRenderer.NeededHeight(text with { Style = text.Style with { FontFamily = other } }));

        var bubble = new Bubble(BubbleId.New(), new BubbleShape(PanelShapes.Rectangle(new Rect2D(0, 0, 60, 30)).Anchors), BubbleStylePreset.Speech, [], "WWWW");
        Assert.NotEqual(Pixels(bubble), Pixels(bubble with { FontFamily = other }));
    }

    private static byte[] Pixels(Bubble bubble)
    {
        using var bitmap = new SKBitmap(60, 30);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        BubbleTextRenderer.Draw(canvas, bubble, 8);
        return bitmap.Bytes;
    }
}
