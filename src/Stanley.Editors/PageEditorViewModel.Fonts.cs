using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.Editors;

// The ribbon's Font group (one FontGroup control, on the Home, Bubble and Text tabs): like
// Word's, it shows the selection's lettering - a bubble's or a text's typeface, size, bold,
// italic and alignment - and changing any of it changes the selection (one undo step) and
// becomes what new ones get. With nothing selected it sets what the next bubble and the
// next text get.
public sealed partial class PageEditorViewModel
{
    private LetteringFont _newBubbleLettering = LetteringFont.BubbleDefault;
    private LetteringFont? _fontKey;

    private void InitializeFontCommands()
    {
        BiggerTextCommand = new RelayCommand(() =>
        {
            var size = TextEditing.Bigger(CurrentLettering.SizePt);
            SetLettering(f => f with { SizePt = size });
        });
        SmallerTextCommand = new RelayCommand(() =>
        {
            var size = TextEditing.Smaller(CurrentLettering.SizePt);
            SetLettering(f => f with { SizePt = size });
        });
        SetTextSizeCommand = new RelayCommand<string>(entry =>
        {
            var size = TextEditing.ParseSize(entry);
            if (size.IsValid)
                SetLettering(f => f with { SizePt = size.Value });
            else
                Apply(EditResult<PageDocument>.Failure(size.Error!));
        });
    }

    /// <summary>The lettering the Font group shows: the selected bubble's or text's, or what the next one gets (the next text's while the Text tool is on).</summary>
    public LetteringFont CurrentLettering =>
        SelectedBubble is { } bubble ? LetteringFont.Of(bubble)
        : SelectedText is { } text ? LetteringFont.Of(text.Style)
        : Tool == PageEditorTool.Text ? LetteringFont.Of(_newTextStyle)
        : _newBubbleLettering;

    /// <summary>Every font the box offers: Stanley's own first, then the ones on this computer.</summary>
    public IReadOnlyList<FontChoice> FontChoices => LetteringFonts.Choices;

    /// <summary>The typeface (null: the default lettering font).</summary>
    public string? CurrentFontFamily
    {
        get => CurrentLettering.Family;
        set
        {
            var family = LetteringFonts.Stored(value);
            SetLettering(f => f with { Family = family });
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

    /// <summary>The letters' size in points, as the font size box shows it (as in Word).</summary>
    public double TextSizePt => CurrentLettering.SizePt;

    /// <summary>The sizes the font size box lists (any other can be typed).</summary>
    public IReadOnlyList<double> TextSizeChoices => TextEditing.SizeSteps;

    public IRelayCommand BiggerTextCommand { get; private set; } = null!;
    public IRelayCommand SmallerTextCommand { get; private set; } = null!;

    /// <summary>The font size box: a size in points picked from its list or typed ("10.5", "12 pt", or "5 mm", turned into points); a bad entry changes nothing and says why.</summary>
    public IRelayCommand<string> SetTextSizeCommand { get; private set; } = null!;

    public bool IsTextBold
    {
        get => CurrentLettering.Bold;
        set => SetLettering(f => f with { Bold = value });
    }

    public bool IsTextItalic
    {
        get => CurrentLettering.Italic;
        set => SetLettering(f => f with { Italic = value });
    }

    public bool IsTextAlignLeft { get => CurrentLettering.Align == TextAlign.Left; set => SetAlign(TextAlign.Left, value); }
    public bool IsTextAlignCenter { get => CurrentLettering.Align == TextAlign.Center; set => SetAlign(TextAlign.Center, value); }
    public bool IsTextAlignRight { get => CurrentLettering.Align == TextAlign.Right; set => SetAlign(TextAlign.Right, value); }

    private void SetAlign(TextAlign align, bool value)
    {
        if (value)
        {
            SetLettering(f => f with { Align = align });
        }
        else
        {
            // A toggle that flipped itself off hears "no, you're still on".
            OnPropertyChanged(nameof(IsTextAlignLeft));
            OnPropertyChanged(nameof(IsTextAlignCenter));
            OnPropertyChanged(nameof(IsTextAlignRight));
        }
    }

    public void SetBubbleFont(PanelId panelId, int bubbleIndex, string? family) =>
        Apply(EditBubbleInPanel(Working, panelId, bubbleIndex, (b, _) => BubbleEditing.SetFont(b, family)));

    public void SetBubbleLettering(PanelId panelId, int bubbleIndex, LetteringFont font) =>
        Apply(EditBubbleInPanel(Working, panelId, bubbleIndex, (b, _) => BubbleEditing.SetLettering(b, font)));

    /// <summary>Changes the lettering of whatever the Font group is showing (see <see cref="CurrentLettering"/>): the selected bubble or text, else what the next bubble and text get.</summary>
    private void SetLettering(Func<LetteringFont, LetteringFont> change)
    {
        if (SelectedBubble is { } bubble && _selectedPanelId is { } panelId)
        {
            var font = change(LetteringFont.Of(bubble)).Validate();
            if (!font.IsValid)
            {
                Apply(EditResult<PageDocument>.Failure(font.Error!));
                return;
            }
            _newBubbleLettering = font.Value;
            if (font.Value.ApplyTo(bubble) != bubble)
                SetBubbleLettering(panelId, _selectedBubbleIndex, font.Value);
        }
        else if (SelectedText is not null)
        {
            SetCurrentTextStyle(change(LetteringFont.Of(CurrentTextStyle)).ApplyTo(CurrentTextStyle));
        }
        else
        {
            _newBubbleLettering = change(_newBubbleLettering);
            _newTextStyle = change(LetteringFont.Of(_newTextStyle)).ApplyTo(_newTextStyle);
        }
        RaiseFontChanged();
    }

    private void RaiseFontChanged()
    {
        var key = CurrentLettering;
        if (key == _fontKey)
            return;
        _fontKey = key;
        OnPropertyChanged(nameof(CurrentLettering));
        OnPropertyChanged(nameof(CurrentFontFamily));
        OnPropertyChanged(nameof(SelectedFont));
        OnPropertyChanged(nameof(FontPlaceholder));
        OnPropertyChanged(nameof(FontTip));
        OnPropertyChanged(nameof(TextSizePt));
        OnPropertyChanged(nameof(IsTextBold));
        OnPropertyChanged(nameof(IsTextItalic));
        OnPropertyChanged(nameof(IsTextAlignLeft));
        OnPropertyChanged(nameof(IsTextAlignCenter));
        OnPropertyChanged(nameof(IsTextAlignRight));
    }
}
