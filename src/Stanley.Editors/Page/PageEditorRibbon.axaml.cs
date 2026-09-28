using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

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

        // A title page design is picked with one click, like Word's cover pages - the gallery
        // closes behind it. Posted: a button runs its command after its Click event, and a
        // closed flyout's buttons have lost the DataContext their commands are bound through.
        void CloseTitlePageGallery() => Dispatcher.UIThread.Post(() => TitlePageButton.Flyout?.Hide());
        TitlePageGallery.AddHandler(Button.ClickEvent, (_, _) => CloseTitlePageGallery());
        RemoveTitlePageButton.Click += (_, _) => CloseTitlePageGallery();
        // Insert › My Assets closes behind a pick the same way.
        MyAssetsGroupGallery.AddHandler(Button.ClickEvent, (_, _) => Dispatcher.UIThread.Post(() => InsertFromMyAssetsButton.Flyout?.Hide()));

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
        // Undo/redo can change the start number, margin or gutter underneath their boxes.
        if (e.PropertyName == nameof(PageEditorViewModel.PageNumberStart) && ViewModel is { } current && PageNumberStartInput.Value != current.PageNumberStart)
            PageNumberStartInput.Value = current.PageNumberStart;
        if (e.PropertyName == nameof(PageEditorViewModel.MarginMm) && ViewModel is { } margins && MarginInput.Value != (decimal)margins.MarginMm)
            MarginInput.Value = (decimal)margins.MarginMm;
        if (e.PropertyName == nameof(PageEditorViewModel.GutterMm) && ViewModel is { } gutters && GutterInput.Value != (decimal)gutters.GutterMm)
            GutterInput.Value = (decimal)gutters.GutterMm;

        if (e.PropertyName is not (nameof(PageEditorViewModel.IsPanelContext) or nameof(PageEditorViewModel.IsBubbleContext)
                or nameof(PageEditorViewModel.IsCharacterContext) or nameof(PageEditorViewModel.IsShapeContext)
                or nameof(PageEditorViewModel.IsTextContext) or nameof(PageEditorViewModel.IsPictureContext)
                or nameof(PageEditorViewModel.IsSpeedLinesContext)) || ViewModel is not { } vm)
            return;
        // Read the view model rather than the tabs' IsVisible: those bindings may not have
        // caught up with this same change notification yet.
        if ((Tabs.SelectedItem == PanelTab && !vm.IsPanelContext) || (Tabs.SelectedItem == BubbleTab && !vm.IsBubbleContext)
            || (Tabs.SelectedItem == CharacterTab && !vm.IsCharacterContext) || (Tabs.SelectedItem == ShapeTab && !vm.IsShapeContext)
            || (Tabs.SelectedItem == TextTab && !vm.IsTextContext) || (Tabs.SelectedItem == PictureTab && !vm.IsPictureContext)
            || (Tabs.SelectedItem == SpeedLinesTab && !vm.IsSpeedLinesContext))
            Tabs.SelectedItem = HomeTab;
    }
}
