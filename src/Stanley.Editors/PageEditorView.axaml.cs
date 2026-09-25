using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stanley.Editing;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Avalonia.Platform.Storage;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Storage;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>
/// The page editor pane: the zoomable page canvas, an inline text editor that opens over
/// a bubble, and a status bar that always says what the pointer can do right now. Its
/// ribbon is <see cref="PageEditorRibbon"/>, hosted by the window above the dock area;
/// the two only meet through <see cref="PageEditorViewModel"/>.
/// </summary>
public partial class PageEditorView : UserControl
{
    private const string DialogueHint = "Type the dialogue · Enter = done · Shift+Enter = new line · Esc = cancel";
    private const string TextHint = "Type the text · Enter = done · Shift+Enter = new line · Esc = cancel";

    private PageEditorViewModel? _subscribed;

    /// <summary>What the inline text editor is open over: a bubble, or a text element (by id, so undo/redo shuffling the lists can't point it elsewhere).</summary>
    private (PanelId Panel, BubbleId? Bubble, ElementId? Element)? _editing;

    public PageEditorView()
    {
        InitializeComponent();

        PageCanvas.ViewChanged += OnCanvasViewChanged;

        InlineTextEditor.AddHandler(KeyDownEvent, OnInlineEditorKeyDown, RoutingStrategies.Tunnel);
        InlineTextEditor.LostFocus += (_, _) => EndTextEdit(commit: true);
        // The box grows with the text (see PositionTextEditor); keep it centred on the bubble.
        InlineTextEditor.SizeChanged += (_, _) => PositionTextEditor();
    }

    private PageEditorViewModel? ViewModel => DataContext as PageEditorViewModel;

    /// <summary>Exposed for headless UI tests, which live in a separate assembly from the generated x:Name fields.</summary>
    public PageCanvasControl Canvas => PageCanvas;

    /// <summary>Exposed for headless UI tests.</summary>
    public TextBox TextEditor => InlineTextEditor;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        EndTextEdit(commit: false);

        if (_subscribed != null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribed.TextEditRequested -= BeginTextEdit;
            _subscribed.ElementTextEditRequested -= BeginElementTextEdit;
            _subscribed.ViewportRequested -= OnViewportRequested;
            _subscribed.PictureImportRequested -= OnPictureImportRequested;
        }
        _subscribed = ViewModel;
        if (_subscribed != null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
            _subscribed.TextEditRequested += BeginTextEdit;
            _subscribed.ElementTextEditRequested += BeginElementTextEdit;
            _subscribed.ViewportRequested += OnViewportRequested;
            _subscribed.PictureImportRequested += OnPictureImportRequested;
        }

        PageCanvas.ViewModel = ViewModel;
        if (ViewModel is { } vm)
        {
            PageInfoText.Text = $"{DescribePaper(vm.PageBounds)} · {vm.PageBounds.Width:0.#} × {vm.PageBounds.Height:0.#} mm";
        }
        UpdateZoomText();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageEditorViewModel.Working) || _editing is not { } editing)
            return;
        // Undo/redo mid-edit could remove what's being typed into; don't leave a text box
        // floating over nothing. A restyle from the ribbon moves or resizes it instead.
        if (FindIndex(editing) < 0)
            EndTextEdit(commit: false);
        else
        {
            StyleTextEditor();
            PositionTextEditor();
        }
    }

    private static string DescribePaper(Rect2D page)
    {
        foreach (var size in Enum.GetValues<ProjectModel.MetricPaperSize>())
        {
            var paper = ProjectModel.MetricPaperSizes.Size(size);
            if (Math.Abs(paper.WidthMm - page.Width) < 0.5 && Math.Abs(paper.HeightMm - page.Height) < 0.5)
                return size.ToString();
            if (Math.Abs(paper.HeightMm - page.Width) < 0.5 && Math.Abs(paper.WidthMm - page.Height) < 0.5)
                return $"{size} landscape";
        }
        return "Custom";
    }

    // ---------------------------------------------------------------- view

    private void OnCanvasViewChanged()
    {
        UpdateZoomText();
        if (_editing != null)
            PositionTextEditor();
    }

    /// <summary>Reports the zoom back to the view model, where the ribbon's readout (outside this view) picks it up.</summary>
    private void UpdateZoomText()
    {
        if (ViewModel is { } vm)
            vm.ZoomPercent = PageCanvas.ZoomPercent;
    }

    private void OnViewportRequested(ViewportRequest request)
    {
        switch (request)
        {
            case ViewportRequest.ZoomIn: PageCanvas.ZoomIn(); break;
            case ViewportRequest.ZoomOut: PageCanvas.ZoomOut(); break;
            case ViewportRequest.FitPage: PageCanvas.FitPage(); break;
            case ViewportRequest.ActualSize: PageCanvas.ActualSize(); break;
            case ViewportRequest.FocusPage: PageCanvas.Focus(); break;
        }
    }

    // ---------------------------------------------------------------- pictures

    /// <summary>Picks a picture file for the view model (Insert › Picture, Background › Picture…) and hands it over.</summary>
    private async void OnPictureImportRequested(PictureImportRequest request)
    {
        if (ViewModel is not { } vm || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = request.AsBackground ? "Choose a picture to fill the panel" : "Choose a picture to place in the panel",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Pictures") { Patterns = IssueArt.Extensions.Select(e => "*." + e).ToList() }],
        });
        if (files is not [var picked])
            return;
        try
        {
            await using var stream = await picked.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var bytes = memory.ToArray();
            var file = IssueArt.IsSvg(picked.Name) ? ArtFile.Svg(System.Text.Encoding.UTF8.GetString(bytes)) : ArtFile.Png(bytes);
            vm.ImportPicture(request, picked.Name, file);
        }
        catch (IOException e)
        {
            ErrorText.Text = $"Couldn't read {picked.Name}: {e.Message}";
            ErrorText.IsVisible = true;
        }
        PageCanvas.Focus();
    }

    // ---------------------------------------------------------------- inline text editing

    /// <summary>
    /// Opens a see-through text box in the bubble's text area, at the lettering's on-screen
    /// size, so editing happens where the text lives. The canvas stops drawing that bubble's
    /// lettering and handles meanwhile, so the bubble itself is never covered.
    /// </summary>
    public void BeginTextEdit(PanelId panelId, int bubbleIndex)
    {
        if (ViewModel is not { } vm || !vm.Working.Panels.TryGetValue(panelId, out var panel) || bubbleIndex < 0 || bubbleIndex >= panel.Bubbles.Count)
            return;

        EndTextEdit(commit: true);
        vm.Select(panelId, bubbleIndex);
        var bubble = panel.Bubbles[bubbleIndex];
        _editing = (panelId, bubble.Id, null);
        PageCanvas.EditingBubble = new EditingBubble(panelId, bubble.Id);
        OpenTextEditor(bubble.Text, DialogueHint);
    }

    /// <summary>The same see-through editor over a text element's text area, lettered in its size, weight, slant, colour and alignment; the canvas leaves off that element's lettering (its box stays).</summary>
    public void BeginElementTextEdit(PanelId panelId, int index)
    {
        if (ViewModel is not { } vm || !vm.Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.Elements.Count
            || panel.Elements[index] is not TextElement text)
            return;

        EndTextEdit(commit: true);
        vm.SelectElement(panelId, index);
        _editing = (panelId, null, text.Id);
        PageCanvas.EditingText = text.Id;
        OpenTextEditor(text.Text, TextHint);
    }

    private void OpenTextEditor(string text, string hint)
    {
        InlineTextEditor.Text = text;
        StyleTextEditor();
        PositionTextEditor();
        InlineTextEditor.IsVisible = true;
        HintText.IsVisible = false;
        TextEditHintText.Text = hint;
        TextEditHintText.IsVisible = true;
        InlineTextEditor.Focus();
        InlineTextEditor.SelectAll();
    }

    /// <summary>The editor takes on the lettering's font - a bubble's or a text's - and a text's colours, so what's typed looks like what it'll be (bubble lettering is black).</summary>
    private void StyleTextEditor()
    {
        var style = EditedText()?.Style;
        var font = style is not null ? LetteringFont.Of(style) : EditedBubble() is { } bubble ? LetteringFont.Of(bubble) : LetteringFont.BubbleDefault;
        InlineTextEditor.FontFamily = LetteringFonts.AvaloniaFamily(font.Family);
        InlineTextEditor.TextAlignment = font.Align switch
        {
            TextAlign.Left => Avalonia.Media.TextAlignment.Left,
            TextAlign.Right => Avalonia.Media.TextAlignment.Right,
            _ => Avalonia.Media.TextAlignment.Center
        };
        InlineTextEditor.FontWeight = font.Bold ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
        InlineTextEditor.FontStyle = font.Italic ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal;
        // Hollow letters (no fill) are typed in their outline's colour, so they can be seen.
        InlineTextEditor.Foreground = style is { } s && (s.Color ?? s.Outline) is { } visible ? DrawingPalette.BrushOf(visible) : Avalonia.Media.Brushes.Black;
        InlineTextEditor.MaxLength = style is null ? BubbleEditing.MaxTextLength : TextEditing.MaxTextLength;
    }

    private ProjectModel.Bubbles.Bubble? EditedBubble() =>
        _editing is { Bubble: not null } editing && ViewModel is { } vm && FindIndex(editing) is var index and >= 0
            ? vm.Working.Panels[editing.Panel].Bubbles[index]
            : null;

    private TextElement? EditedText() =>
        _editing is { Element: not null } editing && ViewModel is { } vm && FindIndex(editing) is var index and >= 0
            ? vm.Working.Panels[editing.Panel].Elements[index] as TextElement
            : null;

    private void PositionTextEditor()
    {
        if (_editing is not { } editing || ViewModel is not { } vm || FindIndex(editing) is var index && index < 0)
            return;

        Rect rect;
        double fontSize;
        if (editing.Element is not null && vm.Working.Panels[editing.Panel].Elements[index] is TextElement text)
        {
            rect = PageCanvas.PageToControl(ElementRenderer.TextArea(text));
            fontSize = Math.Clamp(text.Style.FontSizeMm * PageCanvas.Zoom * 0.95, 11, 160);
        }
        else
        {
            var bubble = vm.Working.Panels[editing.Panel].Bubbles[index];
            rect = PageCanvas.PageToControl(BubbleTextRenderer.TextArea(bubble));
            fontSize = Math.Clamp(FontPoints.ToMm(LetteringFont.Of(bubble).SizePt) * PageCanvas.Zoom * 0.95, 11, 160);
        }
        // Height follows the text, so no line is ever clipped: when there's more text than
        // fits (or the box is smaller than a readable line at this zoom) it grows past it -
        // but it's transparent, so only the typed text spills over, never a box.
        var width = Math.Max(rect.Width, fontSize * 3);
        InlineTextEditor.Width = width;
        InlineTextEditor.MinHeight = rect.Height;
        InlineTextEditor.FontSize = fontSize;
        var height = Math.Max(InlineTextEditor.Bounds.Height, rect.Height);
        Avalonia.Controls.Canvas.SetLeft(InlineTextEditor, rect.Center.X - width / 2);
        Avalonia.Controls.Canvas.SetTop(InlineTextEditor, rect.Center.Y - height / 2);
    }

    private void OnInlineEditorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                EndTextEdit(commit: true);
                e.Handled = true;
                break;
            case Key.Escape:
                EndTextEdit(commit: false);
                e.Handled = true;
                break;
        }
    }

    /// <remarks>
    /// Bare text left with nothing typed in it (placed, then Esc, or cleared) is removed:
    /// it would print as nothing and only clutter the panel. A boxed caption stays, like an
    /// empty bubble - you can see it.
    /// </remarks>
    private void EndTextEdit(bool commit)
    {
        if (_editing is not { } editing)
            return;

        _editing = null;
        var text = InlineTextEditor.Text ?? "";
        InlineTextEditor.IsVisible = false;
        TextEditHintText.IsVisible = false;
        HintText.IsVisible = true;
        PageCanvas.EditingBubble = null;
        PageCanvas.EditingText = null;

        if (ViewModel is { } vm && FindIndex(editing) is var index and >= 0)
        {
            if (editing.Bubble is not null)
            {
                if (commit && vm.Working.Panels[editing.Panel].Bubbles[index].Text != text)
                    vm.SetBubbleText(editing.Panel, index, text);
            }
            else if (vm.Working.Panels[editing.Panel].Elements[index] is TextElement element)
            {
                var result = commit ? text : element.Text;
                if (string.IsNullOrWhiteSpace(result) && element.Style is { BoxFill: null, BoxStroke: null })
                    vm.DeleteElement(editing.Panel, index);
                else if (result != element.Text)
                    vm.SetElementText(editing.Panel, index, result);
            }
        }

        PageCanvas.Focus();
    }

    private int FindIndex((PanelId Panel, BubbleId? Bubble, ElementId? Element) editing)
    {
        if (ViewModel is not { } vm || !vm.Working.Panels.TryGetValue(editing.Panel, out var panel))
            return -1;
        if (editing.Bubble is { } bubbleId)
        {
            for (var i = 0; i < panel.Bubbles.Count; i++)
            {
                if (panel.Bubbles[i].Id.Equals(bubbleId))
                    return i;
            }
        }
        else if (editing.Element is { } elementId)
        {
            for (var i = 0; i < panel.Elements.Count; i++)
            {
                if (panel.Elements[i].Id == elementId)
                    return i;
            }
        }
        return -1;
    }
}
