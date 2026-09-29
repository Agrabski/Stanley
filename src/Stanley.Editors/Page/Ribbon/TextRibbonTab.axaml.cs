using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The page ribbon's contextual Text tab: lettering and its styles; see TextRibbonTab.axaml. Everything goes through <see cref="PageEditorViewModel"/> commands.</summary>
public partial class TextRibbonTab : UserControl
{
    public TextRibbonTab()
    {
        InitializeComponent();
    }
}
