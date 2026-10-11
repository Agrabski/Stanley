using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Stanley.Editors;

/// <summary>
/// Nothing selected: the page itself - layout, spacing, guides, numbering, title page. See
/// PageSection.axaml. Like the ribbon's Layout tab, keeps the margin, gutter and start-number
/// boxes and the view model in step both ways, since undo can change them underneath.
/// </summary>
public partial class PageSection : UserControl
{
    private PageEditorViewModel? _subscribed;

    public PageSection()
    {
        InitializeComponent();

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

        // A design is picked with one click and the gallery closes behind it, as on the ribbon.
        // Posted: a button runs its command after its Click event, and a closed flyout's buttons
        // have lost the DataContext their commands are bound through.
        TitlePageGallery.AddHandler(Button.ClickEvent, (_, _) => Dispatcher.UIThread.Post(() => TitlePageButton.Flyout?.Hide()));
    }

    private PageEditorViewModel? ViewModel => DataContext as PageEditorViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Follow(this.IsAttachedToVisualTree() ? ViewModel : null);
    }

    // The inspector is rebuilt whenever another kind of editor becomes active, so let go of the
    // page's view model while detached rather than keep a discarded section alive through it.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Follow(ViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Follow(null);
    }

    private void Follow(PageEditorViewModel? vm)
    {
        if (!ReferenceEquals(_subscribed, vm))
        {
            if (_subscribed != null)
                _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribed = vm;
            if (_subscribed != null)
                _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        }
        if (vm != null)
        {
            MarginInput.Value = (decimal)vm.MarginMm;
            GutterInput.Value = (decimal)vm.GutterMm;
            PageNumberStartInput.Value = vm.PageNumberStart;
        }
    }

    /// <summary>Undo/redo can change the start number, margin or gutter underneath their boxes.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PageEditorViewModel vm)
            return;
        switch (e.PropertyName)
        {
            case nameof(PageEditorViewModel.MarginMm) when MarginInput.Value != (decimal)vm.MarginMm:
                MarginInput.Value = (decimal)vm.MarginMm;
                break;
            case nameof(PageEditorViewModel.GutterMm) when GutterInput.Value != (decimal)vm.GutterMm:
                GutterInput.Value = (decimal)vm.GutterMm;
                break;
            case nameof(PageEditorViewModel.PageNumberStart) when PageNumberStartInput.Value != vm.PageNumberStart:
                PageNumberStartInput.Value = vm.PageNumberStart;
                break;
        }
    }
}
