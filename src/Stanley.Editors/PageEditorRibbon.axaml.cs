using Avalonia.Controls;
using Avalonia.Layout;

namespace Stanley.Editors;

/// <summary>The page editor's ribbon tabs; see PageEditorRibbon.axaml. Everything it does goes through <see cref="PageEditorViewModel"/> commands, since it lives outside the pane's own view.</summary>
public partial class PageEditorRibbon : UserControl
{
    private PageEditorViewModel? _subscribed;

    public PageEditorRibbon()
    {
        InitializeComponent();

        // Layout tab gallery: every preset as a thumbnail, one click to apply - like
        // Word's styles gallery, no dialog in between.
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

    /// <summary>Exposed for headless UI tests.</summary>
    public TabControl TabControl => Tabs;

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

    /// <summary>Like Word: a contextual tab appears with its selection but isn't forced open; if the one you're on goes away (selection cleared), fall back to Home.</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Undo/redo can change the start number underneath the box.
        if (e.PropertyName == nameof(PageEditorViewModel.PageNumberStart) && ViewModel is { } current && PageNumberStartInput.Value != current.PageNumberStart)
            PageNumberStartInput.Value = current.PageNumberStart;

        if (e.PropertyName is not (nameof(PageEditorViewModel.IsPanelContext) or nameof(PageEditorViewModel.IsBubbleContext)
                or nameof(PageEditorViewModel.IsCharacterContext)) || ViewModel is not { } vm)
            return;
        // Read the view model rather than the tabs' IsVisible: those bindings may not have
        // caught up with this same change notification yet.
        if ((Tabs.SelectedItem == PanelTab && !vm.IsPanelContext) || (Tabs.SelectedItem == BubbleTab && !vm.IsBubbleContext)
            || (Tabs.SelectedItem == CharacterTab && !vm.IsCharacterContext))
            Tabs.SelectedItem = HomeTab;
    }
}
