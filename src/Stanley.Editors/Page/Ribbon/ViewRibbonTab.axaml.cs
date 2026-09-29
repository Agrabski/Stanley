using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The page ribbon's View tab: zoom, snapping and the page display; see ViewRibbonTab.axaml. Everything goes through <see cref="PageEditorViewModel"/> commands.</summary>
public partial class ViewRibbonTab : UserControl
{
    public ViewRibbonTab()
    {
        InitializeComponent();
    }
}
