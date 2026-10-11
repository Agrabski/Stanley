using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The inspector's lettering rows, shared by the Bubble and Text sections; see LetteringRows.axaml.</summary>
public partial class LetteringRows : UserControl
{
    public LetteringRows()
    {
        InitializeComponent();

        // As in the ribbon's Font group: typing a size and pressing Enter (or picking one) hands
        // the keyboard back to the page, so shortcuts reach it again.
        SizeBox.Committed += (_, _) => (DataContext as PageEditorViewModel)?.FocusPage();
    }

    /// <summary>Exposed for headless UI tests.</summary>
    public ComboBox Fonts => FontBox;

    /// <summary>Exposed for headless UI tests.</summary>
    public FontSizeBox Sizes => SizeBox;
}
