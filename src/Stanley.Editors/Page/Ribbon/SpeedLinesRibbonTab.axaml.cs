using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The page ribbon's contextual Speed Lines tab: colour, density and shape of speed lines; see SpeedLinesRibbonTab.axaml. Everything goes through <see cref="PageEditorViewModel"/> commands.</summary>
public partial class SpeedLinesRibbonTab : UserControl
{
    public SpeedLinesRibbonTab()
    {
        InitializeComponent();
    }
}
