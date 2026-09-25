using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

namespace Stanley.Editors;

/// <summary>
/// The palette as a grid of swatches (plus an optional "none" button), each running
/// <see cref="Command"/> with its <see cref="PaletteColor"/> - the one colour menu the
/// ribbon's outline, fill, text and box drop-downs all share.
/// </summary>
public sealed class ColorPalettePicker : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<ColorPalettePicker, ICommand?>(nameof(Command));

    /// <summary>The "none" button's caption ("No fill"), or null for a colour that can't be none.</summary>
    public static readonly StyledProperty<string?> NoneLabelProperty =
        AvaloniaProperty.Register<ColorPalettePicker, string?>(nameof(NoneLabel));

    private readonly Button _none;
    private readonly List<Button> _swatches = [];

    public ColorPalettePicker()
    {
        _none = new Button { Classes = { "small" }, HorizontalAlignment = HorizontalAlignment.Stretch, CommandParameter = DrawingPalette.None, IsVisible = false };
        var grid = new WrapPanel { Width = 6 * 30 };
        foreach (var color in DrawingPalette.Colors)
        {
            var swatch = new Button
            {
                Name = "Swatch" + color.Name.Replace(" ", "", StringComparison.Ordinal),
                Classes = { "small" },
                Width = 28,
                Height = 22,
                Padding = new Thickness(3),
                Margin = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                CommandParameter = color,
                Content = new Border { Background = color.Brush, BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) }
            };
            ToolTip.SetTip(swatch, color.Name);
            _swatches.Add(swatch);
            grid.Children.Add(swatch);
        }
        Content = new StackPanel { Spacing = 4, Children = { _none, grid } };
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public string? NoneLabel
    {
        get => GetValue(NoneLabelProperty);
        set => SetValue(NoneLabelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CommandProperty)
        {
            _none.Command = Command;
            foreach (var swatch in _swatches)
                swatch.Command = Command;
        }
        else if (change.Property == NoneLabelProperty)
        {
            _none.Content = NoneLabel;
            _none.IsVisible = NoneLabel != null;
        }
    }
}

/// <summary>The line thicknesses, each drawn as a sample line, running <see cref="Command"/> with its <see cref="ShapeWeightChoice"/>.</summary>
public sealed class WeightPicker : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<WeightPicker, ICommand?>(nameof(Command));

    private readonly List<Button> _buttons = [];

    public WeightPicker()
    {
        var list = new StackPanel { Spacing = 1, Width = 150 };
        foreach (var weight in DrawingPalette.Weights)
        {
            var button = new Button
            {
                Name = "Weight" + weight.Name,
                Classes = { "small" },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                CommandParameter = weight,
                Content = new DockPanel
                {
                    Children =
                    {
                        new TextBlock { Text = weight.Name, Width = 54, VerticalAlignment = VerticalAlignment.Center, [DockPanel.DockProperty] = Avalonia.Controls.Dock.Left },
                        new Rectangle { Height = weight.PreviewThickness, Fill = Brushes.Black, VerticalAlignment = VerticalAlignment.Center, RadiusX = weight.PreviewThickness / 2, RadiusY = weight.PreviewThickness / 2 }
                    }
                }
            };
            _buttons.Add(button);
            list.Children.Add(button);
        }
        Content = list;
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CommandProperty)
        {
            foreach (var button in _buttons)
                button.Command = Command;
        }
    }
}

/// <summary>Every panel background (paper, colours, gradients) as a named tile, running <see cref="Command"/> with its <see cref="BackgroundChoice"/>.</summary>
public sealed class BackgroundPicker : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<BackgroundPicker, ICommand?>(nameof(Command));

    private readonly List<Button> _buttons = [];

    public BackgroundPicker()
    {
        var grid = new WrapPanel { Width = 7 * 50 };
        foreach (var choice in DrawingPalette.Backgrounds)
        {
            var button = new Button
            {
                Name = "Background" + choice.Name.Replace(" ", "", StringComparison.Ordinal),
                Classes = { "small" },
                Width = 48,
                Height = 50,
                Padding = new Thickness(2),
                Margin = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                CommandParameter = choice,
                Content = new DockPanel
                {
                    Children =
                    {
                        new TextBlock { Text = choice.Name, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, [DockPanel.DockProperty] = Avalonia.Controls.Dock.Bottom },
                        new Border { Background = choice.Preview, BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) }
                    }
                }
            };
            ToolTip.SetTip(button, choice.Background is null ? "No background - the white paper" : choice.Name);
            _buttons.Add(button);
            grid.Children.Add(button);
        }
        Content = grid;
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CommandProperty)
        {
            foreach (var button in _buttons)
                button.Command = Command;
        }
    }
}
