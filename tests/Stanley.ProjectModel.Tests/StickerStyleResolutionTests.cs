using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.ProjectModel.Tests;

/// <summary>Which style each worn sticker shows (docs/sticker-system.md §20): definition → named look → panel, per sticker, under the expression.</summary>
public class StickerStyleResolutionTests
{
    private static Sticker Sticker(string name, string slot, params string[] variants) =>
        new(StickerId.New(), name, slot, [new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp))],
            new SortedDictionary<string, ColorValue>(), variants);

    private static readonly Sticker Cap = Sticker("Cap", StickerSlots.Headwear, "front", "back", "left", "right");
    private static readonly Sticker Hood = Sticker("Hood", StickerSlots.Headwear, "down", "up");
    private static readonly Sticker Eyes = Sticker("Eyes", StickerSlots.Eyes, "neutral", "happy", "wink");

    /// <summary>A character wearing the cap and the hood together in the headwear slot, and the eyes, with <paramref name="styles"/> chosen on the character.</summary>
    private static CharacterDefinition Wearing(params (Sticker Sticker, string Variant)[] styles)
    {
        var character = CharacterDefinition.Create("A") with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>>
            {
                [StickerSlots.Headwear] = [Hood.Id, Cap.Id],
                [StickerSlots.Eyes] = [Eyes.Id],
            },
            StickerVariants = styles.Length == 0 ? null : new SortedDictionary<StickerId, string>(styles.ToDictionary(s => s.Sticker.Id, s => s.Variant)),
        };
        var wardrobe = character.Wardrobe;
        foreach (var sticker in new[] { Cap, Hood, Eyes })
            wardrobe = wardrobe.With(new StickerAsset(sticker, new Dictionary<string, ArtFile>()));
        return character with { Wardrobe = wardrobe };
    }

    private static WornSticker Worn(CharacterLook look, Sticker sticker) => look.Stickers.Single(w => w.Asset.Id == sticker.Id);

    private static string Shown(CharacterLook look, Sticker sticker, IReadOnlyDictionary<string, string>? expression = null)
    {
        var worn = Worn(look, sticker);
        return worn.Asset.Sticker.VariantFor(worn.Slot, expression, worn.Variant);
    }

    private static SortedDictionary<StickerId, string> Styles(params (Sticker Sticker, string Variant)[] styles) =>
        new(styles.ToDictionary(s => s.Sticker.Id, s => s.Variant));

    [Fact]
    public void A_style_goes_character_then_named_look_then_panel()
    {
        var character = Wearing((Cap, "back"));
        var look = new CharacterRevision(CharacterRevisionId.New(), character.Id, "Winter", new SortedDictionary<string, IReadOnlyList<StickerId>>(),
            new SortedDictionary<string, ColorValue>(), null, null, StickerVariantValues: Styles((Cap, "left")));
        var panel = new CharacterInstanceOverrides(null, null, StickerVariantOverrides: Styles((Cap, "right")));
        var plainLook = look with { StickerVariantValues = null };

        Assert.Equal("back", Shown(CharacterLooks.Resolve(character), Cap));
        Assert.Equal("back", Shown(CharacterLooks.Resolve(character, plainLook), Cap)); // a look without its own inherits
        Assert.Equal("left", Shown(CharacterLooks.Resolve(character, look), Cap));
        Assert.Equal("right", Shown(CharacterLooks.Resolve(character, look, panel), Cap));
        Assert.Equal("right", Shown(CharacterLooks.Resolve(character, null, panel), Cap));
    }

    [Fact]
    public void Two_hats_in_one_slot_keep_their_own_styles()
    {
        var look = CharacterLooks.Resolve(Wearing((Cap, "back"), (Hood, "up")));

        Assert.Equal("back", Shown(look, Cap));
        Assert.Equal("up", Shown(look, Hood));
        Assert.Equal("front", Shown(CharacterLooks.Resolve(Wearing((Hood, "up"))), Cap)); // unchosen: its first
    }

    [Fact]
    public void The_expression_beats_a_chosen_style_on_a_face()
    {
        var look = CharacterLooks.Resolve(Wearing((Eyes, "wink")));
        var happy = new Dictionary<string, string> { [StickerSlots.Eyes] = "happy" };
        var missing = new Dictionary<string, string> { [StickerSlots.Eyes] = "angry" };

        Assert.Equal("happy", Shown(look, Eyes, happy));
        Assert.Equal("wink", Shown(look, Eyes, missing)); // an expression it doesn't draw falls back to the style
        Assert.Equal("neutral", Shown(CharacterLooks.Resolve(Wearing()), Eyes, missing));
    }

    [Fact]
    public void A_style_the_sticker_does_not_have_shows_its_default()
    {
        Assert.Equal("front", Cap.VariantFor(StickerSlots.Headwear, null, "inside-out"));
        Assert.Equal("neutral", Eyes.VariantFor(StickerSlots.Eyes, null, "gone"));
        Assert.Equal("up", Hood.VariantFor(StickerSlots.Headwear, null, "up"));
    }
}
