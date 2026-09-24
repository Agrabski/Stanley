using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>
/// The open comic's characters, as the page editor sees them: what to draw each placed
/// instance with, what the Insert tab offers, and how to create or open one. Implemented
/// by the Characters pane (<see cref="CharacterLibraryViewModel"/>).
/// </summary>
public interface ICharacterCatalog
{
    /// <summary>Every character as it looks right now - mid-drag values included, so a slider drag previews on every page live. An immutable snapshot, safe to hand to the render thread.</summary>
    IReadOnlyDictionary<CharacterId, CharacterDefinition> Characters { get; }

    /// <summary>The same characters in display order (by name).</summary>
    IReadOnlyList<CharacterDefinition> InOrder { get; }

    /// <summary>Raised whenever <see cref="Characters"/> changes - an edit, an undo, a character added or removed.</summary>
    event Action? CharactersChanged;

    /// <summary>Adds a brand-new character (undoable) and returns it. Doesn't open it.</summary>
    CharacterDefinition CreateCharacter();

    /// <summary>Shows the character's editor in place of the page.</summary>
    void OpenCharacter(CharacterId id);
}
