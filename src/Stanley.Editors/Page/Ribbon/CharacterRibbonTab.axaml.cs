using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The page ribbon's contextual Character tab: the selected character on the page; see CharacterRibbonTab.axaml. Everything goes through <see cref="PageEditorViewModel"/> commands.</summary>
public partial class CharacterRibbonTab : UserControl
{
    public CharacterRibbonTab()
    {
        InitializeComponent();
    }
}
