using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Editing.Tests;

/// <summary>Named looks and one panel's own changes: edits on a flattened view, kept as sparse differences.</summary>
public class NamedLookTests
{
    private static StickerAsset Asset(string slot, string name) =>
        new(new Sticker(StickerId.New(), name, slot, [new StickerPart("body", BodyRegion.Torso, Cover: new PartCover(slot, 0, 1))],
            new SortedDictionary<string, ColorValue> { [slot] = ColorValue.FromHex("#123456") }, ["default"]), new Dictionary<string, ArtFile>());

    private static readonly StickerAsset Tee = Asset(StickerSlots.Top, "Tee");
    private static readonly StickerAsset Hat = Asset(StickerSlots.Headwear, "Hat");
    private static readonly StickerAsset Shades = Asset(StickerSlots.Glasses, "Shades");

    private static CharacterDefinition Dressed() =>
        LookEditing.Wear(LookEditing.Wear(CharacterDefinition.Create("A"), Tee), Shades);

    [Fact]
    public void A_named_look_keeps_only_what_differs_from_the_default()
    {
        var (character, winter) = LookEditing.NewLook(Dressed(), "Winter");

        var edited = LookEditing.Wear(LookEditing.Project(character, character.Revisions[winter]), Hat);
        edited = LookEditing.SetColor(edited, StickerSlots.Top, ColorValue.FromHex("#aa0000"));
        character = LookEditing.StoreLook(character, winter, edited);

        var look = character.Revisions[winter];
        Assert.Equal([StickerSlots.Headwear], look.ActiveStickers.Keys);
        Assert.Equal([StickerSlots.Top], look.ColorSlotValues.Keys);
        Assert.False(character.Stickers.ContainsKey(StickerSlots.Headwear)); // the default doesn't wear it
        Assert.NotNull(character.Wardrobe.Find(Hat.Id));
        var resolved = CharacterLooks.Resolve(character, look);
        Assert.Equal(["Tee", "Shades", "Hat"], resolved.Stickers.Select(w => w.Asset.Sticker.Name));

        // A change to the default reaches the look where it doesn't differ.
        var sweater = Asset(StickerSlots.Top, "Sweater");
        character = LookEditing.Wear(character, sweater);
        Assert.Contains(CharacterLooks.Resolve(character, character.Revisions[winter]).Stickers, w => w.Asset.Id == sweater.Id);
    }

    [Fact]
    public void Taking_off_in_a_look_and_going_plain_are_kept_as_explicit_changes()
    {
        var character = LookEditing.SetFabric(Dressed(), StickerSlots.Top, new Fabric(new PatternFill(PatternKind.Stripes, [])));
        var (withLook, id) = LookEditing.NewLook(character, "Beach");
        var projected = LookEditing.Project(withLook, withLook.Revisions[id]);
        projected = LookEditing.TakeOff(projected, Shades.Id);
        projected = projected with { Fabrics = null }; // no fabric at all in the look
        var stored = LookEditing.StoreLook(withLook, id, projected);

        var look = stored.Revisions[id];
        Assert.Empty(look.ActiveStickers[StickerSlots.Glasses]);
        Assert.True(look.FabricValues![StickerSlots.Top].IsPlain);
        Assert.Null(CharacterLooks.Resolve(stored, look).FabricOf(StickerSlots.Top));
    }

    [Fact]
    public void A_new_look_can_start_from_another_and_looks_rename_and_delete()
    {
        var (character, winter) = LookEditing.NewLook(Dressed(), "Winter");
        character = LookEditing.StoreLook(character, winter, LookEditing.Wear(LookEditing.Project(character, character.Revisions[winter]), Hat));
        var (copied, snow) = LookEditing.NewLook(character, "Snow", from: winter);

        Assert.Equal(copied.Revisions[winter].ActiveStickers, copied.Revisions[snow].ActiveStickers);
        Assert.Equal("Blizzard", LookEditing.RenameLook(copied, snow, " Blizzard ").Revisions[snow].Name);
        Assert.Same(copied, LookEditing.RenameLook(copied, snow, "  "));
        Assert.False(LookEditing.DeleteLook(copied, snow).Revisions.ContainsKey(snow));
    }

    [Fact]
    public void One_panel_keeps_only_its_own_changes_on_top_of_its_look()
    {
        var character = Dressed();
        var instance = new CharacterInstance(character.Id, new CharacterPlacement(default, 100, false), null, new PoseData(ViewAngle.Front, [], []), null);

        var noShades = LookEditing.StorePanel(character, null, instance, LookEditing.TakeOff(LookEditing.Project(character), Shades.Id));
        Assert.Equal([StickerSlots.Glasses], noShades.Overrides!.ActiveStickerOverrides!.Keys);
        Assert.Null(noShades.Overrides.ColorSlotOverrides);
        Assert.DoesNotContain(CharacterLooks.Resolve(character, null, noShades.Overrides).Stickers, w => w.Asset.Id == Shades.Id);

        // Putting them back on leaves nothing to keep.
        var back = LookEditing.Wear(LookEditing.Project(character, null, noShades.Overrides), Shades);
        Assert.Null(LookEditing.StorePanel(character, null, noShades, back).Overrides);
    }

    [Fact]
    public void An_instance_wears_its_own_look_else_its_issue_one()
    {
        var (character, winter) = LookEditing.NewLook(Dressed(), "Winter");
        var instance = new CharacterInstance(character.Id, new CharacterPlacement(default, 100, false), null, new PoseData(ViewAngle.Front, [], []), null);
        var issue = new Dictionary<CharacterId, CharacterRevisionId> { [character.Id] = winter };

        Assert.Equal(winter, CharacterLooks.LookOf(instance, issue));
        Assert.Null(CharacterLooks.LookOf(instance, null));
        var ownDefault = instance with { RevisionOverride = CharacterLooks.DefaultLook };
        Assert.Equal(CharacterLooks.DefaultLook, CharacterLooks.LookOf(ownDefault, issue));
        Assert.Null(CharacterLooks.Revision(character, CharacterLooks.DefaultLook)); // "default" names no revision
    }
}
