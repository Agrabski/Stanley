using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

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
/// A preset pose: optional view it needs (walking reads side on), trunk (lean, head tilt,
/// hips shift as a fraction of height) and hand/foot goals. Applying one solves the goals
/// with the same inverse kinematics as dragging, so the result is ordinary pose data the
/// user can keep adjusting. Feet with no goal stay planted where they stand.
/// </summary>
public sealed record PosePresetDefinition(
    PosePreset Preset,
    string Name,
    ViewAngle? View,
    double Lean,
    double HeadTilt,
    Point2D HipsShift,
    IReadOnlyList<LimbGoal> Goals);

/// <summary>The same enum-plus-static-lookup shape as <c>BodyPresets</c>.</summary>
public static class PosePresets
{
    public static IReadOnlyList<PosePresetDefinition> All { get; } =
    [
        new(PosePreset.Stand, "Stand", null, 0, 0, default, []),
        new(PosePreset.Wave, "Wave", null, 0, -6, default, [new(Limb.RightArm, 0.4, -0.8)]),
        new(PosePreset.Cheer, "Cheer", ViewAngle.Front, 0, 0, default, [new(Limb.RightArm, 0.45, -0.85), new(Limb.LeftArm, 0.45, -0.85)]),
        new(PosePreset.Point, "Point", null, 0, 0, default, [new(Limb.RightArm, 0.98, -0.12)]),
        new(PosePreset.HandsOnHips, "Hands on hips", ViewAngle.Front, 0, 0, default, [new(Limb.RightArm, 0.05, 0.62), new(Limb.LeftArm, 0.05, 0.62)]),
        new(PosePreset.Shrug, "Shrug", ViewAngle.Front, 0, 8, default, [new(Limb.RightArm, 0.48, 0.42, Tucked: true), new(Limb.LeftArm, 0.48, 0.42, Tucked: true)]),
        new(PosePreset.Think, "Think", null, 0, 10, default, [new(Limb.RightArm, -0.25, -0.12), new(Limb.LeftArm, 0.05, 0.55)]),
        new(PosePreset.Crouch, "Crouch", null, 0, 0, new Point2D(0, 0.13), [new(Limb.RightArm, 0.25, 0.7), new(Limb.LeftArm, 0.25, 0.7)]),
        new(PosePreset.Walk, "Walk", ViewAngle.Profile, 3, 0, new Point2D(0, 0.012),
            [new(Limb.LeftLeg, 0.3, 0.95), new(Limb.RightLeg, -0.28, 0.95), new(Limb.LeftArm, -0.3, 0.93), new(Limb.RightArm, 0.32, 0.92)]),
        new(PosePreset.Run, "Run", ViewAngle.Profile, 14, -5, new Point2D(0, 0.03),
            [new(Limb.LeftLeg, 0.5, 0.55), new(Limb.RightLeg, -0.55, 0.72), new(Limb.LeftArm, 0.5, 0.15), new(Limb.RightArm, -0.5, 0.55)]),
        new(PosePreset.Sit, "Sit", ViewAngle.Profile, -4, 0, new Point2D(-0.03, 0.24),
            [new(Limb.LeftLeg, 0.62, 0.5), new(Limb.RightLeg, 0.58, 0.52), new(Limb.LeftArm, 0.55, 0.55), new(Limb.RightArm, 0.5, 0.58)]),
    ];

    public static PosePresetDefinition Get(PosePreset preset) => All.First(p => p.Preset == preset);

    /// <summary>
    /// <paramref name="instance"/> posed as <paramref name="preset"/>: turned to the view the
    /// preset needs (if any), otherwise kept; standing where it stands, facing the way it
    /// faces. Replaces the whole pose.
    /// </summary>
    public static CharacterInstance Apply(CharacterDefinition character, CharacterInstance instance, PosePresetDefinition preset)
    {
        var view = preset.View ?? instance.Pose.ViewAngle;
        var side = view == ViewAngle.Profile;
        var standing = CharacterPosing.ResetPose(instance) with { Pose = instance.Pose with { ViewAngle = view, BoneRotations = [], HipsShift = null } };
        // Where the feet stand at rest - where ungoaled feet stay planted.
        var planted = new[] { Limb.LeftLeg, Limb.RightLeg }.ToDictionary(leg => leg, leg => CharacterPosing.EndPoint(character, standing, leg));

        // A lean only reads side on; in a front view it would be a sideways tilt.
        var posed = CharacterPosing.WithRotation(standing, ProjectModel.Geometry.HumanoidBone.Spine, side ? preset.Lean : 0);
        posed = CharacterPosing.WithRotation(posed, ProjectModel.Geometry.HumanoidBone.Head, preset.HeadTilt);
        if (preset.HipsShift != default)
        {
            var drop = Math.Min(preset.HipsShift.Y, CharacterPosing.MaxHipsDrop(character, standing));
            posed = posed with { Pose = posed.Pose with { HipsShift = preset.HipsShift with { Y = drop } } };
        }

        foreach (var limb in Enum.GetValues<Limb>())
        {
            var goal = preset.Goals.FirstOrDefault(g => g.Limb == limb);
            if (goal is null)
            {
                if (CharacterPosing.IsLeg(limb) && posed.Pose.HipsShift is not null)
                    posed = CharacterPosing.Reach(character, posed, limb, planted[limb], Bend(character, posed, limb, side, planted[limb]));
                continue;
            }

            var (root, length) = CharacterPosing.LimbFrame(character, posed, limb);
            var outward = side ? 1 : Math.Sign(root.X - CharacterPosing.Joint(CharacterPosing.Figure(character, posed).BaseLayout, ProjectModel.Geometry.HumanoidBone.Hips).X);
            var goalY = root.Y + goal.Down * length;
            // A foot never goes below the floor it stands on (the hips may have come down a long way, as in sitting).
            if (CharacterPosing.IsLeg(limb))
                goalY = Math.Min(goalY, posed.Placement.ToFigure(planted[limb]).Y);
            var target = posed.Placement.ToPage(new Point2D(root.X + goal.Out * length * (outward == 0 ? 1 : outward), goalY));
            posed = CharacterPosing.Reach(character, posed, limb, target, Bend(character, posed, limb, side, target, goal.Tucked));
        }
        return posed;
    }

    /// <summary>
    /// Which way a preset bends a joint: anatomical side on; in a front view whichever
    /// solution puts the elbow or knee further out from the body's centre line (elbows out
    /// on hips, knees out in a crouch) - or further in, when tucked (a shrug).
    /// </summary>
    private static int Bend(CharacterDefinition character, CharacterInstance instance, Limb limb, bool side, Point2D target, bool tucked = false)
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
