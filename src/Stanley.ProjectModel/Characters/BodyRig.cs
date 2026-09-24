using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Characters;

/// <summary>A tapered limb segment: two circles and everything between them.</summary>
public sealed record BodyCapsule(Point2D From, Point2D To, double FromRadius, double ToRadius);

/// <summary>An axis-aligned ellipse - head, hands, feet.</summary>
public sealed record BodyEllipse(Point2D Center, double RadiusX, double RadiusY);

/// <summary>
/// A generated body in <b>figure space</b>: the unit is <see cref="BodyShape.Height"/>
/// (1.0 = an average adult's height), y points down, and the origin is the ground point
/// between the feet - so the top of the head is at <c>y = -Height</c>. A renderer unions
/// every shape into one silhouette; <see cref="Extent"/> is their exact bounding box.
/// </summary>
/// <param name="Torso">A closed outline (clockwise from the neck), meant to be drawn smoothed.</param>
public sealed record BodyFigure(
    ViewAngleRestLayout RestLayout,
    IReadOnlyList<Point2D> Torso,
    IReadOnlyList<BodyCapsule> Limbs,
    IReadOnlyList<BodyEllipse> Blobs,
    Rect2D Extent);

/// <summary>
/// Turns a <see cref="BodyShape"/> into a skeleton rest layout and a set of simple shapes
/// (the flat cartoon mannequin the V1 renderer draws). Pure math, like
/// <see cref="AnchorRing"/>: no stored geometry, everything recomputed from the handful of
/// body numbers, so a file only ever holds those numbers.
///
/// Heights depend only on <see cref="BodyShape.Height"/> and <see cref="BodyShape.HeadsTall"/>;
/// build, muscle and frame change widths only. The generated layout is the front view in
/// a relaxed standing rest pose; a <see cref="Skeleton"/> override (the rig-editor escape
/// hatch) moves individual joints, and the head, neck and limbs follow them.
/// </summary>
public static class BodyRig
{
    public static BodyFigure Build(BodyShape body, Skeleton? overrides = null)
    {
        var b = body.Normalized();
        var height = b.Height;
        var head = height / b.HeadsTall;            // head height
        var below = height - head;                  // chin to ground

        // Legs are a bigger share of the body the more heads tall a figure is: a chibi is
        // mostly head and torso, a heroic figure mostly legs.
        var legShare = 0.45 + (b.HeadsTall - BodyShape.MinHeadsTall) * (0.13 / 4.5);
        var legLength = below * legShare;
        var neckLength = below * 0.06;
        var torsoLength = below - legLength - neckLength;

        // The unit for widths: tracks the body's length for adults, but doesn't let a
        // chibi's tiny body get stick-thin under its big head.
        var w = 0.7 * below / 6.5 + 0.3 * head;
        var (build, muscle, frame) = (b.Build, b.Muscle, b.Frame - 0.5);

        var chinY = -(height - head);
        var shoulderY = chinY + neckLength;
        var hipY = -legLength;

        var neckHalf = w * (0.26 + 0.1 * build + 0.12 * muscle);
        var shoulderHalf = w * (1.0 - 0.3 * frame + 0.12 * build + 0.3 * muscle);
        var chestHalf = w * (0.74 - 0.1 * frame + 0.25 * build + 0.25 * muscle);
        var waistHalf = w * (0.6 + 0.15 * frame + 0.6 * build + 0.05 * muscle);
        var hipHalf = w * (0.78 + 0.35 * frame + 0.4 * build + 0.05 * muscle);

        // ---------------------------------------------------------------- rest layout (front)
        var armTop = w * (0.23 + 0.14 * build + 0.16 * muscle);
        var armElbow = w * (0.16 + 0.09 * build + 0.1 * muscle);
        var armWrist = w * (0.11 + 0.04 * build + 0.03 * muscle);
        var upperArm = below * 0.23;
        var forearm = below * 0.2;
        var hand = below * 0.1;
        // Arms hang slightly out; a heavier or more muscular body holds them further out.
        var armAngle = (9 + 12 * build + 8 * muscle) * Math.PI / 180;
        var armDir = new Point2D(Math.Sin(armAngle), Math.Cos(armAngle));
        var shoulderJoint = new Point2D(shoulderHalf - armTop * 0.35, shoulderY + armTop * 1.1);
        var elbow = Along(shoulderJoint, armDir, upperArm);
        var wrist = Along(elbow, armDir, forearm);

        var thigh = w * (0.44 + 0.3 * build + 0.14 * muscle);
        var knee = w * (0.25 + 0.12 * build + 0.07 * muscle);
        var ankle = w * (0.13 + 0.04 * build);
        var legX = Math.Max(hipHalf - thigh * 0.9, thigh * 0.8);
        var hipJoint = new Point2D(legX, hipY);
        var kneeJoint = new Point2D(legX * 0.92, hipY + legLength * 0.5);
        var ankleHeight = Math.Max(legLength * 0.06, ankle); // the ankle never sinks into the floor, even on short legs
        var ankleJoint = new Point2D(legX * 0.88, -ankleHeight);

        var bones = new List<BoneRestPose>
        {
            new(HumanoidBone.Hips, new Point2D(0, hipY)),
            new(HumanoidBone.Spine, new Point2D(0, hipY - torsoLength * 0.3)),
            new(HumanoidBone.Chest, new Point2D(0, hipY - torsoLength * 0.65)),
            new(HumanoidBone.Neck, new Point2D(0, shoulderY)),
            new(HumanoidBone.Head, new Point2D(0, chinY)),
        };
        // VRM "left" is the character's own left: on the viewer's right in a front view.
        foreach (var (side, sign) in new[] { (Side.Left, 1.0), (Side.Right, -1.0) })
        {
            bones.Add(new(side.Shoulder, new Point2D(sign * neckHalf, shoulderY)));
            bones.Add(new(side.UpperArm, Mirror(shoulderJoint, sign)));
            bones.Add(new(side.LowerArm, Mirror(elbow, sign)));
            bones.Add(new(side.Hand, Mirror(wrist, sign)));
            bones.Add(new(side.UpperLeg, Mirror(hipJoint, sign)));
            bones.Add(new(side.LowerLeg, Mirror(kneeJoint, sign)));
            bones.Add(new(side.Foot, Mirror(ankleJoint, sign)));
        }
        var layout = ApplyOverrides(new ViewAngleRestLayout(ViewAngle.Front, bones), overrides);
        Point2D At(HumanoidBone bone) => layout.Bones.First(p => p.Bone == bone).Position;

        // ---------------------------------------------------------------- shapes
        var torso = new List<Point2D>
        {
            new(neckHalf, shoulderY - neckLength * 0.3),
            new(shoulderHalf * 0.72, shoulderY + w * 0.02),
            new(shoulderHalf, shoulderY + w * 0.22),
            new(chestHalf, shoulderY + w * 0.9),
            new(waistHalf, shoulderY + torsoLength * 0.62),
            new(hipHalf, hipY - torsoLength * 0.1),
            new(hipHalf * 0.9, hipY + below * 0.04),
            new(0, hipY + below * 0.05),
        };
        for (var i = torso.Count - 2; i >= 0; i--)
            torso.Add(Mirror(torso[i], -1));

        var limbs = new List<BodyCapsule> { new(At(HumanoidBone.Head), At(HumanoidBone.Neck), neckHalf, neckHalf) };
        var blobs = new List<BodyEllipse>();
        var headPoint = At(HumanoidBone.Head);
        blobs.Add(new BodyEllipse(new Point2D(headPoint.X, headPoint.Y - head / 2), head * 0.42 * (1 + 0.1 * build), head / 2));

        foreach (var side in new[] { Side.Left, Side.Right })
        {
            var s = At(side.UpperArm);
            var e = At(side.LowerArm);
            var h = At(side.Hand);
            limbs.Add(new BodyCapsule(s, e, armTop, armElbow));
            limbs.Add(new BodyCapsule(e, h, armElbow, armWrist));
            var forearmDir = Normalize(new Point2D(h.X - e.X, h.Y - e.Y), armDir);
            blobs.Add(new BodyEllipse(Along(h, forearmDir, hand * 0.45), armWrist * 1.45, hand * 0.6));

            var hp = At(side.UpperLeg);
            var kp = At(side.LowerLeg);
            var ap = At(side.Foot);
            limbs.Add(new BodyCapsule(hp, kp, thigh, knee));
            limbs.Add(new BodyCapsule(kp, ap, knee, ankle));
            var footHalfHeight = Math.Max(ankleHeight * 0.75, ankle * 0.8);
            var outward = Math.Sign(ap.X) * w * 0.06;
            blobs.Add(new BodyEllipse(new Point2D(ap.X + outward, ap.Y + ankleHeight - footHalfHeight), w * 0.26, footHalfHeight));
        }

        return new BodyFigure(layout, torso, limbs, blobs, ExtentOf(torso, limbs, blobs));
    }

    /// <summary>The body's bounding box in figure space (see <see cref="BodyFigure"/>).</summary>
    public static Rect2D Extent(BodyShape body, Skeleton? overrides = null) => Build(body, overrides).Extent;

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

    private static Rect2D ExtentOf(IReadOnlyList<Point2D> torso, IReadOnlyList<BodyCapsule> limbs, IReadOnlyList<BodyEllipse> blobs)
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
        foreach (var c in limbs)
        {
            Include(c.From.X, c.From.Y, c.FromRadius, c.FromRadius);
            Include(c.To.X, c.To.Y, c.ToRadius, c.ToRadius);
        }
        foreach (var e in blobs)
            Include(e.Center.X, e.Center.Y, e.RadiusX, e.RadiusY);
        return Rect2D.FromEdges(left, top, right, bottom);
    }

    private static Point2D Along(Point2D from, Point2D direction, double distance) =>
        new(from.X + direction.X * distance, from.Y + direction.Y * distance);

    private static Point2D Mirror(Point2D p, double sign) => new(p.X * sign, p.Y);

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
