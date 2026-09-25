using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Editing.Tests;

public class SavedFacesTests
{
    private static PoseData Face(params (string Slot, string Variant)[] variants) =>
        new(ViewAngle.Front, [], new SortedDictionary<string, string>(variants.ToDictionary(v => v.Slot, v => v.Variant)));

    [Fact]
    public void A_saved_face_keeps_the_face_slots_variants_only()
    {
        var pose = Face((StickerSlots.Eyes, "happy"), (StickerSlots.Mouth, "open"), (StickerSlots.Brows, ExpressionPresets.Neutral), (StickerSlots.Headwear, "tipped"));

        var face = SavedFaces.Capture("  Grinning ", pose);

        Assert.Equal("Grinning", face.Name);
        Assert.Equal(new SortedDictionary<string, string> { [StickerSlots.Eyes] = "happy", [StickerSlots.Mouth] = "open" }, face.Variants);
    }

    [Fact]
    public void Applying_a_saved_face_sets_every_face_slot_and_leaves_the_rest()
    {
        var face = SavedFaces.Capture("Grinning", Face((StickerSlots.Eyes, "happy"), (StickerSlots.Mouth, "open")));
        var pose = Face((StickerSlots.Brows, "angry"), (StickerSlots.Nose, "scrunched"), (StickerSlots.Headwear, "tipped"));

        var applied = SavedFaces.Apply(pose, face);

        Assert.Equal(new SortedDictionary<string, string> { [StickerSlots.Eyes] = "happy", [StickerSlots.Headwear] = "tipped", [StickerSlots.Mouth] = "open" },
            applied.Expression);
        Assert.True(SavedFaces.Shows(applied, face));
        Assert.False(SavedFaces.Shows(pose, face));
    }

    [Fact]
    public void Saving_under_a_name_already_there_replaces_it_in_place_and_deleting_the_last_writes_none()
    {
        var character = CharacterDefinition.Create("Pip");
        var grin = SavedFaces.Capture("Grin", Face((StickerSlots.Mouth, "grin")));
        var pout = SavedFaces.Capture("Pout", Face((StickerSlots.Mouth, "frown")));

        character = SavedFaces.Save(SavedFaces.Save(character, grin), pout);
        character = SavedFaces.Save(character, SavedFaces.Capture("grin", Face((StickerSlots.Mouth, "smirk"))));

        Assert.Equal(["grin", "Pout"], character.Expressions!.Select(f => f.Name));
        Assert.Equal("smirk", character.Expressions![0].Variants[StickerSlots.Mouth]);
        Assert.Equal("Pout", SavedFaces.Of(character, Face((StickerSlots.Mouth, "frown")))?.Name);
        Assert.Null(SavedFaces.Of(character, Face((StickerSlots.Mouth, "open"))));

        character = SavedFaces.Delete(SavedFaces.Delete(character, "GRIN"), "Pout");
        Assert.Null(character.Expressions);
    }

    [Fact]
    public void A_row_offers_what_the_worn_face_draws_the_standard_ones_first_then_its_own()
    {
        var mouth = new Sticker(StickerId.New(), "Mine", StickerSlots.Mouth, [], new SortedDictionary<string, ColorValue>(),
            ["crying", "smile", ExpressionPresets.Neutral, "o"]);

        Assert.Equal([ExpressionPresets.Neutral, "smile", "o", "crying"], SavedFaces.VariantsFor(StickerSlots.Mouth, [mouth]));
        Assert.Empty(SavedFaces.VariantsFor(StickerSlots.Mouth, []));
    }

    [Theory]
    [InlineData("My mouth", new string[0], "myMouth")]
    [InlineData("My mouth", new[] { "myMouth" }, "myMouth2")]
    [InlineData("Szeroki uśmiech!", new string[0], "szerokiUśmiech")]
    [InlineData(" - ", new string[0], "drawn")]
    public void A_new_variant_gets_a_folder_safe_key(string name, string[] taken, string key) =>
        Assert.Equal(key, SavedFaces.NewVariantKey(name, taken));

    [Fact]
    public void A_drawn_variants_key_reads_back_as_words() =>
        Assert.Equal("My mouth 2", ExpressionPresets.VariantName(StickerSlots.Mouth, "myMouth2"));
}
