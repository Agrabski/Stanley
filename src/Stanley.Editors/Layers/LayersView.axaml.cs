using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Stanley.Editors;

public partial class LayersView : UserControl
{
    // The row a left press landed on: it's picked when the same row sees the release, so a press
    // that turns into a scroll or a drag away never selects anything.
    private LayerRow? _pressedRow;

    public LayersView()
    {
        InitializeComponent();

        // handledEventsToo: a row's expander button handles its own click; the list still sees every press.
        LayerList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        LayerList.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        LayerList.AddHandler(PointerCaptureLostEvent, (_, _) => _pressedRow = null, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Exposed for headless UI tests.</summary>
    public ItemsControl List => LayerList;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedRow = e.GetCurrentPoint(LayerList).Properties.IsLeftButtonPressed && !IsExpander(e.Source) ? RowFrom(e.Source) : null;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pressed = _pressedRow;
        _pressedRow = null;
        if (pressed is null || DataContext is not LayersViewModel vm || !ReferenceEquals(RowFrom(e.Source), pressed))
            return;
        vm.Pick(pressed, additive: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    private static bool IsExpander(object? source) => source is Visual visual && visual.FindAncestorOfType<Button>(includeSelf: true) is not null;

    private static LayerRow? RowFrom(object? source) =>
        source is Visual visual
            ? visual.GetSelfAndVisualAncestors().OfType<StyledElement>().Select(element => element.DataContext).OfType<LayerRow>().FirstOrDefault()
            : null;
}
