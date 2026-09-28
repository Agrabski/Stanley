using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Rendering.Tests;

/// <summary>
/// Styles on the figure (docs/sticker-system.md §20): a sticker is drawn in the style it's worn
/// in - with no expression involved - through definition → named look → panel, and the
/// render cache tells the styles apart.
/// </summary>
public class StickerStyleRenderingTests
{
    private static readonly SKColor Blue = new(0, 0, 255);

    /// <summary>
    /// A character wearing a two-style "top": worn "down" it's nothing but a band round the
    /// waist; worn "up" its body covers the chest too - like a hood up or down, a part that
    /// exists in one style only.
    /// </summary>
    private static (CharacterDefinition Character, StickerId Id) Wearing()
    {
        var sticker = new Sticker(StickerId.New(), "Hoodie", StickerSlots.Top,
            [
                new StickerPart("chest", BodyRegion.Torso, Cover: new PartCover("top", 0, 0.5), Variants: ["up"]),
                new StickerPart("band", BodyRegion.Torso, Cover: new PartCover("top", 0.8, 1)),
            ],
            new SortedDictionary<string, ColorValue> { ["top"] = ColorValue.FromHex("#0000ff") }, ["down", "up"]);
        var character = CharacterDefinition.Create("A") with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Top] = [sticker.Id] },
        };
        return (character with { Wardrobe = character.Wardrobe.With(new StickerAsset(sticker, new Dictionary<string, ArtFile>())) }, sticker.Id);
    }

    private static SortedDictionary<StickerId, string> Style(StickerId id, string variant) => new() { [id] = variant };

    private static SKColor ChestColour(CharacterDefinition character, CharacterInstanceOverrides? overrides = null, CharacterRevisionId? look = null)
    {
        var figure = BodyRig.Build(character.Body);
        var chest = figure.Regions.Torso.ToFigure(new Point2D(0, figure.Regions.Torso.Top + 0.1));
        using var bitmap = new SKBitmap(400, 440);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            CharacterRenderers.Default.Draw(canvas, character, new CharacterPlacement(new Point2D(200, 420), 400, false), 2f, ViewAngle.Front,
                new PoseData(ViewAngle.Front, [], []), overrides, look);
        }
        return bitmap.GetPixel((int)Math.Round(200 + chest.X * 400), (int)Math.Round(420 + chest.Y * 400));
    }

    [Fact]
    public void A_sticker_is_drawn_in_the_style_the_character_wears_it_in()
    {
        var (character, id) = Wearing();
        var skin = FigureGeometry.ToSk(character.Skin);

        Assert.Equal(skin, ChestColour(character)); // its first style, "down"
        Assert.Equal(Blue, ChestColour(character with { StickerVariants = Style(id, "up") }));
        Assert.Equal(skin, ChestColour(character with { StickerVariants = Style(id, "down") }));
    }

    [Fact]
    public void One_panels_style_wins_over_the_characters_and_the_cache_tells_them_apart()
    {
        var (plain, id) = Wearing();
        var character = plain with { StickerVariants = Style(id, "up") };
        var down = new CharacterInstanceOverrides(null, null, StickerVariantOverrides: Style(id, "down"));

        // The same definition drawn for two panels: each keeps its own style.
        Assert.Equal(Blue, ChestColour(character));
        Assert.Equal(FigureGeometry.ToSk(character.Skin), ChestColour(character, down));
        Assert.Equal(Blue, ChestColour(character));
    }

    [Fact]
    public void A_named_looks_style_is_drawn_and_a_panel_can_still_change_it()
    {
        var (character, id) = Wearing();
        var look = new CharacterRevision(CharacterRevisionId.New(), character.Id, "Winter", new SortedDictionary<string, IReadOnlyList<StickerId>>(),
            new SortedDictionary<string, ColorValue>(), null, null, StickerVariantValues: Style(id, "up"));
        character = character with { Revisions = new Dictionary<CharacterRevisionId, CharacterRevision> { [look.Id] = look } };
        var down = new CharacterInstanceOverrides(null, null, StickerVariantOverrides: Style(id, "down"));

        Assert.Equal(FigureGeometry.ToSk(character.Skin), ChestColour(character));
        Assert.Equal(Blue, ChestColour(character, look: look.Id));
        Assert.Equal(FigureGeometry.ToSk(character.Skin), ChestColour(character, down, look.Id));
    }

    [Fact]
    public void An_expression_for_the_slot_still_wins_over_the_style()
    {
        var (plain, id) = Wearing();
        var character = plain with { StickerVariants = Style(id, "down") };
        var expression = new PoseData(ViewAngle.Front, [], new SortedDictionary<string, string> { [StickerSlots.Top] = "up" });
        var figure = BodyRig.Build(character.Body);
        var chest = figure.Regions.Torso.ToFigure(new Point2D(0, figure.Regions.Torso.Top + 0.1));

        using var bitmap = new SKBitmap(400, 440);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            CharacterRenderers.Default.Draw(canvas, character, new CharacterPlacement(new Point2D(200, 420), 400, false), 2f, ViewAngle.Front, expression);
        }

        Assert.Equal(Blue, bitmap.GetPixel((int)Math.Round(200 + chest.X * 400), (int)Math.Round(420 + chest.Y * 400)));
    }
}
