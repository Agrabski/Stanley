using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editors;

/// <summary>One preset in the pose gallery, with the pose it gives the selected character (for its preview).</summary>
public sealed record PosePresetChoice(PosePresetDefinition Preset, CharacterDefinition Character, PoseData Pose)
{
    public string Name => Preset.Name;
}
