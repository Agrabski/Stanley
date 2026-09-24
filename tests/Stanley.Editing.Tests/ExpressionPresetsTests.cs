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
    [InlineData(ExpressionPreset.Skeptical, "halfClosed", "skeptical", "smirk")]
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
