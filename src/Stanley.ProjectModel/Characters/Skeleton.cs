using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Characters;

/// <summary>One bone's rest-pose joint position within a single view angle's layout.</summary>
public sealed record BoneRestPose(HumanoidBone Bone, Point2D Position);

/// <summary>A view angle's full (or, for a revision override, sparse) set of bone rest positions.</summary>
public sealed record ViewAngleRestLayout(ViewAngle Angle, IReadOnlyList<BoneRestPose> Bones);

/// <summary>
/// A 2D rig's rest layout, stored per view angle since front/three-quarter/profile are
/// genuinely different bone arrangements, not one rig viewed from different cameras.
/// Also reused, sparsely, as a <see cref="CharacterRevision.ProportionOverride"/> - only
/// the bones a revision actually moves need to appear.
/// </summary>
public sealed record Skeleton(IReadOnlyList<ViewAngleRestLayout> RestLayouts);
