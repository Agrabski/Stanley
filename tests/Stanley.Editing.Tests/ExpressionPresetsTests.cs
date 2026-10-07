using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Editing.Tests;

public class ExpressionPresetsTests
{
    private static PoseData Rest => new(ViewAngle.Front, [], new SortedDictionary<string, string>());

    [Theory]
    [InlineData(ExpressionPreset.Happy, "happy", "neutral", "smile")]
    [InlineData(ExpressionPreset.Surprised, "wide", "raised", "o")]
    [InlineData(ExpressionPreset.Skeptical, "neutral", "skeptical", "doubtful")]
    [InlineData(ExpressionPreset.Seductive, "halfClosed", "skeptical", "smirk")]
    [InlineData(ExpressionPreset.Asleep, "closed", "neutral", "neutral")]
    public void A_preset_sets_each_face_slot(ExpressionPreset preset, string eyes, string brows, string mouth)
    {
        var pose = ExpressionPresets.Apply(Rest, ExpressionPresets.Get(preset));

        string Of(string slot) => pose.Expression.GetValueOrDefault(slot) ?? ExpressionPresets.Neutral;
        Assert.Equal(eyes, Of(StickerSlots.Eyes));
        Assert.Equal(brows, Of(StickerSlots.Brows));
        Assert.Equal(mouth, Of(StickerSlots.Mouth));
        Assert.Equal(preset, ExpressionPresets.Of(pose)?.Preset);
    }

    [Fact]
    public void Splitting_eyes_also_sets_the_presets_eyes_on_eyesLeft_and_eyesRight()
    {
        var pose = ExpressionPresets.Apply(Rest, ExpressionPresets.Get(ExpressionPreset.Surprised), splitEyes: true);

        Assert.Equal("wide", pose.Expression[StickerSlots.EyesLeft]);
        Assert.Equal("wide", pose.Expression[StickerSlots.EyesRight]);
        Assert.Equal("wide", pose.Expression[StickerSlots.Eyes]); // the shared key still applies too, for an unsplit character
    }

    [Fact]
    public void Unsplit_apply_never_touches_eyesLeft_or_eyesRight()
    {
        var pose = ExpressionPresets.Apply(Rest, ExpressionPresets.Get(ExpressionPreset.Surprised));

        Assert.DoesNotContain(StickerSlots.EyesLeft, pose.Expression.Keys);
        Assert.DoesNotContain(StickerSlots.EyesRight, pose.Expression.Keys);
    }

    [Fact]
    public void Neutral_split_removes_eyesLeft_and_eyesRight_too()
    {
        var happySplit = ExpressionPresets.Apply(Rest, ExpressionPresets.Get(ExpressionPreset.Happy), splitEyes: true);

        var neutralSplit = ExpressionPresets.Apply(happySplit, ExpressionPresets.Get(ExpressionPreset.Neutral), splitEyes: true);

        Assert.DoesNotContain(StickerSlots.EyesLeft, neutralSplit.Expression.Keys);
        Assert.DoesNotContain(StickerSlots.EyesRight, neutralSplit.Expression.Keys);
    }

    [Fact]
    public void Neutral_stores_nothing_for_the_face_and_other_slots_keep_their_variant()
    {
        var pose = Rest with { Expression = new SortedDictionary<string, string> { ["headwear"] = "tipped", [StickerSlots.Mouth] = "grin" } };

        var neutral = ExpressionPresets.Apply(pose, ExpressionPresets.Get(ExpressionPreset.Neutral));

        Assert.Equal(["headwear"], neutral.Expression.Keys);
        Assert.Equal(ExpressionPreset.Neutral, ExpressionPresets.Of(neutral)?.Preset);
    }

    [Fact]
    public void A_mix_of_its_own_is_no_preset()
    {
        var pose = Rest with { Expression = new SortedDictionary<string, string> { [StickerSlots.Eyes] = "wink", [StickerSlots.Mouth] = "frown" } };

        Assert.Null(ExpressionPresets.Of(pose));
    }

    [Fact]
    public void Mixing_your_own_sets_one_face_slot_and_keeps_the_rest()
    {
        var happy = ExpressionPresets.Apply(Rest, ExpressionPresets.Get(ExpressionPreset.Happy));

        var openMouthed = ExpressionPresets.SetVariant(happy, StickerSlots.Mouth, "open");

        Assert.Equal("happy", ExpressionPresets.VariantOf(openMouthed, StickerSlots.Eyes));
        Assert.Equal(ExpressionPresets.Neutral, ExpressionPresets.VariantOf(openMouthed, StickerSlots.Brows));
        Assert.Equal("open", ExpressionPresets.VariantOf(openMouthed, StickerSlots.Mouth));
        Assert.Null(ExpressionPresets.Of(openMouthed)); // a mix of its own, no preset
        Assert.Equal([StickerSlots.Eyes], ExpressionPresets.SetVariant(openMouthed, StickerSlots.Mouth, ExpressionPresets.Neutral).Expression.Keys);
        Assert.Equal(ExpressionPreset.Happy, ExpressionPresets.Of(ExpressionPresets.SetVariant(openMouthed, StickerSlots.Mouth, "smile"))?.Preset);
    }

    [Theory]
    [InlineData(StickerSlots.Eyes, "halfClosed", "Half closed")]
    [InlineData(StickerSlots.Eyes, "wide", "Wide open")]
    [InlineData(StickerSlots.Mouth, "o", "Oh")]
    [InlineData(StickerSlots.Mouth, "grin", "Grin")]
    [InlineData(StickerSlots.Brows, "neutral", "Neutral")]
    public void Variants_have_names_people_can_read(string slot, string variant, string name) =>
        Assert.Equal(name, ExpressionPresets.VariantName(slot, variant));

    [Fact]
    public void The_face_slots_are_the_ones_the_vocabulary_covers() =>
        Assert.Equal(ExpressionPresets.Vocabulary.Keys.Order(), ExpressionPresets.FaceSlots.Order());

    [Fact]
    public void Every_preset_asks_only_for_the_standard_vocabulary()
    {
        foreach (var preset in ExpressionPresets.All)
            foreach (var (slot, variant) in preset.Variants)
                Assert.Contains(variant, ExpressionPresets.Vocabulary[slot]);
        Assert.Equal(Enum.GetValues<ExpressionPreset>().Length, ExpressionPresets.All.Count);
    }

    [Fact]
    public void Missing_variants_are_listed_for_face_stickers_only()
    {
        var eyes = new Sticker(StickerId.New(), "Eyes", StickerSlots.Eyes, [], new SortedDictionary<string, ColorValue>(), ["neutral", "happy"]);
        var hat = eyes with { Slot = StickerSlots.Headwear };

        Assert.Equal(["sad", "angry", "wide", "closed", "wink", "halfClosed"], ExpressionPresets.MissingVariants(eyes));
        Assert.Empty(ExpressionPresets.MissingVariants(hat));
    }
}
