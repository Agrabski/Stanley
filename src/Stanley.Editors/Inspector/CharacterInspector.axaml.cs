using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>
/// The canvas layout's inspector for a character; see CharacterInspector.axaml. Everything goes
/// through <see cref="CharacterEditorViewModel"/>, as the character ribbon does - including the
/// view model's requests for a window (file pickers, the SVG editor question), which this
/// answers through <see cref="CharacterEditorDialogs"/> in the ribbon's place.
/// </summary>
public partial class CharacterInspector : UserControl
{
    private readonly CharacterEditorDialogs _dialogs;
    private CharacterEditorViewModel? _subscribed;
    private TopLevel? _topLevel;
    private bool _pressedOnStage;
    private StickerId? _pickedSticker;

    public CharacterInspector()
    {
        InitializeComponent();
        _dialogs = new(this);
        // Focus moving to a box (a colour dropdown opening hands it on) mustn't scroll the
        // inspector away from what's being worked on; the print text box is scrolled to on purpose.
        Scroller.BringIntoViewOnFocusChange = false;
    }

    private CharacterEditorViewModel? ViewModel => DataContext as CharacterEditorViewModel;

    /// <summary>Finds a named control in any section (each section is its own name scope) - for headless UI tests.</summary>
    public T? FindInSections<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? Sections.Children.OfType<UserControl>().Select(section => section.FindControl<T>(name)).FirstOrDefault(found => found is not null);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // The column reuses this inspector from one character to the next: follow the one shown.
        if (_subscribed != null)
        {
            _subscribed.TextPrintWorn -= OnTextPrintWorn;
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
        }
        _subscribed = ViewModel;
        _pickedSticker = _subscribed?.SelectedStickerId;
        if (_subscribed != null)
        {
            _subscribed.TextPrintWorn += OnTextPrintWorn;
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
        }
        _dialogs.Attach(_subscribed);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _topLevel?.RemoveHandler(PointerPressedEvent, OnWindowPointerPressed);
        _topLevel = null;
    }

    /// <summary>Notes whether a press is on the character editor's stage - before the stage handles it, which is when a click there picks a sticker.</summary>
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e) =>
        _pressedOnStage = e.Source is Visual source && source.FindAncestorOfType<CharacterEditorView>(includeSelf: true) is not null;

    /// <summary>
    /// A sticker clicked on the stage brings the Sticker section into view: it's the last
    /// section, so it may be below the fold. One picked by a gallery leaves the inspector where
    /// it is, so the galleries stay under the pointer for the next pick.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CharacterEditorViewModel.SelectedStickerId) || sender is not CharacterEditorViewModel vm || vm.SelectedStickerId == _pickedSticker)
            return;
        _pickedSticker = vm.SelectedStickerId;
        if (_pickedSticker is not null && _pressedOnStage)
        {
            _pressedOnStage = false;
            Dispatcher.UIThread.Post(() =>
            {
                UpdateLayout();
                StickerProperties.BringIntoView();
            });
        }
    }

    /// <summary>
    /// A text print was just put on (the Prints gallery's Text): the Sticker section's text box
    /// comes into view, focused, to type over - as the ribbon goes to its Sticker tab. (The
    /// gallery has closed itself: putting anything on rebuilds the slot buttons.)
    /// </summary>
    private void OnTextPrintWorn(object? sender, EventArgs e) => StickerProperties.FocusPrintText();
}
