using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Poses;

namespace Stanley.ProjectModel.Tests;

public class FigureLayerTests
{
    [Fact]
    public void A_front_view_paints_legs_then_torso_then_head_then_arms_and_every_shape_is_in_exactly_one_layer()
    {
        var figure = BodyRig.Build(BodyShape.Default, ViewAngle.Front);

        Assert.Equal(
            [FigureLayerKind.Back, FigureLayerKind.Legs, FigureLayerKind.Torso, FigureLayerKind.Head, FigureLayerKind.Arms, FigureLayerKind.Front],
            figure.Layers.Select(l => l.Kind));
        Assert.Equal(figure.Limbs.Count + figure.NearLimbs.Count, figure.Layers.Sum(l => l.Capsules.Count));
        Assert.Equal(figure.Blobs.Count + figure.NearBlobs.Count, figure.Layers.Sum(l => l.Ellipses.Count));
        Assert.Same(figure.Torso, figure.Layers.Single(l => l.Torso != null).Torso);
        Assert.False(figure.Layers[0].HasBody);
        Assert.False(figure.Layers[^1].HasBody);
    }

    [Fact]
    public void A_side_view_paints_the_far_arm_first_and_the_near_right_arm_last()
    {
        var figure = BodyRig.Build(BodyShape.Default, ViewAngle.Profile);

        Assert.Equal(
            [FigureLayerKind.Back, FigureLayerKind.FarArm, FigureLayerKind.Body, FigureLayerKind.Head, FigureLayerKind.NearFoot, FigureLayerKind.NearArm, FigureLayerKind.Front],
            figure.Layers.Select(l => l.Kind));
        var nearArm = figure.Layers.Single(l => l.Kind == FigureLayerKind.NearArm);
        Assert.Equal(figure.Regions.RightArm.Upper, nearArm.Capsules[0]);
        Assert.Equal(figure.Regions.LeftArm.Upper, figure.Layers.Single(l => l.Kind == FigureLayerKind.FarArm).Capsules[0]);
        Assert.Equal(FigureLayerKind.NearArm, figure.LayerOf(BodyRegion.Hand, LimbSide.Right));
        Assert.Equal(FigureLayerKind.FarArm, figure.LayerOf(BodyRegion.Arm, LimbSide.Left));
        Assert.Equal(FigureLayerKind.NearFoot, figure.LayerOf(BodyRegion.Foot, LimbSide.Right));
        Assert.Equal(FigureLayerKind.Body, figure.LayerOf(BodyRegion.Leg, LimbSide.Right));
    }

    [Theory]
    [InlineData(ViewAngle.Front)]
    [InlineData(ViewAngle.Profile)]
    public void Joints_that_join_a_layer_to_the_ones_behind_it_carry_seams(ViewAngle angle)
    {
        var figure = BodyRig.Build(BodyShape.Default, angle);
        Point2D Joint(HumanoidBone bone) => figure.Layout.Bones.Single(b => b.Bone == bone).Position;

        var arms = figure.Layers.Single(l => l.Kind == (angle == ViewAngle.Front ? FigureLayerKind.Arms : FigureLayerKind.NearArm));
        Assert.Contains(arms.Seams, s => s.Center == Joint(HumanoidBone.RightUpperArm));
        Assert.Contains(figure.Layers.Single(l => l.Kind == FigureLayerKind.Head).Seams, s => s.Center == Joint(HumanoidBone.Head));
        if (angle == ViewAngle.Front)
            Assert.Equal(2, figure.Layers.Single(l => l.Kind == FigureLayerKind.Torso).Seams.Count); // both hips
    }

    [Fact]
    public void The_regions_are_the_shapes_the_figure_draws()
    {
        var figure = BodyRig.Build(BodyShape.Default, ViewAngle.Front);
        var regions = figure.Regions;

        Assert.Equal(figure.Blobs[0], regions.Head);
        Assert.Equal(figure.Limbs[0], regions.Neck);
        Assert.Contains(regions.LeftArm.Upper, figure.Limbs);
        Assert.Contains(regions.RightLeg.Lower, figure.Limbs);
        Assert.Contains(regions.LeftHand, figure.Blobs);
        Assert.Contains(regions.RightFoot, figure.Blobs);
        Assert.Equal(regions.LeftArm.Upper.To, regions.LeftArm.Lower.From); // the elbow
    }

    [Fact]
    public void The_torso_frame_poses_its_upright_outline_into_the_drawn_torso()
    {
        var pose = new PoseData(ViewAngle.Profile,
            [new BoneRotation(HumanoidBone.Spine, 15), new BoneRotation(HumanoidBone.Chest, 10), new BoneRotation(HumanoidBone.UpperChest, 10)], [], new Point2D(0.05, 0.1));
        var figure = BodyRig.Build(BodyShape.Default, ViewAngle.Profile, null, pose);
        var frame = figure.Regions.Torso;

        for (var i = 0; i < figure.Torso.Count; i++)
        {
            var posed = frame.ToFigure(frame.RestOutline[i]);
            Assert.Equal(figure.Torso[i].X, posed.X, 9);
            Assert.Equal(figure.Torso[i].Y, posed.Y, 9);
        }
        Assert.Equal(35, frame.Bend.AngleAt(frame.Top), 9); // the shoulders turn by the whole bend
        Assert.Equal(0, frame.Bend.AngleAt(frame.Bottom), 9); // the pelvis doesn't
    }

    [Fact]
    public void A_bent_back_curves_the_torso_where_a_single_lean_would_keep_it_straight()
    {
        var body = BodyShape.Default;
        var lean = BodyRig.Build(body, ViewAngle.Profile, null, new PoseData(ViewAngle.Profile, [new BoneRotation(HumanoidBone.Spine, 36)], []));
        var bend = BodyRig.Build(body, ViewAngle.Profile, null,
            new PoseData(ViewAngle.Profile, [new BoneRotation(HumanoidBone.Spine, 12), new BoneRotation(HumanoidBone.Chest, 12), new BoneRotation(HumanoidBone.UpperChest, 12)], []));
        Point2D Joint(BodyFigure f, HumanoidBone b) => f.Layout.Bones.Single(x => x.Bone == b).Position;

        // A lean turns the spine as it stood; a bend curls it.
        static double Bent(Point2D a, Point2D b, Point2D c) => Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
        Assert.True(Bent(Joint(bend, HumanoidBone.Hips), Joint(bend, HumanoidBone.Chest), Joint(bend, HumanoidBone.Neck))
            > Bent(Joint(lean, HumanoidBone.Hips), Joint(lean, HumanoidBone.Chest), Joint(lean, HumanoidBone.Neck)) + 1e-3);
        // The same total turn at the top either way: the neck points the same way.
        Assert.Equal(lean.Blobs[0].RotationDegrees, bend.Blobs[0].RotationDegrees, 9);
    }

    [Fact]
    public void Bending_the_neck_carries_the_head_and_tilting_turns_it_on_top()
    {
        var bent = BodyRig.Build(BodyShape.Default, ViewAngle.Profile, null,
            new PoseData(ViewAngle.Profile, [new BoneRotation(HumanoidBone.Neck, 20), new BoneRotation(HumanoidBone.Head, 10)], []));
        var upright = BodyRig.Build(BodyShape.Default, ViewAngle.Profile);
        Point2D Joint(BodyFigure f, HumanoidBone b) => f.Layout.Bones.Single(x => x.Bone == b).Position;

        Assert.Equal(Joint(upright, HumanoidBone.Neck), Joint(bent, HumanoidBone.Neck)); // the neck's base stays
        Assert.True(Joint(bent, HumanoidBone.Head).X > Joint(upright, HumanoidBone.Head).X, "the neck bends forward");
        Assert.Equal(30, bent.Regions.Head.RotationDegrees, 9);
    }

    [Fact]
    public void A_torso_row_spans_the_outline_and_inflating_it_grows_every_row()
    {
        var frame = BodyRig.Build(BodyShape.Default).Regions.Torso;
        var middle = (frame.Top + frame.Bottom) / 2;

        var (left, right) = frame.RowAt(middle);
        Assert.True(left < 0 && right > 0);
        Assert.Equal(-left, right, 9); // a front view is symmetric

        var grown = frame with { RestOutline = frame.Inflated(0.02) };
        var (grownLeft, grownRight) = grown.RowAt(middle);
        Assert.True(grownLeft < left - 0.015 && grownRight > right + 0.015);
        Assert.True(grown.Top < frame.Top && grown.Bottom > frame.Bottom);
    }

    [Fact]
    public void A_limb_frame_finds_points_along_the_whole_chain()
    {
        var arm = BodyRig.Build(BodyShape.Default).Regions.LeftArm;

        Assert.Equal(arm.Upper.From, arm.At(0).Point);
        Assert.Equal(arm.Lower.To, arm.At(1).Point);
        var elbowT = arm.UpperLength / arm.Length;
        Assert.Same(arm.Lower, arm.At(elbowT + 0.01).Segment);
        Assert.Same(arm.Upper, arm.At(elbowT - 0.01).Segment);
    }
}
