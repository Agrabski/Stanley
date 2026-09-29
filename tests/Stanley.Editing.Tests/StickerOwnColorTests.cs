using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editing.Tests;

/// <summary>GitHub issue #127: any worn sticker can take a colour of its own, apart from the others in its slot.</summary>
public class StickerOwnColorTests
{
    private static readonly ColorValue Red = ColorValue.FromHex("#c0392b");
    private static readonly ColorValue Blue = ColorValue.FromHex("#3a6fd8");
    private static readonly ColorValue Green = ColorValue.FromHex("#4caf50");

    private static StickerAsset Hat(string name) =>
        new(new Sticker(StickerId.New(), name, StickerSlots.Headwear, [new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp))],
            new SortedDictionary<string, ColorValue> { ["hat"] = Green }, ["default"], null), new Dictionary<string, ArtFile>());

    private static CharacterDefinition Wearing(params StickerAsset[] assets) => assets.Aggregate(CharacterDefinition.Create("A"), LookEditing.Wear);

    [Fact]
    public void A_sticker_follows_its_slots_colour_until_given_its_own()
    {
        var (a, b) = (Hat("A"), Hat("B"));
        var character = LookEditing.SetColor(Wearing(a, b), "hat", Red);

        var look = CharacterLooks.Resolve(character);
        Assert.Equal(Red, look.Colors[StickerSlots.StickerColorKey(StickerSlots.Headwear, a.Id)]);

        character = LookEditing.SetColor(character, StickerSlots.StickerColorKey(StickerSlots.Headwear, a.Id), Blue);
        look = CharacterLooks.Resolve(character);
        Assert.Equal(Blue, look.Colors[StickerSlots.StickerColorKey(StickerSlots.Headwear, a.Id)]);
        Assert.Equal(Red, look.Colors[StickerSlots.StickerColorKey(StickerSlots.Headwear, b.Id)]); // the other hat keeps the slot's
        Assert.Equal(Red, look.Colors["hat"]);
    }

    [Fact]
    public void The_colour_key_of_a_worn_sticker_is_its_own_and_the_face_and_hair_keep_theirs()
    {
        var hat = Hat("A");
        var character = Wearing(hat);
        var key = StickerSlots.StickerColorKey(StickerSlots.Headwear, hat.Id);
        Assert.Equal(key, HairEditing.ColorKeyOf(character, hat.Id));
        Assert.Equal(key, StickerSlots.ColorKey(StickerSlots.Headwear, hat.Id, "hat"));
        Assert.Equal("accent", StickerSlots.ColorKey(StickerSlots.Headwear, hat.Id, "accent")); // other tags stay shared
        Assert.False(StickerSlots.HasOwnColorKey(StickerSlots.Eyes));
        Assert.False(StickerSlots.HasOwnColorKey(StickerSlots.HairFringe));
        Assert.Equal("hat", StickerSlots.SharedColorOf(key));
        Assert.Equal(hat.Id, StickerSlots.StickerOfKey(key));
    }

    [Fact]
    public void Same_as_slot_drops_the_own_colour_and_removing_the_sticker_drops_it_from_the_files()
    {
        var (a, b) = (Hat("A"), Hat("B"));
        var key = StickerSlots.StickerColorKey(StickerSlots.Headwear, a.Id);
        var character = LookEditing.SetColor(LookEditing.SetColor(Wearing(a, b), "hat", Red), key, Blue);

        Assert.DoesNotContain(key, HairEditing.FollowHair(character, key).ColorSlots.Keys);

        var gone = HairEditing.DropOrphanStreakColors(LookEditing.RemoveFromWardrobe(character, a.Id));
        Assert.DoesNotContain(key, gone.ColorSlots.Keys);
        Assert.Contains("hat", gone.ColorSlots.Keys);
    }

    [Fact]
    public void The_look_tab_does_not_list_a_stickers_own_colour()
    {
        var hat = Hat("A");
        var character = LookEditing.SetColor(Wearing(hat), StickerSlots.StickerColorKey(StickerSlots.Headwear, hat.Id), Blue);
        Assert.DoesNotContain(StickerSlots.StickerColorKey(StickerSlots.Headwear, hat.Id),
            HairEditing.ColorGroupSlots(character, LookEditing.ColorSlotsInUse(character)));
    }
}
