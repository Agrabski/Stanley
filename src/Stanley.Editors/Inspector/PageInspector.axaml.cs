using System.ComponentModel;
using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>
/// The canvas layout's inspector for a page; see PageInspector.axaml. Owns the order of the
/// sections and the title over them (what's selected); each section shows itself when it
/// applies. Everything goes through <see cref="PageEditorViewModel"/>, as the ribbon does.
/// </summary>
public partial class PageInspector : UserControl
{
    private PageEditorViewModel? _subscribed;

    public PageInspector()
    {
        InitializeComponent();
    }

    private PageEditorViewModel? ViewModel => DataContext as PageEditorViewModel;

    /// <summary>The heading over the sections - "Bubble", "Panel", "3 selected", "Page" - for headless UI tests.</summary>
    public string Title => TitleText.Text ?? "";

    /// <summary>Finds a named control in any section (each section is its own name scope) - for headless UI tests.</summary>
    public T? FindInSections<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? Sections.Children.OfType<UserControl>().Select(section => section.FindControl<T>(name)).FirstOrDefault(found => found is not null);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed != null)
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribed = ViewModel;
        if (_subscribed != null)
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        UpdateTitle();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && (name.EndsWith("Context", StringComparison.Ordinal) || name is nameof(PageEditorViewModel.SelectionCount)
                or nameof(PageEditorViewModel.HasMultiSelection) or nameof(PageEditorViewModel.SelectedCharacterName)
                or nameof(PageEditorViewModel.IsSelectedPanelCloud) or nameof(PageEditorViewModel.Tool)))
            UpdateTitle();
    }

    private void UpdateTitle() => TitleText.Text = ViewModel is { } vm ? TitleFor(vm) : "";

    /// <summary>What the inspector is showing the properties of.</summary>
    public static string TitleFor(PageEditorViewModel vm) =>
        vm.HasMultiSelection ? $"{vm.SelectionCount} selected"
        : vm.IsCharacterContext ? vm.SelectedCharacterName
        : vm.IsBubbleContext ? "Bubble"
        : vm.IsTextContext ? "Text"
        : vm.IsPictureContext ? "Picture"
        : vm.IsSpeedLinesContext ? "Speed lines"
        : vm.IsShapeContext ? "Shape"
        : vm.IsPanelContext ? (vm.IsSelectedPanelCloud ? "Thought cloud" : "Panel")
        : vm.IsShapeTool ? "New shape"
        : "Page";
}
