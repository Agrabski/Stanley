using Avalonia.Controls;
using Avalonia.Input;

namespace Stanley.Editors;

/// <summary>The name and body of a character built from shapes: creatures, parts, joined or separate, Height % and skin. See ShapesSection.axaml.</summary>
public partial class ShapesSection : UserControl
{
    public ShapesSection()
    {
        InitializeComponent();
        SliderDrag.Wire(() => ViewModel, BodyHeightSlider);
        // Enter applies the name (as leaving the box does), Escape puts the old one back.
        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && ViewModel is { } vm)
            {
                vm.Name = NameBox.Text ?? "";
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && ViewModel is { } current)
            {
                NameBox.Text = current.Name;
                e.Handled = true;
            }
        };
    }

    private CharacterEditorViewModel? ViewModel => DataContext as CharacterEditorViewModel;

    private void OnGalleryOpening(object? sender, EventArgs e) => InspectorFlyouts.BesideInspector(sender, this);
}
