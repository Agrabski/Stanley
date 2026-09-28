using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Rendering.Tests;

public class CharacterRendererTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_silhouette_fills_the_rigs_extent_at_the_placement(bool mirrored)
    {
        var character = CharacterDefinition.Create("A", BodyPresets.Shape(BodyPreset.Strong));
        var placement = new CharacterPlacement(new Point2D(100, 200), 80, mirrored);

        using var path = CharacterRenderers.Default.BuildSilhouette(character, placement);
        var expected = placement.ToPage(BodyRig.Extent(character.Body));
        var bounds = path.TightBounds;

        Assert.Equal(expected.Left, bounds.Left, 1);
        Assert.Equal(expected.Right, bounds.Right, 1);
        Assert.Equal(expected.Top, bounds.Top, 1);
        Assert.Equal(expected.Bottom, bounds.Bottom, 1);
        Assert.True(path.Contains(100, 200 - 40), "the torso is solid");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_side_view_faces_right_and_mirrored_faces_left(bool mirrored)
    {
        var character = CharacterDefinition.Create("A");
        var placement = new CharacterPlacement(new Point2D(100, 200), 80, mirrored);

        using var path = CharacterRenderers.Default.BuildSilhouette(character, placement, ViewAngle.Profile);
        var expected = placement.ToPage(BodyRig.Extent(character.Body, ViewAngle.Profile));
        var bounds = path.TightBounds;

        Assert.Equal(expected.Left, bounds.Left, 1);
        Assert.Equal(expected.Right, bounds.Right, 1);
        var frontReach = mirrored ? 100 - bounds.Left : bounds.Right - 100;
        var backReach = mirrored ? bounds.Right - 100 : 100 - bounds.Left;
        Assert.True(frontReach > backReach, "the nose and toes lead");
    }

    [Fact]
    public void A_side_view_is_solid_where_the_near_arm_crosses_the_body()
    {
        var character = CharacterDefinition.Create("A");
        var placement = new CharacterPlacement(new Point2D(100, 200), 80, false);
        var nearArm = BodyRig.Build(character.Body, ViewAngle.Profile).NearLimbs[0];
        var onArm = placement.ToPage(new Point2D((nearArm.From.X + nearArm.To.X) / 2, (nearArm.From.Y + nearArm.To.Y) / 2));

        using var path = CharacterRenderers.Default.BuildSilhouette(character, placement, ViewAngle.Profile);

        Assert.True(path.Contains((float)onArm.X, (float)onArm.Y));
    }

    [Fact]
    public void A_page_draws_its_characters_in_skin_colour_clipped_to_their_panel_and_behind_bubbles()
    {
        var character = CharacterDefinition.Create("A") with
        {
            ColorSlots = new SortedDictionary<string, ColorValue> { [CharacterDefinition.SkinSlot] = ColorValue.FromHex("#00ff00") }
        };
        var panelBounds = new Rect2D(10, 10, 80, 60);
        // Feet 20mm below the panel: the legs must be cut off at its border.
        var instance = new CharacterInstance(character.Id, new CharacterPlacement(new Point2D(50, 90), 70, false), null,
            new PoseData(ViewAngle.Front, [], []), null);
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(panelBounds), null, [instance], []);

        using var bitmap = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(bitmap))
            PageRenderer.Draw(canvas, new Rect2D(0, 0, 100, 100), [panel], characters: new Dictionary<CharacterId, CharacterDefinition> { [character.Id] = character });

        Assert.Equal(new SKColor(0, 255, 0), bitmap.GetPixel(50, 50)); // chest
        Assert.Equal(SKColors.White, bitmap.GetPixel(47, 80)); // leg, below the panel
    }

    [Fact]
    public void A_character_missing_from_the_project_draws_a_placeholder_instead_of_failing()
    {
        var instance = new CharacterInstance(CharacterId.New(), new CharacterPlacement(new Point2D(50, 90), 70, false), null,
            new PoseData(ViewAngle.Front, [], []), null);
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(0, 0, 100, 100)), null, [instance], []);

        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        PageRenderer.Draw(canvas, new Rect2D(0, 0, 100, 100), [panel]);
    }
}

public class FigureLayerRenderingTests
{
    private const float Unit = 400;

    private static SKBitmap Render(CharacterDefinition character, PoseData pose)
    {
        var bitmap = new SKBitmap(400, 440);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        CharacterRenderers.Default.Draw(canvas, character, new CharacterPlacement(new Point2D(200, 420), Unit, false), 2f, pose.ViewAngle, pose);
        return bitmap;
    }

    private static SKPoint ToPixel(Point2D figure) => new((float)(200 + figure.X * Unit), (float)(420 + figure.Y * Unit));

    private static bool IsInk(SKColor c) => c.Red < 90 && c.Green < 90 && c.Blue < 90;

    [Fact]
    public void A_forearm_folded_across_the_chest_keeps_its_outline()
    {
        var character = CharacterDefinition.Create("A");
        // The left forearm turned a quarter turn in towards the body: level across the front of the torso.
        var pose = new PoseData(ViewAngle.Front, [new BoneRotation(HumanoidBone.LeftLowerArm, 90)], []);
        var figure = BodyRig.Build(character.Body, ViewAngle.Front, null, pose);
        var forearm = figure.Regions.LeftArm.Lower;
        var mid = new Point2D((forearm.From.X + forearm.To.X) / 2, (forearm.From.Y + forearm.To.Y) / 2);
        var radius = (forearm.FromRadius + forearm.ToRadius) / 2;

        using var bitmap = Render(character, pose);
        var centre = ToPixel(mid);
        var edge = ToPixel(new Point2D(mid.X, mid.Y + radius));
        using var torso = FigureGeometry.SmoothClosed(figure.Torso);

        Assert.True(torso.Contains((float)mid.X, (float)(mid.Y + radius)), "the forearm's lower edge lies over the torso");
        Assert.False(IsInk(bitmap.GetPixel((int)centre.X, (int)centre.Y)));
        Assert.Contains(Enumerable.Range(-3, 7), dy => IsInk(bitmap.GetPixel((int)edge.X, (int)edge.Y + dy)));
    }

    [Theory]
    [InlineData(ViewAngle.Front, HumanoidBone.LeftUpperArm)]
    [InlineData(ViewAngle.Profile, HumanoidBone.RightUpperArm)]
    public void At_rest_the_shoulder_is_seamless(ViewAngle angle, HumanoidBone shoulder)
    {
        var character = CharacterDefinition.Create("A");
        var pose = new PoseData(angle, [], []);
        var figure = BodyRig.Build(character.Body, angle);
        var joint = figure.Layout.Bones.Single(b => b.Bone == shoulder).Position;
        var arm = shoulder == HumanoidBone.LeftUpperArm ? figure.Regions.LeftArm : figure.Regions.RightArm;
        using var torso = FigureGeometry.SmoothClosed(figure.Torso);

        using var bitmap = Render(character, pose);
        // The arm's round top, where it lies over the torso: without the seam, its outline
        // would draw an arc there.
        var inked = 0;
        var checkedPoints = 0;
        for (var a = 0; a < 360; a += 5)
        {
            var r = a * Math.PI / 180;
            var p = new Point2D(joint.X + Math.Cos(r) * arm.Upper.FromRadius, joint.Y + Math.Sin(r) * arm.Upper.FromRadius);
            if (!torso.Contains((float)p.X, (float)p.Y) || DistanceToOutline(figure.Torso, p) < 0.02)
                continue;
            checkedPoints++;
            var px = ToPixel(p);
            if (IsInk(bitmap.GetPixel((int)Math.Round(px.X), (int)Math.Round(px.Y))))
                inked++;
        }
        Assert.True(checkedPoints > 3);
        Assert.Equal(0, inked);
    }

    private static double DistanceToOutline(IReadOnlyList<Point2D> outline, Point2D p) =>
        Enumerable.Range(0, outline.Count).Min(i =>
        {
            var (a, b) = (outline[i], outline[(i + 1) % outline.Count]);
            var (dx, dy) = (b.X - a.X, b.Y - a.Y);
            var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / Math.Max(dx * dx + dy * dy, 1e-12), 0, 1);
            var (x, y) = (a.X + dx * t - p.X, a.Y + dy * t - p.Y);
            return Math.Sqrt(x * x + y * y);
        });
}

public class CoverStickerRenderingTests
{
    private static readonly ColorValue Top = ColorValue.FromHex("#0000ff");

    /// <summary>A character wearing one sticker made of cover parts, all in the "top" colour slot.</summary>
    private static CharacterDefinition Wearing(params StickerPart[] parts)
    {
        var sticker = new Sticker(StickerId.New(), "Top", StickerSlots.Top, parts, new SortedDictionary<string, ColorValue> { ["top"] = Top }, ["default"]);
        var character = CharacterDefinition.Create("A") with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Top] = [sticker.Id] },
        };
        return character with { Wardrobe = character.Wardrobe.With(new StickerAsset(sticker, new Dictionary<string, ArtFile>())) };
    }

    private static SKBitmap Render(CharacterDefinition character, PoseData pose, CharacterInstanceOverrides? overrides = null)
    {
        var bitmap = new SKBitmap(400, 440);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        CharacterRenderers.Default.Draw(canvas, character, new CharacterPlacement(new Point2D(200, 420), 400, false), 2f, pose.ViewAngle, pose, overrides);
        return bitmap;
    }

    private static SKColor At(SKBitmap bitmap, Point2D figure) => bitmap.GetPixel((int)Math.Round(200 + figure.X * 400), (int)Math.Round(420 + figure.Y * 400));

    private static Point2D Along(BodyCapsule c, double t) => new(c.From.X + (c.To.X - c.From.X) * t, c.From.Y + (c.To.Y - c.From.Y) * t);

    [Fact]
    public void A_t_shirt_colours_the_chest_and_the_top_of_the_arms_but_leaves_the_forearms_bare()
    {
        var character = Wearing(
            new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 0.86)),
            new StickerPart("sleeves", BodyRegion.Arm, Cover: new PartCover("top", 0, 0.35)));
        var pose = new PoseData(ViewAngle.Front, [], []);
        var figure = BodyRig.Build(character.Body);
        var chest = figure.Regions.Torso.ToFigure(new Point2D(0, figure.Regions.Torso.Top + 0.1));

        using var bitmap = Render(character, pose);

        Assert.Equal(new SKColor(0, 0, 255), At(bitmap, chest));
        Assert.Equal(new SKColor(0, 0, 255), At(bitmap, Along(figure.Regions.LeftArm.Upper, 0.35)));
        Assert.Equal(FigureGeometry.ToSk(character.Skin), At(bitmap, Along(figure.Regions.RightArm.Lower, 0.5)));
    }

    [Fact]
    public void A_long_sleeve_bends_with_the_elbow()
    {
        var character = Wearing(new StickerPart("sleeves", BodyRegion.Arm, Cover: new PartCover("top", 0, 1)));
        // The forearm folded in across the body.
        var pose = new PoseData(ViewAngle.Front, [new BoneRotation(HumanoidBone.LeftLowerArm, 100)], []);
        var forearm = BodyRig.Build(character.Body, ViewAngle.Front, null, pose).Regions.LeftArm.Lower;

        using var bitmap = Render(character, pose);

        Assert.Equal(new SKColor(0, 0, 255), At(bitmap, Along(forearm, 0.5)));
        Assert.Equal(new SKColor(0, 0, 255), At(bitmap, Along(forearm, 0.9)));
    }

    [Fact]
    public void A_panel_override_takes_the_sticker_off_for_that_panel_only()
    {
        var character = Wearing(new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 1)));
        var pose = new PoseData(ViewAngle.Front, [], []);
        var figure = BodyRig.Build(character.Body);
        var chest = figure.Regions.Torso.ToFigure(new Point2D(0, figure.Regions.Torso.Top + 0.1));
        var off = new CharacterInstanceOverrides(new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Top] = [] }, null);

        using var dressed = Render(character, pose);
        using var bare = Render(character, pose, off);

        Assert.Equal(new SKColor(0, 0, 255), At(dressed, chest));
        Assert.Equal(FigureGeometry.ToSk(character.Skin), At(bare, chest));
    }

    [Fact]
    public void A_part_for_some_variants_only_is_drawn_just_in_those()
    {
        var character = Wearing(new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 1), Variants: ["up"]));
        var id = character.Stickers[StickerSlots.Top][0];
        var asset = character.Wardrobe.Find(id)!;
        character = character with { Wardrobe = character.Wardrobe.With(asset with { Sticker = asset.Sticker with { Variants = ["down", "up"] } }) };
        var figure = BodyRig.Build(character.Body);
        var chest = figure.Regions.Torso.ToFigure(new Point2D(0, figure.Regions.Torso.Top + 0.1));

        using var down = Render(character, new PoseData(ViewAngle.Front, [], []));
        using var up = Render(character, new PoseData(ViewAngle.Front, [], new SortedDictionary<string, string> { [StickerSlots.Top] = "up" }));

        Assert.Equal(FigureGeometry.ToSk(character.Skin), At(down, chest));
        Assert.Equal(new SKColor(0, 0, 255), At(up, chest));
    }

    [Fact]
    public void The_characters_own_colour_wins_over_the_stickers_default()
    {
        var character = Wearing(new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 1)));
        character = character with { ColorSlots = new SortedDictionary<string, ColorValue>(character.ColorSlots) { ["top"] = ColorValue.FromHex("#ff0000") } };
        var figure = BodyRig.Build(character.Body);

        using var bitmap = Render(character, new PoseData(ViewAngle.Front, [], []));

        Assert.Equal(new SKColor(255, 0, 0), At(bitmap, figure.Regions.Torso.ToFigure(new Point2D(0, figure.Regions.Torso.Top + 0.1))));
    }

    [Theory]
    [InlineData(ViewAngle.Front)]
    [InlineData(ViewAngle.Profile)]
    public void A_skirt_hangs_between_the_legs_and_hit_testing_includes_it(ViewAngle view)
    {
        var character = Wearing(new StickerPart("skirt", BodyRegion.Skirt, Cover: new PartCover("top", 0, 0.6, Flare: 0.3)));
        var figure = BodyRig.Build(character.Body, view);
        var (left, right) = (figure.Regions.LeftLeg.At(0.4).Point, figure.Regions.RightLeg.At(0.4).Point);
        var between = new Point2D((left.X + right.X) / 2, (left.Y + right.Y) / 2);
        var placement = new CharacterPlacement(new Point2D(200, 420), 400, false);

        using var bitmap = Render(character, new PoseData(view, [], []));
        using var outline = CharacterRenderers.Default.BuildSilhouette(character, placement, view);
        var widest = figure.Extent;

        Assert.Equal(new SKColor(0, 0, 255), At(bitmap, between));
        Assert.True(outline.TightBounds.Width >= placement.ToPage(widest).Width - 0.5);
    }
}

/// <summary>Automatic layer fit: layers worn in one slot, each fitted over the ones under it - at draw time only.</summary>
public class LayerFitRenderingTests
{
    private static readonly ColorValue Red = ColorValue.FromHex("#ff0000");
    private static readonly ColorValue Blue = ColorValue.FromHex("#0000ff");

    /// <summary>A garment covering the whole torso and arms, <paramref name="ease"/> loose, in its own colour slot.</summary>
    private static StickerAsset Garment(string name, string slot, string colorSlot, ColorValue color, double ease) =>
        new(new Sticker(StickerId.New(), name, slot,
            [
                new StickerPart("body", BodyRegion.Torso, Cover: new PartCover(colorSlot, 0, 1, ease)),
                new StickerPart("sleeves", BodyRegion.Arm, Cover: new PartCover(colorSlot, 0, 1, ease)),
            ],
            new SortedDictionary<string, ColorValue> { [colorSlot] = color }, ["default"]), new Dictionary<string, ArtFile>());

    /// <summary>A character wearing <paramref name="assets"/>, each in its own sticker's slot, in order (bottom to top within a slot).</summary>
    private static CharacterDefinition Wearing(params StickerAsset[] assets)
    {
        var character = CharacterDefinition.Create("A");
        var wardrobe = character.Wardrobe;
        foreach (var asset in assets)
            wardrobe = wardrobe.With(asset);
        var stickers = new SortedDictionary<string, IReadOnlyList<StickerId>>(StringComparer.Ordinal);
        foreach (var slot in assets.GroupBy(a => a.Sticker.Slot))
            stickers[slot.Key] = slot.Select(a => a.Id).ToList();
        return character with { Stickers = stickers, Wardrobe = wardrobe };
    }

    private static readonly CharacterPlacement Placement = new(new Point2D(200, 420), 400, false);

    private static SKBitmap Render(CharacterDefinition character)
    {
        var bitmap = new SKBitmap(400, 440);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        CharacterRenderers.Default.Draw(canvas, character, Placement, 2f);
        return bitmap;
    }

    private static int RedPixels(SKBitmap bitmap) =>
        bitmap.Pixels.Count(c => c.Red > 200 && c.Green < 80 && c.Blue < 80);

    private static SKRect OutlineOf(CharacterDefinition character, StickerAsset asset)
    {
        using var outline = CharacterRenderers.Default.BuildStickerOutline(character, Placement, asset.Id);
        return outline.TightBounds;
    }

    [Fact]
    public void A_looser_layer_under_a_tighter_one_in_the_same_slot_does_not_show_past_its_edge()
    {
        // A loose T-shirt under a tight shirt: drawn as made, the T-shirt would show a band all round the shirt.
        var tee = Garment("T-shirt", StickerSlots.Top, "under", Red, 0.03);
        var shirt = Garment("Shirt", StickerSlots.Top, "over", Blue, 0.005);
        var layered = Wearing(tee, shirt);

        using var bitmap = Render(layered);

        Assert.Equal(0, RedPixels(bitmap));
        var (under, over) = (OutlineOf(layered, tee), OutlineOf(layered, shirt));
        Assert.True(over.Left < under.Left && over.Right > under.Right, $"the shirt ({over}) goes on over the T-shirt ({under})");
    }

    [Fact]
    public void One_item_per_slot_is_drawn_exactly_as_it_is_made()
    {
        // The same two garments in different slots: nothing is fitted, so the T-shirt shows round the vest.
        var tee = Garment("T-shirt", StickerSlots.Top, "under", Red, 0.03);
        var vest = Garment("Vest", StickerSlots.Outer, "over", Blue, 0.005);
        var character = Wearing(tee, vest);

        using var bitmap = Render(character);

        Assert.True(RedPixels(bitmap) > 100);
        Assert.Equal(OutlineOf(Wearing(vest), vest), OutlineOf(character, vest));
        Assert.Equal(OutlineOf(Wearing(tee), tee), OutlineOf(character, tee));
    }

    [Fact]
    public void Fitting_only_ever_loosens_on_the_same_region_and_leaves_a_bottom_layer_alone()
    {
        var tight = new PartCover("top", 0, 1, 0.01);
        var loose = new PartCover("top", 0, 1, 0.05);
        var under = new Dictionary<BodyRegion, double> { [BodyRegion.Torso] = 0.014 };

        Assert.Equal(0.014 + StickerCovers.LayerMargin, StickerCovers.FittedOver(tight, BodyRegion.Torso, under).EaseOrDefault, 9);
        Assert.Same(loose, StickerCovers.FittedOver(loose, BodyRegion.Torso, under));
        Assert.Same(tight, StickerCovers.FittedOver(tight, BodyRegion.Arm, under));
        Assert.Same(tight, StickerCovers.FittedOver(tight, BodyRegion.Torso, null));
        Assert.Equal(PartCover.DefaultEase / 2, StickerCovers.LayerMargin);
    }
}

public class FabricRenderingTests
{
    private static readonly SKColor Blue = new(0, 0, 255);
    private static readonly SKColor White = new(255, 255, 255);

    private static CharacterDefinition Wearing(Fabric fabric, params StickerPart[] parts)
    {
        var sticker = new Sticker(StickerId.New(), "Top", StickerSlots.Top, parts, new SortedDictionary<string, ColorValue> { ["top"] = ColorValue.FromHex("#0000ff") }, ["default"]);
        var character = CharacterDefinition.Create("A") with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Top] = [sticker.Id] },
            Fabrics = new SortedDictionary<string, Fabric> { ["top"] = fabric },
        };
        return character with { Wardrobe = character.Wardrobe.With(new StickerAsset(sticker, new Dictionary<string, ArtFile>())) };
    }

    private static SKBitmap Render(CharacterDefinition character, PoseData pose)
    {
        var bitmap = new SKBitmap(400, 440);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        CharacterRenderers.Default.Draw(canvas, character, new CharacterPlacement(new Point2D(200, 420), 400, false), 2f, pose.ViewAngle, pose);
        return bitmap;
    }

    private static SKColor At(SKBitmap bitmap, Point2D figure) => bitmap.GetPixel((int)Math.Round(200 + figure.X * 400), (int)Math.Round(420 + figure.Y * 400));

    private static bool Near(SKColor a, SKColor b) => Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) < 40;

    /// <summary>How many times the colour flips between <paramref name="a"/> and <paramref name="b"/> sampling from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static int Alternations(SKBitmap bitmap, Point2D from, Point2D to, SKColor a, SKColor b)
    {
        int flips = 0, last = 0;
        for (var i = 0; i <= 200; i++)
        {
            var p = new Point2D(from.X + (to.X - from.X) * i / 200, from.Y + (to.Y - from.Y) * i / 200);
            var c = At(bitmap, p);
            var which = Near(c, a) ? 1 : Near(c, b) ? 2 : 0;
            if (which != 0 && last != 0 && which != last)
                flips++;
            if (which != 0)
                last = which;
        }
        return flips;
    }

    [Fact]
    public void Stripes_run_across_the_chest_in_the_pattern_colour_over_the_slot_colour()
    {
        var character = Wearing(new Fabric(new PatternFill(PatternKind.Stripes, [ColorValue.FromHex("#ffffff")], Size: 0.04)),
            new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 1)));
        var torso = BodyRig.Build(character.Body).Regions.Torso;

        using var bitmap = Render(character, new PoseData(ViewAngle.Front, [], []));

        var top = torso.ToFigure(new Point2D(0, torso.Top + 0.05));
        var bottom = torso.ToFigure(new Point2D(0, torso.Bottom - 0.08));
        Assert.True(Alternations(bitmap, top, bottom, Blue, White) >= 6, "stripes down the chest");
        var left = torso.ToFigure(new Point2D(-0.05, torso.Top + 0.15));
        var right = torso.ToFigure(new Point2D(0.05, torso.Top + 0.15));
        Assert.True(Alternations(bitmap, left, right, Blue, White) == 0, "each stripe runs straight across");
    }

    [Fact]
    public void A_sleeves_stripes_go_round_the_arm_and_turn_with_it()
    {
        var character = Wearing(new Fabric(new PatternFill(PatternKind.Stripes, [ColorValue.FromHex("#ffffff")], Size: 0.03)),
            new StickerPart("sleeves", BodyRegion.Arm, Cover: new PartCover("top", 0, 1)));
        var pose = new PoseData(ViewAngle.Front, [new BoneRotation(HumanoidBone.LeftUpperArm, -80)], []);
        var arm = BodyRig.Build(character.Body, ViewAngle.Front, null, pose).Regions.LeftArm.Upper;

        using var bitmap = Render(character, pose);

        var along = (From: new Point2D(arm.From.X + (arm.To.X - arm.From.X) * 0.2, arm.From.Y + (arm.To.Y - arm.From.Y) * 0.2), To: new Point2D(arm.From.X + (arm.To.X - arm.From.X) * 0.9, arm.From.Y + (arm.To.Y - arm.From.Y) * 0.9));
        Assert.True(Alternations(bitmap, along.From, along.To, Blue, White) >= 3, "stripes along the raised arm");
    }

    [Fact]
    public void A_texture_darkens_the_colour_a_little_without_changing_its_hue()
    {
        var plain = Wearing(new Fabric(), new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 1)));
        var denim = Wearing(new Fabric(Texture: new TextureFill(TextureKind.Denim, 1)), new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 1)));
        var torso = BodyRig.Build(plain.Body).Regions.Torso;
        var chest = torso.ToFigure(new Point2D(0, torso.Top + 0.12));

        using var a = Render(plain, new PoseData(ViewAngle.Front, [], []));
        using var b = Render(denim, new PoseData(ViewAngle.Front, [], []));

        Assert.Equal(Blue, At(a, chest));
        long sum = 0;
        var samples = 0;
        for (var dx = -20; dx <= 20; dx += 2)
            for (var dy = -20; dy <= 20; dy += 2)
            {
                var c = b.GetPixel((int)(200 + chest.X * 400) + dx, (int)(420 + chest.Y * 400) + dy);
                Assert.True(c.Red < 30 && c.Green < 30, $"still blue: {c}");
                sum += c.Blue;
                samples++;
            }
        Assert.InRange(sum / samples, 120, 250);
    }

    [Fact]
    public void Patterned_characters_export_to_pdf()
    {
        var character = Wearing(new Fabric(new PatternFill(PatternKind.Plaid, [ColorValue.FromHex("#ff0000"), ColorValue.FromHex("#ffff00")]), new TextureFill(TextureKind.Wool)),
            new StickerPart("body", BodyRegion.Torso, Cover: new PartCover("top", 0, 1)));
        var instance = new CharacterInstance(character.Id, new CharacterPlacement(new Point2D(50, 90), 70, false), null, new PoseData(ViewAngle.Front, [], []), null);
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(0, 0, 100, 100)), null, [instance], []);
        using var stream = new MemoryStream();

        PageRenderer.ExportPdf(stream, new Rect2D(0, 0, 100, 100), [panel], characters: new Dictionary<CharacterId, CharacterDefinition> { [character.Id] = character });

        Assert.True(stream.Length > 1000);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(stream.ToArray(), 0, 4));
    }
}
