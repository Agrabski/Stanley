using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The page ribbon's contextual Panel tab: splitting, resizing and styling the selected panel; see PanelRibbonTab.axaml. Everything goes through <see cref="PageEditorViewModel"/> commands.</summary>
public partial class PanelRibbonTab : UserControl
{
    public PanelRibbonTab()
    {
        InitializeComponent();
    }
}
