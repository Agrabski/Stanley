using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Characters;

/// <summary>A tapered limb segment: two circles and everything between them.</summary>
public sealed record BodyCapsule(Point2D From, Point2D To, double FromRadius, double ToRadius);

/// <summary>An axis-aligned ellipse - head, hands, feet.</summary>
public sealed record BodyEllipse(Point2D Center, double RadiusX, double RadiusY);

/// <summary>
/// A generated body in <b>figure space</b>: the unit is <see cref="BodyShape.Height"/>
/// (1.0 = an average adult's height), y points down, and the origin is the ground point
/// between the feet - so the top of the head is at <c>y = -Height</c>. A side view faces
/// +x (right); mirroring the placement makes it face left. <see cref="Extent"/> is the
/// exact bounding box of every shape.
/// </summary>
/// <param name="Torso">A closed outline (clockwise from the neck), meant to be drawn smoothed.</param>
/// <param name="Limbs">Unioned with the torso and <paramref name="Blobs"/> into the body's one silhouette.</param>
/// <param name="NearLimbs">The parts nearest the viewer in a side view (the near arm, with <paramref name="NearBlobs"/> its hand and foot): drawn on top as their own outlined shape, so they still read against the body behind them. Empty in the front view.</param>
public sealed record BodyFigure(
    ViewAngle Angle,
    ViewAngleRestLayout RestLayout,
    IReadOnlyList<Point2D> Torso,
    IReadOnlyList<BodyCapsule> Limbs,
    IReadOnlyList<BodyEllipse> Blobs,
    IReadOnlyList<BodyCapsule> NearLimbs,
    IReadOnlyList<BodyEllipse> NearBlobs,
    Rect2D Extent);

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

    public static BodyFigure Build(BodyShape body, ViewAngle angle, Skeleton? overrides = null)
    {
        var m = new Measures(body.Normalized());
        return angle == ViewAngle.Profile ? BuildProfile(m, overrides) : BuildFront(m, overrides);
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

        public BodyEllipse HandAt(Point2D elbow, Point2D wrist, Point2D fallbackDirection) =>
            new(Along(wrist, Normalize(new Point2D(wrist.X - elbow.X, wrist.Y - elbow.Y), fallbackDirection), Hand * 0.45), ArmWrist * 1.45, Hand * 0.6);
    }

    // ---------------------------------------------------------------- front

    private static BodyFigure BuildFront(Measures m, Skeleton? overrides)
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
        var layout = ApplyOverrides(new ViewAngleRestLayout(ViewAngle.Front, bones), overrides);
        Point2D At(HumanoidBone bone) => layout.Bones.First(p => p.Bone == bone).Position;

        var torso = new List<Point2D>
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
        for (var i = torso.Count - 2; i >= 0; i--)
            torso.Add(Mirror(torso[i], -1));

        var limbs = new List<BodyCapsule> { new(At(HumanoidBone.Head), At(HumanoidBone.Neck), m.NeckHalf, m.NeckHalf) };
        var headPoint = At(HumanoidBone.Head);
        var blobs = new List<BodyEllipse> { new(new Point2D(headPoint.X, headPoint.Y - m.Head / 2), m.Head * 0.42 * (1 + 0.1 * build), m.Head / 2) };

        foreach (var side in new[] { Side.Left, Side.Right })
        {
            var (s, e, h) = (At(side.UpperArm), At(side.LowerArm), At(side.Hand));
            limbs.Add(new BodyCapsule(s, e, m.ArmTop, m.ArmElbow));
            limbs.Add(new BodyCapsule(e, h, m.ArmElbow, m.ArmWrist));
            blobs.Add(m.HandAt(e, h, armDir));

            var (hp, kp, ap) = (At(side.UpperLeg), At(side.LowerLeg), At(side.Foot));
            limbs.Add(new BodyCapsule(hp, kp, m.Thigh, m.Knee));
            limbs.Add(new BodyCapsule(kp, ap, m.Knee, m.Ankle));
            var outward = Math.Sign(ap.X) * w * 0.06;
            blobs.Add(new BodyEllipse(new Point2D(ap.X + outward, ap.Y + m.AnkleHeight - m.FootHalfHeight), w * 0.26, m.FootHalfHeight));
        }

        return new BodyFigure(ViewAngle.Front, layout, torso, limbs, blobs, [], [], ExtentOf(torso, [limbs], [blobs]));
    }

    // ---------------------------------------------------------------- side (profile)

    /// <summary>
    /// Facing +x. The body's width becomes its depth: chest (muscle, and a "V" frame),
    /// belly (weight), seat (weight, and an "A" frame). The near (left) arm and foot are
    /// drawn on top of the body as their own shape; the legs merge into it, the far one
    /// set back a little so both feet show.
    /// </summary>
    private static BodyFigure BuildProfile(Measures m, Skeleton? overrides)
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
        bones.Add(new(HumanoidBone.LeftShoulder, new Point2D(0, m.ShoulderY)));
        bones.Add(new(HumanoidBone.LeftUpperArm, nearShoulder));
        bones.Add(new(HumanoidBone.LeftLowerArm, nearElbow));
        bones.Add(new(HumanoidBone.LeftHand, nearWrist));
        bones.Add(new(HumanoidBone.LeftUpperLeg, nearHip));
        bones.Add(new(HumanoidBone.LeftLowerLeg, nearKnee));
        bones.Add(new(HumanoidBone.LeftFoot, nearAnkle));
        bones.Add(new(HumanoidBone.RightShoulder, Offset(new Point2D(0, m.ShoulderY), farOffset)));
        bones.Add(new(HumanoidBone.RightUpperArm, Offset(nearShoulder, farOffset)));
        bones.Add(new(HumanoidBone.RightLowerArm, Offset(nearElbow, farOffset)));
        bones.Add(new(HumanoidBone.RightHand, Offset(nearWrist, farOffset)));
        bones.Add(new(HumanoidBone.RightUpperLeg, Offset(nearHip, farLeg)));
        bones.Add(new(HumanoidBone.RightLowerLeg, Offset(nearKnee, farLeg)));
        bones.Add(new(HumanoidBone.RightFoot, Offset(nearAnkle, farLeg)));
        var layout = ApplyOverrides(new ViewAngleRestLayout(ViewAngle.Profile, bones), overrides);
        Point2D At(HumanoidBone bone) => layout.Bones.First(p => p.Bone == bone).Position;

        var t = m.TorsoLength;
        var torso = new List<Point2D>
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

        var headPoint = At(HumanoidBone.Head);
        var headCenter = new Point2D(headPoint.X + m.Head * 0.04, headPoint.Y - m.Head / 2);
        var headRx = m.Head * 0.47 * (1 + 0.08 * build);
        var limbs = new List<BodyCapsule> { new(headPoint, At(HumanoidBone.Neck), m.NeckHalf * 1.05, m.NeckHalf * 1.05) };
        var blobs = new List<BodyEllipse>
        {
            new(headCenter, headRx, m.Head / 2),
            // The nose: the one detail that says which way a flat side view is facing.
            new(new Point2D(headCenter.X + headRx * 0.93, headCenter.Y + m.Head * 0.07), m.Head * 0.09, m.Head * 0.07),
        };
        var nearLimbs = new List<BodyCapsule>();
        var nearBlobs = new List<BodyEllipse>();

        var footLength = m.Below * 0.17;
        foreach (var (side, near) in new[] { (Side.Right, false), (Side.Left, true) })
        {
            var (targetLimbs, targetBlobs) = near ? (nearLimbs, nearBlobs) : (limbs, blobs);
            var (s, e, h) = (At(side.UpperArm), At(side.LowerArm), At(side.Hand));
            targetLimbs.Add(new BodyCapsule(s, e, m.ArmTop, m.ArmElbow));
            targetLimbs.Add(new BodyCapsule(e, h, m.ArmElbow, m.ArmWrist));
            targetBlobs.Add(m.HandAt(e, h, armDir));

            // Legs merge into the body (a separate near thigh reads as a bowling pin over
            // the hips); only the near foot is drawn on top, so the feet read as two.
            var (hp, kp, ap) = (At(side.UpperLeg), At(side.LowerLeg), At(side.Foot));
            limbs.Add(new BodyCapsule(hp, kp, m.Thigh * 0.9, m.Knee));
            limbs.Add(new BodyCapsule(kp, ap, m.Knee, m.Ankle));
            targetBlobs.Add(new BodyEllipse(new Point2D(ap.X + footLength * 0.3, ap.Y + m.AnkleHeight - m.FootHalfHeight), footLength / 2, m.FootHalfHeight));
        }

        return new BodyFigure(ViewAngle.Profile, layout, torso, limbs, blobs, nearLimbs, nearBlobs,
            ExtentOf(torso, [limbs, nearLimbs], [blobs, nearBlobs]));
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
    public static Rect2D Extent(BodyShape body, ViewAngle angle, Skeleton? overrides = null) => Build(body, angle, overrides).Extent;

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
            Include(e.Center.X, e.Center.Y, e.RadiusX, e.RadiusY);
        return Rect2D.FromEdges(left, top, right, bottom);
    }

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
