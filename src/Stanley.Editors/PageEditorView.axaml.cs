using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stanley.Editing;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
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
    private PageEditorViewModel? _subscribed;
    private (PanelId Panel, BubbleId Bubble)? _editing;

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
            _subscribed.ViewportRequested -= OnViewportRequested;
        }
        _subscribed = ViewModel;
        if (_subscribed != null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
            _subscribed.TextEditRequested += BeginTextEdit;
            _subscribed.ViewportRequested += OnViewportRequested;
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
        // Undo/redo mid-edit could remove the bubble being typed into; don't leave a
        // text box floating over nothing.
        if (e.PropertyName == nameof(PageEditorViewModel.Working) && _editing is { } editing && FindBubbleIndex(editing) < 0)
            EndTextEdit(commit: false);
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
        }
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
        _editing = (panelId, panel.Bubbles[bubbleIndex].Id);
        InlineTextEditor.Text = panel.Bubbles[bubbleIndex].Text;
        PageCanvas.EditingBubble = new EditingBubble(panelId, panel.Bubbles[bubbleIndex].Id);
        PositionTextEditor();
        InlineTextEditor.IsVisible = true;
        HintText.IsVisible = false;
        TextEditHintText.IsVisible = true;
        InlineTextEditor.Focus();
        InlineTextEditor.SelectAll();
    }

    private void PositionTextEditor()
    {
        if (_editing is not { } editing || ViewModel is not { } vm || FindBubbleIndex(editing) is var index && index < 0)
            return;

        var bubble = vm.Working.Panels[editing.Panel].Bubbles[index];
        var rect = PageCanvas.PageToControl(BubbleTextRenderer.TextArea(bubble));
        var fontSize = Math.Clamp(PageCanvasDrawOperation.FontSizeMm * PageCanvas.Zoom * 0.95, 11, 40);
        // Height follows the text, so no line is ever clipped: when there's more text than
        // fits (or the bubble is smaller than a readable line at this zoom) it grows past the
        // bubble - but it's transparent, so only the typed text spills over, never a box.
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

        if (commit && ViewModel is { } vm && FindBubbleIndex(editing) is var index and >= 0 &&
            vm.Working.Panels[editing.Panel].Bubbles[index].Text != text)
            vm.SetBubbleText(editing.Panel, index, text);

        PageCanvas.Focus();
    }

    private int FindBubbleIndex((PanelId Panel, BubbleId Bubble) editing)
    {
        if (ViewModel is not { } vm || !vm.Working.Panels.TryGetValue(editing.Panel, out var panel))
            return -1;
        for (var i = 0; i < panel.Bubbles.Count; i++)
        {
            if (panel.Bubbles[i].Id.Equals(editing.Bubble))
                return i;
        }
        return -1;
    }
}
