using System.ComponentModel;
using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>What any selection can do: cut, copy, paste, duplicate, group, ungroup, delete. See ArrangeSection.axaml.</summary>
public partial class ArrangeSection : UserControl
{
    private PageEditorViewModel? _subscribed;

    public ArrangeSection()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed != null)
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribed = DataContext as PageEditorViewModel;
        if (_subscribed != null)
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        UpdateDeleteTip();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && (name.EndsWith("Context", StringComparison.Ordinal) || name == nameof(PageEditorViewModel.HasMultiSelection)))
            UpdateDeleteTip();
    }

    private void UpdateDeleteTip() => ToolTip.SetTip(DeleteButton, _subscribed is { } vm ? DeleteTip(vm) : null);

    /// <summary>
    /// What Delete says it does - each ribbon tab's own words for its Delete, since the
    /// inspector has the one button for all of them. A character is only taken off the panel,
    /// which is worth saying: it stays in the Characters pane.
    /// </summary>
    public static string DeleteTip(PageEditorViewModel vm) =>
        vm.HasMultiSelection ? "Delete what's selected"
        : vm.IsCharacterContext ? "Remove from this panel (the character itself stays in the Characters pane)"
        : vm.IsBubbleContext ? "Delete this bubble"
        : vm.IsTextContext ? "Delete this text"
        : vm.IsPictureContext ? "Delete this picture"
        : vm.IsSpeedLinesContext ? "Delete these speed lines"
        : vm.IsShapeContext ? "Delete this shape"
        : vm.IsPanelContext ? "Delete this panel and its bubbles"
        : "Delete what's selected";
}
