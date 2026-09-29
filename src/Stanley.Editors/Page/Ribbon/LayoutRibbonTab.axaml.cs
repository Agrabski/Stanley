using Avalonia.Controls;
using Avalonia.Layout;

namespace Stanley.Editors;

/// <summary>The page ribbon's Layout tab; see LayoutRibbonTab.axaml: the layout gallery, margin, gutter and page numbering.</summary>
public partial class LayoutRibbonTab : UserControl
{
    private PageEditorViewModel? _subscribed;

    public LayoutRibbonTab()
    {
        InitializeComponent();

        // Every preset as a thumbnail, one click to apply - like Word's styles gallery, no dialog in between.
        foreach (var preset in Editing.PanelLayoutPresets.All)
        {
            var button = new Button
            {
                Classes = { "small" },
                Width = 40,
                Height = 58,
                Padding = new Avalonia.Thickness(3),
                Content = new LayoutPresetPreview { Preset = preset, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            ToolTip.SetTip(button, $"{preset.Name} - existing panels and their bubbles move into the new slots in reading order");
            button.Click += (_, _) => ViewModel?.ApplyLayoutCommand.Execute(preset);
            LayoutGallery.Children.Add(button);
        }

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
        PageNumberStartInput.ValueChanged += (_, e) =>
        {
            if (ViewModel is { } vm && e.NewValue is { } value)
                vm.PageNumberStart = (int)value;
        };
    }

    private PageEditorViewModel? ViewModel => DataContext as PageEditorViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed != null)
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribed = ViewModel;
        if (_subscribed != null)
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;

        if (ViewModel is { } vm)
        {
            MarginInput.Value = (decimal)vm.MarginMm;
            GutterInput.Value = (decimal)vm.GutterMm;
            PageNumberStartInput.Value = vm.PageNumberStart;
        }
    }

    /// <summary>Undo/redo can change the start number, margin or gutter underneath their boxes.</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PageEditorViewModel.PageNumberStart) && ViewModel is { } current && PageNumberStartInput.Value != current.PageNumberStart)
            PageNumberStartInput.Value = current.PageNumberStart;
        if (e.PropertyName == nameof(PageEditorViewModel.MarginMm) && ViewModel is { } margins && MarginInput.Value != (decimal)margins.MarginMm)
            MarginInput.Value = (decimal)margins.MarginMm;
        if (e.PropertyName == nameof(PageEditorViewModel.GutterMm) && ViewModel is { } gutters && GutterInput.Value != (decimal)gutters.GutterMm)
            GutterInput.Value = (decimal)gutters.GutterMm;
    }
}
