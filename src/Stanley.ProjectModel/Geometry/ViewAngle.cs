using System.Text.Json.Serialization;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Geometry;

/// <summary>
/// The camera angles a character's skeleton and stickers carry distinct 2D art/rest
/// layouts for. Each is a genuinely different 2D bone arrangement, not one rig viewed
/// from different cameras.
/// </summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ViewAngle>))]
public enum ViewAngle
{
    Front,
    ThreeQuarter,
    Profile
}
