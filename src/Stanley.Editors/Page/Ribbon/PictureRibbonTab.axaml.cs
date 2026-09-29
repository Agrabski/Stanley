using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The page ribbon's contextual Picture tab: the selected picture; see PictureRibbonTab.axaml. Everything goes through <see cref="PageEditorViewModel"/> commands.</summary>
public partial class PictureRibbonTab : UserControl
{
    public PictureRibbonTab()
    {
        InitializeComponent();
    }
}
