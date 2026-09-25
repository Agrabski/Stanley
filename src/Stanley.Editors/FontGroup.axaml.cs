using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The ribbon's Font group, shared by every tab that has one; see FontGroup.axaml.</summary>
public partial class FontGroup : UserControl
{
    public FontGroup()
    {
        InitializeComponent();

        // Typing a size and pressing Enter (or picking one) hands the keyboard back to the
        // page, as in Word, so shortcuts reach it again.
        SizeBox.Committed += (_, _) => (DataContext as PageEditorViewModel)?.FocusPage();
    }

    /// <summary>Exposed for headless UI tests.</summary>
    public ComboBox Fonts => FontBox;

    /// <summary>Exposed for headless UI tests.</summary>
    public FontSizeBox Sizes => SizeBox;
}
