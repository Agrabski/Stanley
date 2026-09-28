using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Objects;
using Stanley.ProjectModel.Storage;

namespace Stanley.Editors;

/// <summary>Whether a group on the page is in My Assets, and whether it still matches (docs/asset-packs.md §6.1) - what its right-click menu offers.</summary>
public enum KeptState
{
    /// <summary>Not in My Assets: <i>Keep in My Assets</i>.</summary>
    NotKept,

    /// <summary>In My Assets and the same as there: nothing to do.</summary>
    Kept,

    /// <summary>In My Assets, but changed here since: <i>Save to My Assets</i>.</summary>
    ChangedHere
}

/// <summary>
/// Object groups and My Assets (docs/asset-packs.md §6.1, §6.3): keeping a group from the
/// page, and putting a kept one on it.
/// </summary>
public sealed partial class PageEditorViewModel
{
    private IReadOnlyList<ObjectGroup>? _myAssetsGroups;

    /// <summary>The user's My Assets; null for a page edited without one, which then offers neither keeping nor inserting.</summary>
    public MyAssetsLibrary? MyAssets
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;
            if (field != null)
                field.Changed -= OnMyAssetsChanged;
            field = value;
            if (field != null)
                field.Changed += OnMyAssetsChanged;
            OnMyAssetsChanged();
        }
    }

    /// <summary>The comic's own copies of kept object groups (its <c>objects/</c> folder); a kept group's copy goes here, to be saved with the comic.</summary>
    public ObjectGroupLibrary? ObjectGroups { get; set; }

    public bool HasMyAssets => MyAssets != null;

    /// <summary>Every object group kept in My Assets - Insert › From My Assets. Read when first asked for and again after My Assets changes.</summary>
    public IReadOnlyList<ObjectGroup> MyAssetsObjectGroups => _myAssetsGroups ??= MyAssets?.ObjectGroups() ?? [];

    public bool HasMyAssetsObjectGroups => MyAssetsObjectGroups.Count > 0;

    public IRelayCommand KeepGroupInMyAssetsCommand { get; private set; } = null!;
    public IRelayCommand<ObjectGroup?> InsertObjectGroupCommand { get; private set; } = null!;

    private void InitializeMyAssetsCommands()
    {
        KeepGroupInMyAssetsCommand = new RelayCommand(() => KeepGroupInMyAssets(), () => MyAssets != null && !HasMultiSelection && SelectedElement is GroupElement);
        InsertObjectGroupCommand = new RelayCommand<ObjectGroup?>(group =>
        {
            if (group != null)
                InsertObjectGroup(group);
        }, group => group != null && Working.PanelOrder.Count > 0);
    }

    private void NotifyMyAssetsCommands()
    {
        KeepGroupInMyAssetsCommand?.NotifyCanExecuteChanged();
        InsertObjectGroupCommand?.NotifyCanExecuteChanged();
    }

    private void OnMyAssetsChanged()
    {
        _myAssetsGroups = null;
        OnPropertyChanged(nameof(HasMyAssets));
        OnPropertyChanged(nameof(MyAssetsObjectGroups));
        OnPropertyChanged(nameof(HasMyAssetsObjectGroups));
        NotifyMyAssetsCommands();
    }

    /// <summary>Whether the selected group is kept in My Assets and still matches it; <see cref="KeptState.NotKept"/> for anything else.</summary>
    public KeptState SelectedGroupKeptState()
    {
        if (SelectedElement is not GroupElement { SourceId: { } id } group || MyAssets?.FindObjectGroup(id) is null)
            return KeptState.NotKept;
        if (ObjectGroups?.Groups.TryGetValue(id, out var copy) != true || copy!.MyAssetsVersion is not { } version)
            return KeptState.ChangedHere;
        var now = ObjectGroupLibrary.FromElement(group, id, copy.Name, PictureSnapshot);
        return AssetFingerprint.ObjectGroupFingerprint(now) == version ? KeptState.Kept : KeptState.ChangedHere;
    }

    /// <summary>
    /// Right-click › Keep in My Assets (or Save to My Assets) on a group: writes it to My
    /// Assets straight away, with the pictures it shows, and gives the comic its own copy
    /// (<c>objects/</c>) recording which version it matches. A group that isn't kept yet gets
    /// a name ("Group 3", renamed on File › My Assets) and is linked to it by id - one undo
    /// step on the page; undoing it leaves My Assets as it is. Returns what was kept, or null.
    /// </summary>
    public ObjectGroup? KeepGroupInMyAssets()
    {
        if (MyAssets is not { } myAssets || _selectedPanelId is not { } panelId || SelectedElement is not GroupElement group)
            return null;
        var index = _selectedElementIndex;

        var id = group.SourceId ?? ObjectGroupId.New();
        var name = ObjectGroups?.Groups.GetValueOrDefault(id)?.Name
            ?? myAssets.FindObjectGroup(id)?.Name
            ?? myAssets.NextObjectGroupName(ObjectGroups?.Groups.Values.Select(g => g.Name));

        ObjectGroup kept;
        try
        {
            kept = myAssets.KeepObjectGroup(ObjectGroupLibrary.FromElement(group, id, name, PictureSnapshot));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Apply(EditResult<PageDocument>.Failure($"Couldn't keep it in My Assets: {e.Message}"));
            return null;
        }
        ObjectGroups?.Put(kept);

        if (group.SourceId != id)
        {
            Apply(EditPanel(Working, panelId, p =>
                index >= 0 && index < p.Elements.Count && p.Elements[index] is GroupElement current
                    ? EditResult<Panel>.Success(p with { Elements = [.. p.Elements.Take(index), current with { SourceId = id }, .. p.Elements.Skip(index + 1)] })
                    : EditResult<Panel>.Failure("That group isn't there any more.")));
            SelectElement(panelId, index);
        }
        return kept;
    }

    /// <summary>
    /// Insert › From My Assets: a copy of a kept object group in the selected panel (else the
    /// first), centred in it - shrunk to fit if it's bigger - stepped aside from anything in
    /// that spot, and selected; one undo step, as Paste is. It keeps pointing at the kept
    /// group by id, and the comic gets its own copy of it (<c>objects/</c>). False if nothing
    /// was inserted.
    /// </summary>
    public bool InsertObjectGroup(ObjectGroup group)
    {
        var target = _selectedPanelId is { } selected && Working.Panels.ContainsKey(selected) ? selected
            : Working.PanelOrder.Count > 0 ? Working.PanelOrder[0]
            : (PanelId?)null;
        if (target is not { } panelId || group.Children.Count == 0)
            return false;

        var bounds = Bounds(Working.Panels[panelId]);
        PanelElement element = ObjectGroupLibrary.ToElement(group);
        var box = PanelElements.Bounds(element);
        var fit = Math.Min(1, Math.Min(bounds.Width * 0.8 / Math.Max(box.Width, 1e-9), bounds.Height * 0.8 / Math.Max(box.Height, 1e-9)));
        if (fit < 1 && ElementEditing.Resize(element, new(box.X, box.Y, box.Width * fit, box.Height * fit)) is { IsValid: true } shrunk)
            element = shrunk.Value;
        box = PanelElements.Bounds(element);
        element = ElementEditing.Move(element, bounds.X + (bounds.Width - box.Width) / 2 - box.X, bounds.Y + (bounds.Height - box.Height) / 2 - box.Y);

        if (!PutBack(new ElementClipping(panelId, bounds, element), group.ArtFiles, panelId))
            return false;
        ObjectGroups?.Put(group);
        return true;
    }
}
