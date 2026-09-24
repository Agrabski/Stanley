using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stanley.Editing;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>
/// The page editor pane: a ribbon of tools and actions, the zoomable page canvas, an
/// inline text editor that opens over a bubble, and a status bar that always says what
/// the pointer can do right now.
/// </summary>
public partial class PageEditorView : UserControl
{
    private PageEditorViewModel? _subscribed;
    private (PanelId Panel, BubbleId Bubble)? _editing;

    public PageEditorView()
    {
        InitializeComponent();

        foreach (var preset in PanelLayoutPresets.All)
        {
            var button = new Button
            {
                Classes = { "tool" },
                Width = 80,
                Height = 92,
                Content = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new LayoutPresetPreview { Preset = preset, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                        new TextBlock { Text = preset.Name, FontSize = 11, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                    }
                }
            };
            ToolTip.SetTip(button, $"{preset.Name}: {string.Join(" / ", preset.ColumnsPerRow)} panels per row");
            button.Click += (_, _) =>
            {
                ViewModel?.ApplyLayoutPreset(preset);
                LayoutButton.Flyout?.Hide();
                PageCanvas.Focus();
            };
            LayoutPresetPanel.Children.Add(button);
        }

        PageCanvas.ViewChanged += OnCanvasViewChanged;
        PageCanvas.EditTextRequested += BeginTextEdit;

        MarginInput.ValueChanged += (_, e) =>
        {
            if (ViewModel is { } vm && e.NewValue is { } value)
                vm.MarginMm = (double)value;
        };
        GutterInput.ValueChanged += (_, e) =>
        {
            if (ViewModel is { } vm && e.NewValue is { } value)
                vm.GutterMm = (double)value;
        };

        InlineTextEditor.AddHandler(KeyDownEvent, OnInlineEditorKeyDown, RoutingStrategies.Tunnel);
        InlineTextEditor.LostFocus += (_, _) => EndTextEdit(commit: true);
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
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribed = ViewModel;
        if (_subscribed != null)
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;

        PageCanvas.ViewModel = ViewModel;
        if (ViewModel is { } vm)
        {
            MarginInput.Value = (decimal)vm.MarginMm;
            GutterInput.Value = (decimal)vm.GutterMm;
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

    private void UpdateZoomText() => ZoomText.Text = $"{PageCanvas.ZoomPercent:0}%";

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => PageCanvas.ZoomIn();
    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => PageCanvas.ZoomOut();
    private void OnFitPageClick(object? sender, RoutedEventArgs e) => PageCanvas.FitPage();
    private void OnActualSizeClick(object? sender, RoutedEventArgs e) => PageCanvas.ActualSize();

    // ---------------------------------------------------------------- ribbon actions

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.DeleteSelection();
        PageCanvas.Focus();
    }

    private void OnSplitVerticalClick(object? sender, RoutedEventArgs e) => Split(BoundaryOrientation.Vertical);
    private void OnSplitHorizontalClick(object? sender, RoutedEventArgs e) => Split(BoundaryOrientation.Horizontal);

    private void Split(BoundaryOrientation orientation)
    {
        if (ViewModel is { SelectedPanelId: { } panelId } vm)
            vm.SplitPanel(panelId, orientation, 0.5);
        PageCanvas.Focus();
    }

    /// <summary>Adds to the selected panel - or, with nothing selected, the first panel - so the button never silently does nothing.</summary>
    private void OnAddBubbleClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        if (vm.SelectedPanelId is not { } panelId)
        {
            if (vm.Working.PanelOrder.Count == 0)
                return;
            panelId = vm.Working.PanelOrder[0];
        }

        var bounds = vm.PanelBounds(panelId);
        // Upper third of the panel: where dialogue usually goes, clear of the action.
        var index = vm.CreateBubble(panelId, new Point2D(bounds.MidX, bounds.Top + bounds.Height * 0.3));
        if (index >= 0)
            BeginTextEdit(panelId, index);
    }

    private void OnEditTextClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { SelectedPanelId: { } panelId, SelectedBubbleIndex: >= 0 and var index })
            BeginTextEdit(panelId, index);
    }

    private void OnAddTailClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { SelectedPanelId: { } panelId, SelectedBubbleIndex: >= 0 and var index } vm)
            vm.AddBubbleTail(panelId, index);
        PageCanvas.Focus();
    }

    private void OnRemoveTailClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { SelectedPanelId: { } panelId, SelectedBubbleIndex: >= 0 and var index, SelectedBubble: { Tails.Count: > 0 } bubble } vm)
            vm.RemoveBubbleTail(panelId, index, bubble.Tails.Count - 1);
        PageCanvas.Focus();
    }

    private void OnBringToFrontClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { SelectedPanelId: { } panelId, SelectedBubbleIndex: >= 0 and var index } vm)
            vm.BringBubbleToFront(panelId, index);
        PageCanvas.Focus();
    }

    private void OnSendToBackClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { SelectedPanelId: { } panelId, SelectedBubbleIndex: >= 0 and var index } vm)
            vm.SendBubbleToBack(panelId, index);
        PageCanvas.Focus();
    }

    // ---------------------------------------------------------------- inline text editing

    /// <summary>Opens a text box right over the bubble, sized to it and at the lettering's on-screen size, so editing happens where the text lives.</summary>
    public void BeginTextEdit(PanelId panelId, int bubbleIndex)
    {
        if (ViewModel is not { } vm || !vm.Working.Panels.TryGetValue(panelId, out var panel) || bubbleIndex < 0 || bubbleIndex >= panel.Bubbles.Count)
            return;

        EndTextEdit(commit: true);
        vm.Select(panelId, bubbleIndex);
        _editing = (panelId, panel.Bubbles[bubbleIndex].Id);
        InlineTextEditor.Text = panel.Bubbles[bubbleIndex].Text;
        PositionTextEditor();
        InlineTextEditor.IsVisible = true;
        InlineTextEditor.Focus();
        InlineTextEditor.SelectAll();
    }

    private void PositionTextEditor()
    {
        if (_editing is not { } editing || ViewModel is not { } vm || FindBubbleIndex(editing) is var index && index < 0)
            return;

        var bubble = vm.Working.Panels[editing.Panel].Bubbles[index];
        var rect = PageCanvas.PageToControl(AnchorRing.BoundingBox(bubble.Shape.Anchors));
        var width = Math.Max(rect.Width, 180);
        var height = Math.Max(rect.Height, 64);
        Avalonia.Controls.Canvas.SetLeft(InlineTextEditor, rect.Center.X - width / 2);
        Avalonia.Controls.Canvas.SetTop(InlineTextEditor, rect.Center.Y - height / 2);
        InlineTextEditor.Width = width;
        InlineTextEditor.Height = height;
        InlineTextEditor.FontSize = Math.Clamp(PageCanvasDrawOperation.FontSizeMm * PageCanvas.Zoom * 0.95, 11, 40);
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
