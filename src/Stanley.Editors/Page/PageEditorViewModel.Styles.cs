using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>One style of a worn sticker on right-click › This panel only › Style (docs/sticker-system.md §20).</summary>
public sealed record PanelStyleChoice(string Variant, string Label, bool IsCurrent);

/// <summary>A worn sticker that has styles (a hood, a cap), with every way to wear it, for right-click › This panel only › Style.</summary>
public sealed record PanelStickerStyles(StickerId Sticker, string Name, IReadOnlyList<PanelStyleChoice> Styles);

/// <summary>
/// Styles for one panel only: the hood up for one shot, the cap turned backwards on one
/// panel. Stored, like every "this panel only" change, as the panel's differences from its look.
/// </summary>
public sealed partial class PageEditorViewModel
{
    /// <summary>
    /// The worn stickers of the character at <paramref name="index"/> in <paramref name="panelId"/>
    /// that have styles (not faces: their variants are expressions), each with its styles and
    /// the one this panel shows. Empty if there are none.
    /// </summary>
    public IReadOnlyList<PanelStickerStyles> PanelStyles(PanelId panelId, int index)
    {
        if (PanelView(panelId, index) is not { } shown)
            return [];
        var expression = Working.Panels[panelId].CharacterInstances[index].Pose.Expression;
        return CharacterLooks.Resolve(shown).Stickers
            .Where(w => LookEditing.HasStyles(w.Asset.Sticker, w.Slot))
            .Select(w =>
            {
                var current = w.Asset.Sticker.VariantFor(w.Slot, expression, w.Variant);
                return new PanelStickerStyles(w.Asset.Id, w.Asset.Sticker.Name,
                    w.Asset.Sticker.Variants.Select(v => new PanelStyleChoice(v, LookEditing.StyleName(v), v == current)).ToList());
            })
            .ToList();
    }

    /// <summary>
    /// Wears sticker <paramref name="sticker"/> in <paramref name="variant"/> in this panel only,
    /// as one undo step ("Back to the look" undoes it with the rest); nothing if the panel
    /// already shows it that way.
    /// </summary>
    public void SetPanelStyle(PanelId panelId, int index, StickerId sticker, string variant)
    {
        if (PanelStyles(panelId, index).FirstOrDefault(s => s.Sticker == sticker) is not { } styles
            || styles.Styles.FirstOrDefault(s => s.Variant == variant) is not { IsCurrent: false })
            return;
        EditPanelLook(panelId, index, c => LookEditing.SetVariant(c, sticker, variant));
    }
}
