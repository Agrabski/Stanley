using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>
/// A look on offer on the page's Character tab - for this panel (<see cref="Look"/> null =
/// whatever the issue uses) or for the whole issue (null = the default look) - with the
/// character dressed in it for its preview.
/// </summary>
public sealed record LookChoice(string Name, CharacterRevisionId? Look, bool IsCurrent, CharacterDefinition Preview);
