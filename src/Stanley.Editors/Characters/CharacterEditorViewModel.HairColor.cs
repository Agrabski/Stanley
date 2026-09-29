using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;

namespace Stanley.Editors;

/// <summary>
/// Colour for hair built from pieces (docs: modular hair): clicking a piece, a streak or a
/// whole hairstyle on the character shows its colour-and-dye dropdown on the Sticker tab, a
/// "Same as hair" button for a piece with a colour of its own, and an "Over glasses" toggle.
/// Each piece follows the Hair colour until it is given one here, and only then gets a swatch
/// in the Look tab's Colours group.
/// </summary>
public sealed partial class CharacterEditorViewModel
{
    private ColorSlotEditor? _stickerColorEditor;

    /// <summary>The colour key the selected sticker is coloured under ("hairFringe", "streak-…", "hair"), or null when it isn't worn hair.</summary>
    private string? SelectedColorKey => _selectedSticker is { } id ? HairEditing.ColorKeyOf(LookWorking, id) : null;

    /// <summary>The selected hair sticker's colour-and-dye dropdown, kept alive across edits so it stays open while you drag its sliders; null for anything that isn't worn hair.</summary>
    public ColorSlotEditor? SelectedColorEditor
    {
        get
        {
            RefreshStickerColorEditor();
            return _stickerColorEditor;
        }
    }

    /// <summary>Whether the Sticker tab shows a colour dropdown: the selected sticker is worn hair (a whole hairstyle, a piece or a streak).</summary>
    public bool HasSelectedHairColor => SelectedColorKey is not null;

    /// <summary>Brings the Sticker tab's dropdown up to date; true if it is a different one now (another piece, or none).</summary>
    private bool RefreshStickerColorEditor()
    {
        var key = SelectedColorKey;
        var changed = key != _stickerColorEditor?.Slot;
        if (changed)
            _stickerColorEditor = key is null ? null : NewColorEditor(key);
        if (_stickerColorEditor is { } editor)
            RefreshColorEditor(editor, LookWorking, CharacterLooks.Resolve(LookWorking));
        return changed;
    }

    // ---------------------------------------------------------------- Same as hair

    /// <summary>The default look while a named look is being edited (which can only override it), else null - what <see cref="HairEditing.FollowHair"/> needs.</summary>
    private CharacterDefinition? HairBaseline => _currentLook is null ? null : Committed;

    /// <summary>Whether the Sticker tab offers "Same as hair": the selected sticker is a hair piece that has a colour or dye of its own.</summary>
    public bool CanFollowHair => SelectedColorKey is { } key && StickerSlots.HairPieces.Contains(key) && HairEditing.HasOwnColor(LookWorking, key);

    /// <summary>"Same as hair": the selected piece follows the hair's colour and dye again - one undo step.</summary>
    public IRelayCommand SameAsHairCommand => field ??= new RelayCommand(FollowHair, () => CanFollowHair);

    private void FollowHair()
    {
        if (SelectedColorKey is not { } key || !CanFollowHair)
            return;
        var now = LookWorking;
        var followed = HairEditing.FollowHair(now, key, HairBaseline);
        // A named look that copied the hair colour already has nothing left to change.
        var noFabrics = new SortedDictionary<string, Fabric>();
        if (followed.ColorSlots.SequenceEqual(now.ColorSlots) && (followed.Fabrics ?? noFabrics).SequenceEqual(now.Fabrics ?? noFabrics))
            return;
        ApplyLook(c => HairEditing.FollowHair(c, key, HairBaseline));
    }

    /// <summary>
    /// Said next to "Same as hair" while a named look is edited and the default look gives the
    /// piece a colour of its own: a look can only override, so it takes a copy of the hair colour
    /// it has now instead of following it.
    /// </summary>
    public string? SameAsHairNote =>
        CanFollowHair && _currentLook is not null && SelectedColorKey is { } key && HairEditing.HasOwnColor(Committed, key)
            ? "The default look gives this its own colour, so here it copies the hair colour as it is now and won't follow later changes."
            : null;

    public bool HasSameAsHairNote => SameAsHairNote is not null;

    // ---------------------------------------------------------------- over glasses

    /// <summary>Whether the Sticker tab offers "Over glasses": the selected sticker is worn hair of the <c>hair</c> slot or a piece slot.</summary>
    public bool CanSetOverGlasses =>
        SelectedWornSticker is { } worn && (worn.Slot == StickerSlots.Hair || StickerSlots.HairPieces.Contains(worn.Slot));

    /// <summary>Whether the selected hair sticker paints over glasses (a long fringe over one eye covers that lens too) - two-way, one undo step. It edits the sticker itself, like the tab's other sticker edits, so every look shows it.</summary>
    public bool SelectedOverGlasses
    {
        get => _selectedSticker is { } id && HairEditing.IsOverGlasses(Working, id);
        set
        {
            if (_selectedSticker is not { } id || value == SelectedOverGlasses)
                return;
            Apply(EditResult<CharacterDefinition>.Success(HairEditing.SetOverGlasses(Committed, id, value)));
        }
    }

    private void RaiseHairColorChanged()
    {
        if (RefreshStickerColorEditor())
            OnPropertyChanged(nameof(SelectedColorEditor));
        OnPropertyChanged(nameof(HasSelectedHairColor));
        OnPropertyChanged(nameof(CanFollowHair));
        OnPropertyChanged(nameof(SameAsHairNote));
        OnPropertyChanged(nameof(HasSameAsHairNote));
        OnPropertyChanged(nameof(CanSetOverGlasses));
        OnPropertyChanged(nameof(SelectedOverGlasses));
        SameAsHairCommand.NotifyCanExecuteChanged();
    }
}
