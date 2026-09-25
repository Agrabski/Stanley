using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

namespace Stanley.Editors;

/// <summary>Every panel background (paper, colours, gradients) as a named tile, running <see cref="Command"/> with its <see cref="BackgroundChoice"/> - plus "Picture…", running <see cref="PictureCommand"/>.</summary>
public sealed class BackgroundPicker : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<BackgroundPicker, ICommand?>(nameof(Command));

    /// <summary>Asks for a picture file to fill the panel with; the "Picture…" button shows only with one.</summary>
    public static readonly StyledProperty<ICommand?> PictureCommandProperty =
        AvaloniaProperty.Register<BackgroundPicker, ICommand?>(nameof(PictureCommand));

    private readonly List<Button> _buttons = [];
    private readonly Button _picture;

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
        _picture = new Button { Name = "BackgroundPicture", Classes = { "small" }, Content = "Picture…", HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };
        ToolTip.SetTip(_picture, "Fill the panel with a picture from a file (PNG, JPEG, SVG...) - it's scaled to cover the panel");
        Content = new StackPanel { Spacing = 4, Children = { grid, _picture } };
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public ICommand? PictureCommand
    {
        get => GetValue(PictureCommandProperty);
        set => SetValue(PictureCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CommandProperty)
        {
            foreach (var button in _buttons)
                button.Command = Command;
        }
        else if (change.Property == PictureCommandProperty)
        {
            _picture.Command = PictureCommand;
            _picture.IsVisible = PictureCommand != null;
        }
    }
}
