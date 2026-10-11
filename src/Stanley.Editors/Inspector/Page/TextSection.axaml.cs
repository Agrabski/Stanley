using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The selected free text (or, with the Text tool in hand, the next one): kind, lettering, colours and layer. See TextSection.axaml.</summary>
public partial class TextSection : UserControl
{
    public TextSection()
    {
        InitializeComponent();
    }
}
