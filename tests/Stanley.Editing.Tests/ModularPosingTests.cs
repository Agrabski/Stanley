using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editing.Tests;

/// <summary>A character built from shapes (docs/shape-characters.md) posed with the mannequin's own drags and presets, end to end.</summary>
public class ModularPosingTests
{
    private static readonly CharacterDefinition Bean = CharacterDefinition.Create("Bean") with
    {
        Modules = new ModularBody(
            new ShapeModule(ModuleShapes.Ellipse(), 0.3, 0.3),
            new ShapeModule(ModuleShapes.Triangle(), 0.4, 0.4, FlipY: true),
            new LimbModule(LimbStyle.Taper, 0.36, 0.06, Taper: 0.4),
            new LimbModule(LimbStyle.Tube, 0.4, 0.08))
    };

    private static readonly CharacterDefinition HeadOnLegs = Bean with { Modules = Bean.Modules! with { Torso = null } };

    private static CharacterInstance Placed(CharacterDefinition character, ViewAngle angle = ViewAngle.Front, bool mirrored = false) =>
        new(character.Id, new CharacterPlacement(new Point2D(100, 200), 100, mirrored), null, new PoseData(angle, [], []), null);

    private static double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public static TheoryData<Limb, ViewAngle, bool> Cases()
    {
        var data = new TheoryData<Limb, ViewAngle, bool>();
        foreach (var limb in Enum.GetValues<Limb>())
            foreach (var angle in new[] { ViewAngle.Front, ViewAngle.Profile })
                foreach (var torso in new[] { true, false })
                    data.Add(limb, angle, torso);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Dragging_a_hand_or_foot_puts_it_where_it_was_dragged(Limb limb, ViewAngle angle, bool torso)
    {
        var character = torso ? Bean : HeadOnLegs;
        var instance = Placed(character, angle);
        var start = CharacterPosing.EndPoint(character, instance, limb);
        var target = new Point2D(start.X + 6, start.Y - 8);

        var posed = CharacterPosing.Reach(character, instance, limb, target, CharacterPosing.BendSign(character, instance, limb));

        var end = CharacterPosing.EndPoint(character, posed, limb);
        Assert.True(Distance(end, target) < 0.05, $"{limb} {angle}: ended at {end}, wanted {target}");
        Assert.Equal(2, posed.Pose.BoneRotations.Count);
    }

    [Fact]
    public void Grabbing_a_hand_without_moving_it_leaves_the_elbow_where_it_is()
    {
        var instance = Placed(Bean);
        var elbow = CharacterPosing.BendPoint(Bean, instance, Limb.LeftArm);
        var hand = CharacterPosing.EndPoint(Bean, instance, Limb.LeftArm);

        var posed = CharacterPosing.Reach(Bean, instance, Limb.LeftArm, hand, CharacterPosing.BendSign(Bean, instance, Limb.LeftArm));

        Assert.True(Distance(elbow, CharacterPosing.BendPoint(Bean, posed, Limb.LeftArm)) < 0.05);
    }

    [Fact]
    public void Moving_the_hips_keeps_the_feet_planted_and_leaning_and_tilting_move_the_chest_and_head()
    {
        var instance = Placed(Bean);
        var feet = new[] { Limb.LeftLeg, Limb.RightLeg }.Select(l => CharacterPosing.EndPoint(Bean, instance, l)).ToList();

        var crouched = CharacterPosing.MoveHips(Bean, instance, new Point2D(0, 8));
        Assert.All(new[] { Limb.LeftLeg, Limb.RightLeg }.Select((l, i) => Distance(feet[i], CharacterPosing.EndPoint(Bean, crouched, l))), d => Assert.True(d < 0.1, $"{d}"));

        var chest = CharacterPosing.TrunkPoint(Bean, instance, TrunkPart.Chest);
        var leaned = CharacterPosing.Lean(Bean, instance, new Point2D(chest.X + 10, chest.Y + 3));
        Assert.True(CharacterPosing.TrunkPoint(Bean, leaned, TrunkPart.Chest).X > chest.X + 5);

        var head = CharacterPosing.TrunkPoint(Bean, instance, TrunkPart.Head);
        var tilted = CharacterPosing.TiltHead(Bean, instance, new Point2D(head.X - 8, head.Y + 2));
        Assert.True(CharacterPosing.TrunkPoint(Bean, tilted, TrunkPart.Head).X < head.X - 3);
    }

    [Fact]
    public void Every_pose_preset_applies_and_turns()
    {
        foreach (var character in new[] { Bean, HeadOnLegs })
            foreach (var preset in PosePresets.All)
            {
                var posed = PosePresets.Apply(character, Placed(character), preset);
                Assert.All(posed.Pose.BoneRotations, r => Assert.True(double.IsFinite(r.Degrees), $"{preset.Name}: {r}"));
                var turned = CharacterPosing.Turn(character, posed, posed.Pose.ViewAngle == ViewAngle.Profile ? ViewAngle.Front : ViewAngle.Profile);
                Assert.All(turned.Pose.BoneRotations, r => Assert.True(double.IsFinite(r.Degrees)));
            }
    }

    [Fact]
    public void A_limb_the_character_hasnt_got_cannot_be_posed()
    {
        var oneArm = Bean with { Modules = Bean.Modules! with { Arms = Bean.Modules.Arms! with { Sides = LimbSides.Left } } };
        var instance = Placed(oneArm);
        var missing = CharacterPosing.EndPoint(oneArm, instance, Limb.RightArm);

        var posed = CharacterPosing.Reach(oneArm, instance, Limb.RightArm, new Point2D(missing.X + 10, missing.Y), 1);

        Assert.Same(instance, posed);
    }
}
