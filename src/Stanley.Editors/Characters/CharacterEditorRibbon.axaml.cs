using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The character editor's ribbon; see CharacterEditorRibbon.axaml. Each tab lives in its own control under Ribbon/ (<see cref="BodyRibbonTab"/> or <see cref="ShapesRibbonTab"/>, <see cref="LookRibbonTab"/>, <see cref="StickerRibbonTab"/>, <see cref="ModuleRibbonTab"/>); this owns the tab strip, and attaches the file pickers (<see cref="CharacterEditorDialogs"/>). Everything goes through <see cref="CharacterEditorViewModel"/>.</summary>
public partial class CharacterEditorRibbon : UserControl
{
    private readonly CharacterEditorDialogs _dialogs;

    public CharacterEditorRibbon()
    {
        InitializeComponent();
        _dialogs = new(this);
    }

    private CharacterEditorViewModel? ViewModel => DataContext as CharacterEditorViewModel;

    private CharacterEditorViewModel? _subscribed;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed != null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribed.TextPrintWorn -= OnTextPrintWorn;
        }
        _subscribed = ViewModel;
        if (_subscribed != null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
            _subscribed.TextPrintWorn += OnTextPrintWorn;
        }
        _dialogs.Attach(_subscribed);
        ShowBodyTab();
    }

    /// <summary>The Body tab for a generated body, the Shapes tab for one built from shapes: the other is hidden, so never left selected.</summary>
    private void ShowBodyTab()
    {
        if (ViewModel is not { } vm)
            return;
        if (vm.IsModular && (Tabs.SelectedItem == BodyTab || Tabs.SelectedItem is null))
            Tabs.SelectedItem = ShapesTab;
        else if (!vm.IsModular && Tabs.SelectedItem == ShapesTab)
            Tabs.SelectedItem = BodyTab;
        else if (!vm.HasSelectedModule && Tabs.SelectedItem == ModuleTab)
            Tabs.SelectedItem = vm.IsModular ? ShapesTab : BodyTab;
    }

    /// <summary>Like the page ribbon: the Sticker tab appears with a selection but isn't forced open; if it goes away while shown, back to Look.</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CharacterEditorViewModel.HasSelectedSticker) && ViewModel is { HasSelectedSticker: false } && Tabs.SelectedItem == StickerTab)
            Tabs.SelectedItem = LookTab;
        // The Module tab likewise: it appears with a picked part; once let go of, back to Shapes.
        if (e.PropertyName == nameof(CharacterEditorViewModel.HasSelectedModule) && ViewModel is { HasSelectedModule: false } && Tabs.SelectedItem == ModuleTab)
            Tabs.SelectedItem = ShapesTab;
        if (e.PropertyName == nameof(CharacterEditorViewModel.IsModular))
            ShowBodyTab();
    }

    /// <summary>A text print was just put on: straight to its text box on the Sticker tab, to type over.</summary>
    private void OnTextPrintWorn(object? sender, EventArgs e)
    {
        Tabs.SelectedItem = StickerTab;
        (StickerTab.Content as StickerRibbonTab)?.FocusPrintText();
    }

    /// <summary>
    /// Finds a named control anywhere in the ribbon. Each tab's controls are named inside that
    /// tab's own control, so a plain lookup from here wouldn't see them; this one looks in every tab as well.
    /// </summary>
    public T? FindControl<T>(string name) where T : Control =>
        NameScopeExtensions.Find<T>(this, name) ?? Tabs.Items.OfType<TabItem>()
            .Select(tab => tab.Content).OfType<Control>().Select(content => NameScopeExtensions.Find<T>(content, name)).FirstOrDefault(found => found is not null);

    /// <summary>Exposed for headless UI tests.</summary>
    public TabControl TabControl => Tabs;
}
