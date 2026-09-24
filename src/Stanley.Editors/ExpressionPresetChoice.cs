using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editors;

/// <summary>One preset in an expression gallery, with the pose that shows it on the character (for its close-up preview).</summary>
public sealed record ExpressionPresetChoice(ExpressionPresetDefinition Preset, CharacterDefinition Character, PoseData Pose, bool IsCurrent)
{
    public string Name => Preset.Name;
}
