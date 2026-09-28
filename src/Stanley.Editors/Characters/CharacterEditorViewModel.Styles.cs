using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>
/// Styles (docs/sticker-system.md §20): the ways to wear the selected sticker - a hood up or
/// down, a cap's brim forward, back or to a side - on the Sticker tab's "Worn" gallery. A
/// style is one of the sticker's variants, chosen per sticker and kept with the look being
/// edited (the default look, or a named one), so a cap and a hood worn together each keep
/// their own. Faces have none here: their variants are expressions, set per panel.
/// </summary>
public sealed partial class CharacterEditorViewModel
{
    /// <summary>Wears the selected sticker in a style from the "Worn" gallery: one undo step, in the look being edited.</summary>
    public IRelayCommand<StickerStyleChoice> SetStyleCommand => field ??= new RelayCommand<StickerStyleChoice>(choice =>
    {
        if (choice != null)
            SetStyle(choice.Sticker, choice.Variant);
    });

    /// <summary>The selected sticker as the current look wears it (its slot and chosen style), or null if it isn't worn.</summary>
    private WornSticker? SelectedWornSticker =>
        _selectedSticker is { } id ? CharacterLooks.Resolve(LookWorking).Stickers.FirstOrDefault(w => w.Asset.Id == id) : null;

    /// <summary>Whether the Sticker tab shows the "Worn" gallery: the selected sticker is worn, outside the face, and has more than one variant.</summary>
    public bool HasSelectedStickerStyles => SelectedWornSticker is { } worn && LookEditing.HasStyles(worn.Asset.Sticker, worn.Slot);

    /// <summary>Every way to wear the selected sticker, each previewed on this character wearing it that way, the current one marked - empty when it has no styles.</summary>
    public IReadOnlyList<StickerStyleChoice> SelectedStickerStyles
    {
        get
        {
            if (SelectedWornSticker is not { } worn || !LookEditing.HasStyles(worn.Asset.Sticker, worn.Slot))
                return [];
            var character = LookWorking;
            var pose = StagePose;
            var shown = ShownStyle(worn);
            var closeup = StickerSlots.Get(worn.Slot).Region == BodyRegion.Head;
            return worn.Asset.Sticker.Variants
                .Select(v => new StickerStyleChoice(worn.Asset.Id, v, LookEditing.StyleName(v), LookEditing.SetVariant(character, worn.Asset.Id, v), v == shown, closeup, pose, PreviewAngle))
                .ToList();
        }
    }

    /// <summary>The style the selected sticker is shown in ("Brim back"), or "" when it has no styles.</summary>
    public string SelectedStickerStyleName =>
        SelectedWornSticker is { } worn && LookEditing.HasStyles(worn.Asset.Sticker, worn.Slot) ? LookEditing.StyleName(ShownStyle(worn)) : "";

    /// <summary>Wears sticker <paramref name="id"/> in <paramref name="variant"/> in the look being edited, as one undo step; nothing if it's already worn that way.</summary>
    public void SetStyle(StickerId id, string variant)
    {
        var worn = CharacterLooks.Resolve(LookWorking).Stickers.FirstOrDefault(w => w.Asset.Id == id);
        if (worn is null || !worn.Asset.Sticker.Variants.Contains(variant) || ShownStyle(worn) == variant)
            return;
        ApplyLook(c => LookEditing.SetVariant(c, id, variant));
    }

    /// <summary>The variant a worn sticker shows on the stage: its style, unless the previewed expression picks one (faces).</summary>
    private string ShownStyle(WornSticker worn) => worn.Asset.Sticker.VariantFor(worn.Slot, StagePose?.Expression, worn.Variant);

    /// <summary>The style the current look wears sticker <paramref name="id"/> in, or null for its default way.</summary>
    private string? ChosenStyle(StickerId id) => LookWorking.StickerVariants?.GetValueOrDefault(id);

    private void RaiseStylesChanged()
    {
        OnPropertyChanged(nameof(HasSelectedStickerStyles));
        OnPropertyChanged(nameof(SelectedStickerStyles));
        OnPropertyChanged(nameof(SelectedStickerStyleName));
    }
}
