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

    [Theory]
    [MemberData(nameof(Cases))]
    public void Dragging_the_elbow_or_knee_handle_moves_the_joint_there_and_keeps_the_limb_bent(Limb limb, ViewAngle angle, bool mirrored)
    {
        var instance = Placed(angle, mirrored);
        var start = CharacterPosing.EndPoint(Alice, instance, limb);
        var reached = CharacterPosing.Reach(Alice, instance, limb, new Point2D(start.X + 8, start.Y - 10), CharacterPosing.BendSign(Alice, instance, limb));
        var elbow = CharacterPosing.BendPoint(Alice, reached, limb);
        var hand = CharacterPosing.EndPoint(Alice, reached, limb);
        var root = Root(reached, limb);
        var forearm = Distance(elbow, hand);
        var bendAngle = Angle(root, elbow, hand);

        // Swing the upper bone a quarter turn: a target at the same distance from the shoulder/hip.
        var target = new Point2D(root.X - (elbow.Y - root.Y), root.Y + (elbow.X - root.X));
        var bent = CharacterPosing.Bend(Alice, reached, limb, target);

        var newElbow = CharacterPosing.BendPoint(Alice, bent, limb);
        var newHand = CharacterPosing.EndPoint(Alice, bent, limb);
        Assert.True(Distance(target, newElbow) < 0.05, $"the elbow/knee lands on the target ({newElbow} vs {target})");
        Assert.True(Math.Abs(forearm - Distance(newElbow, newHand)) < 0.05, "the forearm/shin keeps its length");
        Assert.True(Math.Abs(bendAngle - Angle(Root(bent, limb), newElbow, newHand)) < 1, "the limb keeps its bend");
        Assert.Equal(instance.Placement, bent.Placement);
    }

    [Fact]
    public void Dragging_the_elbow_out_of_reach_points_the_upper_arm_at_the_target()
    {
        var instance = Placed();
        var root = Root(instance, Limb.LeftArm);
        var elbow = CharacterPosing.BendPoint(Alice, instance, Limb.LeftArm);
        var far = new Point2D(root.X + 1000, root.Y);

        var bent = CharacterPosing.Bend(Alice, instance, Limb.LeftArm, far);

        var newElbow = CharacterPosing.BendPoint(Alice, bent, Limb.LeftArm);
        Assert.True(Math.Abs(newElbow.Y - root.Y) < 0.05, "the elbow lies on the line to the target");
        Assert.True(newElbow.X > root.X);
        Assert.True(Math.Abs(Distance(root, newElbow) - Distance(root, elbow)) < 0.05, "the upper arm doesn't stretch");
    }

    [Fact]
    public void Dragging_the_elbow_to_where_it_already_is_leaves_the_pose_unchanged()
    {
        var instance = Placed();
        var start = CharacterPosing.EndPoint(Alice, instance, Limb.LeftArm);
        var reached = CharacterPosing.Reach(Alice, instance, Limb.LeftArm, new Point2D(start.X + 8, start.Y - 10), CharacterPosing.BendSign(Alice, instance, Limb.LeftArm));
        var elbow = CharacterPosing.BendPoint(Alice, reached, Limb.LeftArm);

        var same = CharacterPosing.Bend(Alice, reached, Limb.LeftArm, elbow);

        Assert.True(Distance(CharacterPosing.EndPoint(Alice, reached, Limb.LeftArm), CharacterPosing.EndPoint(Alice, same, Limb.LeftArm)) < 0.05);
    }

    private static Point2D Root(CharacterInstance instance, Limb limb) =>
        instance.Placement.ToPage(CharacterPosing.Figure(Alice, instance).Layout.Bones.First(b => b.Bone == CharacterPosing.Chain(limb).Root).Position);

    private static double Angle(Point2D a, Point2D vertex, Point2D b)
    {
        var d = Math.Atan2(b.Y - vertex.Y, b.X - vertex.X) - Math.Atan2(a.Y - vertex.Y, a.X - vertex.X);
        return Math.Abs(Math.IEEERemainder(d * 180 / Math.PI, 360));
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

public class TrunkPosingAndPresetTests
{
    private static readonly CharacterDefinition Alice = CharacterDefinition.Create("Alice");

    private static CharacterInstance Placed(ViewAngle angle = ViewAngle.Front, bool mirrored = false, CharacterDefinition? character = null) =>
        new((character ?? Alice).Id, new CharacterPlacement(new Point2D(100, 200), 100, mirrored), null, new PoseData(angle, [], []), null);

    private static double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Theory]
    [InlineData(ViewAngle.Front, false)]
    [InlineData(ViewAngle.Profile, false)]
    [InlineData(ViewAngle.Profile, true)]
    public void Dragging_the_hips_down_crouches_with_both_feet_staying_exactly_where_they_were(ViewAngle angle, bool mirrored)
    {
        var start = Placed(angle, mirrored);
        var feet = new[] { Limb.LeftLeg, Limb.RightLeg }.Select(l => CharacterPosing.EndPoint(Alice, start, l)).ToList();
        var hips = CharacterPosing.TrunkPoint(Alice, start, TrunkPart.Hips);
        var head = CharacterPosing.TrunkPoint(Alice, start, TrunkPart.Head);

        var crouched = CharacterPosing.MoveHips(Alice, start, new Point2D(0, 15));

        Assert.Equal(hips.Y + 15, CharacterPosing.TrunkPoint(Alice, crouched, TrunkPart.Hips).Y, 3);
        Assert.Equal(head.Y + 15, CharacterPosing.TrunkPoint(Alice, crouched, TrunkPart.Head).Y, 3);
        var after = new[] { Limb.LeftLeg, Limb.RightLeg }.Select(l => CharacterPosing.EndPoint(Alice, crouched, l)).ToList();
        for (var i = 0; i < 2; i++)
            Assert.True(Distance(feet[i], after[i]) < 0.05, $"foot {i} moved from {feet[i]} to {after[i]}");
        Assert.Equal(start.Placement, crouched.Placement);
    }

    [Fact]
    public void Dragging_the_chest_bends_the_back_along_the_spine_and_the_arms_keep_hanging()
    {
        var start = Placed(ViewAngle.Profile);
        var hips = CharacterPosing.TrunkPoint(Alice, start, TrunkPart.Hips);
        var chest = CharacterPosing.TrunkPoint(Alice, start, TrunkPart.Chest);
        var foot = CharacterPosing.EndPoint(Alice, start, Limb.LeftLeg);
        Point2D Joint(CharacterInstance c, HumanoidBone b) => c.Placement.ToPage(CharacterPosing.Figure(Alice, c).Layout.Bones.Single(x => x.Bone == b).Position);
        static double Degrees(CharacterInstance c, HumanoidBone b) => c.Pose.BoneRotations.SingleOrDefault(r => r.Bone == b)?.Degrees ?? 0;

        var target = new Point2D(chest.X + 20, chest.Y + 5);
        var forward = CharacterPosing.Lean(Alice, start, target);

        var reached = CharacterPosing.TrunkPoint(Alice, forward, TrunkPart.Chest);
        Assert.True(Distance(reached, target) < 4, $"the chest follows the pointer ({reached} vs {target})");
        // Every joint of the back takes a share of the bend, all the same way - a curve, not a hinge at the hips.
        foreach (var (bone, limit) in BodyRig.SpineJoints)
        {
            Assert.InRange(Degrees(forward, bone), 3, limit);
        }
        // The arm hangs as before: same direction, just carried along by the shoulder.
        double ArmAngle(CharacterInstance c)
        {
            var (shoulder, elbow) = (Joint(c, HumanoidBone.LeftUpperArm), Joint(c, HumanoidBone.LeftLowerArm));
            return Math.Atan2(elbow.Y - shoulder.Y, elbow.X - shoulder.X);
        }
        Assert.Equal(ArmAngle(start), ArmAngle(forward), 3);
        Assert.Equal(foot, CharacterPosing.EndPoint(Alice, forward, Limb.LeftLeg));
        Assert.Equal(hips.X, CharacterPosing.TrunkPoint(Alice, forward, TrunkPart.Hips).X, 6);

        var tooFar = CharacterPosing.Lean(Alice, start, new Point2D(hips.X + 80, hips.Y + 30));
        foreach (var (bone, limit) in BodyRig.SpineJoints)
            Assert.InRange(Degrees(tooFar, bone), -limit, limit);
    }

    [Fact]
    public void Dragging_the_head_bends_the_neck_and_tilts_the_head_together()
    {
        var start = Placed();
        var top = CharacterPosing.TrunkPoint(Alice, start, TrunkPart.Head);
        static double Degrees(CharacterInstance c, HumanoidBone b) => c.Pose.BoneRotations.SingleOrDefault(r => r.Bone == b)?.Degrees ?? 0;

        var tilted = CharacterPosing.TiltHead(Alice, start, new Point2D(top.X + 5, top.Y + 1));
        Assert.True(CharacterPosing.TrunkPoint(Alice, tilted, TrunkPart.Head).X > top.X + 3);
        Assert.True(Degrees(tilted, HumanoidBone.Neck) > 1 && Degrees(tilted, HumanoidBone.Head) > 1, "both the neck and the head turn");
        Assert.Equal(CharacterPosing.TrunkPoint(Alice, start, TrunkPart.Chest), CharacterPosing.TrunkPoint(Alice, tilted, TrunkPart.Chest));

        var extreme = CharacterPosing.TiltHead(Alice, start, new Point2D(top.X + 100, top.Y + 100));
        Assert.InRange(Degrees(extreme, HumanoidBone.Neck), -BodyRig.MaxNeckBend, BodyRig.MaxNeckBend);
        Assert.InRange(Degrees(extreme, HumanoidBone.Head), -BodyRig.MaxHeadTilt, BodyRig.MaxHeadTilt);
    }

    [Fact]
    public void Mirroring_a_front_view_wave_waves_with_the_other_hand_and_mirroring_twice_is_the_same_pose()
    {
        var wave = PosePresets.Apply(Alice, Placed(), PosePresets.Get(PosePreset.Wave));
        var raised = CharacterPosing.EndPoint(Alice, wave, Limb.RightArm);
        var hips = CharacterPosing.TrunkPoint(Alice, wave, TrunkPart.Hips);

        var mirrored = CharacterPosing.MirrorPose(wave);
        var other = CharacterPosing.EndPoint(Alice, mirrored, Limb.LeftArm);
        Assert.Equal(raised.Y, other.Y, 3);
        Assert.Equal(hips.X - raised.X, other.X - hips.X, 3);

        var back = CharacterPosing.MirrorPose(mirrored);
        Assert.Equal(CharacterPosing.EndPoint(Alice, wave, Limb.RightArm).X, CharacterPosing.EndPoint(Alice, back, Limb.RightArm).X, 6);
    }

    public static TheoryData<PosePreset, BodyPreset, ViewAngle> PresetCases()
    {
        var data = new TheoryData<PosePreset, BodyPreset, ViewAngle>();
        foreach (var preset in Enum.GetValues<PosePreset>())
            foreach (var body in new[] { BodyPreset.Adult, BodyPreset.Child, BodyPreset.Heavy, BodyPreset.Chibi })
                foreach (var angle in new[] { ViewAngle.Front, ViewAngle.Profile })
                    data.Add(preset, body, angle);
        return data;
    }

    [Theory]
    [MemberData(nameof(PresetCases))]
    public void Every_preset_fits_every_body_keeps_its_place_and_plants_feet_it_doesnt_move(PosePreset preset, BodyPreset body, ViewAngle angle)
    {
        var character = CharacterDefinition.Create("A", BodyPresets.Shape(body));
        var start = Placed(angle, character: character);
        var definition = PosePresets.Get(preset);

        var posed = PosePresets.Apply(character, start, definition);

        Assert.Equal(start.Placement, posed.Placement);
        Assert.Equal(definition.View ?? angle, posed.Pose.ViewAngle);
        Assert.Equal(preset != PosePreset.Stand, CharacterPosing.IsPosed(posed.Pose));
        var standing = start with { Pose = start.Pose with { ViewAngle = posed.Pose.ViewAngle } };
        foreach (var leg in new[] { Limb.LeftLeg, Limb.RightLeg }.Where(l => definition.Goals.All(g => g.Limb != l)))
            Assert.True(Distance(CharacterPosing.EndPoint(character, standing, leg), CharacterPosing.EndPoint(character, posed, leg)) < 0.05,
                $"{preset}: the {leg} should stay planted");
        var extent = CharacterPosing.Figure(character, posed).Extent;
        Assert.True(extent.Bottom <= 0.001, "nothing sinks into the floor");
        Assert.True(extent.Top < -0.2 * character.Body.Height, "still a figure, not collapsed");
    }
}
