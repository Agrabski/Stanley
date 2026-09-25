using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Stanley.Editors;

/// <summary>
/// Word's font size box: the size as editable text, with an arrow listing the usual sizes.
/// Pick one, or type any size and press Enter (or click away); Esc puts the size back.
/// Either way the text goes to <see cref="Command"/>, which applies it - or refuses it,
/// and the box shows the size really in effect again. <see cref="Committed"/> fires after,
/// so the host can hand the keyboard back to the page.
/// </summary>
public sealed class FontSizeBox : UserControl
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<FontSizeBox, double>(nameof(Value));
    public static readonly StyledProperty<IEnumerable<double>?> SizesProperty = AvaloniaProperty.Register<FontSizeBox, IEnumerable<double>?>(nameof(Sizes));
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<FontSizeBox, ICommand?>(nameof(Command));

    private readonly TextBox _entry;
    private readonly Button _arrow;
    private readonly MenuFlyout _menu;

    public FontSizeBox()
    {
        _menu = new MenuFlyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft };
        _menu.Opening += (_, _) =>
        {
            foreach (var item in _menu.Items.OfType<MenuItem>())
                item.IsChecked = item.Tag is double size && Math.Abs(size - Value) < 1e-6;
        };
        _arrow = new Button
        {
            Focusable = false,
            Flyout = _menu,
            Width = 18,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 1, 1, 1),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = new PathIcon { Data = Geometry.Parse("M0,0 L1.4,0 L5,3.6 L8.6,0 L10,0 L5,5 Z"), Width = 9, Height = 5 },
        };
        _entry = new TextBox
        {
            Width = 64,
            Height = 26,
            MinHeight = 0,
            Padding = new Thickness(6, 0, 0, 0),
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
            InnerRightContent = _arrow,
            Text = Format(Value),
        };
        _entry.AddHandler(KeyDownEvent, OnEntryKeyDown, RoutingStrategies.Tunnel);
        _entry.GotFocus += (_, _) => Dispatcher.UIThread.Post(_entry.SelectAll);
        _entry.LostFocus += (_, _) =>
        {
            if (_entry.Text != Format(Value))
                Commit(_entry.Text);
        };
        Content = _entry;
    }

    /// <summary>The size in effect, shown in the box.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The sizes the arrow lists.</summary>
    public IEnumerable<double>? Sizes
    {
        get => GetValue(SizesProperty);
        set => SetValue(SizesProperty, value);
    }

    /// <summary>Gets what was typed or picked, as text.</summary>
    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>A size was entered (applied or refused) or the entry was cancelled.</summary>
    public event EventHandler? Committed;

    /// <summary>The editable text; exposed for headless UI tests.</summary>
    public TextBox Entry => _entry;

    /// <summary>The list of sizes; exposed for headless UI tests.</summary>
    public MenuFlyout Menu => _menu;

    public static string Format(double size) => size.ToString("0.##", CultureInfo.CurrentCulture);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
            _entry.Text = Format(Value);
        else if (change.Property == SizesProperty)
            BuildMenu();
    }

    // Built once, when the sizes arrive: a MenuFlyout keeps showing the items it first opened with.
    private void BuildMenu()
    {
        _menu.Items.Clear();
        foreach (var size in Sizes ?? [])
        {
            var item = new MenuItem { Header = Format(size), Tag = size, ToggleType = MenuItemToggleType.CheckBox, Padding = new Thickness(8, 2, 16, 2), MinHeight = 0 };
            item.Click += (_, _) => Commit(Format(size));
            _menu.Items.Add(item);
        }
    }

    private void OnEntryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit(_entry.Text);
                e.Handled = true;
                break;
            case Key.Escape:
                _entry.Text = Format(Value);
                Committed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.Down when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
            case Key.F4:
                _menu.ShowAt(_arrow);
                e.Handled = true;
                break;
        }
    }

    private void Commit(string? entry)
    {
        if (Command?.CanExecute(entry) == true)
            Command.Execute(entry);
        _entry.Text = Format(Value); // what's really in effect, whether the entry was taken or not
        Committed?.Invoke(this, EventArgs.Empty);
    }
}
