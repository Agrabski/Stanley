using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>What the character wears, slot by slot, and the colours of the slots in use. See LookSection.axaml.</summary>
public partial class LookSection : UserControl
{
    public LookSection()
    {
        InitializeComponent();
    }

    private void OnGalleryOpening(object? sender, EventArgs e) => InspectorFlyouts.BesideInspector(sender, this);
}
