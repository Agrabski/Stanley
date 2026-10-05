using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The page ribbon's contextual Shape tab: fill, outline, points (Edit Points) and arrangement of a drawn shape; see ShapeRibbonTab.axaml. Everything goes through <see cref="PageEditorViewModel"/> commands.</summary>
public partial class ShapeRibbonTab : UserControl
{
    public ShapeRibbonTab()
    {
        InitializeComponent();
    }
}
