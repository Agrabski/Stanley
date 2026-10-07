using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Stanley.Editors;

public partial class LayersView : UserControl
{
    // How far (px) a press has to travel up or down before it's a drag rather than a click.
    private const double DragThreshold = 4;

    // How close (px) to the top or bottom of the list a drag has to come to scroll it, and how far each move scrolls.
    private const double ScrollZone = 24;
    private const double ScrollStep = 12;

    // The row a left press landed on: it's picked when the same row sees the release, so a press
    // that turns into a scroll or a drag away never selects anything.
    private LayerRow? _pressedRow;
    private Point _pressedAt;
    private bool _dragging;
    private Cursor? _dragCursor;

    public LayersView()
    {
        InitializeComponent();

        // handledEventsToo: a row's expander button handles its own click; the list still sees every press.
        LayerList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        LayerList.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        LayerList.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        LayerList.AddHandler(PointerCaptureLostEvent, (_, _) => EndPress(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Exposed for headless UI tests.</summary>
    public ItemsControl List => LayerList;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedRow = e.GetCurrentPoint(LayerList).Properties.IsLeftButtonPressed && !IsExpander(e.Source) ? RowFrom(e.Source) : null;
        _pressedAt = e.GetPosition(LayerList);
    }

    /// <summary>A press on a bubble, character or drawing that moves far enough up or down drags it: a line shows where it would land among its panel's layers.</summary>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // A panel or its background stays where it is: a press there is only ever a click.
        if (_pressedRow is not { IsLayer: true } row || DataContext is not LayersViewModel vm)
            return;
        if (!_dragging)
        {
            if (Math.Abs(e.GetPosition(LayerList).Y - _pressedAt.Y) < DragThreshold || !e.GetCurrentPoint(LayerList).Properties.IsLeftButtonPressed)
                return;
            _dragging = vm.BeginDrag(row);
            if (!_dragging)
                return;
            ShowDragCursor();
        }
        ScrollNearEdges(e.GetPosition(LayerScroll).Y);
        vm.DragOver(GapAt(e.GetPosition(LayerList).Y));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pressed = _pressedRow;
        var dragging = _dragging;
        _pressedRow = null;
        _dragging = false; // so the capture lost after this release doesn't cancel the drop
        Cursor = null;
        if (pressed is null || DataContext is not LayersViewModel vm)
            return;
        if (dragging)
            vm.Drop();
        else if (ReferenceEquals(RowFrom(e.Source), pressed))
            vm.Pick(pressed, additive: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    /// <summary>Forgets the press, and lets go of a drag that didn't end in a drop (the pointer was taken away).</summary>
    private void EndPress()
    {
        _pressedRow = null;
        if (_dragging && DataContext is LayersViewModel vm)
            vm.CancelDrag();
        _dragging = false;
        Cursor = null;
    }

    private void ShowDragCursor()
    {
        try
        {
            _dragCursor ??= new Cursor(StandardCursorType.DragMove);
        }
        catch (Exception)
        {
            return; // no cursor support on this platform - purely cosmetic
        }
        Cursor = _dragCursor;
    }

    /// <summary>The gap a pointer at <paramref name="y"/> (in the list) is nearest: the index of the row it's over if it's in that row's top half, the next one's if it's in the bottom half.</summary>
    private int GapAt(double y)
    {
        var count = LayerList.ItemCount;
        for (var i = 0; i < count; i++)
        {
            if (LayerList.ContainerFromIndex(i) is not { } container || container.TranslatePoint(default, LayerList) is not { } top)
                continue;
            if (y < top.Y + container.Bounds.Height / 2)
                return i;
        }
        return count;
    }

    /// <summary>Scrolls the list a little while a drag is held near its top or bottom, so a layer can be taken past what's showing.</summary>
    private void ScrollNearEdges(double y)
    {
        var offset = LayerScroll.Offset;
        var end = Math.Max(0, LayerScroll.Extent.Height - LayerScroll.Viewport.Height);
        if (y < ScrollZone && offset.Y > 0)
            LayerScroll.Offset = offset.WithY(Math.Max(0, offset.Y - ScrollStep));
        else if (y > LayerScroll.Bounds.Height - ScrollZone && offset.Y < end)
            LayerScroll.Offset = offset.WithY(Math.Min(end, offset.Y + ScrollStep));
    }

    private static bool IsExpander(object? source) => source is Visual visual && visual.FindAncestorOfType<Button>(includeSelf: true) is not null;

    private static LayerRow? RowFrom(object? source) =>
        source is Visual visual
            ? visual.GetSelfAndVisualAncestors().OfType<StyledElement>().Select(element => element.DataContext).OfType<LayerRow>().FirstOrDefault()
            : null;
}
