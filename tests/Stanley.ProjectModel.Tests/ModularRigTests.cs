using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Poses;

namespace Stanley.ProjectModel.Tests;

/// <summary>A body built from shapes as a figure (docs/shape-characters.md §4.3, §5): the invariants its rig keeps, every fallback, and posing it with the mannequin's own machinery.</summary>
public class ModularRigTests
{
    private static readonly ShapeModule Head = new(ModuleShapes.Ellipse(), 0.3, 0.3);
    private static readonly ShapeModule Torso = new(ModuleShapes.RoundedRectangle(), 0.36, 0.4);
    private static readonly LimbModule Arms = new(LimbStyle.Tube, 0.36, 0.06);
    private static readonly LimbModule Legs = new(LimbStyle.Tube, 0.4, 0.08);

    private static ModularBody Creature(bool head = true, bool torso = true, bool arms = true, bool legs = true) =>
        new(head ? Head : null, torso ? Torso : null, arms ? Arms : null, legs ? Legs : null);

    /// <summary>Every combination of parts left in and out, front and side.</summary>
    public static TheoryData<bool, bool, bool, bool, ViewAngle> Combinations()
    {
        var data = new TheoryData<bool, bool, bool, bool, ViewAngle>();
        for (var mask = 0; mask < 16; mask++)
            foreach (var angle in new[] { ViewAngle.Front, ViewAngle.Profile })
                data.Add((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, angle);
        return data;
    }

    private static Point2D Joint(ViewAngleRestLayout layout, HumanoidBone bone) => layout.Bones.First(b => b.Bone == bone).Position;

    /// <summary>The same joints in the same places, to rounding (an unbent trunk still turns each arm by a zero angle).</summary>
    private static void Same(ViewAngleRestLayout expected, ViewAngleRestLayout actual)
    {
        Assert.Equal(expected.Bones.Select(b => b.Bone), actual.Bones.Select(b => b.Bone));
        foreach (var (e, a) in expected.Bones.Zip(actual.Bones))
        {
            Assert.Equal(e.Position.X, a.Position.X, 12);
            Assert.Equal(e.Position.Y, a.Position.Y, 12);
        }
    }

    private static Rect2D Bounds(FigureShape shape) => AnchorRing.CurveBounds(shape.Anchors);

    private static FigureShape Shape(BodyFigure figure, FigureLayerKind kind) => figure.Layers.Single(l => l.Kind == kind).Shapes!.First();

    private static bool Inside(Rect2D r, Point2D p, double slack = 1e-9) =>
        p.X >= r.Left - slack && p.X <= r.Right + slack && p.Y >= r.Top - slack && p.Y <= r.Bottom + slack;

    private static PoseData Posed(ViewAngle angle) => new(angle,
    [
        new BoneRotation(HumanoidBone.LeftUpperArm, 40), new BoneRotation(HumanoidBone.LeftLowerArm, 30),
        new BoneRotation(HumanoidBone.RightUpperArm, -60), new BoneRotation(HumanoidBone.RightLowerArm, -20),
        new BoneRotation(HumanoidBone.LeftUpperLeg, 25), new BoneRotation(HumanoidBone.LeftLowerLeg, -30),
        new BoneRotation(HumanoidBone.RightUpperLeg, -15), new BoneRotation(HumanoidBone.RightLowerLeg, 20),
        new BoneRotation(HumanoidBone.Spine, 20), new BoneRotation(HumanoidBone.Chest, 10), new BoneRotation(HumanoidBone.UpperChest, 5),
        new BoneRotation(HumanoidBone.Neck, 10), new BoneRotation(HumanoidBone.Head, 15),
    ], new SortedDictionary<string, string>(), new Point2D(0.05, 0.1));

    // ---------------------------------------------------------------- invariants

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Whatever_is_missing_the_figure_has_every_joint_the_mannequin_has_and_stands_on_the_ground(bool head, bool torso, bool arms, bool legs, ViewAngle angle)
    {
        var body = Creature(head, torso, arms, legs);
        var figure = ModularRig.Build(body, angle);
        var expected = BodyRig.Build(BodyShape.Default, angle).Layout.Bones.Select(b => b.Bone).ToList();

        foreach (var layout in new[] { figure.RestLayout, figure.BaseLayout, figure.Layout })
            Assert.Equal(expected, layout.Bones.Select(b => b.Bone));
        foreach (var (root, middle, end) in BodyRig.LimbChains)
            Assert.All(new[] { root, middle, end }, bone => Assert.Contains(figure.Layout.Bones, b => b.Bone == bone));
        Assert.All(BodyRig.NeckJoints, j => Assert.Contains(figure.Layout.Bones, b => b.Bone == j.Bone));

        Assert.Equal(0, figure.Extent.Bottom, 9);
        Assert.Equal(ModularRig.Height(body), -figure.Extent.Top, 9);
        Assert.Equal(0, Joint(figure.Layout, HumanoidBone.Hips).X, 9);
        Assert.All(figure.Layout.Bones, b => Assert.True(double.IsFinite(b.Position.X) && double.IsFinite(b.Position.Y)));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Every_pose_applies_to_every_combination_and_resting_is_no_pose(bool head, bool torso, bool arms, bool legs, ViewAngle angle)
    {
        var body = Creature(head, torso, arms, legs);
        var rest = ModularRig.Build(body, angle);
        var posed = ModularRig.Build(body, angle, Posed(angle));
        var none = ModularRig.Build(body, angle, new PoseData(angle, [], new SortedDictionary<string, string>()));

        Same(rest.RestLayout, rest.Layout);
        Same(rest.RestLayout, rest.BaseLayout);
        Assert.Equal(rest.Layout.Bones, none.Layout.Bones);
        Assert.Equal(rest.Extent, none.Extent);
        Assert.Equal(rest.RestLayout.Bones, posed.RestLayout.Bones);
        Assert.All(posed.Layout.Bones, b => Assert.True(double.IsFinite(b.Position.X) && double.IsFinite(b.Position.Y)));
        Assert.True(double.IsFinite(posed.Extent.Width) && double.IsFinite(posed.Extent.Height));
        if (arms)
            Assert.NotEqual(Joint(rest.Layout, HumanoidBone.LeftHand), Joint(posed.Layout, HumanoidBone.LeftHand));
        if (legs)
            Assert.NotEqual(Joint(rest.Layout, HumanoidBone.RightFoot), Joint(posed.Layout, HumanoidBone.RightFoot));
        if (head || torso)
            Assert.NotEqual(rest.Blobs[0], posed.Blobs[0]);
    }

    [Fact]
    public void A_symmetric_body_gives_a_symmetric_figure()
    {
        var figure = ModularRig.Build(Creature());

        Assert.Equal(-figure.Extent.Left, figure.Extent.Right, 9);
        foreach (var (left, right) in new[]
        {
            (HumanoidBone.LeftUpperArm, HumanoidBone.RightUpperArm), (HumanoidBone.LeftHand, HumanoidBone.RightHand),
            (HumanoidBone.LeftUpperLeg, HumanoidBone.RightUpperLeg), (HumanoidBone.LeftFoot, HumanoidBone.RightFoot),
        })
        {
            var (l, r) = (Joint(figure.Layout, left), Joint(figure.Layout, right));
            Assert.Equal(-l.X, r.X, 9);
            Assert.Equal(l.Y, r.Y, 9);
            Assert.True(l.X > 0, "the character's left is on the viewer's right from the front");
        }
    }

    [Fact]
    public void The_extent_is_exactly_what_is_drawn()
    {
        var figure = ModularRig.Build(Creature());
        var head = Bounds(Shape(figure, FigureLayerKind.Head));

        Assert.Equal(head.Top, figure.Extent.Top, 9);
        Assert.Equal(0.3, head.Height, 6);
        var feet = figure.Layers.Where(l => l.Kind is FigureLayerKind.LeftLeg or FigureLayerKind.RightLeg).SelectMany(l => l.Ellipses).ToList();
        Assert.Equal(0, feet.Max(e => e.Center.Y + e.RadiusY), 9);
    }

    [Fact]
    public void The_height_is_the_same_from_either_side_and_comes_through_the_character_seam()
    {
        var body = Creature();
        var character = CharacterDefinition.Create("Blob") with { Modules = body };

        Assert.Equal(-ModularRig.Build(body, ViewAngle.Front).Extent.Top, -ModularRig.Build(body, ViewAngle.Profile).Extent.Top, 9);
        Assert.Equal(ModularRig.Height(body), CharacterRig.Height(character), 9);
        Assert.Equal(ModularRig.Build(body).Extent, CharacterRig.Extent(character));
        // Parts stacked: legs, torso, head (less the bit the head sinks onto the neck).
        Assert.InRange(ModularRig.Height(body), 0.4 + 0.4 + 0.3 - 0.05, 0.4 + 0.4 + 0.3 + 0.1);
    }

    [Fact]
    public void A_three_quarter_view_draws_as_the_front()
    {
        var body = Creature();

        Assert.Equal(ModularRig.Build(body).Layout.Bones, ModularRig.Build(body, ViewAngle.ThreeQuarter).Layout.Bones);
        Assert.Equal(ViewAngle.Front, ModularRig.Build(body, ViewAngle.ThreeQuarter).Angle);
    }

    [Theory]
    [InlineData(ViewAngle.Front)]
    [InlineData(ViewAngle.Profile)]
    public void The_layers_are_the_mannequins_in_the_same_order(ViewAngle angle)
    {
        var expected = BodyRig.Build(BodyShape.Default, angle).Layers.Select(l => l.Kind);

        Assert.Equal(expected, ModularRig.Build(Creature(), angle).Layers.Select(l => l.Kind));
        Assert.Equal(expected, ModularRig.Build(new ModularBody(null, null, null, null), angle).Layers.Select(l => l.Kind));
    }

    [Fact]
    public void Each_part_is_in_its_own_layer_from_the_front()
    {
        var figure = ModularRig.Build(Creature());
        FigureLayer Layer(FigureLayerKind kind) => figure.Layers.Single(l => l.Kind == kind);

        Assert.Single(Layer(FigureLayerKind.Torso).Shapes!);
        Assert.Single(Layer(FigureLayerKind.Head).Shapes!);
        Assert.Equal(2, Layer(FigureLayerKind.LeftArm).Capsules.Count);
        Assert.Single(Layer(FigureLayerKind.LeftArm).Ellipses);
        Assert.Equal(2, Layer(FigureLayerKind.RightLeg).Capsules.Count);
        Assert.All(new[] { FigureLayerKind.Back, FigureLayerKind.Front }, k => Assert.False(Layer(k).HasBody));
        Assert.Equal(CharacterDefinition.SkinSlot, Layer(FigureLayerKind.Head).Shapes![0].ColorSlot);
        Assert.Null(Layer(FigureLayerKind.Torso).Torso);
    }

    [Fact]
    public void Side_on_the_near_arm_and_foot_are_on_top_and_the_legs_are_in_the_body()
    {
        var figure = ModularRig.Build(Creature(), ViewAngle.Profile);
        FigureLayer Layer(FigureLayerKind kind) => figure.Layers.Single(l => l.Kind == kind);

        Assert.Equal(4, Layer(FigureLayerKind.Body).Capsules.Count);
        Assert.Single(Layer(FigureLayerKind.Body).Shapes!);
        Assert.Single(Layer(FigureLayerKind.Body).Ellipses); // the far foot
        Assert.Single(Layer(FigureLayerKind.NearFoot).Ellipses);
        Assert.Equal(2, Layer(FigureLayerKind.NearArm).Capsules.Count);
        Assert.Equal(2, figure.NearLimbs.Count);
        // The far arm and leg are set back (-x), the feet point forward (+x).
        Assert.True(Joint(figure.Layout, HumanoidBone.LeftUpperArm).X < Joint(figure.Layout, HumanoidBone.RightUpperArm).X);
        Assert.True(Joint(figure.Layout, HumanoidBone.LeftUpperLeg).X < Joint(figure.Layout, HumanoidBone.RightUpperLeg).X);
        var nearFoot = Layer(FigureLayerKind.NearFoot).Ellipses[0];
        Assert.True(nearFoot.Center.X > Joint(figure.Layout, HumanoidBone.RightFoot).X);
    }

    [Fact]
    public void Joined_pieces_have_seams_at_their_sockets_and_separate_ones_none()
    {
        var joined = ModularRig.Build(Creature());
        var separate = ModularRig.Build(Creature() with { Separate = true });
        FigureLayer Layer(BodyFigure f, FigureLayerKind kind) => f.Layers.Single(l => l.Kind == kind);

        Assert.Equal(2, Layer(joined, FigureLayerKind.Torso).Seams.Count);
        Assert.Single(Layer(joined, FigureLayerKind.Head).Seams);
        Assert.Equal(Joint(joined.Layout, HumanoidBone.LeftUpperArm), Layer(joined, FigureLayerKind.LeftArm).Seams.Single().Center);
        Assert.Single(Layer(joined, FigureLayerKind.RightLeg).Seams);
        Assert.All(separate.Layers, l => Assert.Empty(l.Seams));
    }

    // ---------------------------------------------------------------- fallbacks (§4.3)

    [Fact]
    public void With_everything_arms_hang_from_the_torso_legs_from_its_hips_and_the_head_on_its_neck()
    {
        var figure = ModularRig.Build(Creature());
        var torso = Bounds(Shape(figure, FigureLayerKind.Torso));
        var head = Bounds(Shape(figure, FigureLayerKind.Head));

        Assert.True(Inside(torso, Joint(figure.Layout, HumanoidBone.LeftUpperArm)));
        Assert.True(Inside(torso, Joint(figure.Layout, HumanoidBone.RightUpperLeg)));
        Assert.Equal(torso.Top + Head.Height * ModularRig.HeadSink, head.Bottom, 6);
        Assert.Equal(torso.Top, Joint(figure.Layout, HumanoidBone.Neck).Y, 6);
        // The face is the head itself (a round head's whole circle, near enough).
        Assert.True(Inside(head, figure.Regions.Head.Center));
        Assert.InRange(figure.Regions.Head.RadiusY, 0.14, 0.15);
        Assert.Equal(new[] { new FigurePart(BodyRegion.Neck) }, figure.Absent!);
        Assert.True(figure.Has(BodyRegion.Head) && figure.Has(BodyRegion.Torso) && figure.Has(BodyRegion.Arm, LimbSide.Right));
    }

    [Fact]
    public void With_no_torso_the_head_sits_on_the_legs_and_the_arms_hang_from_its_sides()
    {
        var figure = ModularRig.Build(Creature(torso: false));
        var head = Bounds(Shape(figure, FigureLayerKind.Head));

        Assert.False(figure.Layers.Single(l => l.Kind == FigureLayerKind.Torso).HasBody);
        Assert.True(Inside(head, Joint(figure.Layout, HumanoidBone.LeftUpperArm)));
        Assert.True(Inside(head, Joint(figure.Layout, HumanoidBone.RightUpperLeg)));
        // The legs reach the ground, so the head stands a leg's length up.
        Assert.InRange(head.Bottom, -Legs.Length - 0.1, -Legs.Length + 0.02);
        // The trunk joints collapse onto the head's bottom.
        var hips = Joint(figure.Layout, HumanoidBone.Hips);
        Assert.All(new[] { HumanoidBone.Spine, HumanoidBone.Chest, HumanoidBone.Neck, HumanoidBone.Head }, b => Assert.Equal(hips, Joint(figure.Layout, b)));
        Assert.Equal(head.Bottom, hips.Y, 3);
        Assert.Contains(new FigurePart(BodyRegion.Torso), figure.Absent!);
        Assert.True(figure.Has(BodyRegion.Head));
        // The head's seams meet the legs.
        Assert.Equal(2, figure.Layers.Single(l => l.Kind == FigureLayerKind.Head).Seams.Count);
    }

    [Fact]
    public void With_no_torso_the_arms_turn_with_the_head_and_a_lean_tilts_the_head_about_the_hips()
    {
        var body = Creature(torso: false);
        var rest = ModularRig.Build(body);
        var pose = new PoseData(ViewAngle.Front, [new BoneRotation(HumanoidBone.Spine, 30)], new SortedDictionary<string, string>());
        var leaning = ModularRig.Build(body, ViewAngle.Front, pose);

        var hips = Joint(rest.Layout, HumanoidBone.Hips);
        Assert.Equal(30, leaning.Blobs[0].RotationDegrees, 6);
        Assert.Equal(hips, Joint(leaning.Layout, HumanoidBone.Hips));
        double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        var restShoulder = Joint(rest.Layout, HumanoidBone.LeftUpperArm);
        var leanShoulder = Joint(leaning.Layout, HumanoidBone.LeftUpperArm);
        Assert.Equal(Distance(hips, restShoulder), Distance(hips, leanShoulder), 9);
        Assert.NotEqual(restShoulder, leanShoulder);
    }

    [Fact]
    public void With_no_head_the_face_goes_on_the_torso()
    {
        var figure = ModularRig.Build(Creature(head: false));
        var torso = Bounds(Shape(figure, FigureLayerKind.Torso));
        var face = figure.Regions.Head;

        Assert.False(figure.Layers.Single(l => l.Kind == FigureLayerKind.Head).HasBody);
        Assert.True(face.Center.Y - face.RadiusY >= torso.Top - 1e-9);
        Assert.True(face.Center.Y + face.RadiusY <= torso.Top + torso.Height * 2 / 3 + 1e-6);
        Assert.True(face.RadiusX > 0.1);
        Assert.Equal(face, figure.Blobs[0]);
        Assert.True(figure.Has(BodyRegion.Head));
        Assert.Equal(-torso.Top, ModularRig.Height(Creature(head: false)), 9);
    }

    [Fact]
    public void With_no_legs_the_lowest_piece_stands_on_the_ground()
    {
        var figure = ModularRig.Build(Creature(legs: false, arms: false));
        var torso = Bounds(Shape(figure, FigureLayerKind.Torso));

        Assert.Equal(0, torso.Bottom, 9);
        foreach (var side in new[] { (HumanoidBone.LeftUpperLeg, HumanoidBone.LeftLowerLeg, HumanoidBone.LeftFoot), (HumanoidBone.RightUpperLeg, HumanoidBone.RightLowerLeg, HumanoidBone.RightFoot) })
        {
            Assert.Equal(Joint(figure.Layout, side.Item1), Joint(figure.Layout, side.Item2));
            Assert.Equal(Joint(figure.Layout, side.Item1), Joint(figure.Layout, side.Item3));
            Assert.True(Inside(torso, Joint(figure.Layout, side.Item1)));
        }
        Assert.Contains(new FigurePart(BodyRegion.Leg), figure.Absent!);
        Assert.Contains(new FigurePart(BodyRegion.Foot), figure.Absent!);
        Assert.Contains(new FigurePart(BodyRegion.Skirt), figure.Absent!);
        Assert.Contains(new FigurePart(BodyRegion.Arm), figure.Absent!);
        Assert.Contains(new FigurePart(BodyRegion.Hand), figure.Absent!);
        Assert.False(figure.Has(BodyRegion.Leg, LimbSide.Right));
    }

    [Fact]
    public void Long_arms_still_leave_the_ground_under_the_lowest_thing_drawn()
    {
        var figure = ModularRig.Build(Creature(legs: false) with { Arms = Arms with { Length = 1.2 } });

        Assert.Equal(0, figure.Extent.Bottom, 9);
        Assert.True(Bounds(Shape(figure, FigureLayerKind.Torso)).Bottom < -0.5);
    }

    [Fact]
    public void With_no_arms_their_joints_rest_on_the_shoulders()
    {
        var figure = ModularRig.Build(Creature(arms: false));

        Assert.Equal(Joint(figure.Layout, HumanoidBone.LeftUpperArm), Joint(figure.Layout, HumanoidBone.LeftHand));
        Assert.True(Inside(Bounds(Shape(figure, FigureLayerKind.Torso)), Joint(figure.Layout, HumanoidBone.LeftUpperArm)));
        Assert.False(figure.Layers.Single(l => l.Kind == FigureLayerKind.LeftArm).HasBody);
        Assert.False(figure.Has(BodyRegion.Hand, LimbSide.Left));
        Assert.Equal(0, figure.Regions.LeftArm.Length, 12);
    }

    [Fact]
    public void One_arm_only_leaves_out_just_that_side()
    {
        var figure = ModularRig.Build(Creature() with { Arms = Arms with { Sides = LimbSides.Left } });

        Assert.True(figure.Has(BodyRegion.Arm, LimbSide.Left));
        Assert.False(figure.Has(BodyRegion.Arm, LimbSide.Right));
        Assert.False(figure.Has(BodyRegion.Hand, LimbSide.Right));
        Assert.Contains(new FigurePart(BodyRegion.Arm, LimbSide.Right), figure.Absent!);
        Assert.DoesNotContain(new FigurePart(BodyRegion.Arm), figure.Absent!);
        Assert.True(figure.Layers.Single(l => l.Kind == FigureLayerKind.LeftArm).HasBody);
        Assert.False(figure.Layers.Single(l => l.Kind == FigureLayerKind.RightArm).HasBody);
    }

    [Theory]
    [InlineData(ViewAngle.Front)]
    [InlineData(ViewAngle.Profile)]
    public void Nothing_at_all_is_still_a_figure(ViewAngle angle)
    {
        var body = new ModularBody(null, null, null, null);

        var figure = ModularRig.Build(body, angle, Posed(angle));

        Assert.Equal(0, ModularRig.Height(body));
        Assert.Equal(0, figure.Extent.Width);
        Assert.All(figure.Layers, l => Assert.False(l.HasBody));
        Assert.False(figure.Has(BodyRegion.Head));
        Assert.False(figure.Has(BodyRegion.Torso));
        Assert.True(double.IsFinite(figure.Regions.Torso.Top));
        Assert.Equal(new Point2D(0, 0), Joint(figure.RestLayout, HumanoidBone.Hips));
    }

    [Fact]
    public void Limbs_alone_stand_on_the_ground()
    {
        var figure = ModularRig.Build(Creature(head: false, torso: false));

        Assert.Equal(0, figure.Extent.Bottom, 9);
        Assert.True(figure.Extent.Height > Legs.Length);
        Assert.False(figure.Has(BodyRegion.Head));
        Assert.Empty(figure.Layers.Where(l => l.Kind is FigureLayerKind.LeftArm or FigureLayerKind.RightArm).SelectMany(l => l.Seams));
    }

    // ---------------------------------------------------------------- shapes, turns and posing

    [Fact]
    public void An_upside_down_triangle_torso_holds_its_arms_at_its_wide_top()
    {
        var figure = ModularRig.Build(Creature() with { Torso = new ShapeModule(ModuleShapes.Triangle(), 0.5, 0.4, FlipY: true) });
        var torso = Bounds(Shape(figure, FigureLayerKind.Torso));
        var shoulder = Joint(figure.Layout, HumanoidBone.LeftUpperArm);

        Assert.True(shoulder.X > torso.Right - 0.08, $"{shoulder} should be near the wide top's corner of {torso}");
        Assert.True(shoulder.Y < torso.Top + torso.Height * 0.25);
        Assert.Equal(torso.Top, Joint(figure.Layout, HumanoidBone.Neck).Y, 6);
    }

    [Fact]
    public void A_turned_torso_carries_its_sockets_round()
    {
        var upright = ModularRig.Build(Creature(legs: false));
        var turned = ModularRig.Build(Creature(legs: false) with { Torso = Torso with { Rotation = 30 } });

        // Turned clockwise, the neck socket swings right and the head goes with it.
        Assert.True(Joint(turned.Layout, HumanoidBone.Neck).X > Joint(upright.Layout, HumanoidBone.Neck).X + 0.05);
        Assert.True(Bounds(Shape(turned, FigureLayerKind.Head)).MidX > 0.05);
        Assert.True(Joint(turned.Layout, HumanoidBone.LeftUpperArm).Y > Joint(turned.Layout, HumanoidBone.RightUpperArm).Y);
    }

    [Fact]
    public void A_nudge_moves_a_part_and_what_sits_on_it_but_not_what_it_stands_on()
    {
        var plain = ModularRig.Build(Creature());
        var nudged = ModularRig.Build(Creature() with { Torso = Torso with { Offset = new Point2D(0, -0.05) } });

        Assert.Equal(Joint(plain.Layout, HumanoidBone.LeftUpperLeg), Joint(nudged.Layout, HumanoidBone.LeftUpperLeg));
        Assert.Equal(Bounds(Shape(plain, FigureLayerKind.Head)).Top - 0.05, Bounds(Shape(nudged, FigureLayerKind.Head)).Top, 9);
        Assert.Equal(Joint(plain.Layout, HumanoidBone.LeftUpperArm).Y - 0.05, Joint(nudged.Layout, HumanoidBone.LeftUpperArm).Y, 9);
    }

    [Fact]
    public void Leaning_curves_the_torso_outline_and_tilting_turns_the_head_rigidly()
    {
        var body = Creature();
        var rest = ModularRig.Build(body);
        var lean = ModularRig.Build(body, ViewAngle.Front, new PoseData(ViewAngle.Front,
            [new BoneRotation(HumanoidBone.Spine, 20), new BoneRotation(HumanoidBone.Chest, 20), new BoneRotation(HumanoidBone.Head, 25)],
            new SortedDictionary<string, string>()));

        var restTorso = Shape(rest, FigureLayerKind.Torso).Anchors;
        var leanTorso = Shape(lean, FigureLayerKind.Torso).Anchors;
        Assert.Equal(restTorso.Count, leanTorso.Count);
        // The top of the torso swings right; its bottom, below the hips, stays.
        Assert.True(leanTorso[0].Point.X > restTorso[0].Point.X + 0.05);
        Assert.Equal(restTorso[4].Point, leanTorso[4].Point);
        Assert.Equal(rest.Regions.Torso.RestOutline, lean.Regions.Torso.RestOutline);
        Assert.Equal(65, lean.Blobs[0].RotationDegrees, 6);
        Assert.Equal(65, lean.Regions.Head.RotationDegrees, 6);
        // Rigid: the head's points keep their distances.
        var (a, b) = (Shape(lean, FigureLayerKind.Head).Anchors[0].Point, Shape(lean, FigureLayerKind.Head).Anchors[2].Point);
        Assert.Equal(0.3, Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)), 9);
    }

    [Fact]
    public void Limb_styles_draw_as_capsules_pieces_or_sticks_and_ends_sit_at_the_wrist()
    {
        var piece = ModuleShapes.RoundedRectangle(0.5);
        var pieces = ModularRig.Build(Creature() with { Arms = Arms with { Style = LimbStyle.Pieces, Upper = piece, Lower = piece, End = ModuleShapes.Triangle(), EndSize = 0.1 } });
        var arm = pieces.Layers.Single(l => l.Kind == FigureLayerKind.LeftArm);
        Assert.Empty(arm.Capsules);
        Assert.Equal(3, arm.Shapes!.Count);
        var wrist = Joint(pieces.Layout, HumanoidBone.LeftHand);
        var hand = Bounds(arm.Shapes[2]);
        Assert.True(Inside(hand, wrist, 1e-6));
        Assert.Equal(0.1, pieces.Regions.LeftHand.RadiusY * 2, 9);
        // A piece is the limb's thickness wide and its bone's length long, top centre on the bone's root.
        var blocks = ModularRig.Build(Creature() with { Arms = Arms with { Style = LimbStyle.Pieces, Upper = ModuleShapes.RoundedRectangle(0) } });
        var block = blocks.Layers.Single(l => l.Kind == FigureLayerKind.LeftArm).Shapes![0].Anchors.Select(a => a.Point).ToList();
        double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        Assert.Equal(Arms.Thickness, Distance(block[0], block[1]), 9);
        Assert.Equal(Arms.Length / 2, Distance(block[1], block[2]), 9);
        var root = Joint(blocks.Layout, HumanoidBone.LeftUpperArm);
        Assert.Equal(0, Distance(root, new Point2D((block[0].X + block[1].X) / 2, (block[0].Y + block[1].Y) / 2)), 9);

        var stick = ModularRig.Build(Creature() with { Arms = Arms with { Style = LimbStyle.Stick } });
        Assert.All(stick.Layers.Single(l => l.Kind == FigureLayerKind.RightArm).Capsules, c => Assert.Equal(ModularRig.StickRadius, c.FromRadius));

        var taper = ModularRig.Build(Creature() with { Legs = Legs with { Style = LimbStyle.Taper, Taper = 0.5 } });
        var shin = taper.Regions.LeftLeg.Lower;
        Assert.Equal(0.02, shin.ToRadius, 9);
        Assert.Equal(0, taper.Extent.Bottom, 9);
    }

    [Theory]
    [InlineData(ViewAngle.Front)]
    [InlineData(ViewAngle.Profile)]
    public void An_end_shape_hangs_in_an_end_size_box_from_the_ankle_and_the_ground_goes_under_what_is_drawn(ViewAngle angle)
    {
        // A shoe: the box's full width, only its top 0.44.
        var shoe = new ModuleShape(AnchorRing.Ellipse(new Rect2D(0, 0, 1, 0.44)));
        var figure = ModularRig.Build(Creature() with { Legs = Legs with { End = shoe, EndSize = 0.2 } }, angle);

        var ankle = Joint(figure.Layout, HumanoidBone.RightFoot);
        var foot = figure.Regions.RightFoot;
        Assert.Equal(ankle.X, foot.Center.X, 9);
        Assert.Equal(ankle.Y + 0.044, foot.Center.Y, 9);
        Assert.Equal(0.1, foot.RadiusX, 9);
        Assert.Equal(0.044, foot.RadiusY, 9);
        Assert.Equal(0, figure.Extent.Bottom, 9);
        Assert.Equal(-0.088, ankle.Y, 9);
    }

    [Fact]
    public void A_limb_of_its_own_colour_is_drawn_as_outlines_in_that_colour()
    {
        var figure = ModularRig.Build(Creature() with { Legs = Legs with { Color = "trousers" } });
        var leg = figure.Layers.Single(l => l.Kind == FigureLayerKind.LeftLeg);

        Assert.Empty(leg.Capsules);
        Assert.Empty(leg.Ellipses);
        Assert.Equal(3, leg.Shapes!.Count);
        Assert.All(leg.Shapes, s => Assert.Equal("trousers", s.ColorSlot));
        Assert.Equal(0, figure.Extent.Bottom, 6);
        // The capsule outline is the capsule: its bounds are the two circles'.
        var thigh = figure.Regions.LeftLeg.Upper;
        var bounds = Bounds(leg.Shapes[0]);
        Assert.Equal(Math.Min(thigh.From.Y - thigh.FromRadius, thigh.To.Y - thigh.ToRadius), bounds.Top, 6);
        Assert.Equal(thigh.From.X + thigh.FromRadius, bounds.Right, 6);
    }

    [Fact]
    public void Regions_follow_the_parts()
    {
        var figure = ModularRig.Build(Creature());

        Assert.Equal(Joint(figure.Layout, HumanoidBone.LeftUpperArm), figure.Regions.LeftArm.Upper.From);
        Assert.Equal(Joint(figure.Layout, HumanoidBone.LeftHand), figure.Regions.LeftArm.Lower.To);
        Assert.Equal(Arms.Length, figure.Regions.LeftArm.Length, 9);
        Assert.Equal(Legs.Length, figure.Regions.RightLeg.Length, 9);
        var torso = Bounds(Shape(figure, FigureLayerKind.Torso));
        Assert.Equal(torso.Top, figure.Regions.Torso.Top, 3);
        Assert.Equal(torso.Bottom, figure.Regions.Torso.Bottom, 3);
        var (left, right) = figure.Regions.Torso.RowAt(torso.MidY);
        Assert.Equal(torso.Left, left, 3);
        Assert.Equal(torso.Right, right, 3);
    }
}
