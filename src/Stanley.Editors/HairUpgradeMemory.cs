using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>
/// Which characters the user has told to keep their old whole hairstyle (the upgrade bar's
/// "Keep"). A preference of the person, not part of the comic, so the app keeps it in its
/// settings (<c>AppSettings.DeclinedHairUpgrades</c>); without one plugged in (tests, a
/// character editor opened without a project) it lives in memory only - the same arrangement
/// as <see cref="SystemArtEditing"/>'s SVG editor path.
/// </summary>
public sealed class HairUpgradeMemory
{
    private readonly Func<IReadOnlyCollection<string>> _get;
    private readonly Action<IReadOnlyCollection<string>> _set;

    /// <param name="get">Reads the persisted character ids; omit to keep them in memory only.</param>
    /// <param name="set">Persists the ids.</param>
    public HairUpgradeMemory(Func<IReadOnlyCollection<string>>? get = null, Action<IReadOnlyCollection<string>>? set = null)
    {
        IReadOnlyCollection<string> memory = [];
        _get = get ?? (() => memory);
        _set = set ?? (ids => memory = ids);
    }

    /// <summary>Whether the user chose to keep <paramref name="character"/>'s old hairstyle.</summary>
    public bool IsDeclined(CharacterId character) => _get().Contains(character.Value);

    /// <summary>Remembers that <paramref name="character"/> keeps its old hairstyle - for good.</summary>
    public void Decline(CharacterId character)
    {
        if (!IsDeclined(character))
            _set([.. _get(), character.Value]);
    }
}
