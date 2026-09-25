using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>A colour on offer for drawing or lettering; a null <see cref="Color"/> is "none" (no fill, no outline, no box).</summary>
public sealed record PaletteColor(string Name, ColorValue? Color)
{
    public IBrush Brush { get; } = Color is { } c ? new SolidColorBrush(Avalonia.Media.Color.Parse(ColorSwatchChoice.ColorHex(c))) : Brushes.Transparent;

    public bool IsNone => Color is null;
}

/// <summary>A line thickness on offer, in page millimetres.</summary>
public sealed record ShapeWeightChoice(string Name, double Mm)
{
    /// <summary>A sample line this thick, for the menu (a few pixels at most).</summary>
    public double PreviewThickness => Math.Clamp(Mm * 1.6, 1, 8);
}

/// <summary>A kind of text on offer (caption, plain, sound effect).</summary>
public sealed record TextPresetChoice(TextStylePreset Preset)
{
    public string Name => TextStylePresets.Name(Preset);
}

/// <summary>A panel background on offer: none (the paper), a colour or a gradient.</summary>
public sealed record BackgroundChoice(string Name, PanelBackground? Background)
{
    public IBrush Preview { get; } = Background switch
    {
        ColorBackground c => new SolidColorBrush(Avalonia.Media.Color.Parse(ColorSwatchChoice.ColorHex(c.Color))),
        GradientBackground g => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Avalonia.Media.Color.Parse(ColorSwatchChoice.ColorHex(g.Top)), 0),
                new GradientStop(Avalonia.Media.Color.Parse(ColorSwatchChoice.ColorHex(g.Bottom)), 1)
            }
        },
        _ => Brushes.White
    };
}

/// <summary>
/// The colours, thicknesses and backgrounds the page editor offers. Colours are Word's:
/// the Office theme's ten Theme Colors with their lighter and darker shades, the ten
/// Standard Colors, and the custom colours picked lately (Recent Colors) - the same menu
/// Word's Shape Fill, Shape Outline, Text Fill and Text Outline buttons open.
/// </summary>
public static class DrawingPalette
{
    // Word's Office theme, in its column order: backgrounds and text first, then the six accents.
    private static readonly (string Name, string Hex)[] Theme =
    [
        ("White, Background 1", "#ffffff"), ("Black, Text 1", "#000000"), ("Gray, Background 2", "#e7e6e6"), ("Blue-Gray, Text 2", "#44546a"),
        ("Blue, Accent 1", "#4472c4"), ("Orange, Accent 2", "#ed7d31"), ("Gray, Accent 3", "#a5a5a5"), ("Gold, Accent 4", "#ffc000"),
        ("Blue, Accent 5", "#5b9bd5"), ("Green, Accent 6", "#70ad47"),
    ];

    /// <summary>Theme Colors, top row: the theme's own ten colours.</summary>
    public static IReadOnlyList<PaletteColor> ThemeColors { get; } = Theme.Select(t => new PaletteColor(t.Name, ColorValue.FromHex(t.Hex))).ToList();

    /// <summary>Theme Colors, the five rows under them: each column's lighter and darker shades, the way Word works them out (in HSL lightness).</summary>
    public static IReadOnlyList<IReadOnlyList<PaletteColor>> ThemeShades { get; } = Enumerable.Range(0, 5)
        .Select(row => (IReadOnlyList<PaletteColor>)Theme.Select((t, column) => Shade(t.Name, t.Hex, column, row)).ToList())
        .ToList();

    public static IReadOnlyList<PaletteColor> StandardColors { get; } =
    [
        C("Dark Red", "#c00000"), C("Red", "#ff0000"), C("Orange", "#ffc000"), C("Yellow", "#ffff00"), C("Light Green", "#92d050"),
        C("Green", "#00b050"), C("Light Blue", "#00b0f0"), C("Blue", "#0070c0"), C("Dark Blue", "#002060"), C("Purple", "#7030a0"),
    ];

    /// <summary>Every named colour on offer.</summary>
    public static IReadOnlyList<PaletteColor> Colors { get; } = [.. ThemeColors, .. ThemeShades.SelectMany(r => r), .. StandardColors];

    public static PaletteColor None { get; } = new("None", null);

    private static readonly List<PaletteColor> RecentList = [];

    /// <summary>Recent Colors: custom colours picked with "More Colors…" this session, newest first (at most ten), shared by every colour menu like Word's.</summary>
    public static IReadOnlyList<PaletteColor> RecentColors => RecentList;

    /// <summary>A custom colour just picked, remembered for Recent Colors.</summary>
    public static PaletteColor Remember(ColorValue color)
    {
        var picked = new PaletteColor(color.Hex, color);
        RecentList.RemoveAll(p => p.Color == color);
        RecentList.Insert(0, picked);
        if (RecentList.Count > 10)
            RecentList.RemoveAt(RecentList.Count - 1);
        return picked;
    }

    /// <summary>Line thicknesses for Weight ▸, in millimetres - Word's list, metric, going up to a marker's width for comic linework.</summary>
    public static IReadOnlyList<ShapeWeightChoice> Weights { get; } =
        new[] { 0.1, 0.25, 0.35, 0.5, 0.7, 1, 1.4, 2, 3, 5 }.Select(mm => new ShapeWeightChoice($"{mm:0.##} mm", mm)).ToList();

    /// <summary>Word's Dashes ▸, in its order.</summary>
    public static IReadOnlyList<(LineDash Dash, string Name)> Dashes { get; } =
    [
        (LineDash.Solid, "Solid"), (LineDash.RoundDot, "Round Dot"), (LineDash.SquareDot, "Square Dot"), (LineDash.Dash, "Dash"),
        (LineDash.DashDot, "Dash Dot"), (LineDash.LongDash, "Long Dash"), (LineDash.LongDashDot, "Long Dash Dot"),
    ];

    private static PaletteColor C(string name, string hex) => new(name, ColorValue.FromHex(hex));

    /// <summary>Word's shade rows: white darkens, black lightens, the light gray darkens a long way, everything else goes three steps lighter then two darker.</summary>
    private static PaletteColor Shade(string name, string hex, int column, int row)
    {
        var (step, lighter) = column switch
        {
            0 => (new[] { 0.05, 0.15, 0.25, 0.35, 0.5 }[row], false),
            1 => (new[] { 0.5, 0.35, 0.25, 0.15, 0.05 }[row], true),
            2 => (new[] { 0.1, 0.25, 0.5, 0.75, 0.9 }[row], false),
            _ => (new[] { 0.8, 0.6, 0.4, 0.25, 0.5 }[row], row < 3)
        };
        var color = ColorValue.FromHex(hex);
        var (h, s, l) = ToHsl(color);
        l = lighter ? l + (1 - l) * step : l * (1 - step);
        return new PaletteColor($"{name}, {(lighter ? "Lighter" : "Darker")} {step * 100:0}%", FromHsl(h, s, l));
    }

    private static (double H, double S, double L) ToHsl(ColorValue color)
    {
        var r = Convert.ToInt32(color.Hex[1..3], 16) / 255.0;
        var g = Convert.ToInt32(color.Hex[3..5], 16) / 255.0;
        var b = Convert.ToInt32(color.Hex[5..7], 16) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-9)
            return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    private static ColorValue FromHsl(double h, double s, double l)
    {
        double Channel(double t)
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        var (r, g, b) = s == 0 ? (l, l, l) : (Channel(h + 1.0 / 3), Channel(h), Channel(h - 1.0 / 3));
        return ColorValue.FromHex($"#{(int)Math.Round(r * 255):x2}{(int)Math.Round(g * 255):x2}{(int)Math.Round(b * 255):x2}");
    }

    private static BackgroundChoice Fill(string name, string hex) => new(name, new ColorBackground(ColorValue.FromHex(hex)));

    private static BackgroundChoice Blend(string name, string top, string bottom) =>
        new(name, new GradientBackground(ColorValue.FromHex(top), ColorValue.FromHex(bottom)));

    public static IReadOnlyList<BackgroundChoice> Backgrounds { get; } =
    [
        new("Paper", null),
        Fill("Pale yellow", "#fbf3d5"), Fill("Pale blue", "#d6eaf8"), Fill("Pale pink", "#fadbd8"), Fill("Pale green", "#d5f5e3"),
        Fill("Light grey", "#e5e7e9"), Fill("Grey", "#95a5a6"), Fill("Dark grey", "#4d5656"), Fill("Black", "#111111"),
        Fill("Red", "#c0392b"), Fill("Orange", "#e67e22"), Fill("Yellow", "#f1c40f"), Fill("Sky", "#5dade2"), Fill("Navy", "#1b2a49"),
        Blend("Day sky", "#4a90d9", "#d6eaf8"), Blend("Sunset", "#5b2c6f", "#f5b041"), Blend("Dawn", "#fad7a0", "#fdfefe"),
        Blend("Night", "#0b1026", "#2c3e70"), Blend("Underwater", "#48c9b0", "#154360"), Blend("Fog", "#f2f3f4", "#aab7b8"),
        Blend("Fire", "#f4d03f", "#c0392b"),
    ];

    /// <summary>The name a colour goes by in the menus - its palette name, else its hex.</summary>
    public static string NameOf(ColorValue? color) =>
        color is not { } c ? "None" : Colors.FirstOrDefault(p => p.Color == c)?.Name ?? c.Hex;

    public static IBrush BrushOf(ColorValue? color) =>
        color is not { } c ? Brushes.Transparent
        : Colors.FirstOrDefault(p => p.Color == c)?.Brush ?? new SolidColorBrush(Avalonia.Media.Color.Parse(ColorSwatchChoice.ColorHex(c)));
}

public sealed partial class PageEditorViewModel
{
    /// <summary>Size of a rectangle or ellipse placed with a click rather than a drag, in mm.</summary>
    public const double DefaultShapeWidthMm = 30;
    public const double DefaultShapeHeightMm = 20;

    private int _selectedElementIndex = -1;
    private ShapeStyle _newShapeStyle = ShapeEditing.DefaultStyle;
    private ElementLayer _newShapeLayer = ElementLayer.Background;
    private TextStyle _newTextStyle = TextStylePresets.Style(TextStylePreset.Caption);
    private ElementLayer _newTextLayer = ElementLayer.Foreground;
    private PanelId? _drawingPanel;
    private ElementId _drawingId;
    private bool _drawingValid;

    private void InitializeElementCommands()
    {
        SetStrokeColorCommand = new RelayCommand<PaletteColor>(c => { if (c != null) SetCurrentShapeStyle(CurrentShapeStyle with { Stroke = c.Color }); });
        SetFillColorCommand = new RelayCommand<PaletteColor>(c => { if (c != null) SetCurrentShapeStyle(CurrentShapeStyle with { Fill = c.Color }); });
        // Like Word: picking a weight or a dash for "No Outline" turns the outline on.
        SetStrokeWeightCommand = new RelayCommand<ShapeWeightChoice>(w =>
        {
            if (w != null)
                SetCurrentShapeStyle(CurrentShapeStyle with { StrokeWidthMm = w.Mm, Stroke = CurrentShapeStyle.Stroke ?? TextStylePresets.Ink });
        });
        SetStrokeDashCommand = new RelayCommand<LineDash>(dash =>
            SetCurrentShapeStyle(CurrentShapeStyle with { Dash = dash, Stroke = CurrentShapeStyle.Stroke ?? TextStylePresets.Ink }));
        SetTextColorCommand = new RelayCommand<PaletteColor>(c => { if (c != null) SetCurrentTextStyle(CurrentTextStyle with { Color = c.Color }); });
        SetTextOutlineCommand = new RelayCommand<PaletteColor>(c => { if (c != null) SetCurrentTextStyle(CurrentTextStyle with { Outline = c.Color }); });
        SetTextOutlineWeightCommand = new RelayCommand<ShapeWeightChoice>(w =>
        {
            if (w != null)
                SetCurrentTextStyle(CurrentTextStyle with { OutlineWidthMm = w.Mm, Outline = CurrentTextStyle.Outline ?? TextStylePresets.Ink });
        });
        SetBoxFillCommand = new RelayCommand<PaletteColor>(c => { if (c != null) SetCurrentTextStyle(CurrentTextStyle with { BoxFill = c.Color }); });
        SetBoxOutlineCommand = new RelayCommand<PaletteColor>(c => { if (c != null) SetCurrentTextStyle(CurrentTextStyle with { BoxStroke = c.Color }); });
        SetBoxWeightCommand = new RelayCommand<ShapeWeightChoice>(w =>
        {
            if (w != null)
                SetCurrentTextStyle(CurrentTextStyle with { BoxStrokeWidthMm = w.Mm, BoxStroke = CurrentTextStyle.BoxStroke ?? TextStylePresets.Ink });
        });
        SetBoxDashCommand = new RelayCommand<LineDash>(dash =>
            SetCurrentTextStyle(CurrentTextStyle with { BoxDash = dash, BoxStroke = CurrentTextStyle.BoxStroke ?? TextStylePresets.Ink }));
        ApplyTextPresetCommand = new RelayCommand<TextStylePreset>(preset => SetCurrentTextStyle(TextStylePresets.Style(preset)));
        BiggerTextCommand = new RelayCommand(() => SetCurrentTextStyle(CurrentTextStyle with { FontSizeMm = TextEditing.Bigger(CurrentTextStyle.FontSizeMm) }));
        SmallerTextCommand = new RelayCommand(() => SetCurrentTextStyle(CurrentTextStyle with { FontSizeMm = TextEditing.Smaller(CurrentTextStyle.FontSizeMm) }));
        InsertTextCommand = new RelayCommand<TextStylePreset>(preset => InsertText(preset), _ => Working.PanelOrder.Count > 0);
        UseToolCommand = new RelayCommand<PageEditorTool>(tool => Tool = tool);
        InitializePictureCommands();
        SetBackgroundCommand = new RelayCommand<BackgroundChoice>(choice =>
        {
            if (choice != null && _selectedPanelId is { } panelId)
                SetPanelBackground(panelId, choice.Background);
        }, _ => HasSelectedPanel);
    }

    private void NotifyElementCommands()
    {
        InsertTextCommand.NotifyCanExecuteChanged();
        SetBackgroundCommand.NotifyCanExecuteChanged();
        NotifyPictureCommands();
    }

    // ---------------------------------------------------------------- ribbon

    public IReadOnlyList<PaletteColor> PaletteColors => DrawingPalette.Colors;
    public IReadOnlyList<ShapeWeightChoice> WeightChoices => DrawingPalette.Weights;
    public IReadOnlyList<TextPresetChoice> TextPresetChoices { get; } = TextStylePresets.All.Select(p => new TextPresetChoice(p)).ToList();
    public IReadOnlyList<BackgroundChoice> BackgroundChoices => DrawingPalette.Backgrounds;

    // Word's Shape Fill / Shape Outline for shapes - and for a text's box - and Text Fill / Text
    // Outline for its letters. Each changes the selection and becomes the default for new ones.

    public IRelayCommand<PaletteColor> SetStrokeColorCommand { get; private set; } = null!;
    public IRelayCommand<PaletteColor> SetFillColorCommand { get; private set; } = null!;
    public IRelayCommand<ShapeWeightChoice> SetStrokeWeightCommand { get; private set; } = null!;
    public IRelayCommand<LineDash> SetStrokeDashCommand { get; private set; } = null!;

    /// <summary>Text Fill: the letters' colour, or none - hollow letters, only their outline showing.</summary>
    public IRelayCommand<PaletteColor> SetTextColorCommand { get; private set; } = null!;

    /// <summary>Text Outline: an outline around each letter, like a sound effect's, or none.</summary>
    public IRelayCommand<PaletteColor> SetTextOutlineCommand { get; private set; } = null!;
    public IRelayCommand<ShapeWeightChoice> SetTextOutlineWeightCommand { get; private set; } = null!;

    /// <summary>Shape Fill for a text: a box behind it in that colour, or none.</summary>
    public IRelayCommand<PaletteColor> SetBoxFillCommand { get; private set; } = null!;

    /// <summary>Shape Outline for a text: a border round its box, or none.</summary>
    public IRelayCommand<PaletteColor> SetBoxOutlineCommand { get; private set; } = null!;
    public IRelayCommand<ShapeWeightChoice> SetBoxWeightCommand { get; private set; } = null!;
    public IRelayCommand<LineDash> SetBoxDashCommand { get; private set; } = null!;
    public IRelayCommand<TextStylePreset> ApplyTextPresetCommand { get; private set; } = null!;
    public IRelayCommand BiggerTextCommand { get; private set; } = null!;
    public IRelayCommand SmallerTextCommand { get; private set; } = null!;

    /// <summary>Insert tab: text of that kind in the selected (or first) panel, ready to type into.</summary>
    public IRelayCommand<TextStylePreset> InsertTextCommand { get; private set; } = null!;

    /// <summary>Insert tab: switches to a drawing tool.</summary>
    public IRelayCommand<PageEditorTool> UseToolCommand { get; private set; } = null!;

    /// <summary>Fills the selected panel (or the panel of whatever is selected) with a background.</summary>
    public IRelayCommand<BackgroundChoice> SetBackgroundCommand { get; private set; } = null!;

    /// <summary>Raised when something (the ribbon, a double-click, Enter) wants the inline text editor opened over a text element.</summary>
    public event Action<PanelId, int>? ElementTextEditRequested;

    public void RequestElementTextEdit(PanelId panelId, int index)
    {
        if (Working.Panels.TryGetValue(panelId, out var panel) && index >= 0 && index < panel.Elements.Count && panel.Elements[index] is TextElement)
            ElementTextEditRequested?.Invoke(panelId, index);
    }

    // ---------------------------------------------------------------- tools

    public bool IsDrawTool { get => Tool == PageEditorTool.Draw; set => SetToolFlag(PageEditorTool.Draw, value); }
    public bool IsLineTool { get => Tool == PageEditorTool.Line; set => SetToolFlag(PageEditorTool.Line, value); }
    public bool IsRectangleTool { get => Tool == PageEditorTool.Rectangle; set => SetToolFlag(PageEditorTool.Rectangle, value); }
    public bool IsEllipseTool { get => Tool == PageEditorTool.Ellipse; set => SetToolFlag(PageEditorTool.Ellipse, value); }
    public bool IsTextTool { get => Tool == PageEditorTool.Text; set => SetToolFlag(PageEditorTool.Text, value); }

    /// <summary>Any of the tools that draw a shape.</summary>
    public bool IsShapeTool => Tool is PageEditorTool.Draw or PageEditorTool.Line or PageEditorTool.Rectangle or PageEditorTool.Ellipse;

    private void RaiseToolFlagsChanged()
    {
        OnPropertyChanged(nameof(IsDrawTool));
        OnPropertyChanged(nameof(IsLineTool));
        OnPropertyChanged(nameof(IsRectangleTool));
        OnPropertyChanged(nameof(IsEllipseTool));
        OnPropertyChanged(nameof(IsTextTool));
        OnPropertyChanged(nameof(IsShapeTool));
        RaiseElementDerivedChanged();
    }

    // ---------------------------------------------------------------- selection

    /// <summary>Index into the selected panel's <see cref="Panel.Elements"/>, or -1.</summary>
    public int SelectedElementIndex => _selectedElementIndex;

    public PanelElement? SelectedElement =>
        SelectedPanel is { } panel && _selectedElementIndex >= 0 && _selectedElementIndex < panel.Elements.Count
            ? panel.Elements[_selectedElementIndex]
            : null;

    public ShapeElement? SelectedShape => SelectedElement as ShapeElement;
    public TextElement? SelectedText => SelectedElement as TextElement;
    public bool HasSelectedElement => SelectedElement is not null;
    public bool HasSelectedShape => SelectedShape is not null;
    public bool HasSelectedText => SelectedText is not null;

    /// <summary>A drawn shape is selected: the ribbon shows its "Shape" contextual tab.</summary>
    public bool IsShapeContext => HasSelectedShape;

    /// <summary>A text element is selected: the ribbon shows its "Text" contextual tab.</summary>
    public bool IsTextContext => HasSelectedText;

    public void SelectElement(PanelId panelId, int index) => Select(panelId, -1, -1, index);

    private void RaiseElementSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedElementIndex));
        OnPropertyChanged(nameof(HasSelectedElement));
        OnPropertyChanged(nameof(HasSelectedShape));
        OnPropertyChanged(nameof(HasSelectedText));
        OnPropertyChanged(nameof(IsShapeContext));
        OnPropertyChanged(nameof(IsTextContext));
        OnPropertyChanged(nameof(IsPictureContext));
        RaiseElementDerivedChanged();
    }

    private object? _elementDerivedKey;

    /// <summary>
    /// Everything the ribbon shows about the selected element (or the next new one) and the
    /// selected panel's background - raised only when one of them changed, not on every
    /// pointer move of a drag.
    /// </summary>
    private void RaiseElementDerivedChanged()
    {
        var key = (_selectedPanelId, SelectedShape?.Style, SelectedText?.Style, SelectedElement?.Layer, SelectedPanel?.Background,
            _newShapeStyle, _newTextStyle, _newShapeLayer, _newTextLayer, Tool);
        if (Equals(key, _elementDerivedKey))
            return;
        _elementDerivedKey = key;
        OnPropertyChanged(nameof(CurrentShapeStyle));
        OnPropertyChanged(nameof(StrokeBrush));
        OnPropertyChanged(nameof(FillBrush));
        OnPropertyChanged(nameof(StrokeName));
        OnPropertyChanged(nameof(FillName));
        OnPropertyChanged(nameof(WeightName));
        OnPropertyChanged(nameof(CurrentTextStyle));
        OnPropertyChanged(nameof(TextColorBrush));
        OnPropertyChanged(nameof(TextBoxBrush));
        OnPropertyChanged(nameof(TextOutlineBrush));
        OnPropertyChanged(nameof(TextSizeText));
        OnPropertyChanged(nameof(TextPresetName));
        OnPropertyChanged(nameof(IsTextBold));
        OnPropertyChanged(nameof(IsTextItalic));
        OnPropertyChanged(nameof(IsTextAlignLeft));
        OnPropertyChanged(nameof(IsTextAlignCenter));
        OnPropertyChanged(nameof(IsTextAlignRight));
        OnPropertyChanged(nameof(CurrentElementLayer));
        OnPropertyChanged(nameof(IsElementBehind));
        OnPropertyChanged(nameof(IsElementInFront));
        OnPropertyChanged(nameof(SelectedPanelBackground));
        OnPropertyChanged(nameof(BackgroundPreview));
        OnPropertyChanged(nameof(BackgroundName));
    }

    // ---------------------------------------------------------------- drawing shapes

    /// <summary>Starts drawing a shape with the current tool into <paramref name="panelId"/>; the <c>UpdateDraw*</c> calls show it live, <see cref="CommitDrawShape"/> keeps it.</summary>
    public void BeginDrawShape(PanelId panelId)
    {
        if (!Working.Panels.ContainsKey(panelId))
            return;
        _drawingPanel = panelId;
        _drawingId = ElementId.New();
        _drawingValid = false;
        BeginGesture();
    }

    /// <summary>The freehand pen: the whole trail so far, in page mm. <paramref name="toleranceMm"/> is how far the smoothed curve may stray from it (about a screen pixel), <paramref name="closeDistanceMm"/> how near the start the end must come to close the shape.</summary>
    public void UpdateDrawFreehand(IReadOnlyList<Point2D> trail, double toleranceMm, double closeDistanceMm) =>
        UpdateDrawing(ShapeEditing.Freehand(trail, _newShapeStyle, _newShapeLayer, toleranceMm, closeDistanceMm));

    /// <summary>The line, rectangle and ellipse tools, dragged from <paramref name="from"/> to <paramref name="to"/>; <paramref name="constrain"/> (Shift) keeps a line level, upright or at 45° and makes a rectangle square, an ellipse round.</summary>
    public void UpdateDrawShape(Point2D from, Point2D to, bool constrain = false)
    {
        if (Tool == PageEditorTool.Line)
        {
            if (constrain)
            {
                var angle = Math.Round(Math.Atan2(to.Y - from.Y, to.X - from.X) / (Math.PI / 4)) * (Math.PI / 4);
                var length = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
                to = new Point2D(from.X + Math.Cos(angle) * length, from.Y + Math.Sin(angle) * length);
            }
            UpdateDrawing(ShapeEditing.Line(from, to, _newShapeStyle, _newShapeLayer));
            return;
        }

        if (constrain)
        {
            var side = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
            to = new Point2D(from.X + Math.CopySign(side, to.X - from.X), from.Y + Math.CopySign(side, to.Y - from.Y));
        }
        var box = Rect2D.FromEdges(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Max(from.X, to.X), Math.Max(from.Y, to.Y));
        UpdateDrawing(Tool == PageEditorTool.Ellipse
            ? ShapeEditing.Ellipse(box, _newShapeStyle, _newShapeLayer)
            : ShapeEditing.Rectangle(box, _newShapeStyle, _newShapeLayer));
    }

    private void UpdateDrawing(EditResult<ShapeElement> result)
    {
        if (_drawingPanel is not { } panelId || !IsGestureActive)
            return;
        _drawingValid = result.IsValid;
        if (!result.IsValid)
        {
            // Not a shape yet (a click, a tiny wiggle): show nothing rather than an error.
            UpdateGesture(EditResult<PageDocument>.Success(Committed));
            return;
        }
        var shape = result.Value with { Id = _drawingId };
        UpdateGesture(EditPanel(Committed, panelId, p => EditResult<Panel>.Success(p with { Elements = [.. p.Elements, shape] })));
    }

    /// <summary>
    /// Keeps the shape being drawn (one undo step) and returns its index in its panel, or -1
    /// if it never became a shape. The pen stays on for the next stroke; the line, rectangle
    /// and ellipse tools hand back to Select with the new shape selected, ready to adjust.
    /// </summary>
    public int CommitDrawShape()
    {
        var panelId = _drawingPanel;
        _drawingPanel = null;
        if (panelId is not { } id || !_drawingValid)
        {
            EndGesture(commit: false);
            return -1;
        }

        EndGesture(commit: true);
        var index = IndexOfElement(id, _drawingId);
        if (index >= 0 && Tool != PageEditorTool.Draw)
        {
            Tool = PageEditorTool.Select;
            SelectElement(id, index);
        }
        return index;
    }

    public void CancelDrawShape()
    {
        _drawingPanel = null;
        EndGesture(commit: false);
    }

    private int IndexOfElement(PanelId panelId, ElementId elementId)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel))
            return -1;
        for (var i = 0; i < panel.Elements.Count; i++)
        {
            if (panel.Elements[i].Id == elementId)
                return i;
        }
        return -1;
    }

    // ---------------------------------------------------------------- text

    /// <summary>
    /// New text with the current text style (or <paramref name="style"/>) in the panel: in
    /// <paramref name="box"/> when dragged out, else one line's worth at
    /// <paramref name="at"/> (its start, middle or end, following the alignment), slid inside
    /// the panel. Selected; returns its index, or -1. Open the text editor on it next.
    /// </summary>
    public int CreateText(PanelId panelId, Point2D at, Rect2D? box = null, TextStyle? style = null)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel))
            return -1;
        style ??= _newTextStyle;
        var panelBounds = Bounds(panel);

        Rect2D rect;
        if (box is { } dragged && dragged.Width >= TextEditing.MinWidthMm)
        {
            rect = dragged with { Height = Math.Max(dragged.Height, TextEditing.MinHeightMm) };
        }
        else
        {
            var width = Math.Min(TextStylePresets.DefaultWidthMm(TextStylePresets.Of(style) ?? TextStylePreset.Plain), panelBounds.Width);
            var left = style.Align switch
            {
                TextAlign.Left => at.X,
                TextAlign.Right => at.X - width,
                _ => at.X - width / 2
            };
            rect = new Rect2D(left, at.Y, width, TextEditing.MinHeightMm);
        }

        var created = TextEditing.Create(rect, style, _newTextLayer);
        if (!created.IsValid)
        {
            Apply(EditResult<PageDocument>.Failure(created.Error!));
            return -1;
        }

        var text = TextEditing.GrowToFit(created.Value, ElementRenderer.NeededHeight(created.Value));
        if (box is null)
        {
            // Centre the first line on the click, then slide the box into the panel if it fits.
            var b = text.Bounds;
            var top = b.Top - b.Height / 2;
            var left = b.Width <= panelBounds.Width ? Math.Clamp(b.Left, panelBounds.Left, panelBounds.Right - b.Width) : b.Left;
            top = b.Height <= panelBounds.Height ? Math.Clamp(top, panelBounds.Top, panelBounds.Bottom - b.Height) : top;
            text = text with { Bounds = b with { X = left, Y = top } };
        }

        var element = ElementEditing.KeepReachable(text, panelBounds);
        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with { Elements = [.. p.Elements, element] })));
        var index = IndexOfElement(panelId, element.Id);
        if (index >= 0)
            SelectElement(panelId, index);
        return index;
    }

    /// <summary>Insert tab: text of <paramref name="preset"/>'s kind in the selected (or first) panel - a caption in its top-left corner, anything else in its middle - with the text editor open. It also becomes the kind the Text tool makes next.</summary>
    public int InsertText(TextStylePreset preset)
    {
        if (_selectedPanelId is not { } panelId || !Working.Panels.ContainsKey(panelId))
        {
            if (Working.PanelOrder.Count == 0)
                return -1;
            panelId = Working.PanelOrder[0];
        }

        var style = TextStylePresets.Style(preset);
        _newTextStyle = style;
        var bounds = PanelBounds(panelId);
        var width = Math.Min(TextStylePresets.DefaultWidthMm(preset), bounds.Width);
        const double inset = 1.5;
        var index = preset == TextStylePreset.Caption
            ? CreateText(panelId, default, new Rect2D(bounds.Left + inset, bounds.Top + inset, width, TextEditing.MinHeightMm), style)
            : CreateText(panelId, new Point2D(bounds.MidX, bounds.Top + bounds.Height * (preset == TextStylePreset.SoundEffect ? 0.5 : 0.3)), style: style);
        if (index >= 0)
            RequestElementTextEdit(panelId, index);
        return index;
    }

    /// <summary>Sets a text element's words (one undo step), growing its box to fit them.</summary>
    public void SetElementText(PanelId panelId, int index, string value) =>
        Apply(EditElementInPanel(Working, panelId, index, e => e is TextElement text
            ? TextEditing.SetText(text, value) is { IsValid: true } set
                ? EditResult<PanelElement>.Success(TextEditing.GrowToFit(set.Value, ElementRenderer.NeededHeight(set.Value)))
                : EditResult<PanelElement>.Failure($"Text can't exceed {TextEditing.MaxTextLength} characters.")
            : EditResult<PanelElement>.Failure("That isn't text.")));

    // ---------------------------------------------------------------- moving, resizing, arranging

    public void BeginMoveElement(PanelId panelId, int index) => BeginGesture();

    /// <summary>Moves by (<paramref name="dx"/>, <paramref name="dy"/>) from where the element was when the drag began.</summary>
    public void UpdateMoveElement(PanelId panelId, int index, double dx, double dy) =>
        UpdateGesture(EditElementInPanel(Committed, panelId, index, e => EditResult<PanelElement>.Success(ElementEditing.Move(e, dx, dy))));

    public void BeginResizeElement(PanelId panelId, int index) => BeginGesture();

    public void UpdateResizeElement(PanelId panelId, int index, Rect2D newBounds) =>
        UpdateGesture(EditElementInPanel(Committed, panelId, index, e => ElementEditing.Resize(e, newBounds)));

    public void DeleteElement(PanelId panelId, int index)
    {
        Apply(EditPanel(Working, panelId, p =>
            index < 0 || index >= p.Elements.Count
                ? EditResult<Panel>.Failure("Nothing to delete.")
                : EditResult<Panel>.Success(p with { Elements = p.Elements.Where((_, i) => i != index).ToList() })));
        if (Equals(_selectedPanelId, panelId) && _selectedElementIndex == index)
            Select(panelId);
    }

    /// <summary>To the front or back of its layer.</summary>
    public void ReorderElement(PanelId panelId, int index, bool toFront)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.Elements.Count)
            return;
        var (list, newIndex) = ElementEditing.Reorder(panel.Elements, index, toFront);
        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with { Elements = list })));
        if (Equals(_selectedPanelId, panelId) && _selectedElementIndex == index)
            SelectElement(panelId, newIndex);
    }

    /// <summary>Behind or in front of the panel's characters.</summary>
    public void SetElementLayer(PanelId panelId, int index, ElementLayer layer) =>
        Apply(EditElementInPanel(Working, panelId, index, e => EditResult<PanelElement>.Success(ElementEditing.SetLayer(e, layer))));

    public void SetShapeStyle(PanelId panelId, int index, ShapeStyle style) =>
        Apply(EditElementInPanel(Working, panelId, index, e => e is ShapeElement shape
            ? ShapeEditing.SetStyle(shape, style) is { IsValid: true } styled ? EditResult<PanelElement>.Success(styled.Value) : EditResult<PanelElement>.Failure("Line too thick.")
            : EditResult<PanelElement>.Failure("That isn't a shape.")));

    /// <summary>Restyles a text element; its box grows if the new style needs more room.</summary>
    public void SetTextStyle(PanelId panelId, int index, TextStyle style) =>
        Apply(EditElementInPanel(Working, panelId, index, e => e is TextElement text
            ? TextEditing.SetStyle(text, style) is { IsValid: true } styled
                ? EditResult<PanelElement>.Success(TextEditing.GrowToFit(styled.Value, ElementRenderer.NeededHeight(styled.Value)))
                : EditResult<PanelElement>.Failure($"Letters must be {TextEditing.MinFontSizeMm}-{TextEditing.MaxFontSizeMm}mm tall.")
            : EditResult<PanelElement>.Failure("That isn't text.")));

    // ---------------------------------------------------------------- style: the selection's, and the next new one's

    /// <summary>Like the bubble style: shows the selected shape's style, and changing it restyles the selection and becomes the pen for new shapes.</summary>
    public ShapeStyle CurrentShapeStyle => SelectedShape?.Style ?? _newShapeStyle;

    private void SetCurrentShapeStyle(ShapeStyle style)
    {
        _newShapeStyle = style;
        if (SelectedShape is { } shape && shape.Style != style)
            SetShapeStyle(_selectedPanelId!.Value, _selectedElementIndex, style);
        RaiseElementDerivedChanged();
    }

    public IBrush StrokeBrush => DrawingPalette.BrushOf(CurrentShapeStyle.Stroke);
    public IBrush FillBrush => DrawingPalette.BrushOf(CurrentShapeStyle.Fill);
    public string StrokeName => DrawingPalette.NameOf(CurrentShapeStyle.Stroke);
    public string FillName => DrawingPalette.NameOf(CurrentShapeStyle.Fill);

    public string WeightName =>
        DrawingPalette.Weights.FirstOrDefault(w => Math.Abs(w.Mm - CurrentShapeStyle.StrokeWidthMm) < 1e-6)?.Name ?? $"{CurrentShapeStyle.StrokeWidthMm:0.##} mm";

    /// <summary>The selected text's style, or the one new text gets.</summary>
    public TextStyle CurrentTextStyle => SelectedText?.Style ?? _newTextStyle;

    private void SetCurrentTextStyle(TextStyle style)
    {
        _newTextStyle = style;
        if (SelectedText is { } text && text.Style != style)
            SetTextStyle(_selectedPanelId!.Value, _selectedElementIndex, style);
        RaiseElementDerivedChanged();
    }

    public IBrush TextColorBrush => DrawingPalette.BrushOf(CurrentTextStyle.Color);
    public IBrush TextBoxBrush => DrawingPalette.BrushOf(CurrentTextStyle.BoxFill);
    public IBrush TextOutlineBrush => DrawingPalette.BrushOf(CurrentTextStyle.Outline);
    public string TextSizeText => $"{CurrentTextStyle.FontSizeMm:0.#} mm";
    public string TextPresetName => TextStylePresets.Of(CurrentTextStyle) is { } preset ? TextStylePresets.Name(preset) : "Custom";

    public bool IsTextBold
    {
        get => CurrentTextStyle.Bold;
        set => SetCurrentTextStyle(CurrentTextStyle with { Bold = value });
    }

    public bool IsTextItalic
    {
        get => CurrentTextStyle.Italic;
        set => SetCurrentTextStyle(CurrentTextStyle with { Italic = value });
    }

    public bool IsTextAlignLeft { get => CurrentTextStyle.Align == TextAlign.Left; set => SetAlign(TextAlign.Left, value); }
    public bool IsTextAlignCenter { get => CurrentTextStyle.Align == TextAlign.Center; set => SetAlign(TextAlign.Center, value); }
    public bool IsTextAlignRight { get => CurrentTextStyle.Align == TextAlign.Right; set => SetAlign(TextAlign.Right, value); }

    private void SetAlign(TextAlign align, bool value)
    {
        if (value)
            SetCurrentTextStyle(CurrentTextStyle with { Align = align });
        else
            RaiseToggles(); // a toggle that flipped itself off hears "no, you're still on"
    }

    /// <summary>Re-raises the ribbon's toggle states even though nothing changed: a toggle button that flipped itself off locally needs to hear it's still on.</summary>
    private void RaiseToggles()
    {
        OnPropertyChanged(nameof(IsTextAlignLeft));
        OnPropertyChanged(nameof(IsTextAlignCenter));
        OnPropertyChanged(nameof(IsTextAlignRight));
        OnPropertyChanged(nameof(IsElementBehind));
        OnPropertyChanged(nameof(IsElementInFront));
    }

    // ---------------------------------------------------------------- layer

    /// <summary>The selected element's layer, or the one the current tool's next element goes into.</summary>
    public ElementLayer CurrentElementLayer => SelectedElement?.Layer ?? (Tool == PageEditorTool.Text ? _newTextLayer : _newShapeLayer);

    public bool IsElementBehind { get => CurrentElementLayer == ElementLayer.Background; set => SetCurrentLayer(ElementLayer.Background, value); }
    public bool IsElementInFront { get => CurrentElementLayer == ElementLayer.Foreground; set => SetCurrentLayer(ElementLayer.Foreground, value); }

    private void SetCurrentLayer(ElementLayer layer, bool value)
    {
        if (value)
        {
            if (SelectedElement is { } element)
            {
                if (element is TextElement)
                    _newTextLayer = layer;
                else
                    _newShapeLayer = layer;
                if (element.Layer != layer)
                    SetElementLayer(_selectedPanelId!.Value, _selectedElementIndex, layer);
            }
            else if (Tool == PageEditorTool.Text)
            {
                _newTextLayer = layer;
            }
            else
            {
                _newShapeLayer = layer;
            }
        }
        RaiseElementDerivedChanged();
        RaiseToggles();
    }

    // ---------------------------------------------------------------- panel background

    /// <summary>The background of the selected panel (the panel of whatever is selected), or null.</summary>
    public PanelBackground? SelectedPanelBackground => SelectedPanel?.Background;

    public IBrush BackgroundPreview => DrawingPalette.Backgrounds.FirstOrDefault(b => Equals(b.Background, SelectedPanelBackground))?.Preview
        ?? new BackgroundChoice("", SelectedPanelBackground).Preview;

    public string BackgroundName => SelectedPanel is null ? ""
        : SelectedPanelBackground is InlineBackground ? "Picture"
        : DrawingPalette.Backgrounds.FirstOrDefault(b => Equals(b.Background, SelectedPanelBackground))?.Name ?? "Custom";

    /// <summary>Fills a panel with a background (null = back to plain paper), in one undo step. Not a layout change, so a locked layout allows it.</summary>
    public void SetPanelBackground(PanelId panelId, PanelBackground? background) =>
        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(Equals(p.Background, background) ? p : p with { Background = background })));

    // ---------------------------------------------------------------- helpers

    /// <summary>Applies <paramref name="edit"/> to one element, then keeps it reachable inside its panel - the single place that enforces "an element belongs to its panel".</summary>
    private static EditResult<PageDocument> EditElementInPanel(PageDocument document, PanelId panelId, int index, Func<PanelElement, EditResult<PanelElement>> edit) =>
        EditPanel(document, panelId, panel =>
        {
            if (index < 0 || index >= panel.Elements.Count)
                return EditResult<Panel>.Failure("No such element.");
            var result = edit(panel.Elements[index]);
            if (!result.IsValid)
                return EditResult<Panel>.Failure(result.Error!);
            var list = panel.Elements.ToList();
            list[index] = ElementEditing.KeepReachable(result.Value, Bounds(panel));
            return EditResult<Panel>.Success(panel with { Elements = list });
        });
}
