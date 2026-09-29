using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Tests;

/// <summary>
/// Modular hair (GitHub issue #59) resolved: a sticker that paints over glasses comes after them,
/// a hair piece follows the hair's colour and dye until it is given its own (each separately),
/// and a streak takes the streaks' colour until it is given its own.
/// </summary>
public class HairPiecesResolutionTests
{
    private static readonly ColorValue Brown = ColorValue.FromHex("#5a3a22");
    private static readonly ColorValue Green = ColorValue.FromHex("#2e9e4a");
    private static readonly ColorValue Purple = ColorValue.FromHex("#8040c0");
    private static readonly ColorValue Pink = ColorValue.FromHex("#ff69b4");
    private static readonly ColorValue Blue = ColorValue.FromHex("#3060ff");

    private static readonly Fabric Tips = new(new PatternFill(PatternKind.Tips, [ColorValue.FromHex("#e02020")]));
    private static readonly Fabric Rainbow = new(new PatternFill(PatternKind.Rainbow, []));

    private static Sticker Worn(string slot, string colorSlot = "hair", ColorValue? color = null, bool? overGlasses = null) =>
        new(StickerId.New(), slot, slot, [new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp))],
            new SortedDictionary<string, ColorValue> { [colorSlot] = color ?? Brown }, ["default"], OverGlasses: overGlasses);

    private static CharacterDefinition Wearing(params Sticker[] worn)
    {
        var character = CharacterDefinition.Create("A");
        var stickers = new SortedDictionary<string, IReadOnlyList<StickerId>>();
        var wardrobe = character.Wardrobe;
        foreach (var sticker in worn)
        {
            wardrobe = wardrobe.With(new StickerAsset(sticker, new Dictionary<string, ArtFile>()));
            stickers[sticker.Slot] = stickers.TryGetValue(sticker.Slot, out var already) ? [.. already, sticker.Id] : [sticker.Id];
        }
        return character with { Stickers = stickers, Wardrobe = wardrobe };
    }

    private static CharacterDefinition Coloured(CharacterDefinition character, params (string Slot, ColorValue Color)[] colors)
    {
        var slots = new SortedDictionary<string, ColorValue>(character.ColorSlots);
        foreach (var (slot, color) in colors)
            slots[slot] = color;
        return character with { ColorSlots = slots };
    }

    private static CharacterDefinition Dyed(CharacterDefinition character, params (string Slot, Fabric Fabric)[] fabrics)
    {
        var slots = new SortedDictionary<string, Fabric>(character.Fabrics ?? new SortedDictionary<string, Fabric>());
        foreach (var (slot, fabric) in fabrics)
            slots[slot] = fabric;
        return character with { Fabrics = slots };
    }

    private static string[] Order(CharacterDefinition character) => [.. CharacterLooks.Resolve(character).Stickers.Select(w => w.Slot)];

    // ---- over glasses -----------------------------------------------------------------------

    [Fact]
    public void A_sticker_that_paints_over_glasses_comes_after_them()
    {
        var glasses = Worn(StickerSlots.Glasses, "glasses");

        Assert.Equal([StickerSlots.HairFringe, StickerSlots.Glasses], Order(Wearing(glasses, Worn(StickerSlots.HairFringe)))); // a fringe is under glasses
        Assert.Equal([StickerSlots.Glasses, StickerSlots.HairFringe], Order(Wearing(glasses, Worn(StickerSlots.HairFringe, overGlasses: true))));
        Assert.Equal([StickerSlots.HairFringe, StickerSlots.Glasses], Order(Wearing(glasses, Worn(StickerSlots.HairFringe, overGlasses: false))));
    }

    [Fact]
    public void Painting_over_glasses_never_lowers_a_sticker_that_is_already_above_them()
    {
        Assert.Equal(StickerSlots.OverGlassesZOrder, StickerSlots.ZOrder(StickerSlots.HairFringe, Worn(StickerSlots.HairFringe, overGlasses: true)));
        Assert.Equal(StickerSlots.ZOrder(StickerSlots.HairFringe), StickerSlots.ZOrder(StickerSlots.HairFringe, Worn(StickerSlots.HairFringe)));
        // Streaks (63) and hats (70) are already over glasses (60) and a fringe that paints over them (62).
        Assert.Equal(StickerSlots.ZOrder(StickerSlots.HairStreaks), StickerSlots.ZOrder(StickerSlots.HairStreaks, Worn(StickerSlots.HairStreaks, overGlasses: true)));
        Assert.Equal(StickerSlots.ZOrder(StickerSlots.Headwear), StickerSlots.ZOrder(StickerSlots.Headwear, Worn(StickerSlots.Headwear, overGlasses: true)));
        Assert.Equal([StickerSlots.Glasses, StickerSlots.HairFringe, StickerSlots.HairStreaks],
            Order(Wearing(Worn(StickerSlots.HairStreaks), Worn(StickerSlots.HairFringe, overGlasses: true), Worn(StickerSlots.Glasses, "glasses"))));
    }

    // ---- piece colours and dyes -------------------------------------------------------------

    [Fact]
    public void Every_hair_piece_follows_the_hair_colour_until_given_its_own()
    {
        var character = Coloured(Wearing(Worn(StickerSlots.HairFringe)), ("hair", Green));

        var look = CharacterLooks.Resolve(character);

        foreach (var piece in StickerSlots.HairPieces)
            Assert.Equal(Green, look.Colors[piece]);
        Assert.Equal(Green, look.Colors[StickerSlots.Hair]);
    }

    [Fact]
    public void A_piece_with_its_own_colour_keeps_it_and_the_others_still_follow_the_hair()
    {
        var character = Coloured(Wearing(Worn(StickerSlots.HairFringe)), ("hair", Green), (StickerSlots.HairFringe, Purple));

        var look = CharacterLooks.Resolve(character);

        Assert.Equal(Purple, look.Colors[StickerSlots.HairFringe]);
        Assert.Equal(Green, look.Colors[StickerSlots.HairBack]);
        Assert.Equal(Green, look.Colors[StickerSlots.HairTop]);
        Assert.Equal(Green, look.Colors["hair"]);
    }

    [Fact]
    public void A_piece_with_its_own_colour_keeps_the_hairs_dye()
    {
        var character = Dyed(Coloured(Wearing(Worn(StickerSlots.HairFringe)), ("hair", Green), (StickerSlots.HairFringe, Purple)), ("hair", Tips));

        var look = CharacterLooks.Resolve(character);

        Assert.Equal(Purple, look.Colors[StickerSlots.HairFringe]);
        Assert.Equal(Tips, look.FabricOf(StickerSlots.HairFringe));
        Assert.Equal(Tips, look.FabricOf(StickerSlots.HairBack));
        Assert.Equal(Tips, look.FabricOf("hair"));
    }

    [Fact]
    public void A_piece_with_its_own_dye_keeps_it_and_the_hairs_colour()
    {
        var character = Dyed(Coloured(Wearing(Worn(StickerSlots.HairSides)), ("hair", Green)), ("hair", Tips), (StickerSlots.HairSides, Rainbow));

        var look = CharacterLooks.Resolve(character);

        Assert.Equal(Rainbow, look.FabricOf(StickerSlots.HairSides));
        Assert.Equal(Green, look.Colors[StickerSlots.HairSides]);
        Assert.Equal(Tips, look.FabricOf(StickerSlots.HairTop));
    }

    [Fact]
    public void A_piece_has_no_dye_of_its_own_while_the_hair_has_none()
    {
        var look = CharacterLooks.Resolve(Wearing(Worn(StickerSlots.HairFringe)));

        Assert.Null(look.FabricOf(StickerSlots.HairFringe));
        Assert.Null(look.FabricOf("hair"));
    }

    // ---- streaks ----------------------------------------------------------------------------

    [Fact]
    public void A_streak_takes_the_streak_colour_until_given_its_own()
    {
        var (first, second) = (Worn(StickerSlots.HairStreaks, StickerSlots.StreakColor, Pink), Worn(StickerSlots.HairStreaks, StickerSlots.StreakColor, Pink));
        var character = Coloured(Wearing(first, second), (StickerSlots.StreakColorKey(second.Id), Blue));

        var look = CharacterLooks.Resolve(character);

        Assert.Equal(Pink, look.Colors[StickerSlots.StreakColorKey(first.Id)]);
        Assert.Equal(Blue, look.Colors[StickerSlots.StreakColorKey(second.Id)]);
        Assert.Equal(Pink, look.Colors[StickerSlots.StreakColor]);
    }

    [Fact]
    public void A_picked_streak_colour_reaches_every_streak_without_one_of_its_own()
    {
        var (first, second) = (Worn(StickerSlots.HairStreaks, StickerSlots.StreakColor, Pink), Worn(StickerSlots.HairStreaks, StickerSlots.StreakColor, Pink));
        var character = Coloured(Wearing(first, second), (StickerSlots.StreakColor, Green));

        var look = CharacterLooks.Resolve(character);

        Assert.Equal(Green, look.Colors[StickerSlots.StreakColorKey(first.Id)]);
        Assert.Equal(Green, look.Colors[StickerSlots.StreakColorKey(second.Id)]);
    }

    // ---- the key a tagged element is coloured from ------------------------------------------

    [Fact]
    public void An_element_tagged_hair_is_coloured_from_its_pieces_own_key()
    {
        var id = StickerId.New();

        Assert.Equal(StickerSlots.HairFringe, StickerSlots.ColorKey(StickerSlots.HairFringe, id, "hair"));
        Assert.Equal(StickerSlots.HairBack, StickerSlots.ColorKey(StickerSlots.HairBack, id, "hair"));
        Assert.Equal("hair", StickerSlots.ColorKey(StickerSlots.Hair, id, "hair")); // a whole hairstyle is the hair itself
        Assert.Equal("accent", StickerSlots.ColorKey(StickerSlots.HairFringe, id, "accent")); // other tags are untouched
        Assert.Null(StickerSlots.ColorKey(StickerSlots.HairFringe, id, null));
        Assert.Equal(StickerSlots.StreakColorKey(id), StickerSlots.ColorKey(StickerSlots.HairStreaks, id, StickerSlots.StreakColor));
        Assert.True(StickerSlots.IsStreakColorKey(StickerSlots.StreakColorKey(id)));
    }

    [Fact]
    public void Streaks_and_every_kind_of_hair_are_hair()
    {
        Assert.All(new[] { StickerSlots.Hair, StickerSlots.HairTop, StickerSlots.HairFringe, StickerSlots.HairSides, StickerSlots.HairBack, StickerSlots.HairExtras, StickerSlots.HairStreaks },
            slot => Assert.True(StickerSlots.IsHair(slot), slot));
        Assert.All(new[] { StickerSlots.Headwear, StickerSlots.Glasses, StickerSlots.Brows, StickerSlots.FacialHair, StickerSlots.Top },
            slot => Assert.False(StickerSlots.IsHair(slot), slot));
    }
}
