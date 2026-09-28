using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Editing.Tests;

/// <summary>Styles (docs/sticker-system.md §20): choosing how a sticker is worn, kept sparse on the character, a named look or one panel.</summary>
public class StickerStylesTests
{
    private static StickerAsset Asset(string name, string slot, string? source, params string[] variants) =>
        new(new Sticker(StickerId.New(), name, slot, [new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp))],
            new SortedDictionary<string, ColorValue>(), variants, Source: source), new Dictionary<string, ArtFile>());

    private static readonly StickerAsset Cap = Asset("Cap", StickerSlots.Headwear, null, "front", "back", "left", "right");
    private static readonly StickerAsset Hood = Asset("Hood", StickerSlots.Headwear, null, "down", "up");

    /// <summary>A character wearing the hood and the cap together, both in the headwear slot.</summary>
    private static CharacterDefinition Hatted()
    {
        var character = CharacterDefinition.Create("A") with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Headwear] = [Hood.Id, Cap.Id] },
        };
        return character with { Wardrobe = character.Wardrobe.With(Cap).With(Hood) };
    }

    private static string Shown(CharacterDefinition character, StickerAsset asset, CharacterRevision? look = null, CharacterInstanceOverrides? panel = null)
    {
        var worn = CharacterLooks.Resolve(character, look, panel).Stickers.Single(w => w.Asset.Id == asset.Id);
        return worn.Asset.Sticker.VariantFor(worn.Slot, null, worn.Variant);
    }

    [Fact]
    public void Choosing_a_style_stores_it_and_the_default_style_removes_the_entry()
    {
        var character = LookEditing.SetVariant(Hatted(), Cap.Id, "back");
        Assert.Equal("back", character.StickerVariants![Cap.Id]);
        Assert.Equal("back", Shown(character, Cap));

        var back = LookEditing.SetVariant(character, Cap.Id, "front");

        Assert.Null(back.StickerVariants); // nothing left: absent, so the file stays as it was
        Assert.Equal("front", Shown(back, Cap));
        Assert.Same(back, LookEditing.SetVariant(back, Cap.Id, "front")); // already its default way
    }

    [Fact]
    public void A_style_the_sticker_lacks_or_a_sticker_not_in_the_wardrobe_changes_nothing()
    {
        var character = Hatted();

        Assert.Same(character, LookEditing.SetVariant(character, Cap.Id, "inside-out"));
        Assert.Same(character, LookEditing.SetVariant(character, StickerId.New(), "back"));
        var styled = LookEditing.SetVariant(character, Cap.Id, "left");
        Assert.Same(styled, LookEditing.SetVariant(styled, Cap.Id, "left"));
    }

    [Fact]
    public void Two_hats_in_one_slot_keep_their_own_styles()
    {
        var character = LookEditing.SetVariant(LookEditing.SetVariant(Hatted(), Cap.Id, "back"), Hood.Id, "up");
        character = LookEditing.SetVariant(character, Cap.Id, "right");

        Assert.Equal("right", Shown(character, Cap));
        Assert.Equal("up", Shown(character, Hood));
        Assert.Equal(2, character.StickerVariants!.Count);
    }

    [Fact]
    public void A_named_look_keeps_only_the_styles_that_differ_from_the_default_look()
    {
        var (character, winter) = LookEditing.NewLook(LookEditing.SetVariant(Hatted(), Cap.Id, "back"), "Winter");

        // In the look: the hood up, and the cap turned back to its default way - which has to be said, or the default look's "back" would show through.
        var edited = LookEditing.Project(character, character.Revisions[winter]);
        edited = LookEditing.SetVariant(LookEditing.SetVariant(edited, Hood.Id, "up"), Cap.Id, "front");
        character = LookEditing.StoreLook(character, winter, edited);

        var look = character.Revisions[winter];
        Assert.Equal(new Dictionary<StickerId, string> { [Hood.Id] = "up", [Cap.Id] = "front" }, look.StickerVariantValues);
        Assert.Equal("back", character.StickerVariants![Cap.Id]); // the default look is as it was
        Assert.Null(character.StickerVariants.GetValueOrDefault(Hood.Id));
        Assert.Equal("front", Shown(character, Cap, look));
        Assert.Equal("up", Shown(character, Hood, look));

        // Back to what the default look wears: no difference left to keep.
        edited = LookEditing.SetVariant(LookEditing.Project(character, look), Cap.Id, "back");
        character = LookEditing.StoreLook(character, winter, edited);
        Assert.Equal([Hood.Id], character.Revisions[winter].StickerVariantValues!.Keys);
    }

    [Fact]
    public void A_new_look_starts_with_the_styles_of_the_look_it_copies()
    {
        var (character, winter) = LookEditing.NewLook(Hatted(), "Winter");
        character = LookEditing.StoreLook(character, winter, LookEditing.SetVariant(LookEditing.Project(character, character.Revisions[winter]), Hood.Id, "up"));

        var (copied, copy) = LookEditing.NewLook(character, "Winter 2", winter);

        Assert.Equal(character.Revisions[winter].StickerVariantValues, copied.Revisions[copy].StickerVariantValues);
        Assert.NotSame(character.Revisions[winter].StickerVariantValues, copied.Revisions[copy].StickerVariantValues);
        Assert.Null(LookEditing.NewLook(Hatted(), "Plain").Character.Revisions.Values.Single().StickerVariantValues);
    }

    [Fact]
    public void One_panel_keeps_only_the_styles_that_differ_from_its_look()
    {
        var character = LookEditing.SetVariant(Hatted(), Cap.Id, "back");
        var instance = new CharacterInstance(character.Id, new CharacterPlacement(default, 100, false), null, new PoseData(ViewAngle.Front, [], []), null);

        var hoodUp = LookEditing.StorePanel(character, null, instance, LookEditing.SetVariant(LookEditing.Project(character), Hood.Id, "up"));
        var same = LookEditing.StorePanel(character, null, instance, LookEditing.SetVariant(LookEditing.Project(character), Cap.Id, "back"));
        var capFront = LookEditing.StorePanel(character, null, instance, LookEditing.SetVariant(LookEditing.Project(character), Cap.Id, "front"));

        Assert.Equal(new Dictionary<StickerId, string> { [Hood.Id] = "up" }, hoodUp.Overrides!.StickerVariantOverrides);
        Assert.Null(hoodUp.Overrides.ActiveStickerOverrides);
        Assert.Null(hoodUp.Overrides.ColorSlotOverrides);
        Assert.Null(same.Overrides); // nothing differs from the look
        Assert.Equal("front", capFront.Overrides!.StickerVariantOverrides![Cap.Id]);
        Assert.Equal("front", Shown(character, Cap, null, capFront.Overrides));
        Assert.Equal("back", Shown(character, Cap)); // every other panel
    }

    [Fact]
    public void Removing_a_sticker_from_the_wardrobe_drops_its_styles_everywhere()
    {
        var (character, winter) = LookEditing.NewLook(LookEditing.SetVariant(LookEditing.SetVariant(Hatted(), Cap.Id, "back"), Hood.Id, "up"), "Winter");
        character = LookEditing.StoreLook(character, winter, LookEditing.SetVariant(LookEditing.Project(character, character.Revisions[winter]), Cap.Id, "left"));
        Assert.Equal([Cap.Id], character.Revisions[winter].StickerVariantValues!.Keys);

        var removed = LookEditing.RemoveFromWardrobe(character, Cap.Id);

        Assert.Equal([Hood.Id], removed.StickerVariants!.Keys);
        Assert.Null(removed.Revisions[winter].StickerVariantValues);
        Assert.Null(LookEditing.RemoveFromWardrobe(removed, Hood.Id).StickerVariants);
    }

    [Fact]
    public void Tidying_drops_the_styles_of_library_copies_it_throws_away()
    {
        var triedOn = Asset("Beret", StickerSlots.Headwear, "library:headwear/beret", "flat", "tilted");
        var character = Hatted() with { Wardrobe = Hatted().Wardrobe.With(triedOn) };
        character = LookEditing.SetVariant(LookEditing.SetVariant(character, triedOn.Id, "tilted"), Hood.Id, "up");
        var (withLook, winter) = LookEditing.NewLook(character, "Winter");
        withLook = withLook with
        {
            Revisions = new Dictionary<CharacterRevisionId, CharacterRevision>
            {
                [winter] = withLook.Revisions[winter] with { StickerVariantValues = new SortedDictionary<StickerId, string> { [triedOn.Id] = "flat" } }
            }
        };

        var tidy = LookEditing.TidyWardrobe(withLook, new HashSet<StickerId>());

        Assert.Null(tidy.Wardrobe.Find(triedOn.Id));
        Assert.Equal([Hood.Id], tidy.StickerVariants!.Keys);
        Assert.Null(tidy.Revisions[winter].StickerVariantValues);
        Assert.Same(tidy, LookEditing.TidyWardrobe(tidy, new HashSet<StickerId>())); // nothing more to tidy
    }

    [Fact]
    public void Only_stickers_outside_the_face_with_more_than_one_variant_have_styles()
    {
        var eyes = Asset("Eyes", StickerSlots.Eyes, null, "neutral", "happy").Sticker;
        var plain = Asset("Beanie", StickerSlots.Headwear, null, "default").Sticker;

        Assert.True(LookEditing.HasStyles(Cap.Sticker, StickerSlots.Headwear));
        Assert.False(LookEditing.HasStyles(eyes, StickerSlots.Eyes));
        Assert.False(LookEditing.HasStyles(plain, StickerSlots.Headwear));
    }

    [Theory]
    [InlineData("up", "Up")]
    [InlineData("brim-back", "Brim back")]
    [InlineData("brimBack", "Brim back")]
    [InlineData("brim_left", "Brim left")]
    [InlineData("default", "Default")]
    public void A_styles_name_is_its_key_made_readable(string variant, string name) =>
        Assert.Equal(name, LookEditing.StyleName(variant));
}
