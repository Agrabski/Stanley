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
        var pose = new PoseData(ViewAngle.Front, [new BoneRotation(HumanoidBone.Spine, 20)], [], new Point2D(0.05, 0.1));
        var figure = BodyRig.Build(BodyShape.Default, ViewAngle.Profile, null, pose);
        var frame = figure.Regions.Torso;

        Assert.Equal(20, frame.LeanDegrees, 9);
        for (var i = 0; i < figure.Torso.Count; i++)
        {
            var posed = frame.ToFigure(frame.RestOutline[i]);
            Assert.Equal(figure.Torso[i].X, posed.X, 9);
            Assert.Equal(figure.Torso[i].Y, posed.Y, 9);
        }
    }

    [Fact]
    public void A_torso_row_spans_the_outline_and_inflating_it_grows_every_row()
    {
        var frame = BodyRig.Build(BodyShape.Default).Regions.Torso;
        var middle = (frame.Top + frame.Bottom) / 2;

        var (left, right) = frame.RowAt(middle);
        Assert.True(left < 0 && right > 0);
        Assert.Equal(-left, right, 9); // a front view is symmetric

        var grown = new TorsoFrame(frame.Inflated(0.02), frame.Pivot, frame.LeanDegrees, frame.Shift);
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
