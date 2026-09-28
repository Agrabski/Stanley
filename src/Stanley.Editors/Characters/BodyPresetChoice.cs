using Stanley.ProjectModel.Characters;
namespace Stanley.Editors;

/// <summary>One body type in the ribbon's gallery, with a preview figure.</summary>
public sealed record BodyPresetChoice(BodyPreset Preset, string Name, CharacterDefinition Preview);