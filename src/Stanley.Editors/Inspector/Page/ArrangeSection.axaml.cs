using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>What any selection can do: copy, duplicate, group, put in front or behind, delete. See ArrangeSection.axaml.</summary>
public partial class ArrangeSection : UserControl
{
    public ArrangeSection()
    {
        InitializeComponent();
    }
}
