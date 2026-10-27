using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Poses;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// The one place that knows a character can have either kind of body: generated from
/// sliders (<see cref="BodyRig"/>) or built from shapes (<see cref="ModularRig"/>, when
/// <see cref="CharacterDefinition.Modules"/> is set). Everything that builds a character's
/// figure goes through here, so posing, drawing and the editors never need to ask which.
/// </summary>
public static class CharacterRig
{
    /// <summary>The character's figure seen from <paramref name="angle"/>, posed by <paramref name="pose"/> (null stands at rest).</summary>
    public static BodyFigure Build(CharacterDefinition character, ViewAngle angle = ViewAngle.Front, PoseData? pose = null) =>
        character.Modules is { } modules
            ? ModularRig.Build(modules, angle, pose)
            : BodyRig.Build(character.Body, angle, character.Skeleton, pose);

    /// <summary>The character's bare body's bounding box in figure space (no stickers).</summary>
    public static Rect2D Extent(CharacterDefinition character, ViewAngle angle = ViewAngle.Front, PoseData? pose = null) =>
        Build(character, angle, pose).Extent;

    /// <summary>
    /// How tall the character stands, relative to an average adult (1.0) - the slider for a
    /// generated body, the top of the assembly above the ground for one built from shapes.
    /// </summary>
    public static double Height(CharacterDefinition character) =>
        character.Modules is { } modules
            ? ModularRig.Height(modules)
            : character.Body.Normalized().Height;
}
