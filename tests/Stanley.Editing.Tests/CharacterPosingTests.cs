using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editing.Tests;

public class CharacterPosingTests
{
    private static readonly CharacterDefinition Alice = CharacterDefinition.Create("Alice");

    private static CharacterInstance Placed(ViewAngle angle = ViewAngle.Front, bool mirrored = false) =>
        new(Alice.Id, new CharacterPlacement(new Point2D(100, 200), 100, mirrored), null, new PoseData(angle, [], []), null);

    private static double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    public static TheoryData<Limb, ViewAngle, bool> Cases()
    {
        var data = new TheoryData<Limb, ViewAngle, bool>();
        foreach (var limb in Enum.GetValues<Limb>())
            foreach (var angle in new[] { ViewAngle.Front, ViewAngle.Profile })
                foreach (var mirrored in new[] { false, true })
                    data.Add(limb, angle, mirrored);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Dragging_a_hand_or_foot_to_a_reachable_point_puts_it_exactly_there(Limb limb, ViewAngle angle, bool mirrored)
    {
        var instance = Placed(angle, mirrored);
        var start = CharacterPosing.EndPoint(Alice, instance, limb);
        var target = new Point2D(start.X + 8, start.Y - 10);

        var posed = CharacterPosing.Reach(Alice, instance, limb, target, CharacterPosing.BendSign(Alice, instance, limb));

        var end = CharacterPosing.EndPoint(Alice, posed, limb);
        Assert.True(Distance(end, target) < 0.05, $"{limb} {angle} mirrored={mirrored}: ended at {end}, wanted {target}");
        Assert.Equal(2, posed.Pose.BoneRotations.Count);
        Assert.Equal(instance.Placement, posed.Placement);
    }

    [Fact]
    public void Bones_keep_their_lengths_and_an_out_of_reach_target_just_straightens_the_limb_towards_it()
    {
        var instance = Placed();
        var rest = CharacterPosing.Figure(Alice, instance).Layout;
        var shoulder = instance.Placement.ToPage(rest.Bones.Single(b => b.Bone == HumanoidBone.LeftUpperArm).Position);
        var far = new Point2D(shoulder.X + 500, shoulder.Y);

        var posed = CharacterPosing.Reach(Alice, instance, Limb.LeftArm, far, 1);
        var layout = CharacterPosing.Figure(Alice, posed).Layout;
        Point2D J(HumanoidBone b) => layout.Bones.Single(p => p.Bone == b).Position;
        Point2D R(HumanoidBone b) => rest.Bones.Single(p => p.Bone == b).Position;

        Assert.Equal(Distance(R(HumanoidBone.LeftUpperArm), R(HumanoidBone.LeftLowerArm)), Distance(J(HumanoidBone.LeftUpperArm), J(HumanoidBone.LeftLowerArm)), 6);
        Assert.Equal(Distance(R(HumanoidBone.LeftLowerArm), R(HumanoidBone.LeftHand)), Distance(J(HumanoidBone.LeftLowerArm), J(HumanoidBone.LeftHand)), 6);
        var hand = CharacterPosing.EndPoint(Alice, posed, Limb.LeftArm);
        Assert.Equal(shoulder.Y, hand.Y, 1); // pointing straight at the target
        Assert.True(hand.X > shoulder.X);
    }

    [Fact]
    public void Posing_one_limb_leaves_the_others_alone_and_reset_stands_at_rest()
    {
        var instance = Placed();
        var leftFoot = CharacterPosing.EndPoint(Alice, instance, Limb.LeftLeg);
        var withArm = CharacterPosing.Reach(Alice, instance, Limb.RightArm, new Point2D(80, 140), 1);
        var withBoth = CharacterPosing.Reach(Alice, withArm, Limb.LeftLeg, new Point2D(leftFoot.X + 10, leftFoot.Y - 20), -1);

        Assert.Equal(4, withBoth.Pose.BoneRotations.Count);
        Assert.Equal(CharacterPosing.EndPoint(Alice, withArm, Limb.RightArm), CharacterPosing.EndPoint(Alice, withBoth, Limb.RightArm));
        Assert.Equal(CharacterPosing.EndPoint(Alice, instance, Limb.LeftArm), CharacterPosing.EndPoint(Alice, withBoth, Limb.LeftArm));

        var reset = CharacterPosing.ResetPose(withBoth);
        Assert.Empty(reset.Pose.BoneRotations);
        Assert.Equal(ViewAngle.Front, reset.Pose.ViewAngle);
    }

    [Fact]
    public void In_a_side_view_knees_bend_forward_and_elbows_back()
    {
        var instance = Placed(ViewAngle.Profile);
        var foot = CharacterPosing.EndPoint(Alice, instance, Limb.LeftLeg);
        var hand = CharacterPosing.EndPoint(Alice, instance, Limb.LeftArm);

        // Lift the foot (a step) and raise the hand (a reach): both joints must bend.
        var stepped = CharacterPosing.Reach(Alice, instance, Limb.LeftLeg, new Point2D(foot.X, foot.Y - 30), CharacterPosing.BendSign(Alice, instance, Limb.LeftLeg));
        var reached = CharacterPosing.Reach(Alice, instance, Limb.LeftArm, new Point2D(hand.X, hand.Y - 25), CharacterPosing.BendSign(Alice, instance, Limb.LeftArm));

        var knee = stepped.Placement.ToPage(CharacterPosing.Figure(Alice, stepped).Layout.Bones.Single(b => b.Bone == HumanoidBone.LeftLowerLeg).Position);
        var elbow = reached.Placement.ToPage(CharacterPosing.Figure(Alice, reached).Layout.Bones.Single(b => b.Bone == HumanoidBone.LeftLowerArm).Position);
        Assert.True(knee.X > foot.X + 5, "the knee comes forward (+x, the way the character faces)");
        Assert.True(elbow.X < hand.X - 3, "the elbow goes back");
    }
}
