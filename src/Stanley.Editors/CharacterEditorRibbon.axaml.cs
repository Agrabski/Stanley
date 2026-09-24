using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Stanley.Editors;

/// <summary>The character editor's ribbon; see CharacterEditorRibbon.axaml. Everything goes through <see cref="CharacterEditorViewModel"/>.</summary>
public partial class CharacterEditorRibbon : UserControl
{
    public CharacterEditorRibbon()
    {
        InitializeComponent();

        // One slider drag = one undo step: the gesture opens on press (before the slider
        // jumps to the pointer) and commits on release.
        foreach (var slider in new[] { HeightSlider, WeightSlider, MuscleSlider, HeadSlider, FrameSlider })
        {
            slider.AddHandler(PointerPressedEvent, (_, _) => ViewModel?.BeginSliderDrag(), RoutingStrategies.Tunnel, handledEventsToo: true);
            slider.AddHandler(PointerReleasedEvent, (_, _) => ViewModel?.EndSliderDrag(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            slider.AddHandler(PointerCaptureLostEvent, (_, _) => ViewModel?.EndSliderDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
        }

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

    /// <summary>Exposed for headless UI tests.</summary>
    public TabControl TabControl => Tabs;
}
