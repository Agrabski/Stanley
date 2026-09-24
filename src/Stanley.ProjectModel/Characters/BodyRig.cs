using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Poses;

namespace Stanley.ProjectModel.Characters;

/// <summary>A tapered limb segment: two circles and everything between them.</summary>
public sealed record BodyCapsule(Point2D From, Point2D To, double FromRadius, double ToRadius);

/// <summary>An ellipse - head, hands, feet - turned <paramref name="RotationDegrees"/> clockwise about its centre.</summary>
public sealed record BodyEllipse(Point2D Center, double RadiusX, double RadiusY, double RotationDegrees = 0);

/// <summary>
/// A generated body in <b>figure space</b>: the unit is <see cref="BodyShape.Height"/>
/// (1.0 = an average adult's height), y points down, and the origin is the ground point
/// between the feet - so the top of the head is at <c>y = -Height</c>. A side view faces
/// +x (right); mirroring the placement makes it face left. <see cref="Extent"/> is the
/// exact bounding box of every shape.
/// </summary>
/// <param name="RestLayout">The joints standing at rest (after any skeleton override) - what pose rotations are measured from.</param>
/// <param name="BaseLayout">The joints with only the trunk posed (hips shifted, spine leaned, head tilted) and the limbs still at rest relative to it - what limb rotations, and so inverse kinematics, are measured from.</param>
/// <param name="Layout">The joints as posed - where the shapes are actually drawn. Equal to <paramref name="RestLayout"/> with no pose.</param>
/// <param name="Torso">A closed outline (clockwise from the neck), meant to be drawn smoothed.</param>
/// <param name="Limbs">Unioned with the torso and <paramref name="Blobs"/> into the body's one silhouette.</param>
/// <param name="NearLimbs">The parts nearest the viewer in a side view (the near arm, with <paramref name="NearBlobs"/> its hand and foot): drawn on top as their own outlined shape, so they still read against the body behind them. Empty in the front view.</param>
/// <param name="Layers">The same shapes sorted into depth layers, back to front - what the renderer paints (docs/sticker-system.md §4.2).</param>
/// <param name="Regions">Where each body region is, for attaching sticker parts.</param>
public sealed record BodyFigure(
    ViewAngle Angle,
    ViewAngleRestLayout RestLayout,
    ViewAngleRestLayout BaseLayout,
    ViewAngleRestLayout Layout,
    IReadOnlyList<Point2D> Torso,
    IReadOnlyList<BodyCapsule> Limbs,
    IReadOnlyList<BodyEllipse> Blobs,
    IReadOnlyList<BodyCapsule> NearLimbs,
    IReadOnlyList<BodyEllipse> NearBlobs,
    Rect2D Extent,
    IReadOnlyList<FigureLayer> Layers,
    FigureRegions Regions)
{
    /// <summary>
    /// The layer a region is painted in: front view - legs and feet behind the torso (and
    /// neck, and skirt), then the head, then the arms and hands in front; side view - the
    /// far arm behind everything, then the body (torso, neck, legs, far foot), the head,
    /// the near foot and the near arm. The character's right side is the near one.
    /// </summary>
    public FigureLayerKind LayerOf(BodyRegion region, LimbSide side = LimbSide.Left) =>
        Angle == ViewAngle.Profile
            ? region switch
            {
                BodyRegion.Head => FigureLayerKind.Head,
                BodyRegion.Arm or BodyRegion.Hand => side == LimbSide.Right ? FigureLayerKind.NearArm : FigureLayerKind.FarArm,
                BodyRegion.Foot => side == LimbSide.Right ? FigureLayerKind.NearFoot : FigureLayerKind.Body,
                _ => FigureLayerKind.Body
            }
            : region switch
            {
                BodyRegion.Head => FigureLayerKind.Head,
                BodyRegion.Arm or BodyRegion.Hand => FigureLayerKind.Arms,
                BodyRegion.Leg or BodyRegion.Foot => FigureLayerKind.Legs,
                _ => FigureLayerKind.Torso
            };
}

/// <summary>
/// Turns a <see cref="BodyShape"/> into a skeleton rest layout and a set of simple shapes
/// (the flat cartoon mannequin the V1 renderer draws), for a front or a side view. Pure
/// math, like <see cref="AnchorRing"/>: no stored geometry, everything recomputed from
/// the handful of body numbers, so a file only ever holds those numbers.
///
/// Both views share one set of measurements, so a character is the same height, with
/// the same head and legs, from either side: heights depend only on
/// <see cref="BodyShape.Height"/> and <see cref="BodyShape.HeadsTall"/>, while build,
/// muscle and frame change widths (front) and depths - chest, belly, seat (side). The
/// layout is a relaxed standing rest pose; a <see cref="Skeleton"/> override for that
/// view (the rig-editor escape hatch) moves individual joints, and the head, neck and
/// limbs follow them. A three-quarter view isn't generated yet: it draws as the front.
/// </summary>
public static class BodyRig
{
    public static BodyFigure Build(BodyShape body, Skeleton? overrides = null) => Build(body, ViewAngle.Front, overrides);

    /// <param name="pose">
    /// The pose (its <see cref="PoseData.ViewAngle"/> is ignored - <paramref name="angle"/> decides):
    /// <see cref="PoseData.HipsShift"/> moves the hips; a <see cref="HumanoidBone.Spine"/>
    /// rotation leans everything above them, a <see cref="HumanoidBone.Head"/> rotation
    /// tilts the head, and limb rotations turn the arms and legs (<see cref="ApplyPose"/>).
    /// Degrees, clockwise on the page, each relative to its parent. Null stands at rest.
    /// </param>
    public static BodyFigure Build(BodyShape body, ViewAngle angle, Skeleton? overrides = null, PoseData? pose = null)
    {
        var m = new Measures(body.Normalized());
        return angle == ViewAngle.Profile ? BuildProfile(m, overrides, pose) : BuildFront(m, overrides, pose);
    }

    /// <summary>How far the spine may lean, and the head tilt, either way (degrees).</summary>
    public const double MaxLean = 60;
    public const double MaxHeadTilt = 50;

    /// <summary>How far the hips may move, as a fraction of the character's height (y: up / down).</summary>
    public static Rect2D HipsShiftRange { get; } = Rect2D.FromEdges(-0.3, -0.05, 0.3, 0.45);

    /// <summary>The trunk part of a pose: the hips shifted, everything above them leaned about the hips, the head tilted.</summary>
    private sealed class Trunk
    {
        public Trunk(Measures m, ViewAngleRestLayout rest, PoseData? pose)
        {
            var rotations = pose?.BoneRotations ?? [];
            Lean = Math.Clamp(Degrees(rotations, HumanoidBone.Spine), -MaxLean, MaxLean);
            Tilt = Math.Clamp(Degrees(rotations, HumanoidBone.Head), -MaxHeadTilt, MaxHeadTilt);
            var shift = pose?.HipsShift ?? default;
            Shift = new Point2D(
                Math.Clamp(shift.X, HipsShiftRange.Left, HipsShiftRange.Right) * m.Height,
                Math.Clamp(shift.Y, HipsShiftRange.Top, HipsShiftRange.Bottom) * m.Height);
            Pivot = rest.Bones.FirstOrDefault(b => b.Bone == HumanoidBone.Hips)?.Position ?? new Point2D(0, m.HipY);
        }

        public double Lean { get; }
        public double Tilt { get; }
        public Point2D Shift { get; }
        public Point2D Pivot { get; }

        /// <summary>A point of the upper body (torso, neck, head, arms): leaned about the hips, then moved with them.</summary>
        public Point2D Upper(Point2D p) => Offset(RotateAbout(p, Pivot, Lean), Shift);

        /// <summary>A point of the legs: moved with the hips (the limb rotations do the rest).</summary>
        public Point2D Lower(Point2D p) => Offset(p, Shift);

        public ViewAngleRestLayout Apply(ViewAngleRestLayout rest) =>
            Lean == 0 && Shift == default
                ? rest
                : rest with { Bones = rest.Bones.Select(b => b with { Position = IsLeg(b.Bone) ? Lower(b.Position) : Upper(b.Position) }).ToList() };

        private static bool IsLeg(HumanoidBone bone) => LimbChains.Skip(2).Any(c => c.Root == bone || c.Middle == bone || c.End == bone);
    }

    private static double Degrees(IReadOnlyList<BoneRotation> rotations, HumanoidBone bone) =>
        rotations.LastOrDefault(r => r.Bone == bone)?.Degrees ?? 0;

    private static Point2D RotateAbout(Point2D p, Point2D pivot, double degrees) =>
        degrees == 0 ? p : Offset(pivot, Rotate(new Point2D(p.X - pivot.X, p.Y - pivot.Y), degrees));

    private static double AngleOf(Point2D from, Point2D to) => Math.Atan2(to.Y - from.Y, to.X - from.X) * 180 / Math.PI;

    /// <summary>
    /// How much a foot tips with its shin: a foot on the floor stays flat (a crouch keeps
    /// the soles down); a lifted one turns with the shin, as feet do mid-step.
    /// </summary>
    private static double FootTilt(Measures m, ViewAngleRestLayout rest, Point2D knee, Point2D ankle, HumanoidBone kneeBone, HumanoidBone ankleBone)
    {
        var restAnkle = rest.Bones.First(b => b.Bone == ankleBone).Position;
        var lifted = restAnkle.Y - ankle.Y; // figure space: y grows downwards
        if (lifted < m.Height * 0.02)
            return 0;
        var restKnee = rest.Bones.First(b => b.Bone == kneeBone).Position;
        var turn = Math.IEEERemainder(AngleOf(knee, ankle) - AngleOf(restKnee, restAnkle), 360);
        // Ease in over the first few centimetres of lift so the foot doesn't snap.
        return turn * Math.Min(1, lifted / (m.Height * 0.06));
    }

    /// <summary>The four limbs a pose moves, each a root joint, a middle joint and an end joint.</summary>
    public static IReadOnlyList<(HumanoidBone Root, HumanoidBone Middle, HumanoidBone End)> LimbChains { get; } =
    [
        (HumanoidBone.LeftUpperArm, HumanoidBone.LeftLowerArm, HumanoidBone.LeftHand),
        (HumanoidBone.RightUpperArm, HumanoidBone.RightLowerArm, HumanoidBone.RightHand),
        (HumanoidBone.LeftUpperLeg, HumanoidBone.LeftLowerLeg, HumanoidBone.LeftFoot),
        (HumanoidBone.RightUpperLeg, HumanoidBone.RightLowerLeg, HumanoidBone.RightFoot),
    ];

    /// <summary>
    /// Poses the limbs: each chain's root bone (upper arm, upper leg) turns about its own
    /// joint, carrying the rest of the limb with it, and its middle bone (forearm, shin)
    /// turns about the elbow/knee on top of that - rotations relative to the parent, as
    /// <see cref="PoseData"/> stores them. Angles are degrees, clockwise on the page, in
    /// figure space (a mirrored placement mirrors the whole pose with it). Bones outside
    /// the four limb chains aren't posable yet and are ignored.
    /// </summary>
    public static ViewAngleRestLayout ApplyPose(ViewAngleRestLayout rest, IReadOnlyList<BoneRotation>? pose)
    {
        if (pose is null || pose.Count == 0)
            return rest;

        var positions = rest.Bones.ToDictionary(b => b.Bone, b => b.Position);
        double Degrees(HumanoidBone bone) => pose.LastOrDefault(r => r.Bone == bone)?.Degrees ?? 0;
        foreach (var (root, middle, end) in LimbChains)
        {
            var (a1, a2) = (Degrees(root), Degrees(middle));
            if ((a1 == 0 && a2 == 0) || !positions.TryGetValue(root, out var s) || !positions.TryGetValue(middle, out var e0) || !positions.TryGetValue(end, out var w0))
                continue;
            var e = Offset(s, Rotate(new Point2D(e0.X - s.X, e0.Y - s.Y), a1));
            var w = Offset(e, Rotate(new Point2D(w0.X - e0.X, w0.Y - e0.Y), a1 + a2));
            positions[middle] = e;
            positions[end] = w;
        }
        return rest with { Bones = rest.Bones.Select(b => b with { Position = positions[b.Bone] }).ToList() };
    }

    private static Point2D Rotate(Point2D v, double degrees)
    {
        var r = degrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(r), Math.Sin(r));
        return new Point2D(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    /// <summary>Everything both views share: heights, lengths, radii.</summary>
    private sealed class Measures
    {
        public Measures(BodyShape b)
        {
            Height = b.Height;
            Head = Height / b.HeadsTall;
            Below = Height - Head;

            // Legs are a bigger share of the body the more heads tall a figure is: a chibi
            // is mostly head and torso, a heroic figure mostly legs.
            var legShare = 0.45 + (b.HeadsTall - BodyShape.MinHeadsTall) * (0.13 / 4.5);
            LegLength = Below * legShare;
            NeckLength = Below * 0.06;
            TorsoLength = Below - LegLength - NeckLength;

            // The unit for widths: tracks the body's length for adults, but doesn't let a
            // chibi's tiny body get stick-thin under its big head.
            W = 0.7 * Below / 6.5 + 0.3 * Head;
            (Build, Muscle, Frame) = (b.Build, b.Muscle, b.Frame - 0.5);

            ChinY = -(Height - Head);
            ShoulderY = ChinY + NeckLength;
            HipY = -LegLength;

            NeckHalf = W * (0.26 + 0.1 * Build + 0.12 * Muscle);
            ArmTop = W * (0.23 + 0.14 * Build + 0.16 * Muscle);
            ArmElbow = W * (0.16 + 0.09 * Build + 0.1 * Muscle);
            ArmWrist = W * (0.11 + 0.04 * Build + 0.03 * Muscle);
            UpperArm = Below * 0.23;
            Forearm = Below * 0.2;
            Hand = Below * 0.1;
            Thigh = W * (0.44 + 0.3 * Build + 0.14 * Muscle);
            Knee = W * (0.25 + 0.12 * Build + 0.07 * Muscle);
            Ankle = W * (0.13 + 0.04 * Build);
            AnkleHeight = Math.Max(LegLength * 0.06, Ankle); // the ankle never sinks into the floor, even on short legs
            FootHalfHeight = Math.Max(AnkleHeight * 0.75, Ankle * 0.8);
        }

        public double Height, Head, Below, LegLength, NeckLength, TorsoLength, W, Build, Muscle, Frame;
        public double ChinY, ShoulderY, HipY, NeckHalf;
        public double ArmTop, ArmElbow, ArmWrist, UpperArm, Forearm, Hand;
        public double Thigh, Knee, Ankle, AnkleHeight, FootHalfHeight;

        /// <summary>The hand, lying along the forearm (so it turns as the arm does).</summary>
        public BodyEllipse HandAt(Point2D elbow, Point2D wrist, Point2D fallbackDirection)
        {
            var direction = Normalize(new Point2D(wrist.X - elbow.X, wrist.Y - elbow.Y), fallbackDirection);
            var turn = Math.Atan2(direction.Y, direction.X) * 180 / Math.PI - 90;
            return new(Along(wrist, direction, Hand * 0.45), ArmWrist * 1.45, Hand * 0.6, turn);
        }
    }

    // ---------------------------------------------------------------- front

    private static BodyFigure BuildFront(Measures m, Skeleton? overrides, PoseData? pose)
    {
        var (w, build, muscle, frame) = (m.W, m.Build, m.Muscle, m.Frame);
        var shoulderHalf = w * (1.0 - 0.3 * frame + 0.12 * build + 0.3 * muscle);
        var chestHalf = w * (0.74 - 0.1 * frame + 0.25 * build + 0.25 * muscle);
        var waistHalf = w * (0.6 + 0.15 * frame + 0.6 * build + 0.05 * muscle);
        var hipHalf = w * (0.78 + 0.35 * frame + 0.4 * build + 0.05 * muscle);

        // Arms hang slightly out; a heavier or more muscular body holds them further out.
        var armAngle = (9 + 12 * build + 8 * muscle) * Math.PI / 180;
        var armDir = new Point2D(Math.Sin(armAngle), Math.Cos(armAngle));
        var shoulderJoint = new Point2D(shoulderHalf - m.ArmTop * 0.35, m.ShoulderY + m.ArmTop * 1.1);
        var elbow = Along(shoulderJoint, armDir, m.UpperArm);
        var wrist = Along(elbow, armDir, m.Forearm);

        var legX = Math.Max(hipHalf - m.Thigh * 0.9, m.Thigh * 0.8);
        var hipJoint = new Point2D(legX, m.HipY);
        var kneeJoint = new Point2D(legX * 0.92, m.HipY + m.LegLength * 0.5);
        var ankleJoint = new Point2D(legX * 0.88, -m.AnkleHeight);

        var bones = Spine(m, 0);
        // VRM "left" is the character's own left: on the viewer's right in a front view.
        foreach (var (side, sign) in new[] { (Side.Left, 1.0), (Side.Right, -1.0) })
        {
            bones.Add(new(side.Shoulder, new Point2D(sign * m.NeckHalf, m.ShoulderY)));
            bones.Add(new(side.UpperArm, Mirror(shoulderJoint, sign)));
            bones.Add(new(side.LowerArm, Mirror(elbow, sign)));
            bones.Add(new(side.Hand, Mirror(wrist, sign)));
            bones.Add(new(side.UpperLeg, Mirror(hipJoint, sign)));
            bones.Add(new(side.LowerLeg, Mirror(kneeJoint, sign)));
            bones.Add(new(side.Foot, Mirror(ankleJoint, sign)));
        }
        var rest = ApplyOverrides(new ViewAngleRestLayout(ViewAngle.Front, bones), overrides);
        var trunk = new Trunk(m, rest, pose);
        var baseLayout = trunk.Apply(rest);
        var layout = ApplyPose(baseLayout, pose?.BoneRotations);
        Point2D At(HumanoidBone bone) => layout.Bones.First(p => p.Bone == bone).Position;

        var restTorso = new List<Point2D>
        {
            new(m.NeckHalf, m.ShoulderY - m.NeckLength * 0.3),
            new(shoulderHalf * 0.72, m.ShoulderY + w * 0.02),
            new(shoulderHalf, m.ShoulderY + w * 0.22),
            new(chestHalf, m.ShoulderY + w * 0.9),
            new(waistHalf, m.ShoulderY + m.TorsoLength * 0.62),
            new(hipHalf, m.HipY - m.TorsoLength * 0.1),
            new(hipHalf * 0.9, m.HipY + m.Below * 0.04),
            new(0, m.HipY + m.Below * 0.05),
        };
        for (var i = restTorso.Count - 2; i >= 0; i--)
            restTorso.Add(Mirror(restTorso[i], -1));
        var torso = restTorso.Select(trunk.Upper).ToList();

        var neck = new BodyCapsule(At(HumanoidBone.Head), At(HumanoidBone.Neck), m.NeckHalf, m.NeckHalf);
        var limbs = new List<BodyCapsule> { neck };
        var headPoint = At(HumanoidBone.Head);
        var headTurn = trunk.Lean + trunk.Tilt;
        var head = new BodyEllipse(Offset(headPoint, Rotate(new Point2D(0, -m.Head / 2), headTurn)), m.Head * 0.42 * (1 + 0.1 * build), m.Head / 2, headTurn);
        var blobs = new List<BodyEllipse> { head };

        var arms = new Dictionary<Side, LimbFrame>();
        var legs = new Dictionary<Side, LimbFrame>();
        var hands = new Dictionary<Side, BodyEllipse>();
        var feet = new Dictionary<Side, BodyEllipse>();
        foreach (var side in new[] { Side.Left, Side.Right })
        {
            var (s, e, h) = (At(side.UpperArm), At(side.LowerArm), At(side.Hand));
            arms[side] = new LimbFrame(new BodyCapsule(s, e, m.ArmTop, m.ArmElbow), new BodyCapsule(e, h, m.ArmElbow, m.ArmWrist));
            limbs.Add(arms[side].Upper);
            limbs.Add(arms[side].Lower);
            blobs.Add(hands[side] = m.HandAt(e, h, armDir));

            var (hp, kp, ap) = (At(side.UpperLeg), At(side.LowerLeg), At(side.Foot));
            legs[side] = new LimbFrame(new BodyCapsule(hp, kp, m.Thigh, m.Knee), new BodyCapsule(kp, ap, m.Knee, m.Ankle));
            limbs.Add(legs[side].Upper);
            limbs.Add(legs[side].Lower);
            var outward = Math.Sign(At(side.UpperLeg).X - At(HumanoidBone.Hips).X) * w * 0.06;
            var footTurn = FootTilt(m, rest, kp, ap, side.LowerLeg, side.Foot);
            blobs.Add(feet[side] = new BodyEllipse(Offset(ap, Rotate(new Point2D(outward, m.AnkleHeight - m.FootHalfHeight), footTurn)), w * 0.26, m.FootHalfHeight, footTurn));
        }

        var both = new[] { Side.Left, Side.Right };
        var layers = new List<FigureLayer>
        {
            FigureLayer.Empty(FigureLayerKind.Back),
            new(FigureLayerKind.Legs, null, both.SelectMany(s => new[] { legs[s].Upper, legs[s].Lower }).ToList(), both.Select(s => feet[s]).ToList(), []),
            // The torso covers the tops of the thighs; at the hips its ink stops where it lies over them.
            new(FigureLayerKind.Torso, torso, [neck], [], both.Select(s => Seam(At(s.UpperLeg), m.Thigh * 1.5)).ToList()),
            new(FigureLayerKind.Head, null, [], [head], [Seam(headPoint, m.NeckHalf * 1.2)]),
            new(FigureLayerKind.Arms, null, both.SelectMany(s => new[] { arms[s].Upper, arms[s].Lower }).ToList(), both.Select(s => hands[s]).ToList(),
                both.Select(s => Seam(At(s.UpperArm), m.ArmTop * 1.4)).ToList()),
            FigureLayer.Empty(FigureLayerKind.Front),
        };
        var regions = new FigureRegions(head, neck, new TorsoFrame(restTorso, trunk.Pivot, trunk.Lean, trunk.Shift),
            arms[Side.Left], arms[Side.Right], hands[Side.Left], hands[Side.Right],
            legs[Side.Left], legs[Side.Right], feet[Side.Left], feet[Side.Right]);

        return new BodyFigure(ViewAngle.Front, rest, baseLayout, layout, torso, limbs, blobs, [], [], ExtentOf(torso, [limbs], [blobs]), layers, regions);
    }

    // ---------------------------------------------------------------- side (profile)

    /// <summary>
    /// Facing +x. The body's width becomes its depth: chest (muscle, and a "V" frame),
    /// belly (weight), seat (weight, and an "A" frame). Facing right, the character's own
    /// right side is the one nearest the viewer (as it would be for a real person), so the
    /// right arm and foot are drawn on top of the body as their own shape; the legs merge
    /// into it, the far (left) one set back a little so both feet show. A mirrored
    /// placement is a mirror image of this, not the character turned around.
    /// </summary>
    private static BodyFigure BuildProfile(Measures m, Skeleton? overrides, PoseData? pose)
    {
        var (w, build, muscle, frame) = (m.W, m.Build, m.Muscle, m.Frame);
        var chestFront = w * (0.5 + 0.15 * build + 0.3 * muscle - 0.12 * frame);
        var backDepth = w * (0.42 + 0.15 * build + 0.1 * muscle);
        var bellyFront = w * (0.36 + 0.8 * build);
        var hipFront = w * (0.4 + 0.35 * build);
        var seat = w * (0.46 + 0.3 * build + 0.25 * frame + 0.05 * muscle);

        // Arms hang straight down, a touch forward; the far arm a little behind the near.
        var armAngle = 4 * Math.PI / 180;
        var armDir = new Point2D(Math.Sin(armAngle), Math.Cos(armAngle));
        var nearShoulder = new Point2D(-w * 0.05, m.ShoulderY + m.ArmTop);
        var nearElbow = Along(nearShoulder, armDir, m.UpperArm);
        var nearWrist = Along(nearElbow, armDir, m.Forearm);
        var farOffset = new Point2D(-w * 0.14, 0);

        // Legs: the near one a touch forward, the far one a touch back, so both show.
        var nearHip = new Point2D(w * 0.05, m.HipY);
        var nearKnee = new Point2D(w * 0.12, m.HipY + m.LegLength * 0.5);
        var nearAnkle = new Point2D(w * 0.06, -m.AnkleHeight);
        var farLeg = new Point2D(-w * 0.2, 0);

        var bones = Spine(m, w * 0.02);
        bones.Add(new(HumanoidBone.RightShoulder, new Point2D(0, m.ShoulderY)));
        bones.Add(new(HumanoidBone.RightUpperArm, nearShoulder));
        bones.Add(new(HumanoidBone.RightLowerArm, nearElbow));
        bones.Add(new(HumanoidBone.RightHand, nearWrist));
        bones.Add(new(HumanoidBone.RightUpperLeg, nearHip));
        bones.Add(new(HumanoidBone.RightLowerLeg, nearKnee));
        bones.Add(new(HumanoidBone.RightFoot, nearAnkle));
        bones.Add(new(HumanoidBone.LeftShoulder, Offset(new Point2D(0, m.ShoulderY), farOffset)));
        bones.Add(new(HumanoidBone.LeftUpperArm, Offset(nearShoulder, farOffset)));
        bones.Add(new(HumanoidBone.LeftLowerArm, Offset(nearElbow, farOffset)));
        bones.Add(new(HumanoidBone.LeftHand, Offset(nearWrist, farOffset)));
        bones.Add(new(HumanoidBone.LeftUpperLeg, Offset(nearHip, farLeg)));
        bones.Add(new(HumanoidBone.LeftLowerLeg, Offset(nearKnee, farLeg)));
        bones.Add(new(HumanoidBone.LeftFoot, Offset(nearAnkle, farLeg)));
        var rest = ApplyOverrides(new ViewAngleRestLayout(ViewAngle.Profile, bones), overrides);
        var trunk = new Trunk(m, rest, pose);
        var baseLayout = trunk.Apply(rest);
        var layout = ApplyPose(baseLayout, pose?.BoneRotations);
        Point2D At(HumanoidBone bone) => layout.Bones.First(p => p.Bone == bone).Position;

        var t = m.TorsoLength;
        var restTorso = new List<Point2D>
        {
            new(m.NeckHalf * 0.9, m.ShoulderY - m.NeckLength * 0.3),
            new(chestFront * 0.8, m.ShoulderY + w * 0.15),
            new(chestFront, m.ShoulderY + w * 0.65),
            new(chestFront * 0.85 + bellyFront * 0.15, m.ShoulderY + t * 0.42),
            new(bellyFront, m.ShoulderY + t * 0.68),
            new(hipFront, m.HipY - t * 0.08),
            new(hipFront * 0.6, m.HipY + m.Below * 0.04),
            new(-seat * 0.7, m.HipY + m.Below * 0.04),
            new(-seat, m.HipY - t * 0.12),
            new(-backDepth * 0.8, m.ShoulderY + t * 0.62),
            new(-backDepth, m.ShoulderY + w * 0.55),
            new(-backDepth * 0.8, m.ShoulderY + w * 0.1),
            new(-m.NeckHalf * 0.9, m.ShoulderY - m.NeckLength * 0.3),
        };
        var torso = restTorso.Select(trunk.Upper).ToList();

        var headPoint = At(HumanoidBone.Head);
        var headTurn = trunk.Lean + trunk.Tilt;
        var headCenter = Offset(headPoint, Rotate(new Point2D(m.Head * 0.04, -m.Head / 2), headTurn));
        var headRx = m.Head * 0.47 * (1 + 0.08 * build);
        var neck = new BodyCapsule(headPoint, At(HumanoidBone.Neck), m.NeckHalf * 1.05, m.NeckHalf * 1.05);
        var limbs = new List<BodyCapsule> { neck };
        var head = new BodyEllipse(headCenter, headRx, m.Head / 2, headTurn);
        // The nose: the one detail that says which way a flat side view is facing.
        var nose = new BodyEllipse(Offset(headCenter, Rotate(new Point2D(headRx * 0.93, m.Head * 0.07), headTurn)), m.Head * 0.09, m.Head * 0.07, headTurn);
        var blobs = new List<BodyEllipse> { head, nose };
        var nearLimbs = new List<BodyCapsule>();
        var nearBlobs = new List<BodyEllipse>();

        var arms = new Dictionary<Side, LimbFrame>();
        var legs = new Dictionary<Side, LimbFrame>();
        var hands = new Dictionary<Side, BodyEllipse>();
        var feet = new Dictionary<Side, BodyEllipse>();
        var footLength = m.Below * 0.17;
        foreach (var (side, near) in new[] { (Side.Left, false), (Side.Right, true) })
        {
            var (targetLimbs, targetBlobs) = near ? (nearLimbs, nearBlobs) : (limbs, blobs);
            var (s, e, h) = (At(side.UpperArm), At(side.LowerArm), At(side.Hand));
            arms[side] = new LimbFrame(new BodyCapsule(s, e, m.ArmTop, m.ArmElbow), new BodyCapsule(e, h, m.ArmElbow, m.ArmWrist));
            targetLimbs.Add(arms[side].Upper);
            targetLimbs.Add(arms[side].Lower);
            targetBlobs.Add(hands[side] = m.HandAt(e, h, armDir));

            // Legs merge into the body (a separate near thigh reads as a bowling pin over
            // the hips); only the near foot is drawn on top, so the feet read as two.
            var (hp, kp, ap) = (At(side.UpperLeg), At(side.LowerLeg), At(side.Foot));
            legs[side] = new LimbFrame(new BodyCapsule(hp, kp, m.Thigh * 0.9, m.Knee), new BodyCapsule(kp, ap, m.Knee, m.Ankle));
            limbs.Add(legs[side].Upper);
            limbs.Add(legs[side].Lower);
            var footTurn = FootTilt(m, rest, kp, ap, side.LowerLeg, side.Foot);
            targetBlobs.Add(feet[side] = new BodyEllipse(Offset(ap, Rotate(new Point2D(footLength * 0.3, m.AnkleHeight - m.FootHalfHeight), footTurn)), footLength / 2, m.FootHalfHeight, footTurn));
        }

        var (far, nearSide) = (Side.Left, Side.Right);
        var layers = new List<FigureLayer>
        {
            FigureLayer.Empty(FigureLayerKind.Back),
            new(FigureLayerKind.FarArm, null, [arms[far].Upper, arms[far].Lower], [hands[far]], []),
            new(FigureLayerKind.Body, torso, [neck, legs[far].Upper, legs[far].Lower, legs[nearSide].Upper, legs[nearSide].Lower], [feet[far]], []),
            new(FigureLayerKind.Head, null, [], [head, nose], [Seam(headPoint, m.NeckHalf * 1.25)]),
            new(FigureLayerKind.NearFoot, null, [], [feet[nearSide]], []),
            new(FigureLayerKind.NearArm, null, [arms[nearSide].Upper, arms[nearSide].Lower], [hands[nearSide]], [Seam(At(nearSide.UpperArm), m.ArmTop * 1.4)]),
            FigureLayer.Empty(FigureLayerKind.Front),
        };
        var regions = new FigureRegions(head, neck, new TorsoFrame(restTorso, trunk.Pivot, trunk.Lean, trunk.Shift),
            arms[Side.Left], arms[Side.Right], hands[Side.Left], hands[Side.Right],
            legs[Side.Left], legs[Side.Right], feet[Side.Left], feet[Side.Right]);

        return new BodyFigure(ViewAngle.Profile, rest, baseLayout, layout, torso, limbs, blobs, nearLimbs, nearBlobs,
            ExtentOf(torso, [limbs, nearLimbs], [blobs, nearBlobs]), layers, regions);
    }

    /// <summary>The joints down the body's centre line, shared by both views.</summary>
    private static List<BoneRestPose> Spine(Measures m, double lean) =>
    [
        new(HumanoidBone.Hips, new Point2D(0, m.HipY)),
        new(HumanoidBone.Spine, new Point2D(lean * 0.3, m.HipY - m.TorsoLength * 0.3)),
        new(HumanoidBone.Chest, new Point2D(lean * 0.6, m.HipY - m.TorsoLength * 0.65)),
        new(HumanoidBone.Neck, new Point2D(lean, m.ShoulderY)),
        new(HumanoidBone.Head, new Point2D(lean, m.ChinY)),
    ];

    /// <summary>The body's bounding box in figure space (see <see cref="BodyFigure"/>).</summary>
    public static Rect2D Extent(BodyShape body, Skeleton? overrides = null) => Build(body, overrides).Extent;

    /// <summary>The body's bounding box in figure space, seen from <paramref name="angle"/>.</summary>
    public static Rect2D Extent(BodyShape body, ViewAngle angle, Skeleton? overrides = null, PoseData? pose = null) =>
        Build(body, angle, overrides, pose).Extent;

    private static ViewAngleRestLayout ApplyOverrides(ViewAngleRestLayout generated, Skeleton? overrides)
    {
        var moved = overrides?.RestLayouts.FirstOrDefault(l => l.Angle == generated.Angle);
        if (moved is null || moved.Bones.Count == 0)
            return generated;
        return generated with
        {
            Bones = generated.Bones
                .Select(p => moved.Bones.LastOrDefault(o => o.Bone == p.Bone) ?? p)
                .ToList()
        };
    }

    private static Rect2D ExtentOf(IReadOnlyList<Point2D> torso, IReadOnlyList<IReadOnlyList<BodyCapsule>> limbGroups, IReadOnlyList<IReadOnlyList<BodyEllipse>> blobGroups)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        void Include(double x, double y, double rx, double ry)
        {
            left = Math.Min(left, x - rx);
            right = Math.Max(right, x + rx);
            top = Math.Min(top, y - ry);
            bottom = Math.Max(bottom, y + ry);
        }

        foreach (var p in torso)
            Include(p.X, p.Y, 0, 0);
        foreach (var c in limbGroups.SelectMany(g => g))
        {
            Include(c.From.X, c.From.Y, c.FromRadius, c.FromRadius);
            Include(c.To.X, c.To.Y, c.ToRadius, c.ToRadius);
        }
        foreach (var e in blobGroups.SelectMany(g => g))
        {
            // A turned ellipse's half-widths along x and y.
            var r = e.RotationDegrees * Math.PI / 180;
            var (cos, sin) = (Math.Cos(r), Math.Sin(r));
            Include(e.Center.X, e.Center.Y,
                Math.Sqrt(e.RadiusX * e.RadiusX * cos * cos + e.RadiusY * e.RadiusY * sin * sin),
                Math.Sqrt(e.RadiusX * e.RadiusX * sin * sin + e.RadiusY * e.RadiusY * cos * cos));
        }
        return Rect2D.FromEdges(left, top, right, bottom);
    }

    /// <summary>A round seam zone (see <see cref="FigureLayer.Seams"/>).</summary>
    private static BodyEllipse Seam(Point2D at, double radius) => new(at, radius, radius);

    private static Point2D Along(Point2D from, Point2D direction, double distance) =>
        new(from.X + direction.X * distance, from.Y + direction.Y * distance);

    private static Point2D Mirror(Point2D p, double sign) => new(p.X * sign, p.Y);

    private static Point2D Offset(Point2D p, Point2D by) => new(p.X + by.X, p.Y + by.Y);

    private static Point2D Normalize(Point2D v, Point2D fallback)
    {
        var length = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        return length < 1e-9 ? fallback : new Point2D(v.X / length, v.Y / length);
    }

    private sealed record Side(
        HumanoidBone Shoulder, HumanoidBone UpperArm, HumanoidBone LowerArm, HumanoidBone Hand,
        HumanoidBone UpperLeg, HumanoidBone LowerLeg, HumanoidBone Foot)
    {
        public static readonly Side Left = new(
            HumanoidBone.LeftShoulder, HumanoidBone.LeftUpperArm, HumanoidBone.LeftLowerArm, HumanoidBone.LeftHand,
            HumanoidBone.LeftUpperLeg, HumanoidBone.LeftLowerLeg, HumanoidBone.LeftFoot);

        public static readonly Side Right = new(
            HumanoidBone.RightShoulder, HumanoidBone.RightUpperArm, HumanoidBone.RightLowerArm, HumanoidBone.RightHand,
            HumanoidBone.RightUpperLeg, HumanoidBone.RightLowerLeg, HumanoidBone.RightFoot);
    }
}
