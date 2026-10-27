using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Poses;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// Builds a <see cref="BodyFigure"/> from a <see cref="ModularBody"/> (docs/shape-characters.md §5):
/// the same joints, layers and regions <see cref="BodyRig"/> gives the mannequin, so posing,
/// stickers and drawing work on it unchanged.
/// </summary>
public static class ModularRig
{
    public static BodyFigure Build(ModularBody body, ViewAngle angle = ViewAngle.Front, PoseData? pose = null) =>
        throw new NotImplementedException("ModularRig.Build: docs/shape-characters.md slice 2");

    /// <summary>How tall the assembly stands: its top above the ground point.</summary>
    public static double Height(ModularBody body) => -Build(body).Extent.Top;
}
