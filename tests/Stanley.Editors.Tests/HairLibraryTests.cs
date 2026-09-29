using SkiaSharp;
using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Stanley.Rendering;
using Stanley.StickerLibrary;
using Library = Stanley.StickerLibrary.StickerLibrary;

namespace Stanley.Editors.Tests;

/// <summary>
/// The starter library's hair pieces and hairstyles (docs/sticker-system.md, modular hair):
/// every hairstyle can be put on, every piece is drawn in both views and reads cleanly,
/// hairstyles are told apart from your own mix, and each old whole-hairstyle sticker has
/// the hairstyle that replaces it.
/// </summary>
public sealed class HairLibraryTests
{
    public static TheoryData<string> HairstyleNames() => [.. Hairstyles.All.Select(h => h.Name)];

    public static TheoryData<string> PieceKeys() =>
        [.. Library.All.Where(s => StickerSlots.HairPieces.Contains(s.Slot) || s.Slot == StickerSlots.HairStreaks).Select(s => s.Key)];

    [Theory]
    [MemberData(nameof(HairstyleNames))]
    public void Every_hairstyle_has_all_its_pieces_in_the_library(string name)
    {
        var style = Hairstyles.Get(name)!;
        foreach (var piece in style.Pieces)
        {
            var sticker = Library.Find(piece.Key);
            Assert.True(sticker is not null, $"{name}: {piece.Key} isn't in the library");
            Assert.Equal(piece.Key.Split('/')[0], sticker!.Slot);
            if (piece.Style is { } variant)
                Assert.Contains(variant, sticker.Asset.Sticker.Variants);
        }
        Assert.True(Hairstyles.IsAvailable(style));
    }

    [Theory]
    [MemberData(nameof(PieceKeys))]
    public void Every_piece_is_drawn_front_and_side_and_reads_cleanly(string key)
    {
        var asset = Library.Find(key)!.Asset;
        var sticker = asset.Sticker;
        Assert.All(sticker.Parts, p => Assert.Equal(BodyRegion.Head, p.Region));
        Assert.True(sticker.Colors.ContainsKey(sticker.Slot == StickerSlots.HairStreaks ? StickerSlots.StreakColor : StickerSlots.Hair),
            $"{key} doesn't declare its colour");
        foreach (var variant in sticker.Variants)
        {
            foreach (var view in new[] { ViewAngle.Front, ViewAngle.Profile })
            {
                var file = asset.ArtFor(variant, view);
                Assert.True(file is not null, $"{key} has no {view} art for {variant}");
                var art = StickerSvg.Parse(file!);
                Assert.True(art is not null, $"{key} {variant} {view} isn't an SVG Stanley reads");
                Assert.True(art!.Report.Count == 0, $"{key} {variant} {view}: {string.Join("; ", art.Report)}");
                // The back of the hair (a far pigtail) may be hidden from the front; everything else is drawn in both views.
                foreach (var part in sticker.Parts.Where(p => p.AppliesTo(variant) && !(p.Depth == PartDepth.Back && view == ViewAngle.Front)))
                    Assert.True(art.Part(part.Name).Count > 0, $"{key} {variant} {view} has nothing in its \"{part.Name}\" layer");
            }
        }
    }

    [Fact]
    public void Streaks_stay_on_the_hair()
    {
        foreach (var streak in Library.ForSlot(StickerSlots.HairStreaks))
            Assert.All(streak.Asset.Sticker.Parts, p => Assert.Equal(PartClip.Hair, p.Clip));
    }

    [Theory]
    [InlineData("hair/short", "Short")]
    [InlineData("hair/bob", "Bob")]
    [InlineData("hair/long", "Long")]
    [InlineData("hair/ponytail", "Ponytail")]
    [InlineData("hair/curly", "Curly")]
    [InlineData("hair/bun", "Bun")]
    public void Each_old_hairstyle_is_replaced_by_the_hairstyle_of_its_name(string key, string name)
    {
        var old = Library.Find(key)!.Instantiate();
        Assert.Equal(name, Hairstyles.ForLegacy(old.Sticker)?.Name);
        // One you've edited is yours: no switch is offered.
        Assert.Null(Hairstyles.ForLegacy(old.Sticker with { Source = null }));
    }

    [Theory]
    [MemberData(nameof(HairstyleNames))]
    public void A_hairstyle_as_it_came_is_recognised_and_loses_nothing_when_replaced(string name)
    {
        var style = Hairstyles.Get(name)!;
        var character = CharacterDefinition.Create("A");
        character = HairEditing.WearHairdo(character, Hairstyles.PiecesFor(style, character), replace: true);

        Assert.Equal(name, Hairstyles.Matching(character)?.Name);
        Assert.False(Hairstyles.IsOwnMix(character));
    }

    [Fact]
    public void A_hairstyle_with_a_piece_changed_is_your_own_mix()
    {
        var character = CharacterDefinition.Create("A");
        character = HairEditing.WearHairdo(character, Hairstyles.PiecesFor(Hairstyles.Get("Bob")!, character), replace: true);
        character = LookEditing.Wear(character, Hairstyles.Piece("hairExtras/cowlick", character)!);

        Assert.Null(Hairstyles.Matching(character));
        Assert.True(Hairstyles.IsOwnMix(character));
    }

    [Fact]
    public void An_old_whole_hairstyle_alone_isnt_a_mix()
    {
        var character = LookEditing.Wear(CharacterDefinition.Create("A"), Library.Find("hair/bob")!.Instantiate());

        Assert.False(Hairstyles.IsOwnMix(character));
    }

    [Fact]
    public void Putting_a_hairstyle_on_again_reuses_the_pieces_already_in_the_wardrobe()
    {
        var bob = Hairstyles.Get("Bob")!;
        var character = CharacterDefinition.Create("A");
        character = HairEditing.WearHairdo(character, Hairstyles.PiecesFor(bob, character), replace: true);
        character = HairEditing.WearHairdo(character, Hairstyles.PiecesFor(Hairstyles.Get("Long")!, character), replace: true);
        var count = character.Wardrobe.Stickers.Count;
        character = HairEditing.WearHairdo(character, Hairstyles.PiecesFor(bob, character), replace: true);

        Assert.Equal("Bob", Hairstyles.Matching(character)?.Name);
        Assert.Equal(count, character.Wardrobe.Stickers.Count);
    }

    [Fact]
    public void Switching_an_old_hairstyle_puts_on_its_pieces_and_keeps_the_colour()
    {
        var old = Library.Find("hair/bob")!.Instantiate();
        var character = LookEditing.SetColor(LookEditing.Wear(CharacterDefinition.Create("A"), old), StickerSlots.Hair, ColorValue.FromHex("#1f1a17"));
        var bob = Hairstyles.ForLegacy(old.Sticker)!;

        character = HairEditing.ReplaceLegacy(character, old.Id, Hairstyles.PiecesFor(bob, character));

        Assert.DoesNotContain(old.Id, character.Stickers.GetValueOrDefault(StickerSlots.Hair) ?? []);
        Assert.Equal("Bob", Hairstyles.Matching(character)?.Name);
        Assert.Equal(ColorValue.FromHex("#1f1a17"), character.ColorSlots[StickerSlots.Hair]);
    }

    // ---------------------------------------------------------------- drawn

    // Head and shoulders, big enough to probe inside the art: 1200 px to the character's height.
    private static readonly CharacterPlacement Placement = new(new Point2D(300, 1320), 1200, false);

    private static SKBitmap Render(CharacterDefinition character, ViewAngle view = ViewAngle.Front)
    {
        var bitmap = new SKBitmap(600, 600);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        CharacterRenderers.Default.Draw(canvas, character, Placement, 4f, view, new PoseData(view, [], []));
        return bitmap;
    }

    private static SKColor AtHead(SKBitmap bitmap, CharacterDefinition character, double x, double y, ViewAngle view = ViewAngle.Front)
    {
        var figure = BodyRig.Build(character.Body, view, character.Skeleton, new PoseData(view, [], []));
        var point = RegionMapping.Warp(figure, BodyRegion.Head, LimbSide.Left, null)(new Point2D(x, y));
        var page = Placement.ToPage(point);
        return bitmap.GetPixel((int)Math.Round(page.X), (int)Math.Round(page.Y));
    }

    private static void Close(ColorValue expected, SKColor actual, int tolerance = 12)
    {
        var e = SKColor.Parse(expected.Hex);
        Assert.True(Math.Abs(e.Red - actual.Red) <= tolerance && Math.Abs(e.Green - actual.Green) <= tolerance && Math.Abs(e.Blue - actual.Blue) <= tolerance,
            $"expected about {e}, got {actual}");
    }

    private static int Pixels(SKBitmap bitmap, ColorValue color, int tolerance = 30)
    {
        var c = SKColor.Parse(color.Hex);
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (Math.Abs(p.Red - c.Red) <= tolerance && Math.Abs(p.Green - c.Green) <= tolerance && Math.Abs(p.Blue - c.Blue) <= tolerance)
                    count++;
            }
        }
        return count;
    }

    [Theory]
    [InlineData(ViewAngle.Front)]
    [InlineData(ViewAngle.Profile)]
    public void A_streak_shows_on_the_hair_pieces_and_stops_at_their_edge(ViewAngle view)
    {
        // Regression: a union of warped art with an outline traced along its own fill's edge
        // could silently drop the fill, so the hair a streak is clipped to came out empty and
        // the streak vanished (or showed only as the top's strand lines).
        var streak = Library.Find("hairStreaks/chunky")!.Asset.Sticker.Colors[StickerSlots.StreakColor];
        var bald = LookEditing.Wear(CharacterDefinition.Create("A"), Library.Find("hairStreaks/chunky")!.Instantiate());
        var character = CharacterDefinition.Create("A");
        foreach (var key in new[] { "hairTop/smooth", "hairFringe/blunt", "hairStreaks/chunky" })
            character = LookEditing.Wear(character, Hairstyles.Piece(key, character)!);

        using var alone = Render(bald, view);
        using var onHair = Render(character, view);
        var shown = Pixels(onHair, streak);

        Assert.True(shown > 200, $"only {shown} streak pixels on the hair");
        Assert.True(shown < Pixels(alone, streak), "the streak isn't clipped to the hair");
    }

    [Fact]
    public void A_purple_fringe_on_black_hair_the_issues_photo()
    {
        var black = ColorValue.FromHex("#1f1a17");
        var purple = ColorValue.FromHex("#8e24aa");
        var character = CharacterDefinition.Create("A");
        character = HairEditing.WearHairdo(character, Hairstyles.PiecesFor(Hairstyles.Get("Bob")!, character), replace: true);
        character = LookEditing.SetColor(character, StickerSlots.Hair, black);
        character = LookEditing.SetColor(character, StickerSlots.HairFringe, purple);

        using var bitmap = Render(character);

        // The middle of the forehead is fringe; the crown's top is the top piece's.
        Close(purple, AtHead(bitmap, character, 0, -962));
        Close(black, AtHead(bitmap, character, 0, -1004));
    }
}
