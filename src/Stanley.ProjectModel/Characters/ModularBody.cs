using System.Text.Json.Serialization;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Serialization;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// A body built from shapes instead of generated from sliders (docs/shape-characters.md):
/// a head, a torso, a pair of arms and a pair of legs, any of them left out (null). When a
/// character has one (<see cref="CharacterDefinition.Modules"/>), <see cref="ModularRig"/>
/// builds its figure instead of <see cref="BodyRig"/>; everything downstream (posing,
/// stickers, drawing) reads the same <see cref="BodyFigure"/>.
/// </summary>
/// <param name="Separate">False (null): the pieces join seamlessly, one creature. True: every piece keeps its own full outline, like building blocks.</param>
public sealed record ModularBody(
    ShapeModule? Head,
    ShapeModule? Torso,
    LimbModule? Arms,
    LimbModule? Legs,
    bool? Separate = null)
{
    [JsonIgnore]
    public bool IsSeparate => Separate == true;
}

/// <summary>
/// A head or a torso: an outline (<see cref="ModuleShape"/>, in its own unit box) drawn
/// <paramref name="Width"/> by <paramref name="Height"/> figure units (1.0 = an average
/// adult's height) and sat on its socket.
/// </summary>
/// <param name="Rotation">Degrees clockwise about the shape's centre; null = 0.</param>
/// <param name="FlipX">Mirrored left to right; null = no.</param>
/// <param name="FlipY">Upside down (the "upside-down triangle" torso); null = no.</param>
/// <param name="Offset">A nudge off the socket, figure units; null = none.</param>
/// <param name="Color">The colour slot it's filled with; null = <see cref="CharacterDefinition.SkinSlot"/>.</param>
public sealed record ShapeModule(
    ModuleShape Shape,
    double Width,
    double Height,
    double? Rotation = null,
    bool? FlipX = null,
    bool? FlipY = null,
    Point2D? Offset = null,
    string? Color = null)
{
    [JsonIgnore]
    public string ColorSlot => Color ?? CharacterDefinition.SkinSlot;
}

/// <summary>
/// A closed outline in a unit box - (0,0) top left to (1,1) bottom right, y down - and the
/// sockets other modules attach to. Sockets not listed are derived from the outline
/// (<see cref="ModuleSockets"/>), so a shape needs no setup to work.
/// </summary>
/// <param name="Sockets">Socket name (<see cref="ModuleSockets"/>) -&gt; its point in the unit box, only for those dragged by hand; null = all derived.</param>
/// <param name="Source">"library:shapes/&lt;kind&gt;/&lt;category&gt;/&lt;name&gt;" while it's an unmodified library copy; null once changed or for one's own.</param>
public sealed record ModuleShape(
    IReadOnlyList<ShapeAnchor> Anchors,
    SortedDictionary<string, Point2D>? Sockets = null,
    string? Source = null);

/// <summary>How a <see cref="LimbModule"/> is drawn along its two bones.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<LimbStyle>))]
public enum LimbStyle
{
    /// <summary>A round tube of even thickness, bending at the elbow or knee.</summary>
    Tube,

    /// <summary>A tube thinning towards the hand or foot by <see cref="LimbModule.Taper"/>.</summary>
    Taper,

    /// <summary>Two rigid pieces, one per bone (<see cref="LimbModule.Upper"/>, <see cref="LimbModule.Lower"/>; a capsule when absent).</summary>
    Pieces,

    /// <summary>A thin line, whatever <see cref="LimbModule.Thickness"/> says: a stick figure's.</summary>
    Stick
}

/// <summary>Which of a pair of limbs a character has.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<LimbSides>))]
public enum LimbSides
{
    Both,
    Left,
    Right
}

/// <summary>
/// A pair of arms or legs: two bones each, so they pose by inverse kinematics like the
/// mannequin's, drawn in a <see cref="LimbStyle"/> with an optional end shape (a hand or a foot).
/// </summary>
/// <param name="Length">Root (shoulder, hip) to end (wrist, ankle), figure units.</param>
/// <param name="Thickness">The limb's width at its root, figure units.</param>
/// <param name="Taper">For <see cref="LimbStyle.Taper"/>: how much thinner the end is than the root, 0-0.9; null = 0.5.</param>
/// <param name="Bend">Where the elbow or knee sits along the length, 0.2-0.8; null = 0.5.</param>
/// <param name="End">The hand or foot shape (unit box, its top centre at the wrist or ankle); null = the limb's own round end.</param>
/// <param name="EndSize">The side of the square box the end shape is hung in from the wrist or ankle (it keeps its own proportions inside it), figure units; null = twice the thickness.</param>
/// <param name="Upper">For <see cref="LimbStyle.Pieces"/>: the upper bone's shape (unit box, top centre at the root); null = a capsule.</param>
/// <param name="Lower">For <see cref="LimbStyle.Pieces"/>: the lower bone's shape; null = a capsule.</param>
/// <param name="Sides">Both limbs, or just one (the character's own side); null = both.</param>
/// <param name="Color">The colour slot; null = <see cref="CharacterDefinition.SkinSlot"/>.</param>
public sealed record LimbModule(
    LimbStyle Style,
    double Length,
    double Thickness,
    double? Taper = null,
    double? Bend = null,
    ModuleShape? End = null,
    double? EndSize = null,
    ModuleShape? Upper = null,
    ModuleShape? Lower = null,
    LimbSides? Sides = null,
    string? Color = null)
{
    [JsonIgnore]
    public string ColorSlot => Color ?? CharacterDefinition.SkinSlot;

    public bool Has(LimbSide side) => (Sides ?? LimbSides.Both) switch
    {
        LimbSides.Left => side == LimbSide.Left,
        LimbSides.Right => side == LimbSide.Right,
        _ => true
    };
}

/// <summary>
/// The sockets a head or torso shape offers other modules (docs/shape-characters.md §4.2),
/// by the names <see cref="ModuleShape.Sockets"/> stores them under.
/// </summary>
public static partial class ModuleSockets
{
    /// <summary>Top centre: where a head sits on a torso, and a torso's top under a head.</summary>
    public const string Neck = "neck";

    public const string LeftShoulder = "leftShoulder";
    public const string RightShoulder = "rightShoulder";
    public const string LeftHip = "leftHip";
    public const string RightHip = "rightHip";

    /// <summary>The centre of the face area (the <see cref="BodyRegion.Head"/> region): the head's, or a head-less torso's.</summary>
    public const string Face = "face";

    public static IReadOnlyList<string> All { get; } = [Neck, LeftShoulder, RightShoulder, LeftHip, RightHip, Face];
}
