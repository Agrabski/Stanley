using Stanley.Editing;
using Stanley.ProjectModel.Ids;
using Stanley.Rendering;

namespace Stanley.Editors;

// The font box (Home, Bubble and Text tabs): like Word's, it shows the selection's font -
// a bubble's or a text's - and picking one changes the selection and becomes the font new
// ones get. With nothing selected it sets the font for both new bubbles and new text.
public sealed partial class PageEditorViewModel
{
    private string? _newBubbleFont;
    private object? _fontKey;

    /// <summary>Every font the box offers: Stanley's own first, then the ones on this computer.</summary>
    public IReadOnlyList<FontChoice> FontChoices => LetteringFonts.Choices;

    /// <summary>The font of the selected bubble or text, or the one the next new one gets (null: the default lettering font).</summary>
    public string? CurrentFontFamily
    {
        get
        {
            if (SelectedBubble is { } bubble)
                return bubble.FontFamily;
            if (SelectedText is { } text)
                return text.Style.FontFamily;
            return Tool == PageEditorTool.Text ? _newTextStyle.FontFamily : _newBubbleFont;
        }
        set
        {
            var family = LetteringFonts.Stored(value);
            if (SelectedBubble is { } bubble && _selectedPanelId is { } panelId)
            {
                _newBubbleFont = family;
                if (bubble.FontFamily != family)
                    SetBubbleFont(panelId, _selectedBubbleIndex, family);
            }
            else if (SelectedText is not null)
            {
                SetCurrentTextStyle(CurrentTextStyle with { FontFamily = family });
            }
            else
            {
                _newBubbleFont = family;
                _newTextStyle = _newTextStyle with { FontFamily = family };
            }
            RaiseFontChanged();
        }
    }

    /// <summary>The font box's selection; null while the current font isn't on this computer (then <see cref="FontPlaceholder"/> names it).</summary>
    public FontChoice? SelectedFont
    {
        get => LetteringFonts.Find(CurrentFontFamily);
        set
        {
            if (value is not null)
                CurrentFontFamily = value.Name;
        }
    }

    /// <summary>What the font box says for a font this computer doesn't have - its name is kept, and it's drawn in the default until it's installed.</summary>
    public string? FontPlaceholder =>
        CurrentFontFamily is { } family && !Lettering.IsAvailable(family) ? $"{family} (missing)" : null;

    public string FontTip =>
        CurrentFontFamily is { } family && !Lettering.IsAvailable(family)
            ? $"Font - {family} isn't on this computer, so it's shown in {Lettering.DefaultFamily}; install it and it comes back."
            : "Font - for the selected bubble or text, and what you add next";

    public void SetBubbleFont(PanelId panelId, int bubbleIndex, string? family) =>
        Apply(EditBubbleInPanel(Working, panelId, bubbleIndex, (b, _) => BubbleEditing.SetFont(b, family)));

    private void RaiseFontChanged()
    {
        var key = (HasSelectedBubble, SelectedBubble?.FontFamily, HasSelectedText, SelectedText?.Style.FontFamily,
            _newBubbleFont, _newTextStyle.FontFamily, Tool == PageEditorTool.Text);
        if (Equals(key, _fontKey))
            return;
        _fontKey = key;
        OnPropertyChanged(nameof(CurrentFontFamily));
        OnPropertyChanged(nameof(SelectedFont));
        OnPropertyChanged(nameof(FontPlaceholder));
        OnPropertyChanged(nameof(FontTip));
    }
}
