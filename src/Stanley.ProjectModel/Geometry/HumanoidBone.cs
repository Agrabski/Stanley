using System.Text.Json.Serialization;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Geometry;

/// <summary>
/// The VRM 1.0 humanoid bone set, used as Stanley's canonical skeleton even for the 2D
/// V1 renderer so pose data could later drive a 3D backend without migration. See
/// https://github.com/vrm-c/vrm-specification/tree/master/specification/VRMC_vrm-1.0#humanoid-bones.
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter<HumanoidBone>))]
public enum HumanoidBone
{
    // Torso / head (required)
    Hips,
    Spine,
    Chest,
    UpperChest,
    Neck,
    Head,

    // Head detail (optional)
    LeftEye,
    RightEye,
    Jaw,

    // Legs (required, x2)
    LeftUpperLeg,
    LeftLowerLeg,
    LeftFoot,
    LeftToes,
    RightUpperLeg,
    RightLowerLeg,
    RightFoot,
    RightToes,

    // Arms (required, x2)
    LeftShoulder,
    LeftUpperArm,
    LeftLowerArm,
    LeftHand,
    RightShoulder,
    RightUpperArm,
    RightLowerArm,
    RightHand,

    // Fingers (optional, x2 hands x5 fingers x3 joints)
    LeftThumbMetacarpal,
    LeftThumbProximal,
    LeftThumbDistal,
    LeftIndexProximal,
    LeftIndexIntermediate,
    LeftIndexDistal,
    LeftMiddleProximal,
    LeftMiddleIntermediate,
    LeftMiddleDistal,
    LeftRingProximal,
    LeftRingIntermediate,
    LeftRingDistal,
    LeftLittleProximal,
    LeftLittleIntermediate,
    LeftLittleDistal,
    RightThumbMetacarpal,
    RightThumbProximal,
    RightThumbDistal,
    RightIndexProximal,
    RightIndexIntermediate,
    RightIndexDistal,
    RightMiddleProximal,
    RightMiddleIntermediate,
    RightMiddleDistal,
    RightRingProximal,
    RightRingIntermediate,
    RightRingDistal,
    RightLittleProximal,
    RightLittleIntermediate,
    RightLittleDistal
}
