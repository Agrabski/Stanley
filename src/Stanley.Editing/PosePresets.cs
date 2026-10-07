using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editing;

/// <summary>Ready-made poses - one click, then adjust by dragging.</summary>
public enum PosePreset
{
    Stand,
    Wave,
    Cheer,
    Point,
    HandsOnHips,
    Shrug,
    Think,
    Crouch,
    Walk,
    Run,
    Sit
}

/// <summary>
/// Where one hand or foot goes in a preset, relative to that limb's own root (shoulder or
/// hip) in units of the limb's own length - so a preset fits any body, child or giant.
/// <paramref name="Out"/> is away from the body's centre line (front view) or forward
/// (side view); <paramref name="Down"/> grows downwards. In a front view the elbow or
/// knee bends outwards, unless <paramref name="Tucked"/> keeps it in at the body.
/// </summary>
public sealed record LimbGoal(Limb Limb, double Out, double Down, bool Tucked = false);

/// <summary>
/// A preset as one view shows it: trunk (lean, head tilt, hips shift as a fraction of
/// height) and hand/foot goals. Feet with no goal stay planted where they stand.
/// </summary>
public sealed record PoseGoals(double Lean, double HeadTilt, Point2D HipsShift, IReadOnlyList<LimbGoal> Goals)
{
    public static PoseGoals None { get; } = new(0, 0, default, []);
}

/// <summary>
/// A preset pose, drawn for both views - a flat figure can't turn one set of goals round
/// (an arm raised in front of a side-on body reads across the face from the front), so
/// each view has its own <see cref="PoseGoals"/>. <paramref name="View"/> is the view the
/// preset turns the character to, if it needs one (walking reads side on). Applying one
/// solves the goals with the same inverse kinematics as dragging, so the result is
/// ordinary pose data the user can keep adjusting.
/// </summary>
public sealed record PosePresetDefinition(
    PosePreset Preset,
    string Name,
    ViewAngle? View,
    PoseGoals Front,
    PoseGoals Side)
{
    /// <summary>A preset that looks the same from the front and the side (limb goals are "out" and "down" in either).</summary>
    public PosePresetDefinition(PosePreset preset, string name, ViewAngle? view, PoseGoals both)
        : this(preset, name, view, both, both)
    {
    }

    /// <summary>The goals for <paramref name="view"/> (a three-quarter view draws as the front).</summary>
    public PoseGoals In(ViewAngle view) => view == ViewAngle.Profile ? Side : Front;
}

/// <summary>The same enum-plus-static-lookup shape as <c>BodyPresets</c>.</summary>
public static class PosePresets
{
    // Front goals, then side goals. Side on, the character's right limbs are the near ones
    // (facing right). A front view has no lean (it would be a sideways bend) and can't
    // foreshorten a thigh or forearm coming towards the reader, so what goes forward side
    // on is drawn from the front by what shows of it: a lifted foot, a knee out, a bent arm.
    public static IReadOnlyList<PosePresetDefinition> All { get; } =
    [
        new(PosePreset.Stand, "Stand", null, PoseGoals.None),
        new(PosePreset.Wave, "Wave", null, new PoseGoals(0, -6, default, [new(Limb.RightArm, 0.4, -0.8)])),
        new(PosePreset.Cheer, "Cheer", ViewAngle.Front,
            new PoseGoals(0, 0, default, [new(Limb.RightArm, 0.45, -0.85), new(Limb.LeftArm, 0.45, -0.85)]),
            // Both arms up in front of the face, a little apart so the far one shows; leaning back, looking up.
            new PoseGoals(-4, -10, default, [new(Limb.RightArm, 0.35, -0.85), new(Limb.LeftArm, 0.15, -0.92)])),
        new(PosePreset.Point, "Point", null, new PoseGoals(0, 0, default, [new(Limb.RightArm, 0.98, -0.12)])),
        new(PosePreset.HandsOnHips, "Hands on hips", ViewAngle.Front,
            new PoseGoals(0, 0, default, [new(Limb.RightArm, 0.05, 0.62), new(Limb.LeftArm, 0.05, 0.62)]),
            // The hands at the hip, the elbows back.
            new PoseGoals(0, 0, default, [new(Limb.RightArm, 0.02, 0.62), new(Limb.LeftArm, 0.06, 0.62)])),
        new(PosePreset.Shrug, "Shrug", ViewAngle.Front,
            new PoseGoals(0, 8, default, [new(Limb.RightArm, 0.48, 0.42, Tucked: true), new(Limb.LeftArm, 0.48, 0.42, Tucked: true)]),
            // The elbows at the sides, the forearms forward, palms up.
            new PoseGoals(0, -6, default, [new(Limb.RightArm, 0.5, 0.42), new(Limb.LeftArm, 0.42, 0.45)])),
        new(PosePreset.Think, "Think", null,
            // The hand under the chin with the elbow tucked down in front of the chest - bent
            // outwards, the elbow would fold up over the head (#31).
            new PoseGoals(0, 10, default, [new(Limb.RightArm, -0.3, -0.03, Tucked: true), new(Limb.LeftArm, 0.05, 0.55)]),
            // The near hand up at the chin, the far arm across the waist - both in front of the body.
            new PoseGoals(0, 10, default, [new(Limb.RightArm, 0.3, -0.2), new(Limb.LeftArm, 0.3, 0.5)])),
        new(PosePreset.Crouch, "Crouch", null, new PoseGoals(0, 0, new Point2D(0, 0.13), [new(Limb.RightArm, 0.25, 0.7), new(Limb.LeftArm, 0.25, 0.7)])),
        new(PosePreset.Walk, "Walk", ViewAngle.Profile,
            // Mid-step: one foot just off the floor, the arms swinging a little.
            new PoseGoals(0, 0, default, [new(Limb.RightLeg, 0.06, 0.95), new(Limb.RightArm, 0.18, 0.9), new(Limb.LeftArm, 0.3, 0.95)]),
            new PoseGoals(3, 0, new Point2D(0, 0.012),
                [new(Limb.RightLeg, 0.3, 0.95), new(Limb.LeftLeg, -0.28, 0.95), new(Limb.RightArm, -0.3, 0.93), new(Limb.LeftArm, 0.32, 0.92)])),
        new(PosePreset.Run, "Run", ViewAngle.Profile,
            // A knee up, one arm pumping (elbow down, hand up), the other swung back and out.
            new PoseGoals(0, 0, default, [new(Limb.RightLeg, 0.2, 0.62), new(Limb.RightArm, 0.28, 0.2, Tucked: true), new(Limb.LeftArm, 0.42, 0.75)]),
            new PoseGoals(14, -5, new Point2D(0, 0.03),
                [new(Limb.RightLeg, 0.5, 0.55), new(Limb.LeftLeg, -0.55, 0.72), new(Limb.RightArm, 0.5, 0.15), new(Limb.LeftArm, -0.5, 0.55)])),
        new(PosePreset.Sit, "Sit", ViewAngle.Profile,
            // Seated with the knees apart, the shins straight down to the floor, the hands on the knees.
            new PoseGoals(0, 0, new Point2D(0, 0.12), [new(Limb.RightLeg, 0.36, 0.9), new(Limb.LeftLeg, 0.36, 0.9), new(Limb.RightArm, 0.22, 0.82), new(Limb.LeftArm, 0.22, 0.82)]),
            new PoseGoals(-4, 0, new Point2D(-0.03, 0.24),
                [new(Limb.RightLeg, 0.62, 0.5), new(Limb.LeftLeg, 0.58, 0.52), new(Limb.RightArm, 0.55, 0.55), new(Limb.LeftArm, 0.5, 0.58)])),
    ];

    public static PosePresetDefinition Get(PosePreset preset) => All.First(p => p.Preset == preset);

    /// <summary>
    /// <paramref name="instance"/> posed as <paramref name="preset"/>: turned to the view the
    /// preset needs (if any), otherwise kept; standing where it stands, facing the way it
    /// faces. Replaces the whole pose.
    /// </summary>
    public static CharacterInstance Apply(CharacterDefinition character, CharacterInstance instance, PosePresetDefinition preset) =>
        Apply(character, instance, preset, preset.View ?? instance.Pose.ViewAngle);

    /// <summary><paramref name="instance"/> posed as <paramref name="preset"/> is seen in <paramref name="view"/> - whatever view the preset would pick.</summary>
    public static CharacterInstance Apply(CharacterDefinition character, CharacterInstance instance, PosePresetDefinition preset, ViewAngle view)
    {
        var side = view == ViewAngle.Profile;
        var goals = preset.In(view);
        var standing = CharacterPosing.ResetPose(instance) with { Pose = instance.Pose with { ViewAngle = view, BoneRotations = [], HipsShift = null } };
        // Where the feet stand at rest - where ungoaled feet stay planted.
        var planted = new[] { Limb.LeftLeg, Limb.RightLeg }.ToDictionary(leg => leg, leg => CharacterPosing.EndPoint(character, standing, leg));

        var posed = CharacterPosing.WithRotation(standing, ProjectModel.Geometry.HumanoidBone.Spine, goals.Lean);
        posed = CharacterPosing.WithRotation(posed, ProjectModel.Geometry.HumanoidBone.Head, goals.HeadTilt);
        if (goals.HipsShift != default)
        {
            var drop = Math.Min(goals.HipsShift.Y, CharacterPosing.MaxHipsDrop(character, standing));
            posed = posed with { Pose = posed.Pose with { HipsShift = goals.HipsShift with { Y = drop } } };
        }

        foreach (var limb in Enum.GetValues<Limb>())
        {
            var goal = goals.Goals.FirstOrDefault(g => g.Limb == limb);
            if (goal is null)
            {
                if (CharacterPosing.IsLeg(limb) && posed.Pose.HipsShift is not null)
                    posed = CharacterPosing.Reach(character, posed, limb, planted[limb], Bend(character, posed, limb, side, planted[limb]));
                continue;
            }

            var (root, length) = CharacterPosing.LimbFrame(character, posed, limb);
            var goalY = root.Y + goal.Down * length;
            // A foot never goes below the floor it stands on (the hips may have come down a long way, as in sitting).
            if (CharacterPosing.IsLeg(limb))
                goalY = Math.Min(goalY, posed.Placement.ToFigure(planted[limb]).Y);
            var target = posed.Placement.ToPage(new Point2D(root.X + goal.Out * length * Outward(character, posed, limb), goalY));
            posed = CharacterPosing.Reach(character, posed, limb, target, Bend(character, posed, limb, side, target, goal.Tucked));
        }
        return posed;
    }

    /// <summary>
    /// The preset <paramref name="instance"/>'s pose is, as its view shows it - or the same
    /// preset mirrored (<see cref="CharacterPosing.MirrorPose"/>) - or null for a pose of
    /// its own (or one adjusted since). Standing at rest is <see cref="PosePreset.Stand"/>.
    /// </summary>
    public static (PosePresetDefinition Preset, bool Mirrored)? Recognize(CharacterDefinition character, CharacterInstance instance)
    {
        var view = instance.Pose.ViewAngle;
        foreach (var preset in All)
        {
            var posed = Apply(character, instance, preset, view).Pose;
            if (SamePose(posed, instance.Pose))
                return (preset, false);
            if (preset.Preset != PosePreset.Stand && SamePose(CharacterPosing.MirrorPose(instance with { Pose = posed }).Pose, instance.Pose))
                return (preset, true);
        }
        return null;
    }

    /// <summary>Whether two poses put every bone the same way (to rounding) with the hips in the same place - the view and expression aside.</summary>
    private static bool SamePose(PoseData a, PoseData b)
    {
        const double degrees = 0.05, shift = 1e-6;
        double Of(PoseData pose, ProjectModel.Geometry.HumanoidBone bone) => pose.BoneRotations.LastOrDefault(r => r.Bone == bone)?.Degrees ?? 0;
        var ha = a.HipsShift ?? default;
        var hb = b.HipsShift ?? default;
        return Math.Abs(ha.X - hb.X) < shift && Math.Abs(ha.Y - hb.Y) < shift
            && a.BoneRotations.Select(r => r.Bone).Concat(b.BoneRotations.Select(r => r.Bone)).Distinct()
                .All(bone => Math.Abs(Math.IEEERemainder(Of(a, bone) - Of(b, bone), 360)) < degrees);
    }

    /// <summary>+1 or -1: which way on the figure's x axis is "out" for a limb - forward side on (the figure faces +x), away from the body's centre line from the front.</summary>
    internal static int Outward(CharacterDefinition character, CharacterInstance instance, Limb limb)
    {
        if (instance.Pose.ViewAngle == ViewAngle.Profile)
            return 1;
        var (root, _) = CharacterPosing.LimbFrame(character, instance, limb);
        var hips = CharacterPosing.Joint(CharacterPosing.Figure(character, instance).BaseLayout, ProjectModel.Geometry.HumanoidBone.Hips).X;
        var sign = Math.Sign(root.X - hips);
        return sign == 0 ? 1 : sign;
    }

    /// <summary>
    /// Which way a preset bends a joint: anatomical side on; in a front view whichever
    /// solution puts the elbow or knee further out from the body's centre line (elbows out
    /// on hips, knees out in a crouch) - or further in, when tucked (a shrug).
    /// </summary>
    internal static int Bend(CharacterDefinition character, CharacterInstance instance, Limb limb, bool side, Point2D target, bool tucked = false)
    {
        if (side)
            return CharacterPosing.IsLeg(limb) ? 1 : -1;

        var hips = CharacterPosing.Joint(CharacterPosing.Figure(character, instance).BaseLayout, ProjectModel.Geometry.HumanoidBone.Hips).X;
        var (middleBone, best, bestOut) = (CharacterPosing.Chain(limb).Middle, 1, double.MinValue);
        foreach (var sign in new[] { 1, -1 })
        {
            var tried = CharacterPosing.Reach(character, instance, limb, target, sign);
            var middle = CharacterPosing.Joint(CharacterPosing.Figure(character, tried).Layout, middleBone);
            var root = CharacterPosing.Joint(CharacterPosing.Figure(character, tried).BaseLayout, CharacterPosing.Chain(limb).Root);
            var outness = (middle.X - hips) * Math.Sign(root.X - hips) * (tucked ? -1 : 1);
            if (outness > bestOut)
                (best, bestOut) = (sign, outness);
        }
        return best;
    }
}
