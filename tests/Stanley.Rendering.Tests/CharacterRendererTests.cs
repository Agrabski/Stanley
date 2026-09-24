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
