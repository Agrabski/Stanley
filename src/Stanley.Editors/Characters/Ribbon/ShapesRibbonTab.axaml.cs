using Avalonia.Controls;
using Avalonia.Input;

namespace Stanley.Editors;

/// <summary>The character ribbon's Shapes tab, for a character built from shapes; see ShapesRibbonTab.axaml: creatures, parts, joined or separate, Height %, skin, name, view and Close.</summary>
public partial class ShapesRibbonTab : UserControl
{
    public ShapesRibbonTab()
    {
        InitializeComponent();
        SliderDrag.Wire(() => ViewModel, BodyHeightSlider);
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
