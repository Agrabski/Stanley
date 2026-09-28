using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Rendering.Tests;

/// <summary>Prints on clothes: typed text parts (emoji too) and the clothes clip that keeps a print on the shirt.</summary>
public class PrintRenderingTests
{
    private static readonly ColorValue PrintColor = ColorValue.FromHex("#ff0000");

    private static StickerAsset Text(string text, PartArt? art = null) =>
        new(new Sticker(StickerId.New(), text, StickerSlots.Print,
                [new StickerPart("print", BodyRegion.Torso, Art: art ?? new PartArt(ArtMapping.Pin, KeepReadable: true), Clip: PartClip.Clothes, Text: new PartText(text))],
                new SortedDictionary<string, ColorValue> { ["print"] = PrintColor }, ["default"]),
            new Dictionary<string, ArtFile>());

    /// <summary>A character in a plain blue T-shirt body (unless <paramref name="shirt"/> is false), wearing <paramref name="prints"/>.</summary>
    private static CharacterDefinition Wearing(bool shirt, params StickerAsset[] prints)
    {
        var top = new Sticker(StickerId.New(), "Top", StickerSlots.Top, [new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 0.86))],
            new SortedDictionary<string, ColorValue> { ["top"] = ColorValue.FromHex("#0000ff") }, ["default"]);
        var stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Print] = [.. prints.Select(p => p.Id)] };
        if (shirt)
            stickers[StickerSlots.Top] = [top.Id];
        var character = CharacterDefinition.Create("A") with { Stickers = stickers };
        var wardrobe = character.Wardrobe.With(new StickerAsset(top, new Dictionary<string, ArtFile>()));
        foreach (var print in prints)
            wardrobe = wardrobe.With(print);
        return character with { Wardrobe = wardrobe };
    }

    private static SKBitmap Render(CharacterDefinition character)
    {
        var bitmap = new SKBitmap(400, 440);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        CharacterRenderers.Default.Draw(canvas, character, new CharacterPlacement(new Point2D(200, 420), 400, false), 2f);
        return bitmap;
    }

    private static bool IsPrint(SKColor c) => c.Red > 150 && c.Green < 100 && c.Blue < 100;

    private static List<(int X, int Y)> PrintPixels(SKBitmap bitmap) =>
        [.. from y in Enumerable.Range(0, bitmap.Height) from x in Enumerable.Range(0, bitmap.Width) where IsPrint(bitmap.GetPixel(x, y)) select (x, y)];

    [Fact]
    public void A_text_print_draws_its_letters_on_the_chest()
    {
        using var bitmap = Render(Wearing(true, Text("HELLO")));

        var ink = PrintPixels(bitmap);
        Assert.True(ink.Count > 150, $"only {ink.Count} print-coloured pixels");
        var torso = BodyRig.Build(BodyShape.Default).Regions.Torso;
        var (top, bottom) = (420 + torso.Top * 400, 420 + torso.Bottom * 400);
        Assert.All(ink, p => Assert.InRange(p.Y, top, bottom));
    }

    [Fact]
    public void A_text_print_is_what_a_click_on_it_picks_and_outlines()
    {
        var print = Text("HELLO");
        var character = Wearing(true, print);
        var placement = new CharacterPlacement(new Point2D(200, 420), 400, false);
        using var bitmap = Render(character);
        var (x, y) = PrintPixels(bitmap)[PrintPixels(bitmap).Count / 2];

        Assert.Equal(print.Id, CharacterRenderers.Default.StickerAt(character, placement, new Point2D(x, y)));
        using var outline = CharacterRenderers.Default.BuildStickerOutline(character, placement, print.Id);
        Assert.False(outline.IsEmpty);
    }

    [Fact]
    public void A_clothes_clipped_print_never_spills_off_the_shirt()
    {
        // Big, wide and pushed to one side: much of it lies past the shirt's edge.
        var print = Text("WWWWWWWW", new PartArt(ArtMapping.Pin, Offset: new Point2D(120, 0), Scale: 2.5));
        var character = Wearing(true, print);
        var skin = FigureGeometry.ToSk(character.Skin);
        using var plain = Render(Wearing(true));
        using var printed = Render(character);

        Assert.NotEmpty(PrintPixels(printed));
        // Bare skin and the paper around the figure stay exactly as they were.
        for (var y = 0; y < plain.Height; y++)
            for (var x = 0; x < plain.Width; x++)
                if (plain.GetPixel(x, y) is var was && (was == SKColors.White || was == skin))
                    Assert.Equal(was, printed.GetPixel(x, y));
    }

    [Fact]
    public void With_nothing_worn_under_it_a_print_still_shows()
    {
        using var bitmap = Render(Wearing(false, Text("HELLO")));

        Assert.NotEmpty(PrintPixels(bitmap));
    }

    [Fact]
    public void Emoji_and_symbols_split_into_fallback_runs_and_draw_without_throwing()
    {
        using var font = new SKFont(SKTypeface.Default, 40);

        var runs = Lettering.FallbackRuns("HI \U0001F480⭐!", font);

        Assert.Equal("HI \U0001F480⭐!", string.Concat(runs.Select(r => r.Text)));
        using var bitmap = Render(Wearing(true, Text("HI \U0001F480⭐!")));
        Assert.NotEmpty(PrintPixels(bitmap));
    }
}
