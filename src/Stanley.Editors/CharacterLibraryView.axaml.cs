using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Stanley.Editors;

public partial class CharacterLibraryView : UserControl
{
    private const double DragThresholdPx = 6;

    private CharacterItem? _pressedItem;
    private PointerPressedEventArgs? _pressArgs;
    private Point _pressPoint;
    private bool _dragging;

    public CharacterLibraryView()
    {
        InitializeComponent();

        // handledEventsToo: the list box handles presses itself (to select); a drag starts from the same press.
        CharacterList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        CharacterList.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        CharacterList.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        CharacterList.ContextRequested += OnContextRequested;
        CharacterList.KeyDown += OnListKeyDown;
    }

    private CharacterLibraryViewModel? ViewModel => DataContext as CharacterLibraryViewModel;

    /// <summary>Exposed for headless UI tests.</summary>
    public ListBox List => CharacterList;

    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CharacterList).Properties.IsLeftButtonPressed)
            return;
        _pressedItem = ItemFrom(e.Source);
        _pressArgs = e;
        _pressPoint = e.GetPosition(CharacterList);
        _dragging = false;
    }

    private async void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedItem is not { } item || _pressArgs is not { } press || _dragging)
            return;
        var position = e.GetPosition(CharacterList);
        if (Math.Abs(position.X - _pressPoint.X) < DragThresholdPx && Math.Abs(position.Y - _pressPoint.Y) < DragThresholdPx)
            return;

        _dragging = true;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(CharacterDrag.Format, item.Id.Value));
        try
        {
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy);
        }
        finally
        {
            _dragging = false;
            _pressedItem = null;
            _pressArgs = null;
        }
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Clicking the character that's already current still brings its editor back
        // (e.g. after going back to the page).
        if (!_dragging && _pressedItem is { } item && ViewModel is { } vm && ItemFrom(e.Source) == item)
            vm.Show(item);
        _pressedItem = null;
        _pressArgs = null;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (ViewModel is not { } vm || ItemFrom(e.Source) is not { } item)
            return;

        var menu = new ContextMenu
        {
            ItemsSource = new Control[]
            {
                Item("Edit body", () => vm.Show(item)),
                Item("Place on page", () => vm.PlaceOnPageCommand.Execute(item)),
                new Separator(),
                Item("Duplicate", () => vm.DuplicateCharacterCommand.Execute(item)),
                Item(item.Usage > 0 ? "Delete (remove it from its panels first)" : "Delete", () => vm.DeleteCharacterCommand.Execute(item), item.Usage == 0),
            }
        };
        menu.Open(CharacterList);
        e.Handled = true;
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { Current: { } current } vm)
            return;
        if (e.Key == Key.Delete && vm.DeleteCharacterCommand.CanExecute(current))
        {
            vm.DeleteCharacterCommand.Execute(current);
            e.Handled = true;
        }
    }

    private static MenuItem Item(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private static CharacterItem? ItemFrom(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext as CharacterItem;
}

/// <summary>The drag-and-drop payload of a character dragged out of the Characters pane: its id.</summary>
public static class CharacterDrag
{
    public static readonly DataFormat<string> Format = DataFormat.CreateStringApplicationFormat("stanley-character");
}
