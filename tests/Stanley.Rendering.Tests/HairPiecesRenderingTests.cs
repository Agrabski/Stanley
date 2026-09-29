using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Rendering.Tests;

/// <summary>
/// Modular hair (GitHub issue #59) drawn: a piece coloured from its own slot's key, the dyes
/// (tips, roots, ombre, streaks, rainbow) laid once across each part instead of repeated, and
/// <c>clip: hair</c>. The pieces are synthetic stickers from inline SVG - rectangles on the head,
/// in template units, so a probe lands on the same spot of the art whatever the render size.
/// </summary>
public class HairPiecesRenderingTests
{
    private static readonly SKColor White = SKColors.White;
    private static readonly ColorValue Brown = ColorValue.FromHex("#5a3a22");
    private static readonly ColorValue Shade = ColorValue.FromHex("#3b2414");
    private static readonly ColorValue Green = ColorValue.FromHex("#2e9e4a");
    private static readonly ColorValue Purple = ColorValue.FromHex("#8040c0");
    private static readonly ColorValue Red = ColorValue.FromHex("#e02020");

    // Head and shoulders, big enough to probe inside the art's shapes: 1200 px to the character's height.
    private static readonly CharacterPlacement Placement = new(new Point2D(300, 1320), 1200, false);

    private static string Svg(string layer, string body) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" viewBox="-160 -1060 320 340" data-stanley-view="front"><g id="{layer}" inkscape:groupmode="layer" inkscape:label="{layer}">{body}</g></svg>""";

    private static string Rect(int left, int top, int right, int bottom, ColorValue fill, string classes = "slot-hair") =>
        $"""<rect class="{classes}" x="{left}" y="{top}" width="{right - left}" height="{bottom - top}" fill="{fill.Hex}"/>""";

    /// <summary>A sticker in <paramref name="slot"/> made of one drawn head part, "front", whose art is <paramref name="body"/>; its default for <paramref name="colorSlot"/> is <paramref name="color"/>.</summary>
    private static StickerAsset Piece(string slot, string body, string colorSlot = "hair", ColorValue? color = null, PartClip? clip = null, StickerId? id = null)
    {
        var sticker = new Sticker(id ?? StickerId.New(), slot, slot, [new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp), Clip: clip)],
            new SortedDictionary<string, ColorValue> { [colorSlot] = color ?? Brown }, ["default"]);
        return new StickerAsset(sticker, new Dictionary<string, ArtFile> { ["variants/default/front.svg"] = ArtFile.Svg(Svg("front", body)) });
    }

    /// <summary>A hair piece in <paramref name="slot"/> that is one rectangle in the hair's colour.</summary>
    private static StickerAsset Slab(string slot, int left, int top, int right, int bottom) => Piece(slot, Rect(left, top, right, bottom, Brown));

    /// <summary>A streak: a rectangle in its own colour, clipped to the hair beside it.</summary>
    private static StickerAsset Streak(int left, int top, int right, int bottom, StickerId? id = null) =>
        Piece(StickerSlots.HairStreaks, Rect(left, top, right, bottom, Red, "slot-streak"), StickerSlots.StreakColor, Red, PartClip.Hair, id);

    /// <summary>A wide piece with a fixed id, so its streaks (seeded from the id) come out the same on every run of the test.</summary>
    private static StickerAsset Streaky() =>
        Piece(StickerSlots.HairFringe, Rect(-40, -990, 40, -900, Brown), id: StickerId.FromValue("streaky"));

    private static CharacterDefinition Wearing(params StickerAsset[] worn)
    {
        var character = CharacterDefinition.Create("A");
        var stickers = new SortedDictionary<string, IReadOnlyList<StickerId>>();
        var wardrobe = character.Wardrobe;
        foreach (var asset in worn)
        {
            wardrobe = wardrobe.With(asset);
            var slot = asset.Sticker.Slot;
            stickers[slot] = stickers.TryGetValue(slot, out var already) ? [.. already, asset.Id] : [asset.Id];
        }
        return character with { Stickers = stickers, Wardrobe = wardrobe };
    }

    private static CharacterDefinition Coloured(CharacterDefinition character, params (string Slot, ColorValue Color)[] colors)
    {
        var slots = new SortedDictionary<string, ColorValue>(character.ColorSlots);
        foreach (var (slot, color) in colors)
            slots[slot] = color;
        return character with { ColorSlots = slots };
    }

    private static CharacterDefinition Dyed(CharacterDefinition character, string slot, PatternFill pattern)
    {
        var fabrics = new SortedDictionary<string, Fabric>(character.Fabrics ?? new SortedDictionary<string, Fabric>()) { [slot] = new Fabric(pattern) };
        return character with { Fabrics = fabrics };
    }

    private static PatternFill Dye(PatternKind kind, params ColorValue[] colors) => new(kind, colors);

    private static SKBitmap Render(CharacterDefinition character, FigureRenderer? renderer = null)
    {
        var bitmap = new SKBitmap(600, 600);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(White);
        (renderer ?? (FigureRenderer)CharacterRenderers.Default).Draw(canvas, character, Placement, 4f);
        return bitmap;
    }

    /// <summary>The pixel a point of the head given in template units (x, y as the art is drawn) lands on.</summary>
    private static (int X, int Y) PagePoint(double x, double y)
    {
        var figure = BodyRig.Build(BodyShape.Default, ViewAngle.Front);
        var page = Placement.ToPage(RegionMapping.Warp(figure, BodyRegion.Head, LimbSide.Left, null)(new Point2D(x, y)));
        return ((int)Math.Round(page.X), (int)Math.Round(page.Y));
    }

    private static SKColor At(SKBitmap bitmap, double x, double y)
    {
        var (px, py) = PagePoint(x, y);
        return bitmap.GetPixel(px, py);
    }

    private static void Close(ColorValue expected, SKColor actual, int tolerance = 2) => Close(FigureGeometry.ToSk(expected), actual, tolerance);

    private static void Close(SKColor expected, SKColor actual, int tolerance = 2)
    {
        Assert.True(Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance
            && Math.Abs(expected.Blue - actual.Blue) <= tolerance, $"expected about {expected}, got {actual}");
    }

    private static double Distance(SKColor a, SKColor b) =>
        Math.Sqrt(Math.Pow(a.Red - b.Red, 2) + Math.Pow(a.Green - b.Green, 2) + Math.Pow(a.Blue - b.Blue, 2));

    /// <summary>The colours a row crosses from <paramref name="fromX"/> to <paramref name="toX"/> (template units), in order, each held for at least a few pixels - so where two meet, the pixel between doesn't count.</summary>
    private static List<SKColor> Bands(SKBitmap bitmap, double y, double fromX, double toX)
    {
        var (first, row) = PagePoint(fromX, y);
        var (last, _) = PagePoint(toX, y);
        var runs = new List<(SKColor Color, int Length)>();
        for (var x = first; x <= last; x++)
        {
            var color = bitmap.GetPixel(x, row);
            if (runs.Count > 0 && runs[^1].Color == color)
                runs[^1] = (color, runs[^1].Length + 1);
            else
                runs.Add((color, 1));
        }
        return [.. runs.Where(r => r.Length >= 4).Select(r => r.Color)];
    }

    // ---- a piece's colour -------------------------------------------------------------------

    [Fact]
    public void A_piece_with_no_colour_of_its_own_draws_in_the_hair_colour()
    {
        var fringe = Slab(StickerSlots.HairFringe, -30, -990, 30, -900);

        using (var plain = Render(Wearing(fringe)))
            Assert.Equal(FigureGeometry.ToSk(Brown), At(plain, 0, -940)); // the sticker's own default
        using (var picked = Render(Coloured(Wearing(fringe), ("hair", Green))))
            Close(Green, At(picked, 0, -940));
    }

    [Fact]
    public void A_piece_with_its_own_colour_draws_in_it_while_another_piece_stays_the_hair_colour()
    {
        // Side by side, out of each other's way: the fringe over the forehead, the back to one side.
        var fringe = Slab(StickerSlots.HairFringe, -30, -990, 30, -900);
        var back = Slab(StickerSlots.HairBack, -80, -990, -40, -900);
        var character = Coloured(Wearing(fringe, back), ("hair", Green), (StickerSlots.HairFringe, Purple));

        using var bitmap = Render(character);

        Close(Purple, At(bitmap, 0, -940));
        Close(Green, At(bitmap, -60, -940));
    }

    [Fact]
    public void A_strand_shade_on_a_piece_keeps_its_offset_from_the_pieces_colour()
    {
        var fringe = Piece(StickerSlots.HairFringe, Rect(-30, -990, 30, -900, Brown) + Rect(-10, -970, 10, -950, Shade));
        var character = Coloured(Wearing(fringe), (StickerSlots.HairFringe, Purple));

        using var bitmap = Render(character);

        var body = At(bitmap, 0, -985);
        var strand = At(bitmap, 0, -960);
        Close(ColorMath.Recolor(FigureGeometry.ToSk(Brown), FigureGeometry.ToSk(Brown), FigureGeometry.ToSk(Purple)), body);
        Close(ColorMath.Recolor(FigureGeometry.ToSk(Shade), FigureGeometry.ToSk(Brown), FigureGeometry.ToSk(Purple)), strand);
        // Relative, not flat: darker than the piece's purple, and not the purple itself.
        Assert.True(strand.Red + strand.Green + strand.Blue < body.Red + body.Green + body.Blue, $"the strand {strand} should be darker than {body}");
    }

    [Fact]
    public void A_split_eye_keeps_its_artist_shading()
    {
        const string eyes = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="-80 -1010 160 150">
              <ellipse class="slot-eyes side-right" cx="-21" cy="-930" rx="14" ry="14" fill="#4a7ab5"/>
              <ellipse class="slot-eyes side-right" cx="-21" cy="-930" rx="6" ry="6" fill="#2a4a75"/>
              <ellipse class="slot-eyes side-left" cx="21" cy="-930" rx="14" ry="14" fill="#4a7ab5"/>
              <ellipse class="slot-eyes side-left" cx="21" cy="-930" rx="6" ry="6" fill="#2a4a75"/>
            </svg>
            """;
        var iris = ColorValue.FromHex("#4a7ab5");
        var pupil = ColorValue.FromHex("#2a4a75");
        var sticker = new Sticker(StickerId.New(), "Round", StickerSlots.Eyes, [new StickerPart("eyes", BodyRegion.Head, Art: new PartArt(ArtMapping.Pin))],
            new SortedDictionary<string, ColorValue> { [StickerSlots.Eyes] = iris }, ["neutral"]);
        var asset = new StickerAsset(sticker, new Dictionary<string, ArtFile> { ["variants/neutral/front.svg"] = ArtFile.Svg(eyes) });
        var character = Coloured(Wearing(asset), (StickerSlots.EyesLeft, Green)) with
        {
            StickerSides = new SortedDictionary<StickerId, LimbSide> { [sticker.Id] = LimbSide.Left },
        };

        using var bitmap = Render(character);

        Close(ColorMath.Recolor(FigureGeometry.ToSk(iris), FigureGeometry.ToSk(iris), FigureGeometry.ToSk(Green)), At(bitmap, 31, -930));
        var centre = At(bitmap, 21, -930);
        Close(ColorMath.Recolor(FigureGeometry.ToSk(pupil), FigureGeometry.ToSk(iris), FigureGeometry.ToSk(Green)), centre);
        Assert.True(Distance(centre, FigureGeometry.ToSk(Green)) > 20, $"the pupil {centre} should be a darker shade, not the flat {Green.Hex}");
    }

    // ---- dyes -------------------------------------------------------------------------------

    [Fact]
    public void Tips_dye_the_bottom_of_a_tall_piece_and_leave_its_top_alone()
    {
        // 350 tall: the tips are the bottom 30% - from -745 down.
        var fringe = Slab(StickerSlots.HairFringe, -25, -990, 25, -640);
        var character = Dyed(Wearing(fringe), "hair", Dye(PatternKind.Tips, Red));

        using var bitmap = Render(character);

        Close(Red, At(bitmap, 0, -660));
        Close(Red, At(bitmap, 0, -730));
        Assert.Equal(FigureGeometry.ToSk(Brown), At(bitmap, 0, -985));
        Assert.Equal(FigureGeometry.ToSk(Brown), At(bitmap, 0, -800));
    }

    [Fact]
    public void The_same_tips_on_a_short_piece_still_show_at_its_own_bottom()
    {
        // 90 tall: the same dye, on the bottom 30% of this piece - from -927 down.
        var fringe = Slab(StickerSlots.HairFringe, -25, -990, 25, -900);
        var character = Dyed(Wearing(fringe), "hair", Dye(PatternKind.Tips, Red));

        using var bitmap = Render(character);

        Close(Red, At(bitmap, 0, -905));
        Close(Red, At(bitmap, 0, -920));
        Assert.Equal(FigureGeometry.ToSk(Brown), At(bitmap, 0, -985));
        Assert.Equal(FigureGeometry.ToSk(Brown), At(bitmap, 0, -950));
    }

    [Fact]
    public void A_pieces_own_weight_says_how_much_of_it_the_tips_cover()
    {
        var fringe = Slab(StickerSlots.HairFringe, -25, -990, 25, -890); // 100 tall
        var character = Dyed(Wearing(fringe), "hair", Dye(PatternKind.Tips, Red) with { Weight = 0.6 });

        using var bitmap = Render(character);

        Close(Red, At(bitmap, 0, -935)); // 45% down from the top: in the bottom 60%
        Assert.Equal(FigureGeometry.ToSk(Brown), At(bitmap, 0, -970));
    }

    [Fact]
    public void Roots_dye_the_top_of_a_piece()
    {
        var fringe = Slab(StickerSlots.HairFringe, -25, -990, 25, -890); // the top 25% is -990 to -965
        var character = Dyed(Wearing(fringe), "hair", Dye(PatternKind.Roots, Red));

        using var bitmap = Render(character);

        Close(Red, At(bitmap, 0, -985));
        Assert.Equal(FigureGeometry.ToSk(Brown), At(bitmap, 0, -935));
        Assert.Equal(FigureGeometry.ToSk(Brown), At(bitmap, 0, -895));
    }

    [Fact]
    public void An_ombre_fades_from_the_ground_colour_into_the_dye_at_the_bottom()
    {
        var fringe = Slab(StickerSlots.HairFringe, -25, -990, 25, -890); // the fade runs from -960 to -890
        var character = Dyed(Wearing(fringe), "hair", Dye(PatternKind.Ombre, Red));

        using var bitmap = Render(character);

        var (ground, dye) = (FigureGeometry.ToSk(Brown), FigureGeometry.ToSk(Red));
        var (top, middle, bottom) = (At(bitmap, 0, -985), At(bitmap, 0, -925), At(bitmap, 0, -893));
        Assert.Equal(ground, top);
        Assert.True(top.Red < middle.Red && middle.Red < bottom.Red, $"expected the red to grow towards the bottom: {top}, {middle}, {bottom}");
        Assert.True(Distance(bottom, dye) < Distance(bottom, ground), $"the bottom {bottom} should be nearer the dye than the ground");
        Assert.True(Distance(middle, dye) > 20 && Distance(middle, ground) > 20, $"the middle {middle} should be a mix");
    }

    [Fact]
    public void A_dye_turned_ninety_degrees_lays_across_the_piece_the_other_way()
    {
        // Turned a quarter, the dye's bottom is the piece's left: tips are its left 30% (-25 to -7).
        var fringe = Slab(StickerSlots.HairFringe, -25, -990, 35, -900);
        var character = Dyed(Wearing(fringe), "hair", Dye(PatternKind.Tips, Red) with { Angle = 90 });

        using var bitmap = Render(character);

        Close(Red, At(bitmap, -20, -940));
        Assert.Equal(FigureGeometry.ToSk(Brown), At(bitmap, 20, -940));
        Assert.Equal(FigureGeometry.ToSk(Brown), At(bitmap, 0, -905)); // not at the bottom any more
    }

    [Fact]
    public void A_rainbow_lays_one_band_per_colour_side_by_side_across_the_piece()
    {
        var colors = new[] { ColorValue.FromHex("#e53935"), ColorValue.FromHex("#fdd835"), ColorValue.FromHex("#43a047"), ColorValue.FromHex("#1e88e5") };
        var fringe = Slab(StickerSlots.HairFringe, -30, -990, 30, -900);
        var character = Dyed(Wearing(fringe), "hair", Dye(PatternKind.Rainbow, colors));

        using var bitmap = Render(character);

        Assert.Equal(colors.Select(FigureGeometry.ToSk), Bands(bitmap, -940, -28, 28));
    }

    [Fact]
    public void A_rainbow_with_no_colours_picked_uses_the_default_six()
    {
        var fringe = Slab(StickerSlots.HairFringe, -30, -990, 30, -900);
        var character = Dyed(Wearing(fringe), "hair", Dye(PatternKind.Rainbow));

        using var bitmap = Render(character);

        var bands = Bands(bitmap, -940, -28, 28);
        Assert.Equal(PatternFill.RainbowColors.Count, bands.Count);
        Assert.Equal(PatternFill.RainbowColors.Select(FigureGeometry.ToSk), bands);
    }

    [Fact]
    public void Rainbow_bands_fit_the_piece_however_wide_it_is()
    {
        var narrow = Slab(StickerSlots.HairFringe, -15, -990, 15, -900);
        var character = Dyed(Wearing(narrow), "hair", Dye(PatternKind.Rainbow));

        using var bitmap = Render(character);

        Assert.Equal(PatternFill.RainbowColors.Select(FigureGeometry.ToSk), Bands(bitmap, -940, -14, 14));
    }

    [Fact]
    public void Streaks_dye_some_of_the_piece_and_leave_the_rest_as_the_ground()
    {
        var fringe = Streaky();
        var character = Dyed(Wearing(fringe), "hair", Dye(PatternKind.Streaks, Red));

        using var bitmap = Render(character);

        var (first, row) = PagePoint(-38, -940);
        var (last, _) = PagePoint(38, -940);
        var pixels = Enumerable.Range(first, last - first + 1).Select(x => bitmap.GetPixel(x, row)).ToList();
        Assert.Contains(FigureGeometry.ToSk(Red), pixels);
        Assert.Contains(FigureGeometry.ToSk(Brown), pixels);
        // Bands, not a wash: several separate streaks across it.
        Assert.True(Bands(bitmap, -940, -38, 38).Count(c => c == FigureGeometry.ToSk(Red)) >= 2, "expected several streaks across the piece");
        // They run down the piece: the same at the top as at the bottom.
        Assert.Equal(At(bitmap, -20, -985), At(bitmap, -20, -905));
    }

    [Fact]
    public void Streaks_are_the_same_on_every_render_of_the_same_piece()
    {
        var fringe = Streaky();
        var character = Dyed(Wearing(fringe), "hair", Dye(PatternKind.Streaks, Red));

        // Separate renderers, so nothing is shared between the two draws.
        using var one = Render(character, new FigureRenderer());
        using var two = Render(character, new FigureRenderer());

        Assert.True(one.Bytes.AsSpan().SequenceEqual(two.Bytes), "two renders of the same streaks differ");
    }

    [Fact]
    public void A_pattern_that_repeats_still_repeats_down_a_tall_piece()
    {
        var fringe = Slab(StickerSlots.HairFringe, -25, -990, 25, -640);
        var blue = ColorValue.FromHex("#3060ff");
        var character = Dyed(Wearing(fringe), "hair", Dye(PatternKind.Stripes, blue));

        using var bitmap = Render(character);

        var (column, top) = PagePoint(0, -985);
        var (_, bottom) = PagePoint(0, -645);
        var stripes = Enumerable.Range(top, bottom - top + 1).Select(y => bitmap.GetPixel(column, y)).ToList();
        var changes = stripes.Zip(stripes.Skip(1)).Count(p => p.First != p.Second);
        Assert.True(changes >= 6, $"expected several stripes down the piece, saw {changes} changes");
        Assert.Contains(FigureGeometry.ToSk(blue), stripes);
        Assert.Contains(FigureGeometry.ToSk(Brown), stripes);
    }

    // ---- clip: hair -------------------------------------------------------------------------

    [Fact]
    public void A_streak_draws_only_over_the_hair_beside_it()
    {
        // The hair is 60 wide; the streak reaches 30 past it on both sides, over the face.
        var hair = Slab(StickerSlots.HairFringe, -30, -990, 30, -900);
        var streak = Streak(-60, -990, 60, -900);
        var character = Wearing(hair, streak);
        var skin = FigureGeometry.ToSk(character.Skin);

        using var bitmap = Render(character);

        Assert.Equal(FigureGeometry.ToSk(Red), At(bitmap, 0, -940)); // over the hair
        Assert.Equal(skin, At(bitmap, 45, -940)); // past its edge: nothing
        Assert.Equal(skin, At(bitmap, -45, -940));
    }

    [Fact]
    public void With_no_hair_worn_a_streak_draws_in_full()
    {
        var streak = Streak(-60, -990, 60, -900);

        using var bitmap = Render(Wearing(streak));

        Assert.Equal(FigureGeometry.ToSk(Red), At(bitmap, 0, -940));
        Assert.Equal(FigureGeometry.ToSk(Red), At(bitmap, 45, -940));
    }

    [Fact]
    public void Streaks_do_not_count_as_hair_for_each_other()
    {
        // Two streaks and no hair: each stays whole - a clipped part is not what another clips to.
        var left = Streak(-60, -990, -40, -900);
        var right = Streak(40, -990, 60, -900);

        using var bitmap = Render(Wearing(left, right));

        Assert.Equal(FigureGeometry.ToSk(Red), At(bitmap, -50, -940));
        Assert.Equal(FigureGeometry.ToSk(Red), At(bitmap, 50, -940));
    }

    [Fact]
    public void A_streak_clips_to_hair_in_any_hair_slot_and_ignores_other_stickers()
    {
        // A cap under the streak's overhang is not hair, so with only a cap worn nothing clips the streak.
        var cap = Piece(StickerSlots.Headwear, Rect(-30, -990, 30, -900, Brown, "slot-hat"), "hat");
        var sides = Slab(StickerSlots.HairSides, -30, -990, 30, -900);
        var streak = Streak(-60, -990, 60, -900);
        var skin = FigureGeometry.ToSk(Wearing(streak).Skin);

        using (var bitmap = Render(Wearing(cap, streak)))
            Assert.Equal(FigureGeometry.ToSk(Red), At(bitmap, 45, -940));
        using (var bitmap = Render(Wearing(sides, streak)))
            Assert.Equal(skin, At(bitmap, 45, -940));
    }
}
