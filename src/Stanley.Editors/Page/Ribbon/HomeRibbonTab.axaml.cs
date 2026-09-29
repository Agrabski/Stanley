using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The page ribbon's Home tab: clipboard, fonts, styles and the everyday tools; see HomeRibbonTab.axaml. Everything goes through <see cref="PageEditorViewModel"/> commands.</summary>
public partial class HomeRibbonTab : UserControl
{
    public HomeRibbonTab()
    {
        InitializeComponent();
    }
}
