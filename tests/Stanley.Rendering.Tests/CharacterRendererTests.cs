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
