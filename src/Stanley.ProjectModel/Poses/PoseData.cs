using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Poses;

/// <summary>One bone's rotation, in degrees, relative to its parent.</summary>
public sealed record BoneRotation(HumanoidBone Bone, double Degrees);

/// <summary>
/// Pure pose data: bone rotations, a view angle, and an expression preset (face slot
/// name -&gt; active sticker variant name, e.g. "surprised" = wide eyes + open mouth).
/// Independent of any one character's artwork identity and angle-agnostic - rotations
/// apply relative to whichever angle's rest layout is active, and slot/variant names
/// line up across characters, so this same shape works both as a library entry
/// (wrapped by <see cref="Pose"/>) and embedded inline in a panel's character instance.
/// </summary>
public sealed record PoseData(
    ViewAngle ViewAngle,
    IReadOnlyList<BoneRotation> BoneRotations,
    SortedDictionary<string, string> Expression);
