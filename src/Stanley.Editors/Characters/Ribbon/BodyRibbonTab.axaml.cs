using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Stanley.Editors;

/// <summary>The character ribbon's Body tab; see BodyRibbonTab.axaml: body types, the body sliders, skin, name, view and Close.</summary>
public partial class BodyRibbonTab : UserControl
{
    public BodyRibbonTab()
    {
        InitializeComponent();
        SliderDrag.Wire(() => ViewModel, HeightSlider, WeightSlider, MuscleSlider, HeadSlider, FrameSlider);
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
}
