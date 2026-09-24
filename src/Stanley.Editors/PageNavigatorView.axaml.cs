using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Stanley.Editors;

public partial class PageNavigatorView : UserControl
{
    private const double DragThresholdPx = 6;

    private PageItem? _pressedItem;
    private Point _pressPoint;
    private bool _dragging;
    private int _dropIndex = -1;

    public PageNavigatorView()
    {
        InitializeComponent();

        // handledEventsToo: the list box handles presses itself (to select); a drag starts from the same press.
        PageList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        PageList.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        PageList.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        PageList.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(apply: false), RoutingStrategies.Bubble, handledEventsToo: true);
        PageList.ContextRequested += OnContextRequested;
        PageList.KeyDown += OnListKeyDown;
    }

    private PageNavigatorViewModel? ViewModel => DataContext as PageNavigatorViewModel;

    /// <summary>Exposed for headless UI tests.</summary>
    public ListBox List => PageList;

    // ---------------------------------------------------------------- drag to reorder

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PageList).Properties.IsLeftButtonPressed)
            return;
        _pressedItem = ItemFrom(e.Source);
        _pressPoint = e.GetPosition(PageList);
        _dragging = false;
    }

    private void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedItem is null || ViewModel is null)
            return;

        var position = e.GetPosition(PageList);
        if (!_dragging)
        {
            if (Math.Abs(position.Y - _pressPoint.Y) < DragThresholdPx)
                return;
            _dragging = true;
            e.Pointer.Capture(PageList);
        }

        _dropIndex = DropIndexAt(position.Y);
        ShowDropIndicator(_dropIndex);
        e.Handled = true;
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            EndDrag(apply: true);
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        _pressedItem = null;
    }

    private void EndDrag(bool apply)
    {
        if (_dragging && apply && _pressedItem is { } item && ViewModel is { } vm && _dropIndex >= 0)
        {
            var from = vm.Pages.IndexOf(item);
            // The drop index counts gaps between pages; removing the dragged page first
            // shifts every gap after it up by one.
            var to = _dropIndex > from ? _dropIndex - 1 : _dropIndex;
            vm.MovePage(from, to);
        }

        _dragging = false;
        _pressedItem = null;
        _dropIndex = -1;
        DropIndicator.IsVisible = false;
    }

    /// <summary>Which gap between pages (0 = before the first, Count = after the last) the pointer is nearest.</summary>
    private int DropIndexAt(double y)
    {
        var count = ViewModel?.Pages.Count ?? 0;
        for (var i = 0; i < count; i++)
        {
            if (PageList.ContainerFromIndex(i) is not { } container)
                continue;
            var top = container.TranslatePoint(new Point(0, 0), PageList)?.Y ?? 0;
            if (y < top + container.Bounds.Height / 2)
                return i;
        }
        return count;
    }

    private void ShowDropIndicator(int index)
    {
        var count = ViewModel?.Pages.Count ?? 0;
        if (count == 0)
            return;

        double y;
        if (index < count && PageList.ContainerFromIndex(index) is { } before)
            y = before.TranslatePoint(new Point(0, 0), PageList)?.Y ?? 0;
        else if (PageList.ContainerFromIndex(count - 1) is { } last)
            y = (last.TranslatePoint(new Point(0, 0), PageList)?.Y ?? 0) + last.Bounds.Height;
        else
            return;

        Canvas.SetLeft(DropIndicator, 28);
        Canvas.SetTop(DropIndicator, y - 1.5);
        DropIndicator.Width = Math.Max(0, PageList.Bounds.Width - 44);
        DropIndicator.IsVisible = true;
    }

    // ---------------------------------------------------------------- menu & keys

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        var item = ItemFrom(e.Source) ?? vm.CurrentPage;
        vm.CurrentPage = item;

        MenuItem Item(string header, System.Windows.Input.ICommand command, string? gesture = null)
        {
            var menuItem = new MenuItem { Header = header, Command = command, CommandParameter = item };
            if (gesture != null)
                menuItem.InputGesture = KeyGesture.Parse(gesture);
            return menuItem;
        }

        var menu = new ContextMenu
        {
            ItemsSource = new Control[]
            {
                Item("New page after", vm.AddPageCommand),
                Item("Duplicate page", vm.DuplicatePageCommand, "Ctrl+D"),
                new Separator(),
                Item("Move up", vm.MovePageUpCommand, "Ctrl+Up"),
                Item("Move down", vm.MovePageDownCommand, "Ctrl+Down"),
                new Separator(),
                Item("Delete page", vm.DeletePageCommand, "Delete")
            }
        };
        menu.Open(PageList);
        e.Handled = true;
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.Delete or Key.Back when !ctrl:
                vm.DeletePageCommand.Execute(null);
                break;
            case Key.D when ctrl:
                vm.DuplicatePageCommand.Execute(null);
                break;
            case Key.Up when ctrl:
                vm.MovePageUpCommand.Execute(null);
                break;
            case Key.Down when ctrl:
                vm.MovePageDownCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private static PageItem? ItemFrom(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext as PageItem;
}
