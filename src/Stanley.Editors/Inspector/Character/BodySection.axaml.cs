using Avalonia.Controls;
using Avalonia.Input;

namespace Stanley.Editors;

/// <summary>The character's name and body: body types, sliders and skin. See BodySection.axaml.</summary>
public partial class BodySection : UserControl
{
    public BodySection()
    {
        InitializeComponent();
        SliderDrag.Wire(() => ViewModel, HeightSlider, WeightSlider, MuscleSlider, HeadSlider, FrameSlider);
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
