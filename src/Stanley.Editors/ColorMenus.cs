using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>
/// What one Word-style colour menu offers: the palette running <paramref name="Command"/>
/// with a <see cref="PaletteColor"/>, "No Fill"/"No Outline" when <paramref name="NoneLabel"/>
/// is given, "More … Colors…", and - for outlines - Weight ▸ and Dashes ▸ when their commands
/// are given. The current values get a highlight or a check mark.
/// </summary>
public sealed record ColorMenuOptions(
    ICommand Command,
    string? NoneLabel,
    string MoreLabel,
    ColorValue? Current = null,
    ICommand? WeightCommand = null,
    double? CurrentWeight = null,
    ICommand? DashCommand = null,
    LineDash? CurrentDash = null);

/// <summary>
/// Word's colour menus - the drop-downs of Shape Fill, Shape Outline, Text Fill and Text
/// Outline: Theme Colors (with their lighter and darker shades), Standard Colors, Recent
/// Colors, then No Fill / No Outline, More Colors…, and for outlines Weight ▸ and
/// Dashes ▸. Built as plain menu items, so the ribbon's split buttons and the canvas's
/// right-click menu show exactly the same thing.
/// </summary>
public static class ColorMenus
{
    /// <summary>The menu's items, marked for <paramref name="options"/>' current values. <paramref name="close"/> shuts the menu after a swatch is picked (swatches are buttons inside it, which a menu doesn't close for by itself); <paramref name="anchor"/> is where "More Colors…" opens.</summary>
    /// <param name="picked">Told about every colour picked (a split button remembers it as the one its face applies).</param>
    public static List<object> Items(ColorMenuOptions options, Action close, Control anchor, bool atPointer = false, Action<PaletteColor>? picked = null)
    {
        var menu = new ColorMenu(options, close, anchor, atPointer, picked);
        menu.Update(options.Current, options.CurrentWeight, options.CurrentDash);
        return [.. menu.Items];
    }

    /// <summary>
    /// "More Colors…": any colour at all - Avalonia's colour spectrum with RGB/HSV and hex
    /// entry - in a flyout at <paramref name="anchor"/>; OK hands it to <paramref name="apply"/>
    /// (which also files it under Recent Colors).
    /// </summary>
    public static void ShowMoreColors(Control anchor, ColorValue? initial, Action<ColorValue> apply, bool atPointer = false)
    {
        var view = new ColorView
        {
            Name = "MoreColorsView",
            Color = ToAvalonia(initial ?? ColorValue.FromHex("#4472c4")),
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            IsColorPaletteVisible = false,
            Width = 320,
            Height = 360
        };
        var ok = new Button { Name = "MoreColorsOk", Content = "OK", IsDefault = true, MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center };
        var flyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Colors", FontWeight = FontWeight.SemiBold },
                    view,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } }
                }
            }
        };
        ok.Click += (_, _) =>
        {
            flyout.Hide();
            apply(FromAvalonia(view.Color));
        };
        cancel.Click += (_, _) => flyout.Hide();
        flyout.ShowAt(anchor, atPointer);
    }

    internal static Color ToAvalonia(ColorValue color) => Color.Parse(ColorSwatchChoice.ColorHex(color));

    internal static ColorValue FromAvalonia(Color color) => ColorValue.FromHex($"#{color.R:x2}{color.G:x2}{color.B:x2}");
}

/// <summary>
/// One colour menu's items (see <see cref="ColorMenus"/>), built once and then only
/// <see cref="Update"/>d - which colour is current, the Recent Colors row, the Weight and
/// Dashes check marks - because a menu keeps showing the items it was first opened with.
/// </summary>
public sealed class ColorMenu
{
    private const double SwatchSize = 16;

    private readonly Action<PaletteColor> _pick;
    private readonly List<Border> _chips = [];
    private readonly TextBlock _recentHeading = Heading("Recent Colors");
    private readonly StackPanel _recentRow = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly List<(MenuItem Item, double Mm)> _weights = [];
    private readonly List<(MenuItem Item, LineDash Dash)> _dashes = [];
    private ColorValue? _current;

    public ColorMenu(ColorMenuOptions options, Action close, Control anchor, bool atPointer = false, Action<PaletteColor>? picked = null)
    {
        _pick = color =>
        {
            close();
            options.Command.Execute(color);
            if (!color.IsNone)
                picked?.Invoke(color);
        };

        var items = new List<object>
        {
            new MenuItem { Classes = { "palette" }, Header = Palette(), StaysOpenOnClick = true },
            new Separator()
        };
        if (options.NoneLabel is { } none)
            items.Add(new MenuItem { Header = none, Icon = NoneIcon(), Command = options.Command, CommandParameter = DrawingPalette.None });
        var more = new MenuItem { Header = options.MoreLabel, Icon = MoreIcon() };
        more.Click += (_, _) => ColorMenus.ShowMoreColors(anchor, _current, color => _pick(DrawingPalette.Remember(color)), atPointer);
        items.Add(more);

        if (options.WeightCommand is { } weightCommand)
        {
            var weights = DrawingPalette.Weights.Select(w =>
            {
                var item = new MenuItem { Header = WeightSample(w), ToggleType = MenuItemToggleType.Radio, GroupName = "Weight", Command = weightCommand, CommandParameter = w };
                _weights.Add((item, w.Mm));
                return item;
            }).ToList();
            items.Add(new MenuItem { Header = "Weight", Icon = WeightIcon(), ItemsSource = weights });
        }
        if (options.DashCommand is { } dashCommand)
        {
            var dashes = DrawingPalette.Dashes.Select(d =>
            {
                var item = new MenuItem { Header = DashSample(d.Dash), ToggleType = MenuItemToggleType.Radio, GroupName = "Dashes", Command = dashCommand, CommandParameter = d.Dash };
                ToolTip.SetTip(item, d.Name);
                _dashes.Add((item, d.Dash));
                return item;
            }).ToList();
            items.Add(new MenuItem { Header = "Dashes", Icon = DashesIcon(), ItemsSource = dashes });
        }
        Items = items;
    }

    public IReadOnlyList<object> Items { get; }

    /// <summary>Marks what the selection has now - its colour highlighted, its weight and dash checked - and brings Recent Colors up to date.</summary>
    public void Update(ColorValue? current, double? weight, LineDash? dash)
    {
        _current = current;
        foreach (var chip in _chips)
            chip.Classes.Set("current", current is not null && (chip.Tag as PaletteColor)?.Color == current);
        foreach (var (item, mm) in _weights)
            item.IsChecked = weight is { } w && Math.Abs(w - mm) < 1e-6;
        foreach (var (item, d) in _dashes)
            item.IsChecked = dash == d;

        _chips.RemoveAll(c => c.Parent is Button { Parent: var row } && ReferenceEquals(row, _recentRow));
        _recentRow.Children.Clear();
        AddSwatches(_recentRow, DrawingPalette.RecentColors, touching: false);
        _recentHeading.IsVisible = _recentRow.IsVisible = DrawingPalette.RecentColors.Count > 0;
    }

    // ---------------------------------------------------------------- the palette

    /// <summary>Theme Colors (the theme row, a gap, then five rows of shades - touching, as in Word), Standard Colors, and Recent Colors once there are any.</summary>
    private Control Palette()
    {
        var panel = new StackPanel { Name = "Palette", Spacing = 3, Margin = new Thickness(0, 2) };
        panel.Children.Add(Heading("Theme Colors"));
        panel.Children.Add(Row(DrawingPalette.ThemeColors));
        var shades = new StackPanel { Spacing = 0 };
        foreach (var row in DrawingPalette.ThemeShades)
            shades.Children.Add(Row(row, touching: true));
        panel.Children.Add(shades);
        panel.Children.Add(Heading("Standard Colors"));
        panel.Children.Add(Row(DrawingPalette.StandardColors));
        panel.Children.Add(_recentHeading);
        panel.Children.Add(_recentRow);
        return panel;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 2, 0, 1) };

    private StackPanel Row(IEnumerable<PaletteColor> colors, bool touching = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        AddSwatches(row, colors, touching);
        return row;
    }

    private void AddSwatches(StackPanel row, IEnumerable<PaletteColor> colors, bool touching)
    {
        foreach (var color in colors)
        {
            var chip = new Border { Classes = { "chip" }, Width = SwatchSize, Height = touching ? SwatchSize - 2 : SwatchSize, Background = color.Brush, Tag = color };
            chip.Classes.Set("current", _current is not null && color.Color == _current);
            _chips.Add(chip);
            var swatch = new Button { Classes = { "swatch" }, Content = chip, Tag = color };
            ToolTip.SetTip(swatch, color.Name);
            swatch.Click += (_, _) => _pick(color);
            row.Children.Add(swatch);
        }
    }

    // ---------------------------------------------------------------- samples and icons

    /// <summary>A Weight ▸ entry as Word shows it: the thickness, then a line that thick.</summary>
    private static Control WeightSample(ShapeWeightChoice weight) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = weight.Name, Width = 64, VerticalAlignment = VerticalAlignment.Center },
            Inked(new Rectangle { Width = 90, Height = weight.PreviewThickness, VerticalAlignment = VerticalAlignment.Center }, fill: true)
        }
    };

    /// <summary>A Dashes ▸ entry: a sample line in that pattern.</summary>
    private static Control DashSample(LineDash dash)
    {
        var line = Inked(new Line
        {
            StartPoint = new Point(1, 6),
            EndPoint = new Point(141, 6),
            StrokeThickness = 2,
            StrokeLineCap = dash == LineDash.RoundDot ? PenLineCap.Round : PenLineCap.Flat,
            Width = 142,
            Height = 12
        }, fill: false);
        // Avalonia measures dashes in multiples of the thickness, as the page does.
        if (LinePatterns.Intervals(dash, 1) is { } intervals)
            line.StrokeDashArray = new AvaloniaList<double>(intervals.Select(i => (double)i));
        return line;
    }

    /// <summary>
    /// <paramref name="shape"/> drawn in the menu's own text colour, so a sample line reads in
    /// a dark theme as well as a light one (#46: drawn black, it all but vanished on a dark menu).
    /// </summary>
    private static T Inked<T>(T shape, bool fill) where T : Shape
    {
        shape.Bind(fill ? Shape.FillProperty : Shape.StrokeProperty, shape.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
        return shape;
    }

    private static Control NoneIcon() => new Grid
    {
        Width = 14,
        Height = 14,
        Children =
        {
            new Border { Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) },
            new Line { StartPoint = new Point(13, 1), EndPoint = new Point(1, 13), Stroke = Brushes.Red, StrokeThickness = 1.5 }
        }
    };

    private static Control MoreIcon()
    {
        var grid = new UniformGrid { Rows = 2, Columns = 2, Width = 14, Height = 14 };
        foreach (var hex in new[] { "#ed7d31", "#ffc000", "#4472c4", "#70ad47" })
            grid.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse(hex)) });
        return grid;
    }

    private static Control WeightIcon() => new StackPanel
    {
        Width = 14,
        Spacing = 2,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new Rectangle { Height = 1, Fill = Brushes.Gray },
            new Rectangle { Height = 2, Fill = Brushes.Gray },
            new Rectangle { Height = 3, Fill = Brushes.Gray }
        }
    };

    private static Control DashesIcon() => new StackPanel
    {
        Width = 14,
        Spacing = 3,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new Line { StartPoint = new Point(0, 1), EndPoint = new Point(14, 1), Stroke = Brushes.Gray, StrokeThickness = 2, StrokeDashArray = new AvaloniaList<double> { 1, 1 } },
            new Line { StartPoint = new Point(0, 1), EndPoint = new Point(14, 1), Stroke = Brushes.Gray, StrokeThickness = 2, StrokeDashArray = new AvaloniaList<double> { 2.5, 1 } }
        }
    };
}

/// <summary>
/// A Word-style colour button - Shape Fill, Shape Outline, Text Fill, Text Outline: its
/// icon over a bar in the last colour picked, and its name; clicking it applies that
/// colour again, its arrow opens the colour menu (<see cref="ColorMenus"/>). The bar shows
/// what a click will do, as in Word; the menu marks what the selection has now.
/// </summary>
public sealed class ColorMenuButton : UserControl
{
    public static readonly StyledProperty<string> LabelProperty = AvaloniaProperty.Register<ColorMenuButton, string>(nameof(Label), "");
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<ColorMenuButton, Geometry?>(nameof(Icon));

    /// <summary>Draw the icon as an outline rather than filled in (Text Outline's hollow "A").</summary>
    public static readonly StyledProperty<bool> IconOutlinedProperty = AvaloniaProperty.Register<ColorMenuButton, bool>(nameof(IconOutlined));
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<ColorMenuButton, ICommand?>(nameof(Command));
    public static readonly StyledProperty<string?> NoneLabelProperty = AvaloniaProperty.Register<ColorMenuButton, string?>(nameof(NoneLabel));
    public static readonly StyledProperty<string> MoreLabelProperty = AvaloniaProperty.Register<ColorMenuButton, string>(nameof(MoreLabel), "More Colors…");

    /// <summary>The colour the bar starts with, before anything is picked.</summary>
    public static readonly StyledProperty<string> DefaultColorProperty = AvaloniaProperty.Register<ColorMenuButton, string>(nameof(DefaultColor), "#000000");
    public static readonly StyledProperty<ColorValue?> CurrentColorProperty = AvaloniaProperty.Register<ColorMenuButton, ColorValue?>(nameof(CurrentColor));
    public static readonly StyledProperty<ICommand?> WeightCommandProperty = AvaloniaProperty.Register<ColorMenuButton, ICommand?>(nameof(WeightCommand));
    public static readonly StyledProperty<double?> CurrentWeightProperty = AvaloniaProperty.Register<ColorMenuButton, double?>(nameof(CurrentWeight));
    public static readonly StyledProperty<ICommand?> DashCommandProperty = AvaloniaProperty.Register<ColorMenuButton, ICommand?>(nameof(DashCommand));
    public static readonly StyledProperty<LineDash?> CurrentDashProperty = AvaloniaProperty.Register<ColorMenuButton, LineDash?>(nameof(CurrentDash));

    private readonly SplitButton _button;
    private readonly Rectangle _bar;
    private readonly Avalonia.Controls.Shapes.Path _icon;
    private readonly TextBlock _label;
    private readonly MenuFlyout _menu;
    private ColorMenu? _content;
    private PaletteColor? _last;

    public ColorMenuButton()
    {
        _icon = new Avalonia.Controls.Shapes.Path { Width = 16, Height = 13, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        _bar = new Rectangle { Width = 16, Height = 4, Margin = new Thickness(0, 1, 0, 0) };
        _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        _menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        _menu.Opening += (_, _) => _content?.Update(CurrentColor, CurrentWeight, CurrentDash);
        _button = new SplitButton
        {
            Classes = { "colorMenu" },
            Flyout = _menu,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _icon, _bar } }, _label }
            }
        };
        _button.Click += (_, _) =>
        {
            if (Command is { } command && LastColor is { } color)
                command.Execute(color);
        };
        Content = _button;
        UpdateBar();
    }

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public bool IconOutlined { get => GetValue(IconOutlinedProperty); set => SetValue(IconOutlinedProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public string? NoneLabel { get => GetValue(NoneLabelProperty); set => SetValue(NoneLabelProperty, value); }
    public string MoreLabel { get => GetValue(MoreLabelProperty); set => SetValue(MoreLabelProperty, value); }
    public string DefaultColor { get => GetValue(DefaultColorProperty); set => SetValue(DefaultColorProperty, value); }
    public ColorValue? CurrentColor { get => GetValue(CurrentColorProperty); set => SetValue(CurrentColorProperty, value); }
    public ICommand? WeightCommand { get => GetValue(WeightCommandProperty); set => SetValue(WeightCommandProperty, value); }
    public double? CurrentWeight { get => GetValue(CurrentWeightProperty); set => SetValue(CurrentWeightProperty, value); }
    public ICommand? DashCommand { get => GetValue(DashCommandProperty); set => SetValue(DashCommandProperty, value); }
    public LineDash? CurrentDash { get => GetValue(CurrentDashProperty); set => SetValue(CurrentDashProperty, value); }

    /// <summary>What clicking the button's face applies: the last colour picked from its menu (or the default).</summary>
    public PaletteColor? LastColor =>
        _last ?? (ColorValue.TryParse(DefaultColor, null, out var color) ? DrawingPalette.Colors.FirstOrDefault(c => c.Color == color) ?? new PaletteColor(color.Hex, color) : null);

    /// <summary>Exposed for headless UI tests: the menu the arrow opens.</summary>
    public MenuFlyout Menu => _menu;

    /// <summary>Exposed for headless UI tests.</summary>
    public SplitButton Button => _button;

    /// <summary>
    /// Builds the menu's items as soon as what's in them is known (the commands and labels,
    /// bound on load) - before the menu first opens, since it keeps showing the items it opened
    /// with; opening then only marks the current values.
    /// </summary>
    private void Rebuild()
    {
        _menu.Items.Clear();
        _content = Command is { } command
            ? new ColorMenu(new ColorMenuOptions(command, NoneLabel, MoreLabel, WeightCommand: WeightCommand, DashCommand: DashCommand), () => _menu.Hide(), _button, picked: Remember)
            : null;
        foreach (var item in _content?.Items ?? [])
            _menu.Items.Add(item);
    }

    private void Remember(PaletteColor color)
    {
        _last = color;
        UpdateBar();
    }

    private void UpdateBar() => _bar.Fill = LastColor?.Brush ?? Brushes.Transparent;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty)
            _label.Text = Label;
        else if (change.Property == IconProperty || change.Property == IconOutlinedProperty)
            UpdateIcon();
        else if (change.Property == DefaultColorProperty)
            UpdateBar();
        else if (change.Property == CommandProperty || change.Property == NoneLabelProperty || change.Property == MoreLabelProperty
                 || change.Property == WeightCommandProperty || change.Property == DashCommandProperty)
            Rebuild();
    }

    private void UpdateIcon()
    {
        _icon.Data = Icon;
        if (IconOutlined)
        {
            _icon.Fill = null;
            _icon.Bind(Shape.StrokeProperty, _label.GetObservable(TextBlock.ForegroundProperty));
            _icon.StrokeThickness = 1.4;
        }
        else
        {
            _icon.Bind(Shape.FillProperty, _label.GetObservable(TextBlock.ForegroundProperty));
            _icon.Stroke = null;
        }
    }
}
