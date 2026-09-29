using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The page ribbon's contextual Bubble tab: the selected bubble's style, tail and text; see BubbleRibbonTab.axaml. Everything goes through <see cref="PageEditorViewModel"/> commands.</summary>
public partial class BubbleRibbonTab : UserControl
{
    public BubbleRibbonTab()
    {
        InitializeComponent();
    }
}
