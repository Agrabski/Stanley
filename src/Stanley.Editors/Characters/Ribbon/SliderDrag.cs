using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Stanley.Editors;

/// <summary>One slider drag = one undo step: the gesture opens on press (before the slider jumps to the pointer) and commits on release.</summary>
internal static class SliderDrag
{
    public static void Wire(Func<CharacterEditorViewModel?> viewModel, params Slider[] sliders)
    {
        foreach (var slider in sliders)
        {
            slider.AddHandler(InputElement.PointerPressedEvent, (_, _) => viewModel()?.BeginSliderDrag(), RoutingStrategies.Tunnel, handledEventsToo: true);
            slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) => viewModel()?.EndSliderDrag(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            slider.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => viewModel()?.EndSliderDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }
}
