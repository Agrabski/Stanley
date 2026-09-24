using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>A look in the character editor's Look dropdown: the default, or a named look.</summary>
public sealed record LookItem(string Name, CharacterRevisionId? Id, bool IsCurrent, CharacterDefinition Preview);

/// <summary>
/// Named looks (docs/sticker-system.md §13.1; revisions in the code): the Look tab edits
/// one look at a time - the default, or a named one such as "Winter" - and the stage
/// shows it. A named look keeps only what differs from the default, so changing the
/// default still reaches it wherever it agrees.
/// </summary>
public sealed partial class CharacterEditorViewModel
{
    private CharacterRevisionId? _currentLook;
    private CharacterDefinition? _lookWorking;

    public IRelayCommand<LookItem> ShowLookCommand { get; private set; } = null!;
    public IRelayCommand NewLookCommand { get; private set; } = null!;
    public IRelayCommand DeleteLookCommand { get; private set; } = null!;

    private void InitializeLooks()
    {
        ShowLookCommand = new RelayCommand<LookItem>(item =>
        {
            if (item != null)
                CurrentLook = item.Id;
        });
        NewLookCommand = new RelayCommand(NewLook);
        DeleteLookCommand = new RelayCommand(DeleteCurrentLook, () => CanDeleteCurrentLook);
    }

    /// <summary>The look the Look tab edits and the stage shows: null for the default, else a named look.</summary>
    public CharacterRevisionId? CurrentLook
    {
        get => _currentLook;
        set
        {
            if (value is { } id && !Working.Revisions.ContainsKey(id))
                value = null;
            if (_currentLook == value)
                return;
            _currentLook = value;
            RaiseLookChanged();
        }
    }

    /// <summary>The character dressed in the current look, flattened - what the stage and the Look tab's galleries and colours show.</summary>
    public CharacterDefinition LookWorking => _lookWorking ??= ProjectLook(Working);

    /// <summary>The default look, then each named look.</summary>
    public IReadOnlyList<LookItem> Looks =>
        new[] { new LookItem("Default", null, _currentLook is null, Working) }
            .Concat(Working.Revisions.Values.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => new LookItem(r.Name, r.Id, _currentLook == r.Id, LookEditing.Project(Working, r))))
            .ToList();

    public bool IsNamedLook => _currentLook is not null;

    /// <summary>The current look's name; renaming a named look is one undo step (the default look is always "Default").</summary>
    public string CurrentLookName
    {
        get => _currentLook is { } id && Working.Revisions.TryGetValue(id, out var r) ? r.Name : "Default";
        set
        {
            if (_currentLook is { } id)
                Apply(EditResult<CharacterDefinition>.Success(LookEditing.RenameLook(Committed, id, value)));
            OnPropertyChanged(); // a blank name is refused: show the real one again
        }
    }

    /// <summary>How many panels (and issues) use a look - set by whoever can see the pages; a look in use can't be deleted.</summary>
    private int LookUsage(CharacterRevisionId look) => Library?.LookUsageCounter?.Invoke(CharacterId, look) ?? 0;

    public bool CanDeleteCurrentLook => _currentLook is { } id && LookUsage(id) == 0;

    /// <summary>Why the current look can't be deleted, for its tooltip, or null.</summary>
    public string DeleteLookTip => _currentLook is { } id && LookUsage(id) is var n && n > 0
        ? $"Used in {n} {(n == 1 ? "place" : "places")} - pick another look there first"
        : "Delete this look (what it wears stays in the wardrobe)";

    /// <summary>A new look, starting as a copy of the current one, shown at once. One undo step.</summary>
    public void NewLook()
    {
        var names = Working.Revisions.Values.Select(r => r.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var n = Working.Revisions.Count + 2;
        while (names.Contains($"Look {n}"))
            n++;
        var (character, id) = LookEditing.NewLook(Committed, $"Look {n}", _currentLook);
        _currentLook = id;
        Apply(EditResult<CharacterDefinition>.Success(character));
    }

    private void DeleteCurrentLook()
    {
        if (_currentLook is not { } id || !CanDeleteCurrentLook)
            return;
        _currentLook = null;
        Apply(EditResult<CharacterDefinition>.Success(LookEditing.DeleteLook(Committed, id)));
    }

    /// <summary>The character dressed in the current look (flattened), from <paramref name="character"/>.</summary>
    private CharacterDefinition ProjectLook(CharacterDefinition character) =>
        _currentLook is { } id && character.Revisions.TryGetValue(id, out var revision) ? LookEditing.Project(character, revision) : character;

    /// <summary><paramref name="edit"/> applied to the current look of <paramref name="baseline"/>: directly for the default look, through the named look's sparse changes otherwise.</summary>
    private CharacterDefinition StoreLookEdit(CharacterDefinition baseline, Func<CharacterDefinition, CharacterDefinition> edit) =>
        _currentLook is { } id && baseline.Revisions.TryGetValue(id, out var revision)
            ? LookEditing.StoreLook(baseline, id, edit(LookEditing.Project(baseline, revision)))
            : edit(baseline);

    /// <summary>A dressing edit to the current look, as one undo step.</summary>
    private void ApplyLook(Func<CharacterDefinition, CharacterDefinition> edit) =>
        Apply(EditResult<CharacterDefinition>.Success(StoreLookEdit(Committed, edit)));

    private void RaiseLooksChanged()
    {
        OnPropertyChanged(nameof(CurrentLook));
        OnPropertyChanged(nameof(Looks));
        OnPropertyChanged(nameof(IsNamedLook));
        OnPropertyChanged(nameof(CurrentLookName));
        OnPropertyChanged(nameof(CanDeleteCurrentLook));
        OnPropertyChanged(nameof(DeleteLookTip));
        DeleteLookCommand.NotifyCanExecuteChanged();
    }
}
