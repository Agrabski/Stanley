using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>
/// The page editor's ribbon tabs; see PageEditorRibbon.axaml. Each tab's groups live in their own
/// control under Ribbon/ (<see cref="HomeRibbonTab"/>, <see cref="InsertRibbonTab"/>...); this
/// class owns the tab strip - which contextual tab shows and where the selection falls back to.
/// Everything the tabs do goes through <see cref="PageEditorViewModel"/> commands, since the ribbon lives outside the pane's own view.
/// </summary>
public partial class PageEditorRibbon : UserControl
{
    public PageEditorRibbon()
    {
        InitializeComponent();
    }

    private PageEditorViewModel? ViewModel => DataContext as PageEditorViewModel;

    /// <summary>Exposed for headless UI tests.</summary>
    public TabControl TabControl => Tabs;

    /// <summary>
    /// Finds a named control anywhere in the ribbon. Each tab's controls are named inside that
    /// tab's own control, so a plain lookup from here wouldn't see them; this one looks in every tab as well.
    /// </summary>
    public T? FindControl<T>(string name) where T : Control =>
        NameScopeExtensions.Find<T>(this, name) ?? Tabs.Items.OfType<TabItem>()
            .Select(tab => tab.Content).OfType<Control>().Select(content => NameScopeExtensions.Find<T>(content, name)).FirstOrDefault(found => found is not null);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed != null)
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribed = ViewModel;
        if (_subscribed != null)
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
    }

    private PageEditorViewModel? _subscribed;

    /// <summary>Like Word: a contextual tab appears with its selection but isn't forced open; if the one you're on goes away (selection cleared), fall back to Home.</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
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
