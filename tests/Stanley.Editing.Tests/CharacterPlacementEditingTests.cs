using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editing.Tests;

public class CharacterPlacementEditingTests
{
    private static readonly Rect2D Panel = new(10, 10, 100, 80);

    private static CharacterInstance At(double x, double y, double unit, bool mirrored = false) =>
        new(CharacterId.New(), new CharacterPlacement(new Point2D(x, y), unit, mirrored), null, new PoseData(ViewAngle.Front, [], []), null);

    [Fact]
    public void The_first_character_in_a_panel_fills_most_of_its_height_centred()
    {
        var figure = BodyRig.Extent(BodyShape.Default);
        var placement = CharacterPlacementEditing.DefaultPlacement(Panel, figure, []);

        Assert.Equal(Panel.Height * CharacterPlacementEditing.DefaultPanelFill, figure.Height * placement.UnitHeightMm, 6);
        Assert.Equal(Panel.MidX, placement.Ground.X, 6);
        Assert.True(placement.Ground.Y < Panel.Bottom && placement.Ground.Y - figure.Height * placement.UnitHeightMm > Panel.Top);
    }

    [Fact]
    public void Later_characters_share_the_panels_scale_and_floor_and_stand_to_the_right()
    {
        var figure = BodyRig.Extent(BodyPresets.Shape(BodyPreset.Child));
        var first = At(40, 80, 50);
        var firstBounds = first.Placement.ToPage(BodyRig.Extent(BodyShape.Default));

        var placement = CharacterPlacementEditing.DefaultPlacement(Panel, figure, [(first, firstBounds)]);

        Assert.Equal(50, placement.UnitHeightMm);
        Assert.Equal(80, placement.Ground.Y);
        Assert.True(placement.Ground.X > firstBounds.Right);
    }

    [Fact]
    public void Resizing_together_resizes_everyone_at_the_same_scale_but_not_a_character_sized_apart()
    {
        IReadOnlyList<CharacterInstance> list = [At(30, 80, 50), At(60, 80, 50), At(90, 80, 20)];

        var together = CharacterPlacementEditing.Resize(list, 0, 60, together: true);
        Assert.Equal([60.0, 60, 20], together.Select(c => c.Placement.UnitHeightMm));
        Assert.Equal(list.Select(c => c.Placement.Ground), together.Select(c => c.Placement.Ground)); // each scales about its feet

        var alone = CharacterPlacementEditing.Resize(list, 0, 60, together: false);
        Assert.Equal([60.0, 50, 20], alone.Select(c => c.Placement.UnitHeightMm));
    }

    [Fact]
    public void The_panel_scale_is_what_most_characters_share()
    {
        IReadOnlyList<CharacterInstance> list = [At(30, 80, 20), At(60, 80, 50), At(90, 80, 50)];

        Assert.Equal(50, CharacterPlacementEditing.PanelScale(list));
        Assert.Equal(20, CharacterPlacementEditing.PanelScale(list.Take(1).ToList()));
        Assert.Null(CharacterPlacementEditing.PanelScale(list.Take(1).ToList(), except: 0));
    }

    [Fact]
    public void KeepReachable_pulls_a_character_dragged_out_of_sight_back_to_the_panel_edge_but_allows_cropping()
    {
        var figure = BodyRig.Extent(BodyShape.Default);
        var lost = At(500, 80, 50);
        var pulled = CharacterPlacementEditing.KeepReachable(lost, figure, Panel);
        var box = pulled.Placement.ToPage(figure);
        Assert.Equal(Panel.Right - CharacterPlacementEditing.MinVisibleMm, box.Left, 6);

        var waistUp = At(60, 120, 50); // feet well below the frame: a normal cropped shot
        Assert.Same(waistUp, CharacterPlacementEditing.KeepReachable(waistUp, figure, Panel));
    }

    [Fact]
    public void Turn_changes_only_the_view()
    {
        var c = At(10, 10, 10, mirrored: true);
        var side = CharacterPlacementEditing.Turn(c, ViewAngle.Profile);

        Assert.Equal(ViewAngle.Profile, side.Pose.ViewAngle);
        Assert.Equal(c.Placement, side.Placement);
        Assert.Same(side, CharacterPlacementEditing.Turn(side, ViewAngle.Profile));
    }

    [Fact]
    public void Flip_toggles_mirroring()
    {
        var c = At(10, 10, 10);
        Assert.True(CharacterPlacementEditing.Flip(c).Placement.Mirrored);
        Assert.False(CharacterPlacementEditing.Flip(CharacterPlacementEditing.Flip(c)).Placement.Mirrored);
    }

    [Fact]
    public void Resizing_a_panel_carries_its_characters_along_at_the_same_size()
    {
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(Panel), null, [At(60, 90, 50)], []);

        var moved = PanelLayoutEditing.Resize(panel, new Rect2D(110, 10, 200, 80), new Rect2D(0, 0, 400, 300)).Value;

        var placement = moved.CharacterInstances.Single().Placement;
        Assert.Equal(new Point2D(210, 90), placement.Ground);
        Assert.Equal(50, placement.UnitHeightMm);
    }

    [Fact]
    public void Splitting_a_panel_sends_each_character_to_the_half_its_feet_are_in()
    {
        var left = At(30, 80, 40);
        var right = At(95, 80, 40);
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(Panel), null, [left, right], []);

        var (first, second) = PanelLayoutEditing.Split(panel, BoundaryOrientation.Vertical, 0.5, 4).Value;

        Assert.Equal([left], first.CharacterInstances);
        Assert.Equal([right], second.CharacterInstances);
    }
}
