using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editing;

/// <summary>A limb that can be posed by dragging its hand or foot.</summary>
public enum Limb
{
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg
}

/// <summary>
/// Posing by dragging: grab a hand or foot and the arm or leg follows (two-bone inverse
/// kinematics), so nobody has to rotate individual bones. The result is stored as plain
/// bone rotations in the instance's <see cref="PoseData"/> - relative to the parent and
/// measured from the character's rest layout - so a pose survives body edits (a taller
/// or heavier character keeps the same gesture) and could later move between characters.
///
/// The torso doesn't move yet: hips stay put, so feet stay planted unless dragged.
/// </summary>
public static class CharacterPosing
{
    /// <summary>A figure with the instance's pose applied (the shapes a renderer draws).</summary>
    public static BodyFigure Figure(CharacterDefinition character, CharacterInstance instance) =>
        BodyRig.Build(character.Body, instance.Pose.ViewAngle, character.Skeleton, instance.Pose.BoneRotations);

    public static (HumanoidBone Root, HumanoidBone Middle, HumanoidBone End) Chain(Limb limb) => BodyRig.LimbChains[(int)limb];

    /// <summary>Where the limb's hand or foot joint (wrist, ankle) is on the page - the drag handle.</summary>
    public static Point2D EndPoint(CharacterDefinition character, CharacterInstance instance, Limb limb)
    {
        var figure = Figure(character, instance);
        return instance.Placement.ToPage(Joint(figure.Layout, Chain(limb).End));
    }

    /// <summary>
    /// Which way the elbow or knee bends: +1 or -1 (the side of the root-to-end line the
    /// middle joint is on, in figure space). Read once when a drag starts and held for the
    /// whole drag, so the joint never snaps inside-out as the limb passes straight. In a
    /// side view it's anatomical - knees bend forward, elbows back.
    /// </summary>
    public static int BendSign(CharacterDefinition character, CharacterInstance instance, Limb limb)
    {
        if (instance.Pose.ViewAngle == ViewAngle.Profile)
            return limb is Limb.LeftLeg or Limb.RightLeg ? 1 : -1; // figure faces +x: knee to +x, elbow to -x

        var layout = Figure(character, instance).Layout;
        var (root, middle, end) = Chain(limb);
        var sign = Side(Joint(layout, root), Joint(layout, end), Joint(layout, middle));
        if (sign != 0)
            return sign;
        // Dead straight: bend the way the rest pose does.
        var rest = Figure(character, instance with { Pose = instance.Pose with { BoneRotations = [] } }).RestLayout;
        sign = Side(Joint(rest, root), Joint(rest, end), Joint(rest, middle));
        return sign != 0 ? sign : 1;
    }

    /// <summary>
    /// Reaches the limb's hand or foot towards <paramref name="pageTarget"/>: the elbow or
    /// knee bends (on the <paramref name="bendSign"/> side) so the end lands on the target,
    /// or the limb points straight at it when it's out of reach. Only that limb's two
    /// rotations change; the rest of the pose is kept.
    /// </summary>
    public static CharacterInstance Reach(CharacterDefinition character, CharacterInstance instance, Limb limb, Point2D pageTarget, int bendSign)
    {
        var rest = Figure(character, instance with { Pose = instance.Pose with { BoneRotations = [] } }).RestLayout;
        var (root, middle, end) = Chain(limb);
        var s = Joint(rest, root);
        var e0 = Joint(rest, middle);
        var w0 = Joint(rest, end);
        var upper = Distance(s, e0);
        var lower = Distance(e0, w0);
        if (upper < 1e-9 || lower < 1e-9)
            return instance;

        var target = instance.Placement.ToFigure(pageTarget);
        var toTarget = new Point2D(target.X - s.X, target.Y - s.Y);
        var reach = Math.Clamp(Math.Sqrt(toTarget.X * toTarget.X + toTarget.Y * toTarget.Y),
            Math.Abs(upper - lower) + 1e-6, upper + lower - 1e-6);
        var baseAngle = Math.Atan2(toTarget.Y, toTarget.X);
        // Law of cosines: the angle at the shoulder/hip between the target line and the upper bone.
        var cosAtRoot = Math.Clamp((upper * upper + reach * reach - lower * lower) / (2 * upper * reach), -1, 1);
        var upperAngle = baseAngle - Math.Sign(bendSign == 0 ? 1 : bendSign) * Math.Acos(cosAtRoot);
        var elbow = new Point2D(s.X + Math.Cos(upperAngle) * upper, s.Y + Math.Sin(upperAngle) * upper);
        var endPoint = new Point2D(s.X + Math.Cos(baseAngle) * reach, s.Y + Math.Sin(baseAngle) * reach);

        var restUpper = Math.Atan2(e0.Y - s.Y, e0.X - s.X);
        var restLower = Math.Atan2(w0.Y - e0.Y, w0.X - e0.X);
        var a1 = Normalize(upperAngle - restUpper);
        var a2 = Normalize(Math.Atan2(endPoint.Y - elbow.Y, endPoint.X - elbow.X) - restLower - a1);

        var rotations = instance.Pose.BoneRotations
            .Where(r => r.Bone != root && r.Bone != middle)
            .Append(new BoneRotation(root, Math.Round(a1 * 180 / Math.PI, 2)))
            .Append(new BoneRotation(middle, Math.Round(a2 * 180 / Math.PI, 2)))
            .ToList();
        return instance with { Pose = instance.Pose with { BoneRotations = rotations } };
    }

    /// <summary>Back to standing at rest (the view is kept).</summary>
    public static CharacterInstance ResetPose(CharacterInstance instance) =>
        instance.Pose.BoneRotations.Count == 0 ? instance : instance with { Pose = instance.Pose with { BoneRotations = [] } };

    private static Point2D Joint(ViewAngleRestLayout layout, HumanoidBone bone) => layout.Bones.First(b => b.Bone == bone).Position;

    private static int Side(Point2D from, Point2D to, Point2D p)
    {
        var cross = (to.X - from.X) * (p.Y - from.Y) - (to.Y - from.Y) * (p.X - from.X);
        return Math.Abs(cross) < 1e-12 ? 0 : Math.Sign(cross);
    }

    private static double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Normalize(double radians) => Math.IEEERemainder(radians, 2 * Math.PI);
}
