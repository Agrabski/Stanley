using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;
namespace Stanley.Editors;

/// <summary>A look in the character editor's Look dropdown: the default, or a named look.</summary>
public sealed record LookItem(string Name, CharacterRevisionId? Id, bool IsCurrent, CharacterDefinition Preview);