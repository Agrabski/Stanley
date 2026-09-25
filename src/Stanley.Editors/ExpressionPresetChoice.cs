using CommunityToolkit.Mvvm.Input;
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
/// One face slot's row in the expression gallery's "mix your own" part: every variant the
/// face worn there draws, each previewed with the rest of the character's face as it is now.
/// A drawn face can get a new variant (<paramref name="DrawNew"/>, a copy of the one shown)
/// or have the one shown redrawn (<paramref name="EditDrawing"/>), in the user's SVG editor.
/// </summary>
public sealed record ExpressionSlotRow(string Slot, string Label, IReadOnlyList<ExpressionVariantChoice> Choices, bool CanDraw,
    IRelayCommand DrawNew, IRelayCommand EditDrawing);

/// <summary>One variant of a face slot in a <see cref="ExpressionSlotRow"/>, with the pose that shows it (for its close-up preview).</summary>
public sealed record ExpressionVariantChoice(string Slot, string Variant, string Name, CharacterDefinition Character, PoseData Pose, bool IsCurrent);

/// <summary>A face saved on the character, in the Face dropdown: a close-up of it, and what a click (or right-click › Delete) does.</summary>
public sealed record SavedFaceChoice(SavedExpression Face, CharacterDefinition Character, PoseData Pose, bool IsCurrent, IRelayCommand Apply, IRelayCommand Delete)
{
    public string Name => Face.Name;
}
