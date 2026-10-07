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
    /// Which way the elbow or knee bends, as <see cref="Reach"/> takes it: +1 or -1 for the
    /// side of the root-to-end line the middle joint is on now (in figure space), so reaching
    /// for where the hand or foot already is leaves the joint where it is. Read once when a
    /// drag starts and held for the whole drag, so the joint never snaps inside-out as the
    /// limb passes straight. In a side view it's anatomical - knees bend forward, elbows back.
    /// </summary>
    public static int BendSign(CharacterDefinition character, CharacterInstance instance, Limb limb)
    {
        if (instance.Pose.ViewAngle == ViewAngle.Profile)
            return IsLeg(limb) ? 1 : -1; // figure faces +x: knee to +x, elbow to -x

        // Solve turns the upper bone back from the root-to-end line by +1's angle, which puts
        // the joint on the line's negative side - so the sign is the opposite of Side's (#76:
        // read as Side, the elbow jumped inside-out the moment a hand was grabbed).
        var figure = Figure(character, instance);
        var (root, middle, end) = Chain(limb);
        var sign = Side(Joint(figure.Layout, root), Joint(figure.Layout, end), Joint(figure.Layout, middle));
        if (sign != 0)
            return -sign;
        // Dead straight: bend the way the limb does at rest.
        sign = Side(Joint(figure.BaseLayout, root), Joint(figure.BaseLayout, end), Joint(figure.BaseLayout, middle));
        return sign != 0 ? -sign : 1;
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

    /// <summary>
    /// Bends the back so the chest handle (the base of the neck) goes towards
    /// <paramref name="pageTarget"/>: inverse kinematics over the lower back, mid back and
    /// upper chest together (<see cref="BodyRig.SpineJoints"/>), preferring a bend shared
    /// along the spine to one sharp joint - so the back curves, it doesn't swing about the
    /// hips like a board. Solved from <paramref name="start"/> (the drag's starting pose);
    /// the arms keep pointing the way they did, so hanging arms keep hanging. Legs and feet
    /// don't move.
    /// </summary>
    public static CharacterInstance Lean(CharacterDefinition character, CharacterInstance start, Point2D pageTarget)
    {
        var target = start.Placement.ToFigure(pageTarget);
        var bent = SolveTrunkChain(character, start, BodyRig.SpineJoints, target, figure => Joint(figure.Layout, HumanoidBone.Neck), smoothness: 0.02);
        return KeepArmDirections(character, start, bent);
    }

    /// <summary>
    /// Bends the neck and tilts the head so the top of the head goes towards
    /// <paramref name="pageTarget"/> - inverse kinematics over both
    /// (<see cref="BodyRig.NeckJoints"/>), sharing the turn between them. The chest stays.
    /// </summary>
    public static CharacterInstance TiltHead(CharacterDefinition character, CharacterInstance start, Point2D pageTarget)
    {
        var target = start.Placement.ToFigure(pageTarget);
        return SolveTrunkChain(character, start, BodyRig.NeckJoints, target, HeadTop, smoothness: 0.004);
    }

    /// <summary>
    /// Damped least-squares inverse kinematics over a few trunk joints: finds rotations
    /// (within each joint's limit) that bring <paramref name="effector"/> as close to
    /// <paramref name="target"/> (figure space) as the chain allows, while keeping
    /// neighbouring joints' bends alike (<paramref name="smoothness"/>, per radian
    /// squared, against figure units squared of distance). Starts from the instance's
    /// own rotations, so a drag is a pure function of where it started and the pointer.
    /// </summary>
    internal static CharacterInstance SolveTrunkChain(CharacterDefinition character, CharacterInstance start,
        IReadOnlyList<(HumanoidBone Bone, double Limit)> joints, Point2D target, Func<BodyFigure, Point2D> effector, double smoothness)
    {
        var n = joints.Count;
        var angles = joints.Select(j => start.Pose.BoneRotations.LastOrDefault(r => r.Bone == j.Bone)?.Degrees ?? 0).ToArray();
        CharacterInstance With(double[] a)
        {
            var posed = start;
            for (var i = 0; i < n; i++)
                posed = WithRotation(posed, joints[i].Bone, a[i]);
            return posed;
        }
        Point2D Effector(double[] a)
        {
            var pose = start.Pose with { BoneRotations = start.Pose.BoneRotations.Where(r => joints.All(j => j.Bone != r.Bone)).Concat(joints.Select((j, i) => new BoneRotation(j.Bone, a[i]))).ToList() };
            return effector(BodyRig.Build(character.Body, start.Pose.ViewAngle, character.Skeleton, pose));
        }

        const double step = 0.5; // degrees, for the numeric Jacobian
        const double damping = 1e-6;
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var at = Effector(angles);
            var (ex, ey) = (target.X - at.X, target.Y - at.Y);
            // Jacobian: how the effector moves per radian of each joint.
            var jx = new double[n];
            var jy = new double[n];
            for (var i = 0; i < n; i++)
            {
                var nudged = (double[])angles.Clone();
                nudged[i] += step;
                var p = Effector(nudged);
                jx[i] = (p.X - at.X) / (step * Math.PI / 180);
                jy[i] = (p.Y - at.Y) / (step * Math.PI / 180);
            }
            // Normal equations: (JᵀJ + μL + λI) Δ = Jᵀe - μL·a, with L the chain's
            // difference Laplacian (neighbouring joints alike).
            var a = new double[n, n];
            var b = new double[n];
            var radians = angles.Select(d => d * Math.PI / 180).ToArray();
            for (var i = 0; i < n; i++)
            {
                for (var k = 0; k < n; k++)
                    a[i, k] = jx[i] * jx[k] + jy[i] * jy[k];
                a[i, i] += damping;
                b[i] = jx[i] * ex + jy[i] * ey;
            }
            for (var i = 0; i + 1 < n; i++)
            {
                a[i, i] += smoothness;
                a[i + 1, i + 1] += smoothness;
                a[i, i + 1] -= smoothness;
                a[i + 1, i] -= smoothness;
                var difference = radians[i] - radians[i + 1];
                b[i] -= smoothness * difference;
                b[i + 1] += smoothness * difference;
            }
            var delta = SolveLinear(a, b);
            var moved = 0.0;
            for (var i = 0; i < n; i++)
            {
                var degrees = delta[i] * 180 / Math.PI;
                var next = Math.Clamp(angles[i] + Math.Clamp(degrees, -20, 20), -joints[i].Limit, joints[i].Limit);
                moved = Math.Max(moved, Math.Abs(next - angles[i]));
                angles[i] = next;
            }
            if (moved < 1e-3)
                break;
        }
        return With(angles);
    }

    /// <summary>
    /// <paramref name="bent"/> with each arm turned back by however much the body turned
    /// under its shoulder since <paramref name="start"/> - so arms keep their direction
    /// (a hanging arm keeps hanging) instead of swinging round with the torso.
    /// </summary>
    internal static CharacterInstance KeepArmDirections(CharacterDefinition character, CharacterInstance start, CharacterInstance bent)
    {
        var before = Figure(character, start).BaseLayout;
        var after = Figure(character, bent).BaseLayout;
        var result = bent;
        foreach (var limb in new[] { Limb.LeftArm, Limb.RightArm })
        {
            var (root, middle, _) = Chain(limb);
            var turned = Wrap(AngleOf(Joint(after, root), Joint(after, middle)) - AngleOf(Joint(before, root), Joint(before, middle)));
            if (Math.Abs(turned) < 1e-9)
                continue;
            var current = result.Pose.BoneRotations.LastOrDefault(r => r.Bone == root)?.Degrees ?? 0;
            result = WithRotation(result, root, Wrap(current - turned));
        }
        return result;
    }

    private static double[] SolveLinear(double[,] a, double[] b)
    {
        var n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var row = col + 1; row < n; row++)
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                    pivot = row;
            if (Math.Abs(m[pivot, col]) < 1e-15)
                continue;
            if (pivot != col)
            {
                for (var k = 0; k < n; k++)
                    (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                (x[col], x[pivot]) = (x[pivot], x[col]);
            }
            for (var row = 0; row < n; row++)
            {
                if (row == col)
                    continue;
                var f = m[row, col] / m[col, col];
                for (var k = col; k < n; k++)
                    m[row, k] -= f * m[col, k];
                x[row] -= f * x[col];
            }
        }
        for (var i = 0; i < n; i++)
            x[i] = Math.Abs(m[i, i]) < 1e-15 ? 0 : x[i] / m[i, i];
        return x;
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

    /// <summary>
    /// Turns the character to <paramref name="angle"/> and carries its pose round with it.
    /// The stored rotations can't simply be kept: an angle means something else in the
    /// other view (a side-on wave, turned to the front, swings the arm across the face).
    /// A preset - as the gallery gave it, or mirrored - becomes the same preset as the new
    /// view draws it. Any other pose is translated limb by limb (<see cref="Translate"/>).
    /// Standing at rest, or front to three-quarter (drawn alike), only the view changes.
    /// </summary>
    public static CharacterInstance Turn(CharacterDefinition character, CharacterInstance instance, ViewAngle angle)
    {
        var from = instance.Pose.ViewAngle;
        if (from == angle)
            return instance;
        if (!IsPosed(instance.Pose) || (from == ViewAngle.Profile) == (angle == ViewAngle.Profile))
            return instance with { Pose = instance.Pose with { ViewAngle = angle } };
        if (PosePresets.Recognize(character, instance) is { } known)
        {
            var posed = PosePresets.Apply(character, instance, known.Preset, angle);
            return known.Mirrored ? MirrorPose(posed) : posed;
        }
        return Translate(character, instance, angle);
    }

    /// <summary>
    /// A pose of one's own seen from the other side, as near as a flat figure allows: each
    /// hand keeps its height by its shoulder and reaches out as far - out to the side from
    /// the front is forward side on, and forward or back side on is out to the side from
    /// the front; each foot keeps its lift off the floor and half its step (a step to the
    /// side from the front is a stride side on - the near foot forward); the hips keep
    /// their drop and the head its tilt; a lean or a sideways bend of the back, which
    /// means something else in the other view, straightens. Elbows and knees bend the
    /// way presets bend them (<see cref="PosePresets"/>).
    /// </summary>
    private static CharacterInstance Translate(CharacterDefinition character, CharacterInstance instance, ViewAngle angle)
    {
        const double stepKept = 0.5;
        var toSide = angle == ViewAngle.Profile;
        var figure = Figure(character, instance);
        CharacterInstance Standing(CharacterInstance c, ViewAngle view) => c with { Pose = c.Pose with { ViewAngle = view, BoneRotations = [], HipsShift = null } };
        var restBefore = Figure(character, Standing(instance, instance.Pose.ViewAngle)).Layout;
        var restAfter = Figure(character, Standing(instance, angle)).Layout;

        // The trunk: the hips drop as far, the neck and head keep their tilt, the back straightens.
        var keptTrunk = new[] { HumanoidBone.Neck, HumanoidBone.Head };
        var posed = Standing(instance, angle);
        posed = posed with { Pose = posed.Pose with { BoneRotations = instance.Pose.BoneRotations.Where(r => keptTrunk.Contains(r.Bone)).ToList() } };
        if (instance.Pose.HipsShift is { } shift && shift.Y != 0)
            posed = posed with { Pose = posed.Pose with { HipsShift = new Point2D(0, Math.Min(shift.Y, MaxHipsDrop(character, posed))) } };

        foreach (var limb in Enum.GetValues<Limb>())
        {
            var (root, middle, end) = Chain(limb);
            var bent = instance.Pose.BoneRotations.Any(r => (r.Bone == root || r.Bone == middle) && Math.Abs(r.Degrees) > 1e-9);
            var (rootBefore, length) = LimbFrame(character, instance, limb);
            var (rootAfter, newLength) = LimbFrame(character, posed, limb);
            var at = Joint(figure.Layout, end);
            var outward = PosePresets.Outward(character, posed, limb);
            Point2D target;
            if (IsLeg(limb))
            {
                if (!bent && posed.Pose.HipsShift is null)
                    continue;
                // Measured from where the foot stands at rest, so a planted foot stays planted.
                var rest = Joint(restBefore, end);
                var step = Math.Abs(at.X - rest.X) / length * stepKept;
                var lift = (at.Y - rest.Y) / length;
                if (toSide)
                    step *= limb == Limb.RightLeg ? 1 : -1; // the near foot forward
                var floor = Joint(restAfter, end);
                target = new Point2D(floor.X + step * newLength * outward, Math.Min(floor.Y, floor.Y + lift * newLength));
            }
            else
            {
                if (!bent)
                    continue;
                var reach = Math.Abs(at.X - rootBefore.X) / length;
                target = new Point2D(rootAfter.X + reach * newLength * outward, rootAfter.Y + (at.Y - rootBefore.Y) / length * newLength);
            }
            var page = posed.Placement.ToPage(target);
            posed = Reach(character, posed, limb, page, PosePresets.Bend(character, posed, limb, toSide, page));
        }
        return posed;
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
    internal static Point2D HeadTop(BodyFigure figure)
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
