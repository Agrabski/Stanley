using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Tests;

/// <summary>Resolving split eyes (docs/sticker-system.md §21): <see cref="WornSticker.Side"/> and the "eyesLeft"/"eyesRight" colour fallback.</summary>
public class SplitEyesResolutionTests
{
    private static Sticker EyeSticker(string name) =>
        new(StickerId.New(), name, StickerSlots.Eyes, [new StickerPart("eyes", BodyRegion.Head, Art: new PartArt(ArtMapping.Pin))],
            new SortedDictionary<string, ColorValue> { [StickerSlots.Eyes] = ColorValue.FromHex("#4a7ab5") }, ["neutral"]);

    private static CharacterDefinition SplitCharacter(Sticker left, Sticker right)
    {
        var character = CharacterDefinition.Create("A") with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Eyes] = [left.Id, right.Id] },
            StickerSides = new SortedDictionary<StickerId, LimbSide> { [left.Id] = LimbSide.Left, [right.Id] = LimbSide.Right },
        };
        var wardrobe = character.Wardrobe.With(new StickerAsset(left, new Dictionary<string, ArtFile>())).With(new StickerAsset(right, new Dictionary<string, ArtFile>()));
        return character with { Wardrobe = wardrobe };
    }

    [Fact]
    public void An_unsided_sticker_resolves_with_no_side()
    {
        var eyes = EyeSticker("Round");
        var character = CharacterDefinition.Create("A") with { Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Eyes] = [eyes.Id] } };
        character = character with { Wardrobe = character.Wardrobe.With(new StickerAsset(eyes, new Dictionary<string, ArtFile>())) };

        var worn = Assert.Single(CharacterLooks.Resolve(character).Stickers);

        Assert.Null(worn.Side);
    }

    [Fact]
    public void A_side_restricted_sticker_resolves_with_its_side()
    {
        var (left, right) = (EyeSticker("Round"), EyeSticker("Lashes"));
        var character = SplitCharacter(left, right);

        var look = CharacterLooks.Resolve(character);

        Assert.Equal(LimbSide.Left, look.Stickers.Single(w => w.Asset.Id == left.Id).Side);
        Assert.Equal(LimbSide.Right, look.Stickers.Single(w => w.Asset.Id == right.Id).Side);
    }

    [Fact]
    public void EyesLeft_and_eyesRight_fall_back_to_the_shared_eyes_colour_when_unset()
    {
        var (left, right) = (EyeSticker("Round"), EyeSticker("Round"));
        var character = SplitCharacter(left, right);

        var look = CharacterLooks.Resolve(character);

        Assert.Equal(ColorValue.FromHex("#4a7ab5"), look.Colors[StickerSlots.EyesLeft]);
        Assert.Equal(ColorValue.FromHex("#4a7ab5"), look.Colors[StickerSlots.EyesRight]);
    }

    [Fact]
    public void An_explicit_eyesLeft_colour_wins_over_the_shared_fallback()
    {
        var (left, right) = (EyeSticker("Round"), EyeSticker("Round"));
        var character = SplitCharacter(left, right) with
        {
            ColorSlots = new SortedDictionary<string, ColorValue> { [StickerSlots.EyesLeft] = ColorValue.FromHex("#ff0000") }
        };

        var look = CharacterLooks.Resolve(character);

        Assert.Equal(ColorValue.FromHex("#ff0000"), look.Colors[StickerSlots.EyesLeft]);
        Assert.Equal(ColorValue.FromHex("#4a7ab5"), look.Colors[StickerSlots.EyesRight]); // still the shared fallback
    }

    [Fact]
    public void A_character_level_eyes_override_still_reaches_both_sides()
    {
        var (left, right) = (EyeSticker("Round"), EyeSticker("Round"));
        var character = SplitCharacter(left, right) with
        {
            ColorSlots = new SortedDictionary<string, ColorValue> { [StickerSlots.Eyes] = ColorValue.FromHex("#123456") }
        };

        var look = CharacterLooks.Resolve(character);

        Assert.Equal(ColorValue.FromHex("#123456"), look.Colors[StickerSlots.EyesLeft]);
        Assert.Equal(ColorValue.FromHex("#123456"), look.Colors[StickerSlots.EyesRight]);
    }
}
