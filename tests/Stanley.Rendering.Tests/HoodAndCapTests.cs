using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;
using Library = Stanley.StickerLibrary.StickerLibrary;

namespace Stanley.Rendering.Tests;

/// <summary>
/// The library's Hood (up or down) and Cap (brim forward, backward, right or left), drawn:
/// pixel probes at points given in template units on the head (as the art is drawn) and
/// mapped onto the character, so they land on the same spot of the art on any body.
/// </summary>
public class HoodAndCapTests
{
    private static readonly SKColor White = SKColors.White;
    private static readonly SKColor Hood = FigureGeometry.ToSk(Library.Find("headwear/hood")!.Asset.Sticker.Colors["top"]);
    private static readonly SKColor Cap = FigureGeometry.ToSk(Library.Find("headwear/cap")!.Asset.Sticker.Colors["hat"]);
    private static readonly SKColor Hair = FigureGeometry.ToSk(Library.Find("hair/bob")!.Asset.Sticker.Colors["hair"]);

    // Head and shoulders, big enough to probe inside the art's shapes: 1200 px to the character's height.
    private static readonly CharacterPlacement Placement = new(new Point2D(300, 1320), 1200, false);

    /// <summary>A default character wearing these library stickers, each in its own slot, in order (the last is on top).</summary>
    private static CharacterDefinition Wearing(params string[] keys)
    {
        var character = CharacterDefinition.Create("A");
        var stickers = new SortedDictionary<string, IReadOnlyList<StickerId>>();
        var wardrobe = character.Wardrobe;
        foreach (var key in keys)
        {
            var asset = Library.Find(key)!.Instantiate();
            wardrobe = wardrobe.With(asset);
            var slot = asset.Sticker.Slot;
            stickers[slot] = stickers.TryGetValue(slot, out var worn) ? [.. worn, asset.Id] : [asset.Id];
        }
        return character with { Stickers = stickers, Wardrobe = wardrobe };
    }

    private static PoseData Pose(ViewAngle view, string? headwear = null) =>
        new(view, [], headwear is null ? [] : new SortedDictionary<string, string> { [StickerSlots.Headwear] = headwear });

    private static SKBitmap Render(CharacterDefinition character, PoseData pose, bool mirrored = false)
    {
        var bitmap = new SKBitmap(600, 600);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(White);
        CharacterRenderers.Default.Draw(canvas, character, Placement with { Mirrored = mirrored }, 4f, pose.ViewAngle, pose);
        return bitmap;
    }

    /// <summary>The pixel at a point of <paramref name="region"/> given in template units (x, y as the art is drawn).</summary>
    private static SKColor At(SKBitmap bitmap, CharacterDefinition character, PoseData pose, BodyRegion region, double x, double y, bool mirrored = false)
    {
        var figure = BodyRig.Build(character.Body, pose.ViewAngle, character.Skeleton, pose);
        var point = RegionMapping.Warp(figure, region, LimbSide.Left, null)(new Point2D(x, y));
        var page = (Placement with { Mirrored = mirrored }).ToPage(point);
        return bitmap.GetPixel((int)Math.Round(page.X), (int)Math.Round(page.Y));
    }

    private static SKColor AtHead(SKBitmap bitmap, CharacterDefinition character, PoseData pose, double x, double y, bool mirrored = false) =>
        At(bitmap, character, pose, BodyRegion.Head, x, y, mirrored);

    private static void Close(SKColor expected, SKColor actual, int tolerance = 2)
    {
        Assert.True(Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance
            && Math.Abs(expected.Blue - actual.Blue) <= tolerance, $"expected about {expected}, got {actual}");
    }

    // Probes, in template units: the middle of the face; and where a bob's hair is but the
    // hood's opening isn't - the side of the head from the front, the back of it side on.
    private static (double X, double Y) FaceOf(ViewAngle view) => view == ViewAngle.Profile ? (52, -915) : (0, -925);

    private static (double X, double Y) CrownOf(ViewAngle view) => view == ViewAngle.Profile ? (0, -960) : (-60, -958);

    [Theory]
    [InlineData(ViewAngle.Front)]
    [InlineData(ViewAngle.Profile)]
    public void A_hood_up_shows_the_face_through_its_opening_and_hides_the_hair(ViewAngle view)
    {
        var character = Wearing("hair/bob", "headwear/hood");
        var pose = Pose(view); // "up" is the hood's first style
        var skin = FigureGeometry.ToSk(character.Skin);

        using var bitmap = Render(character, pose);

        var (fx, fy) = FaceOf(view);
        var (cx, cy) = CrownOf(view);
        Assert.Equal(skin, AtHead(bitmap, character, pose, fx, fy));
        Assert.Equal(Hood, AtHead(bitmap, character, pose, cx, cy));
    }

    [Theory]
    [InlineData(ViewAngle.Front)]
    [InlineData(ViewAngle.Profile)]
    public void A_hood_down_leaves_the_head_bare_and_lies_behind_the_neck(ViewAngle view)
    {
        var character = Wearing("hair/bob", "headwear/hood");
        var down = Pose(view, "down");
        var up = Pose(view, "up");
        // Beside the neck above the shoulder from the front; out behind the upper back side on.
        var (rx, ry) = view == ViewAngle.Profile ? (-95.0, -800.0) : (75.0, -850.0);

        using var lying = Render(character, down);
        using var worn = Render(character, up);

        var (cx, cy) = CrownOf(view);
        Assert.Equal(Hair, AtHead(lying, character, down, cx, cy));
        Assert.Equal(Hood, At(lying, character, down, BodyRegion.Torso, rx, ry));
        if (view == ViewAngle.Profile)
            Assert.Equal(White, At(worn, character, up, BodyRegion.Torso, rx, ry)); // up, nothing hangs down the back that far
    }

    [Fact]
    public void A_hood_is_the_colour_of_the_top_it_is_worn_with()
    {
        var character = Wearing("top/hoodie", "headwear/hood");
        var pose = Pose(ViewAngle.Front);
        var (cx, cy) = CrownOf(ViewAngle.Front);

        using (var bitmap = Render(character, pose))
        {
            var chest = At(bitmap, character, pose, BodyRegion.Torso, 0, -760);
            Assert.Equal(FigureGeometry.ToSk(Library.Find("top/hoodie")!.Asset.Sticker.Colors["top"]), chest);
            Assert.Equal(chest, AtHead(bitmap, character, pose, cx, cy));
        }

        // A colour picked for the top dyes the hood too.
        var picked = ColorValue.FromHex("#b03a48");
        var dyed = character with { ColorSlots = new SortedDictionary<string, ColorValue>(character.ColorSlots) { ["top"] = picked } };
        using (var bitmap = Render(dyed, pose))
        {
            Assert.Equal(FigureGeometry.ToSk(picked), At(bitmap, dyed, pose, BodyRegion.Torso, 0, -760));
            Close(FigureGeometry.ToSk(picked), AtHead(bitmap, dyed, pose, cx, cy));
        }
    }

    [Fact]
    public void A_caps_brim_points_ahead_forward_and_behind_backward_side_on()
    {
        var character = Wearing("hair/short", "headwear/cap");
        var forward = Pose(ViewAngle.Profile); // "forward" is the cap's first style
        var backward = Pose(ViewAngle.Profile, "backward");

        using var ahead = Render(character, forward);
        using var behind = Render(character, backward);

        // Past the face (a profile faces +x) and past the back of the head, at the brim's height.
        Assert.Equal(Cap, AtHead(ahead, character, forward, 100, -965));
        Assert.Equal(White, AtHead(ahead, character, forward, -85, -965));
        Assert.Equal(White, AtHead(behind, character, backward, 100, -965));
        Assert.Equal(Cap, AtHead(behind, character, backward, -85, -965));
    }

    [Fact]
    public void A_backward_caps_strap_sits_on_its_rim_not_hanging_down_the_forehead()
    {
        // #123: the strap was centred on the rim, so half of it hung below the cap.
        var character = Wearing("hair/short", "headwear/cap");
        var backward = Pose(ViewAngle.Front, "backward");
        var strap = FigureGeometry.ToSk(ColorValue.FromHex("#216541"));

        using var bitmap = Render(character, backward);

        Assert.Equal(strap, AtHead(bitmap, character, backward, 5, -974)); // between its holes, just above the rim
        Assert.NotEqual(strap, AtHead(bitmap, character, backward, 5, -963)); // just below the rim: no strap
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cap_turned_right_points_to_the_characters_own_right_even_mirrored(bool mirrored)
    {
        var character = Wearing("hair/short", "headwear/cap");
        var right = Pose(ViewAngle.Front, "right");
        var left = Pose(ViewAngle.Front, "left");
        // From the front, which way the character's own right lies (figure space).
        var side = Math.Sign(BodyRig.Build(character.Body).Regions.RightArm.Upper.From.X);

        using var toRight = Render(character, right, mirrored);
        using var toLeft = Render(character, left, mirrored);

        Assert.Equal(Cap, AtHead(toRight, character, right, side * 100, -965, mirrored));
        Assert.Equal(White, AtHead(toRight, character, right, -side * 100, -965, mirrored));
        Assert.Equal(Cap, AtHead(toLeft, character, left, -side * 100, -965, mirrored));
        Assert.Equal(White, AtHead(toLeft, character, left, side * 100, -965, mirrored));
    }

    [Fact]
    public void Side_on_a_cap_turned_right_shows_its_brim_on_the_near_side_and_turned_left_hides_it_behind_the_head()
    {
        var character = Wearing("hair/short", "headwear/cap");
        var right = Pose(ViewAngle.Profile, "right");
        var left = Pose(ViewAngle.Profile, "left");

        using var near = Render(character, right);
        using var far = Render(character, left);

        // Over the side of the head, just under the crown's edge.
        Assert.Equal(Cap, AtHead(near, character, right, 40, -964));
        Assert.NotEqual(Cap, AtHead(far, character, left, 40, -964));
    }

    [Fact]
    public void A_hood_worn_over_a_cap_shows_the_brim_in_its_opening_and_lets_it_stick_out_side_on()
    {
        var character = Wearing("hair/bob", "headwear/cap", "headwear/hood");
        var front = Pose(ViewAngle.Front);
        var side = Pose(ViewAngle.Profile);

        using var facing = Render(character, front);
        using var profile = Render(character, side);

        var (cx, cy) = CrownOf(ViewAngle.Front);
        Assert.Equal(Cap, AtHead(facing, character, front, -25, -965)); // the brim, inside the face opening
        Assert.Equal(Hood, AtHead(facing, character, front, cx, cy)); // the crown, under the hood
        Assert.Equal(Cap, AtHead(profile, character, side, 100, -965));
    }

    [Fact]
    public void A_cap_and_a_hood_worn_together_each_keep_their_own_style()
    {
        var character = Wearing("hair/bob", "headwear/cap", "headwear/hood");
        var (cap, hood) = (character.Stickers[StickerSlots.Headwear][0], character.Stickers[StickerSlots.Headwear][1]);
        character = character with { StickerVariants = new SortedDictionary<StickerId, string> { [cap] = "backward", [hood] = "down" } };
        var pose = Pose(ViewAngle.Profile); // no expression: the styles come from the character

        using var bitmap = Render(character, pose);

        var (cx, cy) = CrownOf(ViewAngle.Profile);
        Assert.Equal(Cap, AtHead(bitmap, character, pose, -85, -965)); // the brim, behind the head
        Assert.Equal(White, AtHead(bitmap, character, pose, 100, -965)); // nothing ahead of the face
        Assert.NotEqual(Hood, AtHead(bitmap, character, pose, cx, cy)); // the hood isn't up
        Assert.Equal(Hood, At(bitmap, character, pose, BodyRegion.Torso, -95, -800)); // it lies on the back
    }
}
