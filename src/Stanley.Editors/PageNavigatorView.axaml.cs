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
        PageList.AddHandler(PointerCaptureLostEvent, (_, _) => { EndDrag(apply: false); ShowCurrentSelection(); }, RoutingStrategies.Bubble, handledEventsToo: true);
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
            if (Math.Abs(position.X - _pressPoint.X) < DragThresholdPx && Math.Abs(position.Y - _pressPoint.Y) < DragThresholdPx)
                return;
            _dragging = true;
            e.Pointer.Capture(PageList);
        }

        _dropIndex = DropIndexAt(position);
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
        // A click - press and release on the same page without dragging - shows it, even
        // when it's already the current page (e.g. coming back from a character's editor):
        // the list box's own selection only fires on a *change*, which a re-click of the
        // already-selected item isn't.
        else if (_pressedItem is { } item && ViewModel is { } vm && ItemFrom(e.Source) == item)
        {
            vm.Reveal(item);
        }
        _pressedItem = null;
        ShowCurrentSelection();
    }

    /// <summary>The list box selects on press; put its highlight back on the page that's actually current when that press didn't change it (e.g. an aborted drag).</summary>
    private void ShowCurrentSelection()
    {
        if (ViewModel is { } vm && !ReferenceEquals(PageList.SelectedItem, vm.CurrentPage))
            PageList.SelectedItem = vm.CurrentPage;
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

    /// <summary>
    /// Which gap between pages (0 = before the first, Count = after the last) the pointer
    /// is at, in reading order across the grid: the page under the pointer (or the nearest
    /// one in its row), before it if on its left half, after it if on its right half.
    /// </summary>
    private int DropIndexAt(Point point)
    {
        var count = ViewModel?.Pages.Count ?? 0;
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < count; i++)
        {
            if (ContainerBounds(i) is not { } bounds)
                continue;
            // Rows first (vertical distance dominates), then horizontal within the row.
            var dy = point.Y < bounds.Top ? bounds.Top - point.Y : point.Y > bounds.Bottom ? point.Y - bounds.Bottom : 0;
            var dx = Math.Abs(point.X - bounds.Center.X);
            var distance = dy * 10_000 + dx;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        if (best < 0)
            return count;
        return point.X < ContainerBounds(best)!.Value.Center.X ? best : best + 1;
    }

    private Rect? ContainerBounds(int index)
    {
        if (PageList.ContainerFromIndex(index) is not { } container || container.TranslatePoint(new Point(0, 0), PageList) is not { } topLeft)
            return null;
        return new Rect(topLeft, container.Bounds.Size);
    }

    /// <summary>A vertical bar in the gap the page would drop into: at the left of the page after the gap, or the right of the page before it when the gap ends a row.</summary>
    private void ShowDropIndicator(int index)
    {
        var count = ViewModel?.Pages.Count ?? 0;
        var after = index < count ? ContainerBounds(index) : null;
        var before = index > 0 ? ContainerBounds(index - 1) : null;

        // Prefer the page before the gap when the gap is at the end of its row (the next
        // page starts a new row below), so the bar sits where the pointer is.
        Rect anchor;
        double x;
        if (before is { } b && (after is not { } a || a.Top > b.Top + 1))
        {
            anchor = b;
            x = b.Right;
        }
        else if (after is { } a2)
        {
            anchor = a2;
            x = a2.Left;
        }
        else
        {
            DropIndicator.IsVisible = false;
            return;
        }

        Canvas.SetLeft(DropIndicator, x - 1.5);
        Canvas.SetTop(DropIndicator, anchor.Top + 4);
        DropIndicator.Height = Math.Max(0, anchor.Height - 8);
        DropIndicator.IsVisible = true;
    }

    // ---------------------------------------------------------------- menu & keys

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        var item = ItemFrom(e.Source) ?? vm.CurrentPage;
        vm.Reveal(item);

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
                Item("Move earlier", vm.MovePageUpCommand, "Ctrl+Left"),
                Item("Move later", vm.MovePageDownCommand, "Ctrl+Right"),
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

        if (e.Key == Key.Enter && PageList.SelectedItem is PageItem selected)
        {
            vm.Reveal(selected);
            e.Handled = true;
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.Delete or Key.Back when !ctrl:
                vm.DeletePageCommand.Execute(null);
                break;
            case Key.D when ctrl:
                vm.DuplicatePageCommand.Execute(null);
                break;
            case Key.Up or Key.Left when ctrl:
                vm.MovePageUpCommand.Execute(null);
                break;
            case Key.Down or Key.Right when ctrl:
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
