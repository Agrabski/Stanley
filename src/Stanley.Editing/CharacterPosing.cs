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

/// <summary>A part of the trunk that can be dragged: the hips (crouch, sit, shift weight - feet stay planted), the chest (lean) and the head (tilt).</summary>
public enum TrunkPart
{
    Hips,
    Chest,
    Head
}

/// <summary>
/// Posing by dragging: grab a hand or foot and the arm or leg follows (two-bone inverse
/// kinematics); grab the hips and the body moves while the feet stay planted; grab the
/// chest to lean, the head to tilt. Nobody has to rotate individual bones. Everything is
/// stored as plain pose data - bone rotations relative to the parent plus a hips shift
/// as a fraction of the character's height - so a pose survives body edits (a taller or
/// heavier character keeps the same gesture) and moves between characters.
/// </summary>
public static class CharacterPosing
{
    /// <summary>A figure with the instance's pose applied (the shapes a renderer draws).</summary>
    public static BodyFigure Figure(CharacterDefinition character, CharacterInstance instance) =>
        BodyRig.Build(character.Body, instance.Pose.ViewAngle, character.Skeleton, instance.Pose);

    public static (HumanoidBone Root, HumanoidBone Middle, HumanoidBone End) Chain(Limb limb) => BodyRig.LimbChains[(int)limb];

    /// <summary>Where the limb's hand or foot joint (wrist, ankle) is on the page - the drag handle.</summary>
    public static Point2D EndPoint(CharacterDefinition character, CharacterInstance instance, Limb limb) =>
        instance.Placement.ToPage(Joint(Figure(character, instance).Layout, Chain(limb).End));

    /// <summary>Where the limb's elbow or knee joint is on the page - a second drag handle that swings the upper arm or thigh.</summary>
    public static Point2D BendPoint(CharacterDefinition character, CharacterInstance instance, Limb limb) =>
        instance.Placement.ToPage(Joint(Figure(character, instance).Layout, Chain(limb).Middle));

    /// <summary>Where a trunk handle is on the page: the hips joint, the base of the neck, the top of the head.</summary>
    public static Point2D TrunkPoint(CharacterDefinition character, CharacterInstance instance, TrunkPart part)
    {
        var figure = Figure(character, instance);
        return instance.Placement.ToPage(part switch
        {
            TrunkPart.Hips => Joint(figure.Layout, HumanoidBone.Hips),
            TrunkPart.Chest => Joint(figure.Layout, HumanoidBone.Neck),
            _ => HeadTop(figure)
        });
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
            return IsLeg(limb) ? 1 : -1; // figure faces +x: knee to +x, elbow to -x

        var figure = Figure(character, instance);
        var (root, middle, end) = Chain(limb);
        var sign = Side(Joint(figure.Layout, root), Joint(figure.Layout, end), Joint(figure.Layout, middle));
        if (sign != 0)
            return sign;
        // Dead straight: bend the way the limb does at rest.
        sign = Side(Joint(figure.BaseLayout, root), Joint(figure.BaseLayout, end), Joint(figure.BaseLayout, middle));
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
        var solved = Solve(character, instance, limb, instance.Placement.ToFigure(pageTarget), bendSign);
        return solved is { } angles ? WithLimb(instance, limb, angles.Upper, angles.Lower) : instance;
    }

    /// <summary>
    /// Drags the limb's elbow or knee handle: the upper arm or thigh swings about the
    /// shoulder or hip so the joint follows <paramref name="pageTarget"/> (landing on it
    /// when it's at the bone's length, pointing at it otherwise - the same reach-towards
    /// behaviour as a hand/foot drag, one bone shorter). The forearm or shin keeps its bend,
    /// so the hand or foot rides along. Only the limb's upper rotation changes.
    /// </summary>
    public static CharacterInstance Bend(CharacterDefinition character, CharacterInstance instance, Limb limb, Point2D pageTarget)
    {
        var reference = Figure(character, instance).BaseLayout;
        var (root, middle, _) = Chain(limb);
        var s = Joint(reference, root);
        var e0 = Joint(reference, middle);
        var target = instance.Placement.ToFigure(pageTarget);
        if (Distance(s, e0) < 1e-9 || Distance(s, target) < 1e-9)
            return instance;
        var lower = instance.Pose.BoneRotations.FirstOrDefault(r => r.Bone == middle)?.Degrees ?? 0;
        return WithLimb(instance, limb, Wrap(AngleOf(s, target) - AngleOf(s, e0)), lower);
    }

    /// <summary>
    /// Moves the hips by <paramref name="pageDelta"/> from where they are in
    /// <paramref name="start"/> (the drag's starting pose) while both feet stay exactly
    /// where they were: the legs bend (crouch, sit) or straighten to keep them planted;
    /// the upper body, arms included, rides along. The hips can't rise past the point the
    /// legs are straight.
    /// </summary>
    public static CharacterInstance MoveHips(CharacterDefinition character, CharacterInstance start, Point2D pageDelta)
    {
        var height = Math.Max(character.Body.Normalized().Height, 1e-6);
        var unit = Math.Max(start.Placement.UnitHeightMm, 1e-6);
        var delta = new Point2D((start.Placement.Mirrored ? -pageDelta.X : pageDelta.X) / unit / height, pageDelta.Y / unit / height);
        var from = start.Pose.HipsShift ?? default;
        var range = BodyRig.HipsShiftRange;
        var shift = new Point2D(Math.Clamp(from.X + delta.X, range.Left, range.Right),
            Math.Clamp(from.Y + delta.Y, range.Top, Math.Min(range.Bottom, MaxHipsDrop(character, start))));

        var pins = new[] { Limb.LeftLeg, Limb.RightLeg }
            .Select(leg => (Leg: leg, Foot: EndPoint(character, start, leg), Bend: BendSign(character, start, leg)))
            .ToList();
        var moved = start with { Pose = start.Pose with { HipsShift = shift } };
        foreach (var (leg, foot, bend) in pins)
            moved = Reach(character, moved, leg, foot, bend);
        return moved;
    }

    /// <summary>Leans the upper body about the hips so the chest handle points at <paramref name="pageTarget"/> (limited to <see cref="BodyRig.MaxLean"/>). Legs and feet don't move.</summary>
    public static CharacterInstance Lean(CharacterDefinition character, CharacterInstance instance, Point2D pageTarget)
    {
        var upright = Figure(character, WithRotation(instance, HumanoidBone.Spine, 0));
        var hips = Joint(upright.Layout, HumanoidBone.Hips);
        var neck = Joint(upright.Layout, HumanoidBone.Neck);
        var target = instance.Placement.ToFigure(pageTarget);
        var lean = Wrap(AngleOf(hips, target) - AngleOf(hips, neck));
        return WithRotation(instance, HumanoidBone.Spine, Math.Clamp(lean, -BodyRig.MaxLean, BodyRig.MaxLean));
    }

    /// <summary>Tilts the head about the neck so it points at <paramref name="pageTarget"/> (limited to <see cref="BodyRig.MaxHeadTilt"/>).</summary>
    public static CharacterInstance TiltHead(CharacterDefinition character, CharacterInstance instance, Point2D pageTarget)
    {
        var level = Figure(character, WithRotation(instance, HumanoidBone.Head, 0));
        var neck = Joint(level.Layout, HumanoidBone.Head);
        var target = instance.Placement.ToFigure(pageTarget);
        var tilt = Wrap(AngleOf(neck, target) - AngleOf(neck, HeadTop(level)));
        return WithRotation(instance, HumanoidBone.Head, Math.Clamp(tilt, -BodyRig.MaxHeadTilt, BodyRig.MaxHeadTilt));
    }

    /// <summary>
    /// The same pose the other way round: in a front view left and right swap (a wave with
    /// the right hand becomes a wave with the left) and leans and tilts reverse; in a side
    /// view the near and far limbs swap. The character stays where it is, facing the same way.
    /// </summary>
    public static CharacterInstance MirrorPose(CharacterInstance instance)
    {
        var front = instance.Pose.ViewAngle != ViewAngle.Profile;
        var sign = front ? -1 : 1;
        var swap = new Dictionary<HumanoidBone, HumanoidBone>();
        for (var i = 0; i < BodyRig.LimbChains.Count; i += 2)
        {
            var (a, b) = (BodyRig.LimbChains[i], BodyRig.LimbChains[i + 1]);
            swap[a.Root] = b.Root;
            swap[b.Root] = a.Root;
            swap[a.Middle] = b.Middle;
            swap[b.Middle] = a.Middle;
        }
        var rotations = instance.Pose.BoneRotations
            .Select(r => swap.TryGetValue(r.Bone, out var other)
                ? new BoneRotation(other, r.Degrees * sign)
                : new BoneRotation(r.Bone, r.Degrees * sign))
            .ToList();
        var shift = instance.Pose.HipsShift is { } s ? new Point2D(s.X * sign, s.Y) : (Point2D?)null;
        return instance with { Pose = instance.Pose with { BoneRotations = rotations, HipsShift = shift } };
    }

    /// <summary>Back to standing at rest (the view is kept).</summary>
    public static CharacterInstance ResetPose(CharacterInstance instance) =>
        instance.Pose.BoneRotations.Count == 0 && instance.Pose.HipsShift is null
            ? instance
            : instance with { Pose = instance.Pose with { BoneRotations = [], HipsShift = null } };

    /// <summary>
    /// How far the hips can come down (a fraction of height, as <see cref="PoseData.HipsShift"/>
    /// has it) before the seat would go through the floor: about half the leg's length, so a
    /// chibi's short legs crouch less far than a heroic figure's long ones.
    /// </summary>
    public static double MaxHipsDrop(CharacterDefinition character, CharacterInstance instance)
    {
        var standing = instance with { Pose = instance.Pose with { BoneRotations = [], HipsShift = null } };
        var (_, legLength) = LimbFrame(character, standing, Limb.LeftLeg);
        return 0.55 * legLength / Math.Max(character.Body.Normalized().Height, 1e-6);
    }

    public static bool IsPosed(PoseData pose) => pose.BoneRotations.Count > 0 || pose.HipsShift is not null;

    // ---------------------------------------------------------------- building blocks (also used by PosePresets)

    internal static bool IsLeg(Limb limb) => limb is Limb.LeftLeg or Limb.RightLeg;

    /// <summary>Two-bone IK in figure space, measured from the pose's trunk (so arms keep their gesture relative to a leaning chest): the limb's two rotations (degrees), or null for a degenerate limb.</summary>
    internal static (double Upper, double Lower)? Solve(CharacterDefinition character, CharacterInstance instance, Limb limb, Point2D target, int bendSign)
    {
        var reference = Figure(character, instance).BaseLayout;
        var (root, middle, end) = Chain(limb);
        var s = Joint(reference, root);
        var e0 = Joint(reference, middle);
        var w0 = Joint(reference, end);
        var upper = Distance(s, e0);
        var lower = Distance(e0, w0);
        if (upper < 1e-9 || lower < 1e-9)
            return null;

        var toTarget = new Point2D(target.X - s.X, target.Y - s.Y);
        var reach = Math.Clamp(Math.Sqrt(toTarget.X * toTarget.X + toTarget.Y * toTarget.Y),
            Math.Abs(upper - lower) + 1e-6, upper + lower - 1e-6);
        var baseAngle = Math.Atan2(toTarget.Y, toTarget.X);
        // Law of cosines: the angle at the shoulder/hip between the target line and the upper bone.
        var cosAtRoot = Math.Clamp((upper * upper + reach * reach - lower * lower) / (2 * upper * reach), -1, 1);
        var upperAngle = baseAngle - Math.Sign(bendSign == 0 ? 1 : bendSign) * Math.Acos(cosAtRoot);
        var elbow = new Point2D(s.X + Math.Cos(upperAngle) * upper, s.Y + Math.Sin(upperAngle) * upper);
        var endPoint = new Point2D(s.X + Math.Cos(baseAngle) * reach, s.Y + Math.Sin(baseAngle) * reach);

        var a1 = Wrap((upperAngle - Math.Atan2(e0.Y - s.Y, e0.X - s.X)) * 180 / Math.PI);
        var a2 = Wrap((Math.Atan2(endPoint.Y - elbow.Y, endPoint.X - elbow.X) - Math.Atan2(w0.Y - e0.Y, w0.X - e0.X)) * 180 / Math.PI - a1);
        return (a1, a2);
    }

    /// <summary>The limb's root joint and full length (upper + lower bone) in figure space, as the pose's trunk has it.</summary>
    internal static (Point2D Root, double Length) LimbFrame(CharacterDefinition character, CharacterInstance instance, Limb limb)
    {
        var reference = Figure(character, instance).BaseLayout;
        var (root, middle, end) = Chain(limb);
        return (Joint(reference, root), Distance(Joint(reference, root), Joint(reference, middle)) + Distance(Joint(reference, middle), Joint(reference, end)));
    }

    internal static CharacterInstance WithLimb(CharacterInstance instance, Limb limb, double upper, double lower)
    {
        var (root, middle, _) = Chain(limb);
        var rotations = instance.Pose.BoneRotations
            .Where(r => r.Bone != root && r.Bone != middle)
            .Append(new BoneRotation(root, Math.Round(upper, 2)))
            .Append(new BoneRotation(middle, Math.Round(lower, 2)))
            .ToList();
        return instance with { Pose = instance.Pose with { BoneRotations = rotations } };
    }

    internal static CharacterInstance WithRotation(CharacterInstance instance, HumanoidBone bone, double degrees)
    {
        var rotations = instance.Pose.BoneRotations.Where(r => r.Bone != bone).ToList();
        if (Math.Abs(degrees) > 1e-9)
            rotations.Add(new BoneRotation(bone, Math.Round(degrees, 2)));
        return instance with { Pose = instance.Pose with { BoneRotations = rotations } };
    }

    internal static Point2D Joint(ViewAngleRestLayout layout, HumanoidBone bone) => layout.Bones.First(b => b.Bone == bone).Position;

    /// <summary>The top of the head: the head ellipse's far end from the neck.</summary>
    private static Point2D HeadTop(BodyFigure figure)
    {
        var head = figure.Blobs[0];
        var r = head.RotationDegrees * Math.PI / 180;
        return new Point2D(head.Center.X + Math.Sin(r) * head.RadiusY, head.Center.Y - Math.Cos(r) * head.RadiusY);
    }

    private static int Side(Point2D from, Point2D to, Point2D p)
    {
        var cross = (to.X - from.X) * (p.Y - from.Y) - (to.Y - from.Y) * (p.X - from.X);
        return Math.Abs(cross) < 1e-12 ? 0 : Math.Sign(cross);
    }

    private static double AngleOf(Point2D from, Point2D to) => Math.Atan2(to.Y - from.Y, to.X - from.X) * 180 / Math.PI;

    private static double Distance(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double Wrap(double degrees) => Math.IEEERemainder(degrees, 360);
}
