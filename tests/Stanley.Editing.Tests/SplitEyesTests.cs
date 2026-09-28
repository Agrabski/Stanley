using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editing.Tests;

/// <summary>Split eyes (docs/sticker-system.md §21, GitHub issues #100-#102): independent shape, colour and expression per eye.</summary>
public class SplitEyesTests
{
    private static readonly ColorValue DefaultEyeColor = ColorValue.FromHex("#4a7ab5");

    private static StickerAsset Asset(string name, string? source = null) =>
        new(new Sticker(StickerId.New(), name, StickerSlots.Eyes, [new StickerPart("eyes", BodyRegion.Head, Art: new PartArt(ArtMapping.Pin))],
            new SortedDictionary<string, ColorValue> { [StickerSlots.Eyes] = DefaultEyeColor }, ["neutral", "happy"], source), new Dictionary<string, ArtFile>());

    [Fact]
    public void Splitting_copies_the_worn_eyes_sticker_to_the_other_side_unchanged()
    {
        var round = Asset("Round");
        var c = LookEditing.Wear(CharacterDefinition.Create("A"), round);
        Assert.False(LookEditing.IsEyesSplit(c));

        var split = LookEditing.SplitEyes(c);

        Assert.True(LookEditing.IsEyesSplit(split));
        var worn = split.Stickers[StickerSlots.Eyes];
        Assert.Equal(2, worn.Count);
        Assert.Equal(round.Id, worn[0]); // the left eye keeps the original id
        Assert.NotEqual(round.Id, worn[1]);
        Assert.Equal(LimbSide.Left, split.StickerSides![worn[0]]);
        Assert.Equal(LimbSide.Right, split.StickerSides![worn[1]]);
        Assert.Equal("Round", split.Wardrobe.Find(worn[1])!.Sticker.Name);
        Assert.Same(split, LookEditing.SplitEyes(split)); // already split
    }

    [Fact]
    public void Splitting_with_no_eyes_worn_does_nothing()
    {
        var c = CharacterDefinition.Create("A");
        Assert.Same(c, LookEditing.SplitEyes(c));
        Assert.False(LookEditing.IsEyesSplit(c));
    }

    [Fact]
    public void Wearing_on_one_side_leaves_the_other_alone()
    {
        var round = Asset("Round");
        var lashes = Asset("Lashes");
        var c = LookEditing.SplitEyes(LookEditing.Wear(CharacterDefinition.Create("A"), round));

        c = LookEditing.WearOnSide(c, lashes, LimbSide.Right);

        Assert.Equal("Round", LookEditing.EyeSticker(c, LimbSide.Left)!.Sticker.Name);
        Assert.Equal("Lashes", LookEditing.EyeSticker(c, LimbSide.Right)!.Sticker.Name);
        Assert.Equal(2, c.Stickers[StickerSlots.Eyes].Count); // the old right sticker came off, not stacked
    }

    [Fact]
    public void Wearing_the_other_sides_own_design_makes_it_a_copy_instead_of_stealing_it()
    {
        var round = Asset("Round");
        var c = LookEditing.SplitEyes(LookEditing.Wear(CharacterDefinition.Create("A"), round));
        var rightId = c.Stickers[StickerSlots.Eyes][1];
        var rightCopy = c.Wardrobe.Find(rightId)!;

        // Picking "Round" for the left eye too, via the very copy currently worn on the right.
        var resolved = LookEditing.ResolveForSide(c, rightCopy, LimbSide.Left);
        c = LookEditing.WearOnSide(c, resolved, LimbSide.Left);

        Assert.NotEqual(rightId, resolved.Id); // a fresh copy, not the right eye's own id
        Assert.Equal(LimbSide.Right, c.StickerSides![rightId]); // the right eye is untouched
        Assert.Equal("Round", LookEditing.EyeSticker(c, LimbSide.Left)!.Sticker.Name);
        Assert.Equal("Round", LookEditing.EyeSticker(c, LimbSide.Right)!.Sticker.Name);
        Assert.NotEqual(LookEditing.EyeSticker(c, LimbSide.Left)!.Id, LookEditing.EyeSticker(c, LimbSide.Right)!.Id);
    }

    [Fact]
    public void Resolving_for_the_same_side_or_an_unworn_design_changes_nothing()
    {
        var round = Asset("Round");
        var c = LookEditing.SplitEyes(LookEditing.Wear(CharacterDefinition.Create("A"), round));
        var leftAsset = LookEditing.EyeSticker(c, LimbSide.Left)!;
        var fresh = Asset("Lashes");

        Assert.Same(leftAsset, LookEditing.ResolveForSide(c, leftAsset, LimbSide.Left));
        Assert.Same(fresh, LookEditing.ResolveForSide(c, fresh, LimbSide.Right));
    }

    [Fact]
    public void Unsplitting_keeps_the_left_eye_and_takes_off_the_right()
    {
        var round = Asset("Round");
        var lashes = Asset("Lashes");
        var c = LookEditing.SplitEyes(LookEditing.Wear(CharacterDefinition.Create("A"), round));
        c = LookEditing.WearOnSide(c, lashes, LimbSide.Right);
        c = LookEditing.SetColor(c, StickerSlots.EyesLeft, ColorValue.FromHex("#ff0000"));
        c = LookEditing.SetColor(c, StickerSlots.EyesRight, ColorValue.FromHex("#00ff00"));

        var unsplit = LookEditing.UnsplitEyes(c);

        Assert.False(LookEditing.IsEyesSplit(unsplit));
        Assert.Equal([round.Id], unsplit.Stickers[StickerSlots.Eyes]);
        Assert.Null(unsplit.StickerSides);
        Assert.False(unsplit.ColorSlots.ContainsKey(StickerSlots.EyesLeft));
        Assert.False(unsplit.ColorSlots.ContainsKey(StickerSlots.EyesRight));
        Assert.Same(unsplit, LookEditing.UnsplitEyes(unsplit)); // already unsplit
    }

    [Fact]
    public void Colour_slots_in_use_list_left_and_right_once_split()
    {
        var round = Asset("Round");
        var c = LookEditing.SplitEyes(LookEditing.Wear(CharacterDefinition.Create("A"), round));

        Assert.Equal(["skin", StickerSlots.EyesLeft, StickerSlots.EyesRight], LookEditing.ColorSlotsInUse(c));
    }

    [Fact]
    public void A_split_eye_with_no_colour_of_its_own_falls_back_to_the_shared_eyes_colour()
    {
        var round = Asset("Round");
        var c = LookEditing.SplitEyes(LookEditing.Wear(CharacterDefinition.Create("A"), round));

        var look = CharacterLooks.Resolve(c);
        Assert.Equal(DefaultEyeColor, look.Colors[StickerSlots.EyesLeft]);
        Assert.Equal(DefaultEyeColor, look.Colors[StickerSlots.EyesRight]);

        var recoloredLeft = LookEditing.SetColor(c, StickerSlots.EyesLeft, ColorValue.FromHex("#00ff00"));
        var recoloredLook = CharacterLooks.Resolve(recoloredLeft);
        Assert.Equal(ColorValue.FromHex("#00ff00"), recoloredLook.Colors[StickerSlots.EyesLeft]);
        Assert.Equal(DefaultEyeColor, recoloredLook.Colors[StickerSlots.EyesRight]); // untouched

        // A legacy (pre-split) "eyes" override still reaches both sides while neither has its own.
        var legacy = LookEditing.SetColor(c, StickerSlots.Eyes, ColorValue.FromHex("#123456"));
        var legacyLook = CharacterLooks.Resolve(legacy);
        Assert.Equal(ColorValue.FromHex("#123456"), legacyLook.Colors[StickerSlots.EyesLeft]);
        Assert.Equal(ColorValue.FromHex("#123456"), legacyLook.Colors[StickerSlots.EyesRight]);
    }

    [Fact]
    public void Each_side_resolves_its_own_worn_sticker_with_its_own_side_on_the_look()
    {
        var round = Asset("Round");
        var lashes = Asset("Lashes");
        var c = LookEditing.SplitEyes(LookEditing.Wear(CharacterDefinition.Create("A"), round));
        c = LookEditing.WearOnSide(c, lashes, LimbSide.Right);

        var look = CharacterLooks.Resolve(c);
        var eyeStickers = look.Stickers.Where(w => w.Slot == StickerSlots.Eyes).ToList();
        Assert.Equal(2, eyeStickers.Count);
        Assert.Contains(eyeStickers, w => w.Asset.Sticker.Name == "Round" && w.Side == LimbSide.Left);
        Assert.Contains(eyeStickers, w => w.Asset.Sticker.Name == "Lashes" && w.Side == LimbSide.Right);
    }
}
