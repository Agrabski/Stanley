using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editing.Tests;

public class LookEditingTests
{
    private static StickerAsset Asset(string slot, string name, string? source = null, params StickerPart[] parts) =>
        new(new Sticker(StickerId.New(), name, slot, parts.Length > 0 ? parts : [new StickerPart("body", BodyRegion.Torso, Cover: new PartCover(slot, 0, 1))],
            new SortedDictionary<string, ColorValue> { [slot] = ColorValue.FromHex("#123456") }, ["default"], source), new Dictionary<string, ArtFile>());

    private static IReadOnlyList<StickerId> Worn(CharacterDefinition c, string slot) => c.Stickers.TryGetValue(slot, out var ids) ? ids : [];

    [Fact]
    public void Wearing_replaces_what_the_slot_held_and_keeps_both_in_the_wardrobe()
    {
        var (tee, sweater) = (Asset(StickerSlots.Top, "Tee"), Asset(StickerSlots.Top, "Sweater"));
        var c = LookEditing.Wear(CharacterDefinition.Create("A"), tee);
        c = LookEditing.Wear(c, sweater);

        Assert.Equal([sweater.Id], Worn(c, StickerSlots.Top));
        Assert.NotNull(c.Wardrobe.Find(tee.Id));
        Assert.Same(c, LookEditing.Wear(c, sweater));
    }

    [Fact]
    public void Accessories_stack_and_any_slot_stacks_on_request()
    {
        var (watch, ring) = (Asset(StickerSlots.Accessory, "Watch"), Asset(StickerSlots.Accessory, "Ring"));
        var c = LookEditing.Wear(LookEditing.Wear(CharacterDefinition.Create("A"), watch), ring);
        Assert.Equal([watch.Id, ring.Id], Worn(c, StickerSlots.Accessory));

        var (tee, vest) = (Asset(StickerSlots.Top, "Tee"), Asset(StickerSlots.Top, "Vest"));
        c = LookEditing.Wear(LookEditing.Wear(c, tee), vest, stack: true);
        Assert.Equal([tee.Id, vest.Id], Worn(c, StickerSlots.Top));

        c = LookEditing.MoveInStack(c, tee.Id, +1);
        Assert.Equal([vest.Id, tee.Id], Worn(c, StickerSlots.Top));
        Assert.Same(c, LookEditing.MoveInStack(c, tee.Id, +1)); // already on top
    }

    [Fact]
    public void None_empties_the_slot_and_taking_off_keeps_it_in_the_wardrobe()
    {
        var tee = Asset(StickerSlots.Top, "Tee");
        var c = LookEditing.Wear(CharacterDefinition.Create("A"), tee);

        Assert.Empty(Worn(LookEditing.ClearSlot(c, StickerSlots.Top), StickerSlots.Top));
        var off = LookEditing.TakeOff(c, tee.Id);
        Assert.Empty(Worn(off, StickerSlots.Top));
        Assert.NotNull(off.Wardrobe.Find(tee.Id));
    }

    [Fact]
    public void Removing_from_the_wardrobe_takes_it_off_everywhere_the_character_wears_it()
    {
        var tee = Asset(StickerSlots.Top, "Tee");
        var c = LookEditing.Wear(CharacterDefinition.Create("A"), tee);
        var lookId = CharacterRevisionId.New();
        c = c with
        {
            Revisions = new Dictionary<CharacterRevisionId, CharacterRevision>
            {
                [lookId] = new(lookId, c.Id, "Winter", new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Top] = [tee.Id] }, [], null, null)
            }
        };

        var removed = LookEditing.RemoveFromWardrobe(c, tee.Id);

        Assert.Null(removed.Wardrobe.Find(tee.Id));
        Assert.Empty(Worn(removed, StickerSlots.Top));
        Assert.Empty(removed.Revisions[lookId].ActiveStickers[StickerSlots.Top]);
    }

    [Fact]
    public void A_colour_belongs_to_the_character_and_survives_a_change_of_clothes()
    {
        var (tee, sweater) = (Asset(StickerSlots.Top, "Tee"), Asset(StickerSlots.Top, "Sweater"));
        var c = LookEditing.SetColor(LookEditing.Wear(CharacterDefinition.Create("A"), tee), "top", ColorValue.FromHex("#ff0000"));
        c = LookEditing.Wear(c, sweater);

        Assert.Equal(ColorValue.FromHex("#ff0000"), CharacterLooks.Resolve(c).Colors["top"]);
        Assert.Equal(["skin", "top"], LookEditing.ColorSlotsInUse(c));
    }

    [Fact]
    public void Editing_a_library_copy_makes_it_the_users_own_and_tidying_keeps_only_what_is_worn_or_own()
    {
        var worn = Asset(StickerSlots.Top, "Worn", "library:top/a");
        var triedOn = Asset(StickerSlots.Bottom, "Tried on", "library:bottom/b");
        var inAPanel = Asset(StickerSlots.Shoes, "In a panel", "library:shoes/c");
        var imported = Asset(StickerSlots.Outer, "Imported");
        var c = CharacterDefinition.Create("A");
        foreach (var a in new[] { triedOn, inAPanel, imported })
            c = LookEditing.TakeOff(LookEditing.Wear(c, a), a.Id);
        c = LookEditing.Wear(c, worn);
        var edited = Asset(StickerSlots.Bottom, "Edited", "library:bottom/d");
        c = LookEditing.TakeOff(LookEditing.Wear(c, edited), edited.Id);
        c = LookEditing.UpdateSticker(c, edited.Sticker with { Name = "Edited!" });
        Assert.Null(c.Wardrobe.Find(edited.Id)!.Sticker.Source);

        var panel = new CharacterInstance(c.Id, new CharacterPlacement(default, 100, false), null, new PoseData(ViewAngle.Front, [], []),
            new CharacterInstanceOverrides(new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Shoes] = [inAPanel.Id] }, null));
        var tidy = LookEditing.TidyWardrobe(c, LookEditing.WornElsewhere(c, [panel]));

        Assert.NotNull(tidy.Wardrobe.Find(worn.Id));
        Assert.NotNull(tidy.Wardrobe.Find(inAPanel.Id));
        Assert.NotNull(tidy.Wardrobe.Find(imported.Id));
        Assert.NotNull(tidy.Wardrobe.Find(edited.Id));
        Assert.Null(tidy.Wardrobe.Find(triedOn.Id));
    }

    [Fact]
    public void The_fitting_sliders_move_a_garments_hem_sleeves_and_ease()
    {
        var sticker = Asset(StickerSlots.Top, "Tee", null,
            new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 0.8)),
            new StickerPart("sleeves", BodyRegion.Arm, Cover: new PartCover("top", 0, 0.3, Ease: 0.01)),
            new StickerPart("skirt", BodyRegion.Skirt, Cover: new PartCover("top", 0, 0.4, Ease: 0.02))).Sticker;

        Assert.Equal(0.4, StickerFitting.Length(sticker)); // a skirt wins over the torso
        Assert.Equal(0.7, StickerFitting.Length(StickerFitting.WithLength(sticker, 0.7)));
        Assert.Equal(0.9, StickerFitting.Sleeves(StickerFitting.WithSleeves(sticker, 0.9)));
        Assert.Equal(0.02, StickerFitting.Fit(sticker));
        var looser = StickerFitting.WithFit(sticker, 0.03);
        Assert.Equal(0.03, StickerFitting.Fit(looser), 9);
        Assert.Equal(0.02, looser.Parts[1].Cover!.Ease!.Value, 9); // every part eased by the same amount
    }
}
