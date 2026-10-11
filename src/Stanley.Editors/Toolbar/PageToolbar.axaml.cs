using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Stanley.Editors;

/// <summary>
/// The canvas layout's tools for a page, in the top bar (docs/ui-shell.md §6); see PageToolbar.axaml.
/// The code here does two things the bindings can't: shows the shape tool in hand (or used last) as
/// the one shapes button, and closes Insert ▾ behind a pick.
/// </summary>
public partial class PageToolbar : UserControl
{
    private PageEditorViewModel? _subscribed;
    private PageEditorTool _shapeTool = PageEditorTool.Draw;

    public PageToolbar()
    {
        InitializeComponent();

        // Insert ▾ closes behind a pick, like the ribbon's title page and My Assets galleries.
        // Posted: a button runs its command after its Click event, and a closed flyout's buttons
        // have lost the DataContext their commands are bound through. Background stays open,
        // as on the ribbon, so a few can be tried in turn - all but Picture…, which asks for a file.
        void CloseInsert() => Dispatcher.UIThread.Post(() => InsertButton.Flyout?.Hide());
        TitlePageGallery.AddHandler(Button.ClickEvent, (_, _) => CloseInsert());
        RemoveTitlePageButton.Click += (_, _) => CloseInsert();
        CharacterGallery.AddHandler(Button.ClickEvent, (_, _) => CloseInsert());
        NewCharacterButton.Click += (_, _) => CloseInsert();
        MyAssetsGroupGallery.AddHandler(Button.ClickEvent, (_, _) => CloseInsert());
        InsertBackgroundPicker.AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is Control { Name: "BackgroundPicture" })
                CloseInsert();
        });

        // A page whose chip is hidden (no Characters pane, say) can't stay the one showing.
        ((PopupFlyoutBase)InsertButton.Flyout!).Opening += (_, _) => ShowAnInsertPage();
    }

    /// <summary>Insert ▾'s flyout, for headless UI tests.</summary>
    public FlyoutBase InsertFlyout => InsertButton.Flyout!;

    /// <summary>The shape tool the shapes button picks: the one in hand, or the one used last (Draw to begin with).</summary>
    public PageEditorTool ShapeTool => _shapeTool;

    // Listens only while showing: a new toolbar is made each time a page comes back after a
    // character, and the page's view model mustn't keep the old ones alive.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Follow(this.IsAttachedToVisualTree() ? DataContext as PageEditorViewModel : null);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Follow(DataContext as PageEditorViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Follow(null);
    }

    private void Follow(PageEditorViewModel? viewModel)
    {
        if (ReferenceEquals(viewModel, _subscribed))
            return;
        if (_subscribed != null)
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribed = viewModel;
        if (_subscribed != null)
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        FollowShapeTool();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PageEditorViewModel.Tool))
            FollowShapeTool();
    }

    /// <summary>Shows the button of the shape tool in hand - or, with another tool in hand, keeps the last one used.</summary>
    private void FollowShapeTool()
    {
        if (_subscribed is { IsShapeTool: true } vm)
            _shapeTool = vm.Tool;
        DrawToolButton.IsVisible = _shapeTool == PageEditorTool.Draw;
        FreeformToolButton.IsVisible = _shapeTool == PageEditorTool.Freeform;
        LineToolButton.IsVisible = _shapeTool == PageEditorTool.Line;
        RectangleToolButton.IsVisible = _shapeTool == PageEditorTool.Rectangle;
        EllipseToolButton.IsVisible = _shapeTool == PageEditorTool.Ellipse;
    }

    // Read off the view model, not the chips' IsVisible: a closed flyout's bindings may not have caught up yet.
    private void ShowAnInsertPage()
    {
        if (_subscribed is not { } vm)
            return;
        (RadioButton Page, bool Offered)[] pages =
        [
            (InsertCharactersPage, vm.HasCharacterCatalog), (InsertBackgroundPage, true),
            (InsertTitlePagePage, vm.HasTitlePageHost), (InsertMyAssetsPage, vm.HasMyAssets)
        ];
        if (!pages.Any(p => p.Offered && p.Page.IsChecked == true))
            pages.First(p => p.Offered).Page.IsChecked = true;
    }
}
