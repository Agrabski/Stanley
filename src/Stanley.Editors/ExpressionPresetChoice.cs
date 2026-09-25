using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Poses;

namespace Stanley.Editors;

/// <summary>One preset in an expression gallery, with the pose that shows it on the character (for its close-up preview).</summary>
public sealed record ExpressionPresetChoice(ExpressionPresetDefinition Preset, CharacterDefinition Character, PoseData Pose, bool IsCurrent)
{
    public string Name => Preset.Name;
}

/// <summary>
/// One face slot's row in the expression gallery's "mix your own" part: every variant of
/// the slot, each previewed with the rest of the character's face as it is now.
/// </summary>
public sealed record ExpressionSlotRow(string Slot, string Label, IReadOnlyList<ExpressionVariantChoice> Choices);

/// <summary>One variant of a face slot in a <see cref="ExpressionSlotRow"/>, with the pose that shows it (for its close-up preview).</summary>
public sealed record ExpressionVariantChoice(string Slot, string Variant, string Name, CharacterDefinition Character, PoseData Pose, bool IsCurrent);
