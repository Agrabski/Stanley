using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Tests;

/// <summary>Prints on clothes in <c>sticker.json</c>: a text part and the clothes clip, sparse when unused.</summary>
public class PrintJsonTests
{
    private static Sticker WithPart(StickerPart part) =>
        new(StickerId.New(), "Print", StickerSlots.Print, [part], new SortedDictionary<string, ColorValue> { ["print"] = ColorValue.FromHex("#f4f4f4") }, ["default"]);

    [Fact]
    public void A_text_print_round_trips_with_its_text_and_the_clothes_clip()
    {
        var sticker = WithPart(new StickerPart("print", BodyRegion.Torso, Art: new PartArt(ArtMapping.Pin, KeepReadable: true), Clip: PartClip.Clothes,
            Text: new PartText("SKATE \U0001F6F9", Bold: false)));

        var json = ProjectJson.Serialize(sticker);
        var read = ProjectJson.Deserialize<Sticker>(json);

        Assert.Contains("\"clip\": \"clothes\"", json);
        var part = Assert.Single(read.Parts);
        Assert.Equal(PartClip.Clothes, part.Clip);
        Assert.Equal(new PartText("SKATE \U0001F6F9", Bold: false), part.Text);
        Assert.True(part.Art!.KeepReadable);
    }

    [Fact]
    public void A_drawn_part_has_no_text_key_and_old_files_read_without_one()
    {
        var sticker = WithPart(new StickerPart("print", BodyRegion.Torso, Art: new PartArt(ArtMapping.Pin)));

        var json = ProjectJson.Serialize(sticker);

        Assert.DoesNotContain("\"text\"", json);
        Assert.Null(Assert.Single(ProjectJson.Deserialize<Sticker>(json).Parts).Text);
    }

    [Fact]
    public void Prints_stamp_copies_between_the_top_and_the_outer_layer()
    {
        var print = StickerSlots.Get(StickerSlots.Print);

        Assert.True(print.StampsCopies);
        Assert.False(StickerSlots.Get(StickerSlots.Top).StampsCopies);
        Assert.InRange(print.ZOrder, StickerSlots.ZOrder(StickerSlots.Top) + 1, StickerSlots.ZOrder(StickerSlots.Outer) - 1);
    }
}
