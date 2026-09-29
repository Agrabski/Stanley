using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

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

    /// <summary>The colour key the selected sticker is coloured under ("hairFringe", "streak-…", "hair"), or null when the sticker has no colour of its own to pick.</summary>
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
    public bool CanFollowHair =>
        SelectedColorKey is { } key && (StickerSlots.HairPieces.Contains(key) || StickerSlots.IsStickerColorKey(key)) && HairEditing.HasOwnColor(LookWorking, key);

    /// <summary>The Sticker tab's colour group: "Hair" for hair, "Colour" for any other sticker with a colour of its own to pick.</summary>
    public string SelectedColorGroupLabel => SelectedColorKey is { } key && StickerSlots.IsStickerColorKey(key) ? "Colour" : "Hair";

    /// <summary>What the "back to shared" button says: the hair for a piece, the slot for a sticker (a hat follows every hat's colour).</summary>
    public string FollowLabel => SelectedColorKey is { } key && StickerSlots.IsStickerColorKey(key) ? "Same as slot" : "Same as hair";

    public string FollowTip => SelectedColorKey is { } key && StickerSlots.IsStickerColorKey(key)
        ? $"Drop its own colour, so it follows the {ColorSlotLabel(StickerSlots.SharedColorOf(key))} colour again"
        : "Drop its own colour and dye, so it follows the Hair colour again";

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
            ? "The default look gives this its own colour, so here it copies the shared colour as it is now and won't follow later changes."
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

    // ---------------------------------------------------------------- schemes

    private ColorValue _hairAccent = ColorSlotEditor.DefaultDyeColor;

    /// <summary>The second colour the Hair colour's schemes use - a vivid purple until another is picked. Only a preview setting: picking it changes nothing on the character.</summary>
    public ColorValue HairAccent => _hairAccent;

    private static readonly (HairScheme Scheme, string Label)[] SchemeLabels =
    [
        (HairScheme.Natural, "Natural"), (HairScheme.TwoTone, "Two-tone"), (HairScheme.Peekaboo, "Peekaboo"), (HairScheme.FringeOnly, "Fringe only"),
        (HairScheme.DipDye, "Dip-dye"), (HairScheme.Ombre, "Ombré"), (HairScheme.Rainbow, "Rainbow"),
    ];

    /// <summary>Every scheme, each as a close-up of this character with it applied in the current accent colour.</summary>
    internal IReadOnlyList<HairSchemeChoice> HairSchemeChoices
    {
        get
        {
            var character = LookWorking;
            var pose = StagePose;
            return SchemeLabels.Select(s => new HairSchemeChoice(s.Scheme, s.Label, HairEditing.ApplyScheme(character, s.Scheme, _hairAccent, HairBaseline), pose, PreviewAngle)).ToList();
        }
    }

    /// <summary>A scheme click: the hair (and its pieces) dressed in it, one undo step. Every scheme starts from Natural, so picking one after another never piles up.</summary>
    internal void ApplyHairScheme(HairScheme scheme) => ApplyLook(c => HairEditing.ApplyScheme(c, scheme, _hairAccent, HairBaseline));

    internal void SetHairAccent(ColorValue color)
    {
        if (color == _hairAccent)
            return;
        _hairAccent = color;
        foreach (var editor in _colorEditors.Append(_stickerColorEditor).OfType<ColorSlotEditor>())
            editor.RefreshSchemes();
    }

    private void RaiseHairColorChanged()
    {
        if (RefreshStickerColorEditor())
            OnPropertyChanged(nameof(SelectedColorEditor));
        OnPropertyChanged(nameof(HasSelectedHairColor));
        OnPropertyChanged(nameof(SelectedColorGroupLabel));
        OnPropertyChanged(nameof(FollowLabel));
        OnPropertyChanged(nameof(FollowTip));
        OnPropertyChanged(nameof(CanFollowHair));
        OnPropertyChanged(nameof(SameAsHairNote));
        OnPropertyChanged(nameof(HasSameAsHairNote));
        OnPropertyChanged(nameof(CanSetOverGlasses));
        OnPropertyChanged(nameof(SelectedOverGlasses));
        SameAsHairCommand.NotifyCanExecuteChanged();
    }
}
