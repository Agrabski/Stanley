using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>What a press on the page canvas does. <see cref="Select"/> is the default and already covers moving/resizing everything; the others are one-purpose shortcuts for creating things.</summary>
public enum PageEditorTool
{
    Select,
    Panel,
    Bubble,
    Pan,

    /// <summary>Freehand pen: every drag draws a stroke; the tool stays on for the next one.</summary>
    Draw,
    Line,
    Rectangle,
    Ellipse,

    /// <summary>Click in a panel to type text there, or drag to size its box.</summary>
    Text
}

/// <summary>View changes the ribbon can ask for; the view that owns the zoom transform carries them out.</summary>
public enum ViewportRequest
{
    ZoomIn,
    ZoomOut,
    FitPage,
    ActualSize,

    /// <summary>Give the keyboard back to the page - after typing into a ribbon box, so shortcuts reach the page again.</summary>
    FocusPage
}

public sealed partial class PageEditorViewModel : EditorViewModel<PageDocument>, IDragGesture
{
    /// <summary>Default size for a bubble created with a single click, in mm - roughly two short lines of lettering.</summary>
    public const double DefaultBubbleWidthMm = 42;
    public const double DefaultBubbleHeightMm = 26;

    private PanelId? _selectedPanelId;
    private int _selectedBubbleIndex = -1;
    private int _selectedCharacterIndex = -1;
    private ICharacterCatalog? _catalog;
    private BubbleStylePreset _newBubbleStyle = BubbleStylePreset.Speech;
    private PanelId _pendingPanelId;
    private IPageNumberingHost? _numberingHost;

    public Rect2D PageBounds { get; }

    public PageEditorViewModel(EditorHistory history, Rect2D pageBounds, PageDocument initial)
        : base(history, "Page", initial)
    {
        PageBounds = pageBounds;
        PropertyChanged += OnSelfPropertyChanged;

        DeleteSelectionCommand = new RelayCommand(DeleteSelection, () => HasSelectedBubble || HasSelectedCharacter || HasSelectedElement || (HasSelectedPanel && !Working.LayoutLocked));
        SplitColumnsCommand = new RelayCommand(() => SplitSelected(BoundaryOrientation.Vertical), () => HasSelectedPanel && !Working.LayoutLocked);
        SplitRowsCommand = new RelayCommand(() => SplitSelected(BoundaryOrientation.Horizontal), () => HasSelectedPanel && !Working.LayoutLocked);
        AddBubbleCommand = new RelayCommand(AddBubbleToSelectedPanel, () => Working.PanelOrder.Count > 0);
        EditTextCommand = new RelayCommand(() =>
        {
            if (HasSelectedBubble)
                RequestTextEdit(_selectedPanelId!.Value, _selectedBubbleIndex);
            else
                RequestElementTextEdit(_selectedPanelId!.Value, _selectedElementIndex);
        }, () => HasSelectedBubble || HasSelectedText);
        AddTailCommand = new RelayCommand(() => AddBubbleTail(_selectedPanelId!.Value, _selectedBubbleIndex), () => HasSelectedBubble);
        RemoveTailCommand = new RelayCommand(
            () => RemoveBubbleTail(_selectedPanelId!.Value, _selectedBubbleIndex, SelectedBubble!.Tails.Count - 1),
            () => SelectedBubbleHasTails);
        BringToFrontCommand = new RelayCommand(() => ReorderSelection(toFront: true), () => HasSelectedBubble || HasSelectedCharacter || HasSelectedElement);
        SendToBackCommand = new RelayCommand(() => ReorderSelection(toFront: false), () => HasSelectedBubble || HasSelectedCharacter || HasSelectedElement);
        InsertCharacterCommand = new RelayCommand<CharacterDefinition>(character =>
        {
            if (character != null)
                InsertCharacter(character.Id);
        }, _ => Working.PanelOrder.Count > 0);
        NewCharacterCommand = new RelayCommand(() =>
        {
            if (_catalog != null)
                InsertCharacter(_catalog.CreateCharacter().Id);
        }, () => _catalog != null && Working.PanelOrder.Count > 0);
        FlipCharacterCommand = new RelayCommand(() => FlipCharacter(_selectedPanelId!.Value, _selectedCharacterIndex), () => HasSelectedCharacter);
        BiggerCharacterCommand = new RelayCommand(() => ScaleCharacter(_selectedPanelId!.Value, _selectedCharacterIndex, 1.1), () => HasSelectedCharacter);
        SmallerCharacterCommand = new RelayCommand(() => ScaleCharacter(_selectedPanelId!.Value, _selectedCharacterIndex, 1 / 1.1), () => HasSelectedCharacter);
        MatchCharacterSizeCommand = new RelayCommand(() => MatchCharacterSize(_selectedPanelId!.Value, _selectedCharacterIndex), () => SelectedCharacterHasOddScale);
        ApplyPosePresetCommand = new RelayCommand<PosePresetChoice>(choice =>
        {
            if (choice != null && HasSelectedCharacter)
                ApplyPosePreset(_selectedPanelId!.Value, _selectedCharacterIndex, choice.Preset);
        });
        SetPanelLookCommand = new RelayCommand<LookChoice>(choice =>
        {
            if (choice != null && HasSelectedCharacter)
                SetPanelLook(_selectedPanelId!.Value, _selectedCharacterIndex, choice.Look);
        });
        SetIssueLookCommand = new RelayCommand<LookChoice>(choice =>
        {
            if (choice != null && SelectedCharacter is { } instance)
                _looksHost?.SetIssueLook(instance.CharacterId, choice.Look);
        });
        ApplyExpressionCommand = new RelayCommand<ExpressionPresetChoice>(choice =>
        {
            if (choice != null && HasSelectedCharacter)
                ApplyExpression(_selectedPanelId!.Value, _selectedCharacterIndex, choice.Preset);
        });
        MirrorPoseCommand = new RelayCommand(() => MirrorCharacterPose(_selectedPanelId!.Value, _selectedCharacterIndex), () => SelectedCharacterIsPosed);
        ResetPoseCommand = new RelayCommand(() => ResetCharacterPose(_selectedPanelId!.Value, _selectedCharacterIndex), () => SelectedCharacterIsPosed);
        EditCharacterCommand = new RelayCommand(() => _catalog?.OpenCharacter(SelectedCharacter!.CharacterId), () => HasSelectedCharacter && _catalog != null);
        DrawPanelCommand = new RelayCommand(() => Tool = PageEditorTool.Panel);
        InsertBubbleCommand = new RelayCommand<BubbleStylePreset>(style =>
        {
            _newBubbleStyle = style;
            RaiseBubbleDerivedChanged();
            AddBubbleToSelectedPanel();
        }, _ => Working.PanelOrder.Count > 0);
        ApplyLayoutCommand = new RelayCommand<PanelLayoutPreset>(preset =>
        {
            if (preset != null)
                ApplyLayoutPreset(preset);
        }, _ => !Working.LayoutLocked);
        ZoomInCommand = new RelayCommand(() => ViewportRequested?.Invoke(ViewportRequest.ZoomIn));
        ZoomOutCommand = new RelayCommand(() => ViewportRequested?.Invoke(ViewportRequest.ZoomOut));
        FitPageCommand = new RelayCommand(() => ViewportRequested?.Invoke(ViewportRequest.FitPage));
        ActualSizeCommand = new RelayCommand(() => ViewportRequested?.Invoke(ViewportRequest.ActualSize));
        RemovePageNumbersCommand = new RelayCommand(() => _numberingHost?.SetPageNumbering(CurrentNumbering with { Position = PageNumberPosition.None }),
            () => PageNumbersEnabled);
        InitializeElementCommands();
        InitializeTitlePageCommands();
        InitializeFieldCommands();
        InitializeFaceCommands();
        InitializeClipboardCommands();
    }

    // A ribbon slider (the Speed Lines tab's "Lines" and "Thickness") is a drag gesture too,
    // like a character's body sliders: the view calls BeginSliderDrag on press and
    // EndSliderDrag on release, so one drag is one undo step - wired up here through
    // IDragGesture so `local:DragGesture.Target="{Binding}"` can bind straight to this view
    // model, the same as everywhere else that attaches a slider to a drag gesture.
    public void BeginSliderDrag() => BeginGesture();

    public void EndSliderDrag() => CommitGesture();

    void IDragGesture.BeginDrag() => BeginSliderDrag();

    void IDragGesture.EndDrag() => EndSliderDrag();

    // ---------------------------------------------------------------- ribbon commands
    //
    // The ribbon lives in the window, outside this pane's view, so everything it can do
    // is a command (or, for view-only things like zoom and the inline text editor, an
    // event the pane's view carries out).

    public IRelayCommand DeleteSelectionCommand { get; }
    public IRelayCommand SplitColumnsCommand { get; }
    public IRelayCommand SplitRowsCommand { get; }
    public IRelayCommand AddBubbleCommand { get; }
    public IRelayCommand EditTextCommand { get; }
    public IRelayCommand AddTailCommand { get; }
    public IRelayCommand RemoveTailCommand { get; }
    public IRelayCommand BringToFrontCommand { get; }
    public IRelayCommand SendToBackCommand { get; }
    public IRelayCommand<PanelLayoutPreset> ApplyLayoutCommand { get; }

    /// <summary>Insert tab: places the given character in the selected (or first) panel, at the panel's scale.</summary>
    public IRelayCommand<CharacterDefinition> InsertCharacterCommand { get; }

    /// <summary>Insert tab: a brand-new character (default body), placed in the selected panel - double-click it to shape its body.</summary>
    public IRelayCommand NewCharacterCommand { get; }
    public IRelayCommand FlipCharacterCommand { get; }
    public IRelayCommand BiggerCharacterCommand { get; }
    public IRelayCommand SmallerCharacterCommand { get; }
    public IRelayCommand MatchCharacterSizeCommand { get; }
    public IRelayCommand EditCharacterCommand { get; }

    /// <summary>Back to standing at rest.</summary>
    public IRelayCommand ResetPoseCommand { get; }

    /// <summary>Insert tab: switches to the panel tool, ready to drag out a new panel.</summary>
    public IRelayCommand DrawPanelCommand { get; }

    /// <summary>Insert tab: a bubble of the given style in the selected (or first) panel, ready to type into.</summary>
    public IRelayCommand<BubbleStylePreset> InsertBubbleCommand { get; }
    public IRelayCommand ZoomInCommand { get; }
    public IRelayCommand ZoomOutCommand { get; }
    public IRelayCommand FitPageCommand { get; }
    public IRelayCommand ActualSizeCommand { get; }

    public IReadOnlyList<PanelLayoutPreset> LayoutPresets => PanelLayoutPresets.All;

    /// <summary>Raised for zoom/fit requests; the pane's view owns the transform and applies them.</summary>
    public event Action<ViewportRequest>? ViewportRequested;

    /// <summary>
    /// Hands the keyboard back to the page (see <see cref="ViewportRequest.FocusPage"/>). If
    /// no view is showing the page just now - it's only just been switched back to - the
    /// view that shows it next takes the keyboard (<see cref="TakeFocusRequest"/>).
    /// </summary>
    public void FocusPage()
    {
        if (ViewportRequested is { } requested)
            requested(ViewportRequest.FocusPage);
        else
            _focusWhenShown = true;
    }

    private bool _focusWhenShown;

    /// <summary>Whether a <see cref="FocusPage"/> is waiting for a view to show this page; asking clears it.</summary>
    public bool TakeFocusRequest()
    {
        var wanted = _focusWhenShown;
        _focusWhenShown = false;
        return wanted;
    }

    /// <summary>Raised when something (the ribbon, a double-click, Enter) wants the inline text editor opened over a bubble.</summary>
    public event Action<PanelId, int>? TextEditRequested;

    public void RequestTextEdit(PanelId panelId, int bubbleIndex)
    {
        if (Working.Panels.TryGetValue(panelId, out var panel) && bubbleIndex >= 0 && bubbleIndex < panel.Bubbles.Count)
            TextEditRequested?.Invoke(panelId, bubbleIndex);
    }

    /// <summary>The zoom the view is currently showing, reported back by it, for the ribbon's readout.</summary>
    public double ZoomPercent
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                OnPropertyChanged(nameof(ZoomText));
        }
    } = 100;

    public string ZoomText => $"{ZoomPercent:0}%";

    private void SplitSelected(BoundaryOrientation orientation)
    {
        if (_selectedPanelId is { } id)
            SplitPanel(id, orientation, 0.5);
    }

    /// <summary>Adds to the selected panel - or, with nothing selected, the first panel - in its upper third (where dialogue usually goes), then opens the text editor.</summary>
    public void AddBubbleToSelectedPanel()
    {
        if (_selectedPanelId is not { } panelId || !Working.Panels.ContainsKey(panelId))
        {
            if (Working.PanelOrder.Count == 0)
                return;
            panelId = Working.PanelOrder[0];
        }

        var bounds = PanelBounds(panelId);
        var index = CreateBubble(panelId, new Point2D(bounds.MidX, bounds.Top + bounds.Height * 0.3));
        if (index >= 0)
            RequestTextEdit(panelId, index);
    }

    private void NotifyCommands()
    {
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        SaveFaceCommand?.NotifyCanExecuteChanged(); // null while the constructor is still setting up
        SplitColumnsCommand.NotifyCanExecuteChanged();
        SplitRowsCommand.NotifyCanExecuteChanged();
        ApplyLayoutCommand.NotifyCanExecuteChanged();
        AddBubbleCommand.NotifyCanExecuteChanged();
        InsertBubbleCommand.NotifyCanExecuteChanged();
        EditTextCommand.NotifyCanExecuteChanged();
        AddTailCommand.NotifyCanExecuteChanged();
        RemoveTailCommand.NotifyCanExecuteChanged();
        BringToFrontCommand.NotifyCanExecuteChanged();
        SendToBackCommand.NotifyCanExecuteChanged();
        InsertCharacterCommand.NotifyCanExecuteChanged();
        NewCharacterCommand.NotifyCanExecuteChanged();
        FlipCharacterCommand.NotifyCanExecuteChanged();
        BiggerCharacterCommand.NotifyCanExecuteChanged();
        SmallerCharacterCommand.NotifyCanExecuteChanged();
        MatchCharacterSizeCommand.NotifyCanExecuteChanged();
        EditCharacterCommand.NotifyCanExecuteChanged();
        InsertFieldCommand.NotifyCanExecuteChanged();
        NotifyElementCommands();
        if (CopyCommand != null) // null while the constructor is still setting up
            NotifyClipboardCommands();
    }

    // ---------------------------------------------------------------- tool & settings

    public PageEditorTool Tool
    {
        get;
        set
        {
            SetProperty(ref field, value);
            // A drawing or text tool shows (and changes) the style of what it makes next, not
            // of a selected element, so let go of the element.
            if ((IsShapeTool || value == PageEditorTool.Text) && HasSelectedElement)
                Select(_selectedPanelId);
            // Always re-raise every flag: a toggle button bound to one of them may have
            // flipped itself off locally, and needs to hear "no, you're still on".
            OnPropertyChanged(nameof(IsSelectTool));
            OnPropertyChanged(nameof(IsPanelTool));
            OnPropertyChanged(nameof(IsBubbleTool));
            OnPropertyChanged(nameof(IsPanTool));
            OnPropertyChanged(nameof(ShowBubbleStyle));
            RaiseToolFlagsChanged();
            _notice = null;
            OnPropertyChanged(nameof(Hint));
        }
    } = PageEditorTool.Select;

    public bool IsSelectTool { get => Tool == PageEditorTool.Select; set => SetToolFlag(PageEditorTool.Select, value); }
    public bool IsPanelTool { get => Tool == PageEditorTool.Panel; set => SetToolFlag(PageEditorTool.Panel, value); }
    public bool IsBubbleTool { get => Tool == PageEditorTool.Bubble; set => SetToolFlag(PageEditorTool.Bubble, value); }
    public bool IsPanTool { get => Tool == PageEditorTool.Pan; set => SetToolFlag(PageEditorTool.Pan, value); }

    private void SetToolFlag(PageEditorTool tool, bool value) => Tool = value ? tool : Tool;

    /// <summary>Margin and gutter used for snapping, splitting and layout presets - the comic's, set by its <see cref="SpacingHost"/>.</summary>
    public PanelGrid Grid
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
                return;
            OnPropertyChanged(nameof(MarginMm));
            OnPropertyChanged(nameof(GutterMm));
        }
    } = PanelGrid.Default;

    /// <summary>Where the comic's margin and gutter live (the navigator), so the Layout tab sets them for every page; null for a page edited on its own, which keeps its own.</summary>
    public IPageSpacingHost? SpacingHost { get; set; }

    /// <summary>Layout tab › Margin: the page edge to panel distance, for the whole comic. Panel edges on the old margin follow it.</summary>
    public double MarginMm
    {
        get => Grid.MarginMm;
        set => SetSpacing(Grid with { MarginMm = Math.Max(0, value) });
    }

    /// <summary>Layout tab › Gutter: the space between neighbouring panels, for the whole comic.</summary>
    public double GutterMm
    {
        get => Grid.GutterMm;
        set => SetSpacing(Grid with { GutterMm = Math.Max(0, value) });
    }

    private void SetSpacing(PanelGrid grid)
    {
        if (grid == Grid)
            return;
        if (SpacingHost is { } host)
        {
            host.SetSpacing(grid);
            return;
        }
        var oldMargin = Grid.MarginMm;
        Grid = grid;
        MoveMargin(oldMargin, grid.MarginMm);
    }

    /// <summary>The comic's margin moved from <paramref name="oldMarginMm"/>: panel edges that sat on it move onto the new one (<see cref="PanelLayoutEditing.MoveMargin"/>), one undo step - unless the layout is locked.</summary>
    public void MoveMargin(double oldMarginMm, double newMarginMm)
    {
        if (Committed.LayoutLocked)
            return;
        var panels = PanelLayoutEditing.MoveMargin(Committed.Panels, PageBounds, oldMarginMm, newMarginMm);
        if (!ReferenceEquals(panels, Committed.Panels))
            Apply(EditResult<PageDocument>.Success(Committed with { Panels = panels }));
    }

    // ---------------------------------------------------------------- page numbers

    /// <summary>The number printed on this page (set by whoever knows the page's position - the navigator), or null for none.</summary>
    public PageFolio? Folio
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>Where the comic's page-numbering setting lives; null for a page edited on its own, which then has no page-number controls.</summary>
    public IPageNumberingHost? NumberingHost
    {
        get => _numberingHost;
        set
        {
            if (_numberingHost != null)
                _numberingHost.PageNumberingChanged -= RaisePageNumberingChanged;
            _numberingHost = value;
            if (_numberingHost != null)
                _numberingHost.PageNumberingChanged += RaisePageNumberingChanged;
            RaisePageNumberingChanged();
        }
    }

    public bool HasPageNumbering => _numberingHost != null;

    // ---------------------------------------------------------------- looks

    private IIssueLooksHost? _looksHost;
    private static readonly IReadOnlyDictionary<CharacterId, CharacterRevisionId> NoLooks = new Dictionary<CharacterId, CharacterRevisionId>();

    /// <summary>Where the issue's look per character lives (the navigator); null for a page edited on its own - its characters wear their default looks.</summary>
    public IIssueLooksHost? LooksHost
    {
        get => _looksHost;
        set
        {
            if (_looksHost != null)
                _looksHost.IssueLooksChanged -= RaiseIssueLooksChanged;
            _looksHost = value;
            if (_looksHost != null)
                _looksHost.IssueLooksChanged += RaiseIssueLooksChanged;
            RaiseIssueLooksChanged();
        }
    }

    /// <summary>The issue's look per character, for drawing.</summary>
    public IReadOnlyDictionary<CharacterId, CharacterRevisionId> IssueLooks => _looksHost?.IssueLooks ?? NoLooks;

    /// <summary>The look <paramref name="instance"/> is drawn in: its panel's own, else the issue's.</summary>
    public CharacterRevisionId? LookOf(CharacterInstance instance) => CharacterLooks.LookOf(instance, IssueLooks);

    private void RaiseIssueLooksChanged()
    {
        OnPropertyChanged(nameof(IssueLooks));
        RaiseLookChoicesChanged();
    }

    public IReadOnlyList<PageNumberOption> PageNumberOptions => PageNumberOption.All;

    private PageNumbering CurrentNumbering => _numberingHost?.PageNumbering ?? PageNumbering.Off;

    /// <summary>Insert tab: page-number position for the whole comic (every page, not just this one).</summary>
    public PageNumberOption PageNumberOption
    {
        get => PageNumberOption.All.First(o => o.Position == CurrentNumbering.Position);
        set
        {
            if (value != null && value.Position != CurrentNumbering.Position)
                _numberingHost?.SetPageNumbering(CurrentNumbering with { Position = value.Position });
        }
    }

    public int PageNumberStart
    {
        get => CurrentNumbering.StartAt;
        set
        {
            if (value != CurrentNumbering.StartAt)
                _numberingHost?.SetPageNumbering(CurrentNumbering with { StartAt = value });
        }
    }

    public bool NumberFirstPage
    {
        get => CurrentNumbering.NumberFirstPage;
        set
        {
            if (value != CurrentNumbering.NumberFirstPage)
                _numberingHost?.SetPageNumbering(CurrentNumbering with { NumberFirstPage = value });
        }
    }

    public bool PageNumbersEnabled => CurrentNumbering.Position != PageNumberPosition.None;

    // The Layout tab's Page Numbers menu: one check mark per position, like Word's Page Number menu.
    public bool IsPageNumbersTopOuter { get => IsNumbering(PageNumberPosition.TopOuter); set => SetNumbering(PageNumberPosition.TopOuter, value); }
    public bool IsPageNumbersBottomCenter { get => IsNumbering(PageNumberPosition.BottomCenter); set => SetNumbering(PageNumberPosition.BottomCenter, value); }
    public bool IsPageNumbersBottomOuter { get => IsNumbering(PageNumberPosition.BottomOuter); set => SetNumbering(PageNumberPosition.BottomOuter, value); }

    /// <summary>What the Page Numbers button says under its name.</summary>
    public string PageNumbersSummary => CurrentNumbering.Position switch
    {
        PageNumberPosition.BottomCenter => "Bottom, centred",
        PageNumberPosition.BottomOuter => "Bottom, outer",
        PageNumberPosition.TopOuter => "Top, outer",
        _ => "None"
    };

    /// <summary>Page Numbers › Remove page numbers.</summary>
    public IRelayCommand RemovePageNumbersCommand { get; private set; } = null!;

    private bool IsNumbering(PageNumberPosition position) => CurrentNumbering.Position == position;

    private void SetNumbering(PageNumberPosition position, bool value)
    {
        if (value && CurrentNumbering.Position != position)
            _numberingHost?.SetPageNumbering(CurrentNumbering with { Position = position });
        else
            RaisePageNumberingChanged(); // a check mark that flipped itself off hears "no, you're still on"
    }

    private void RaisePageNumberingChanged()
    {
        OnPropertyChanged(nameof(HasPageNumbering));
        OnPropertyChanged(nameof(PageNumberOption));
        OnPropertyChanged(nameof(PageNumberStart));
        OnPropertyChanged(nameof(NumberFirstPage));
        OnPropertyChanged(nameof(PageNumbersEnabled));
        OnPropertyChanged(nameof(IsPageNumbersTopOuter));
        OnPropertyChanged(nameof(IsPageNumbersBottomCenter));
        OnPropertyChanged(nameof(IsPageNumbersBottomOuter));
        OnPropertyChanged(nameof(PageNumbersSummary));
        RemovePageNumbersCommand?.NotifyCanExecuteChanged();
    }

    /// <summary>Whether the canvas draws the dashed margin (live area) guide.</summary>
    public bool ShowMarginGuides
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    public bool SnapEnabled
    {
        get;
        set => SetProperty(ref field, value);
    } = true;

    /// <summary>
    /// A Word-style "protect this layout" switch: while true, panels on this page can't be
    /// moved, resized, split, deleted, drawn or re-tiled from a layout preset. Bubbles and
    /// characters are unaffected. Persisted per page and undoable, like the panel content
    /// itself - so it reads/writes through <see cref="EditorViewModel{T}.Working"/> rather
    /// than being a plain session-only flag.
    /// </summary>
    public bool IsLayoutLocked
    {
        get => Working.LayoutLocked;
        set
        {
            if (value == Working.LayoutLocked)
                return;
            Apply(EditResult<PageDocument>.Success(Working with { LayoutLocked = value }));
        }
    }

    /// <summary>The lines the current drag snapped to, for the canvas to draw. Empty outside a snapping drag.</summary>
    public IReadOnlyList<SnapGuide> ActiveGuides
    {
        get;
        private set => SetProperty(ref field, value);
    } =
        [];

    // ---------------------------------------------------------------- selection

    /// <summary>What kind of thing a <see cref="SelectedItem"/> points at.</summary>
    private enum SelectionKind { Bubble, Character, Element }

    /// <summary>One bubble, character or element in the selected panel, by its list index - what Shift+click adds to or removes from the selection.</summary>
    private readonly record struct SelectedItem(SelectionKind Kind, int Index);

    /// <summary>
    /// Everything Shift+click has added to the selection beyond the primary item (the fields
    /// above) - always in <see cref="_selectedPanelId"/>'s panel. Kept separate from the primary
    /// fields, rather than folded into them, so the single-selection code they already drive
    /// (<see cref="SelectedBubble"/> and friends) needs no changes.
    /// </summary>
    private readonly List<SelectedItem> _extraSelection = [];

    public PanelId? SelectedPanelId => _selectedPanelId;

    /// <summary>Index into the selected panel's <see cref="Panel.Bubbles"/>, or -1.</summary>
    public int SelectedBubbleIndex => _selectedBubbleIndex;

    /// <summary>Index into the selected panel's <see cref="Panel.CharacterInstances"/>, or -1. At most one of this, <see cref="SelectedBubbleIndex"/> and <see cref="SelectedElementIndex"/> is set.</summary>
    public int SelectedCharacterIndex => _selectedCharacterIndex;

    public bool HasSelectedPanel => _selectedPanelId is not null;
    public bool HasSelectedBubble => SelectedBubble is not null;
    public bool HasSelectedCharacter => SelectedCharacter is not null;
    public bool HasSelection => HasSelectedPanel;
    public bool SelectedBubbleHasTails => SelectedBubble is { Tails.Count: > 0 };

    /// <summary>The primary item - the bubble, character or element the single-selection properties point at - as a <see cref="SelectedItem"/>, or null with a panel (or nothing) selected.</summary>
    private SelectedItem? PrimaryItem =>
        _selectedBubbleIndex >= 0 ? new SelectedItem(SelectionKind.Bubble, _selectedBubbleIndex)
        : _selectedCharacterIndex >= 0 ? new SelectedItem(SelectionKind.Character, _selectedCharacterIndex)
        : _selectedElementIndex >= 0 ? new SelectedItem(SelectionKind.Element, _selectedElementIndex)
        : null;

    /// <summary>Every selected item, primary first then the extras in the order they were added - for group actions (move, nudge, delete).</summary>
    private IEnumerable<SelectedItem> AllSelected()
    {
        if (PrimaryItem is { } primary)
            yield return primary;
        foreach (var extra in _extraSelection)
            yield return extra;
    }

    /// <summary>How many bubbles, characters and elements are selected together: 0 with nothing selected, 1 for an ordinary single selection.</summary>
    public int SelectionCount => PrimaryItem is null ? 0 : 1 + _extraSelection.Count;

    /// <summary>More than one bubble, character or element is selected - Shift+click added to what a plain click picked.</summary>
    public bool HasMultiSelection => SelectionCount > 1;

    /// <summary>Whether this bubble, character or element (by list index, one of the three) is part of the current selection - the primary item or one Shift+click added. (Named apart from "IsSelected", the docking tab flag this class inherits.)</summary>
    public bool IsPartOfSelection(PanelId panelId, int bubbleIndex = -1, int characterIndex = -1, int elementIndex = -1)
    {
        if (!Equals(_selectedPanelId, panelId))
            return false;
        if (bubbleIndex >= 0)
            characterIndex = elementIndex = -1;
        else if (characterIndex >= 0)
            elementIndex = -1;
        SelectedItem? item = bubbleIndex >= 0 ? new SelectedItem(SelectionKind.Bubble, bubbleIndex)
            : characterIndex >= 0 ? new SelectedItem(SelectionKind.Character, characterIndex)
            : elementIndex >= 0 ? new SelectedItem(SelectionKind.Element, elementIndex)
            : null;
        return item is { } value && (PrimaryItem == value || _extraSelection.Contains(value));
    }

    /// <summary>The page-space bounding box of every extra (everything Shift+click added beyond the primary item), for the canvas to outline. The primary keeps its own outline and handles, drawn as it always was.</summary>
    public IReadOnlyList<Rect2D> ExtraSelectionBounds
    {
        get
        {
            if (_extraSelection.Count == 0 || SelectedPanel is not { } panel)
                return [];
            var bounds = new List<Rect2D>(_extraSelection.Count);
            foreach (var item in _extraSelection)
            {
                switch (item.Kind)
                {
                    case SelectionKind.Bubble when item.Index < panel.Bubbles.Count:
                        bounds.Add(AnchorRing.BoundingBox(panel.Bubbles[item.Index].Shape.Anchors));
                        break;
                    case SelectionKind.Character when item.Index < panel.CharacterInstances.Count:
                        bounds.Add(CharacterBounds(panel.CharacterInstances[item.Index]));
                        break;
                    case SelectionKind.Element when item.Index < panel.Elements.Count:
                        bounds.Add(PanelElements.Bounds(panel.Elements[item.Index]));
                        break;
                }
            }
            return bounds;
        }
    }

    /// <summary>A comic panel (and nothing in it) is selected: the ribbon shows its "Panel" contextual groups.</summary>
    public bool IsPanelContext => HasSelectedPanel && !HasSelectedBubble && !HasSelectedCharacter && !HasSelectedElement;

    /// <summary>A placed character is selected: the ribbon shows its "Character" contextual groups.</summary>
    public bool IsCharacterContext => HasSelectedCharacter;

    /// <summary>A bubble is selected: the ribbon shows its "Bubble" contextual groups.</summary>
    public bool IsBubbleContext => HasSelectedBubble;

    /// <summary>The bubble style picker is useful for the selected bubble and for the bubble tool's next bubble.</summary>
    public bool ShowBubbleStyle => HasSelectedBubble || IsBubbleTool;

    public string BubbleContextTitle => HasSelectedBubble ? "BUBBLE" : "NEW BUBBLE";

    public Panel? SelectedPanel =>
        _selectedPanelId is { } id && Working.Panels.TryGetValue(id, out var panel) ? panel : null;

    public Bubble? SelectedBubble =>
        SelectedPanel is { } panel && _selectedBubbleIndex >= 0 && _selectedBubbleIndex < panel.Bubbles.Count
            ? panel.Bubbles[_selectedBubbleIndex]
            : null;

    public CharacterInstance? SelectedCharacter =>
        SelectedPanel is { } panel && _selectedCharacterIndex >= 0 && _selectedCharacterIndex < panel.CharacterInstances.Count
            ? panel.CharacterInstances[_selectedCharacterIndex]
            : null;

    /// <summary>The selected character's name, for the contextual tab.</summary>
    public string SelectedCharacterName =>
        SelectedCharacter is { } instance && CharacterSnapshot.TryGetValue(instance.CharacterId, out var character) ? character.Name : "Missing character";

    public event Action? SelectionChanged;

    /// <remarks>
    /// A locked layout still lets a panel itself be selected - only its geometry (move,
    /// resize, split, delete, re-tile) is protected, so a double-click character or Insert
    /// &gt; Character has somewhere to land. Bubbles, characters and elements inside panels
    /// stay selectable too. At most one thing in the panel is selected: a bubble wins over a
    /// character, a character over an element, either over the panel itself.
    /// </remarks>
    public void Select(PanelId? panelId, int bubbleIndex = -1, int characterIndex = -1, int elementIndex = -1)
    {
        if (panelId is null)
            bubbleIndex = characterIndex = elementIndex = -1;
        if (bubbleIndex >= 0)
            characterIndex = elementIndex = -1;
        if (characterIndex >= 0)
            elementIndex = -1;
        // A plain selection is always just this one item: Shift+click is the only way to build
        // a multi-selection, so anything else picking a single thing collapses it back to that.
        var hadExtras = _extraSelection.Count > 0;
        _extraSelection.Clear();
        if (Equals(_selectedPanelId, panelId) && _selectedBubbleIndex == bubbleIndex && _selectedCharacterIndex == characterIndex
            && _selectedElementIndex == elementIndex && !hadExtras)
            return;

        _selectedPanelId = panelId;
        _selectedBubbleIndex = bubbleIndex;
        _selectedCharacterIndex = characterIndex;
        _selectedElementIndex = elementIndex;
        _notice = null;
        RaiseSelectionChanged();
    }

    public void SelectCharacter(PanelId panelId, int characterIndex) => Select(panelId, -1, characterIndex);

    public void ClearSelection() => Select(null);

    /// <summary>
    /// Shift+click on a bubble, character or element: adds it to the selection, becoming the
    /// new primary item (the last one clicked keeps driving the ribbon and property edits);
    /// Shift+click on one already selected removes it instead, promoting an extra to primary if
    /// the primary itself was removed. A different panel starts a fresh (single-item) selection
    /// there, since a multi-selection stays within one panel.
    /// </summary>
    public void ToggleSelect(PanelId panelId, int bubbleIndex = -1, int characterIndex = -1, int elementIndex = -1)
    {
        if (bubbleIndex >= 0)
            characterIndex = elementIndex = -1;
        else if (characterIndex >= 0)
            elementIndex = -1;
        if (bubbleIndex < 0 && characterIndex < 0 && elementIndex < 0)
            return;
        var item = bubbleIndex >= 0 ? new SelectedItem(SelectionKind.Bubble, bubbleIndex)
            : characterIndex >= 0 ? new SelectedItem(SelectionKind.Character, characterIndex)
            : new SelectedItem(SelectionKind.Element, elementIndex);

        if (!Equals(_selectedPanelId, panelId))
        {
            Select(panelId, bubbleIndex, characterIndex, elementIndex);
            return;
        }

        if (PrimaryItem is { } primary && primary == item)
        {
            // Toggling off the primary: the most recently added extra (if any) takes over.
            if (_extraSelection.Count > 0)
            {
                var next = _extraSelection[^1];
                _extraSelection.RemoveAt(_extraSelection.Count - 1);
                SetPrimaryIndex(next);
            }
            else
            {
                Select(null);
            }
            return;
        }

        var existing = _extraSelection.IndexOf(item);
        if (existing >= 0)
        {
            _extraSelection.RemoveAt(existing);
            RaiseMultiSelectionChanged();
            return;
        }

        if (PrimaryItem is { } current)
            _extraSelection.Add(current);
        SetPrimaryIndex(item);
    }

    /// <summary>Sets the primary item's index within the current panel, keeping <see cref="_extraSelection"/> as it is - the multi-selection half of what <see cref="Select"/> does for a plain single selection.</summary>
    private void SetPrimaryIndex(SelectedItem item)
    {
        _selectedBubbleIndex = item.Kind == SelectionKind.Bubble ? item.Index : -1;
        _selectedCharacterIndex = item.Kind == SelectionKind.Character ? item.Index : -1;
        _selectedElementIndex = item.Kind == SelectionKind.Element ? item.Index : -1;
        _notice = null;
        RaiseSelectionChanged();
    }

    private void RaiseMultiSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(Hint));
        NotifyCommands();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedPanelId));
        OnPropertyChanged(nameof(SelectedBubbleIndex));
        OnPropertyChanged(nameof(SelectedCharacterIndex));
        OnPropertyChanged(nameof(HasSelectedPanel));
        OnPropertyChanged(nameof(HasSelectedBubble));
        OnPropertyChanged(nameof(HasSelectedCharacter));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(HasMultiSelection));
        OnPropertyChanged(nameof(IsPanelContext));
        OnPropertyChanged(nameof(IsBubbleContext));
        OnPropertyChanged(nameof(IsCharacterContext));
        OnPropertyChanged(nameof(SelectedCharacterName));
        OnPropertyChanged(nameof(SelectedCharacterHasOddScale));
        RaiseCharacterViewChanged();
        OnPropertyChanged(nameof(ShowBubbleStyle));
        OnPropertyChanged(nameof(BubbleContextTitle));
        RaiseBubbleDerivedChanged();
        RaiseElementSelectionChanged();
        OnPropertyChanged(nameof(Hint));
        NotifyCommands();
        SelectionChanged?.Invoke();
    }

    private void RaiseBubbleDerivedChanged()
    {
        OnPropertyChanged(nameof(SelectedBubbleHasTails));
        OnPropertyChanged(nameof(CurrentBubbleStyle));
        OnPropertyChanged(nameof(IsSpeechStyle));
        OnPropertyChanged(nameof(IsShoutStyle));
        OnPropertyChanged(nameof(IsWhisperStyle));
        RaiseFontChanged();
    }

    /// <summary>Undo/redo or a delete can remove what's selected out from under us; drop whatever no longer exists instead of pointing at a stale index.</summary>
    private void OnSelfPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Working))
            return;

        if (_selectedPanelId is { } id)
        {
            if (!Working.Panels.TryGetValue(id, out var panel))
            {
                Select(null);
            }
            else
            {
                // Drop any extra whose bubble, character or element no longer exists - the same
                // clean-up the primary already gets below, extended to the multi-selection.
                var extrasBefore = _extraSelection.Count;
                _extraSelection.RemoveAll(item => !IsValidIndex(panel, item));

                if (_selectedBubbleIndex >= panel.Bubbles.Count || _selectedCharacterIndex >= panel.CharacterInstances.Count
                         || _selectedElementIndex >= panel.Elements.Count)
                    Select(id);
                else if (_extraSelection.Count != extrasBefore)
                    RaiseMultiSelectionChanged();
            }
        }
        OnPropertyChanged(nameof(SelectedCharacterHasOddScale));
        OnPropertyChanged(nameof(IsLayoutLocked));
        OnPropertyChanged(nameof(Hint));
        RaiseCharacterViewChanged();
        RaiseBubbleDerivedChanged();
        RaiseElementDerivedChanged();
        NotifyCommands();
    }

    /// <summary>Whether <paramref name="item"/>'s index is still in range for its kind's list in <paramref name="panel"/> - what makes an extra stale.</summary>
    private static bool IsValidIndex(Panel panel, SelectedItem item) => item.Kind switch
    {
        SelectionKind.Bubble => item.Index >= 0 && item.Index < panel.Bubbles.Count,
        SelectionKind.Character => item.Index >= 0 && item.Index < panel.CharacterInstances.Count,
        SelectionKind.Element => item.Index >= 0 && item.Index < panel.Elements.Count,
        _ => false
    };

    /// <summary>
    /// Moves every selected bubble, character and element in <paramref name="panelId"/> by
    /// (<paramref name="dx"/>, <paramref name="dy"/>) from <paramref name="document"/>, each kept
    /// inside or reachable exactly as its own single move would - the shared move logic behind
    /// both a group drag (<see cref="UpdateMoveSelection"/>) and a group nudge
    /// (<see cref="NudgeSelection"/>). Bubbles carry their tails along, drag or nudge alike,
    /// since the point of moving a group is usually keeping a bubble with its speaker.
    /// </summary>
    private EditResult<PageDocument> MoveSelectedItems(PageDocument document, PanelId panelId, double dx, double dy) =>
        EditPanel(document, panelId, panel =>
        {
            var bounds = Bounds(panel);
            var bubbles = panel.Bubbles.ToList();
            var characters = panel.CharacterInstances.ToList();
            var elements = panel.Elements.ToList();
            foreach (var item in AllSelected())
            {
                switch (item.Kind)
                {
                    case SelectionKind.Bubble when item.Index >= 0 && item.Index < bubbles.Count:
                        bubbles[item.Index] = BubbleEditing.KeepInside(BubbleEditing.Move(bubbles[item.Index], dx, dy, withTails: true).Value, bounds);
                        break;
                    case SelectionKind.Character when item.Index >= 0 && item.Index < characters.Count:
                        var moved = CharacterPlacementEditing.Move(characters[item.Index], dx, dy);
                        characters[item.Index] = CharacterPlacementEditing.KeepReachable(moved, InstanceExtent(moved), bounds);
                        break;
                    case SelectionKind.Element when item.Index >= 0 && item.Index < elements.Count:
                        elements[item.Index] = ElementEditing.KeepReachable(ElementEditing.Move(elements[item.Index], dx, dy), bounds);
                        break;
                }
            }
            return EditResult<Panel>.Success(panel with { Bubbles = bubbles, CharacterInstances = characters, Elements = elements });
        });

    public void BeginMoveSelection(PanelId panelId) => BeginGesture();

    /// <summary>Drags the whole multi-selection together by (<paramref name="dx"/>, <paramref name="dy"/>) from where the drag began - one undo step (<see cref="EndGesture"/> commits it).</summary>
    public void UpdateMoveSelection(PanelId panelId, double dx, double dy) =>
        UpdateGesture(MoveSelectedItems(MoveBase, panelId, dx, dy));

    // ---------------------------------------------------------------- bubble style (selection + next new bubble)

    /// <summary>
    /// One style control for both "the selected bubble" and "the next bubble I create",
    /// the way a word processor's font box works: it shows the selection's style, and
    /// picking a style restyles the selection and becomes the default for new bubbles.
    /// </summary>
    public BubbleStylePreset CurrentBubbleStyle
    {
        get => SelectedBubble?.Style ?? _newBubbleStyle;
        set
        {
            _newBubbleStyle = value;
            if (SelectedBubble is { } bubble && bubble.Style != value && _selectedPanelId is { } panelId)
                SetBubbleStyle(panelId, _selectedBubbleIndex, value);
            RaiseBubbleDerivedChanged();
        }
    }

    public bool IsSpeechStyle { get => CurrentBubbleStyle == BubbleStylePreset.Speech; set => SetStyleFlag(BubbleStylePreset.Speech, value); }
    public bool IsShoutStyle { get => CurrentBubbleStyle == BubbleStylePreset.Shout; set => SetStyleFlag(BubbleStylePreset.Shout, value); }
    public bool IsWhisperStyle { get => CurrentBubbleStyle == BubbleStylePreset.Whisper; set => SetStyleFlag(BubbleStylePreset.Whisper, value); }

    private void SetStyleFlag(BubbleStylePreset style, bool value)
    {
        if (value)
            CurrentBubbleStyle = style;
        else
            RaiseBubbleDerivedChanged();
    }

    // ---------------------------------------------------------------- status

    /// <summary>A one-line "what can I do here" for the status bar, so nothing depends on having read a manual.</summary>
    public string Hint => _notice ?? Tool switch
    {
        PageEditorTool.Panel => "Drag on the page to draw a panel. Edges snap to the margins and a gutter away from other panels (hold Alt to place freely).",
        PageEditorTool.Bubble => "Click inside a panel to add a bubble there, or drag to size it. The bubble stays inside that panel.",
        PageEditorTool.Pan => "Drag to move around the page. Ctrl+scroll zooms.",
        PageEditorTool.Draw => "Drag inside a panel to draw; end where you started to close the shape (it fills). Pick colours on the Home tab · Esc when done.",
        PageEditorTool.Line => "Drag inside a panel to draw a straight line (Shift keeps it level, upright or at 45°).",
        PageEditorTool.Rectangle or PageEditorTool.Ellipse => "Drag inside a panel to draw the shape (Shift for a square or circle), or click for a standard size.",
        PageEditorTool.Text => "Click inside a panel to type there, or drag to size the text box first.",
        _ when HasMultiSelection => $"{SelectionCount} selected - drag to move them together, Delete removes them, Shift+click adds or removes.",
        _ when HasSelectedShape => "Drag to move the shape (Alt+drag drags off a copy) · drag a handle to resize · Home or Shape tab for colours · behind or in front of the characters on the Shape tab · Delete removes it.",
        _ when IsPictureContext => "Drag to move the picture · drag a handle to resize it (it keeps its shape) · Picture tab: behind or in front of the characters · Delete removes it.",
        _ when IsSpeedLinesContext => "Drag the clear circle to move where the lines radiate from · drag a handle to resize it · Speed Lines tab for colour, count and thickness · Delete removes it.",
        _ when HasSelectedText => "Drag to move the text (Alt+drag drags off a copy) · drag a handle to resize its box · double-click or Enter to edit · Text tab for size and style · Delete removes it.",
        _ when HasSelectedCharacter => "Pick a pose on the Character tab, or drag the dots: hands/feet to reach, hips to crouch (feet stay put), chest to lean, head to tilt · drag the body to move (Alt+drag for a copy).",
        _ when HasSelectedBubble => "Drag to move the bubble (hold Ctrl to take its tail along, Alt to drag off a copy) · drag the orange dot to aim a tail · double-click or Enter to edit text · Delete removes it.",
        _ when HasSelectedPanel && Working.LayoutLocked => "Layout is locked - this panel can't be moved or resized. Double-click inside it to add a bubble, or double-click a character in the Characters pane to put it here. Unlock on the Layout tab.",
        _ when HasSelectedPanel => "Drag to move the panel (Alt+drag drags off a copy) · drag an edge, corner or gutter to resize · split it or pick a layout from the ribbon · Home › Shape Fill colours it · Delete removes it.",
        _ when IsComicTitlePage => "The comic's title page - every issue opens with it, showing its own {issue}. To change it for this issue alone: Insert › Title page › Only this issue.",
        _ when Working.LayoutLocked => "Layout is locked - click a panel to select it (or a bubble or character to edit it). Double-click inside a panel to add a bubble. Unlock on the Layout tab.",
        _ => "Pick a page layout from the ribbon, or click a panel to select it. Double-click inside a panel to add a speech bubble; Insert › Character adds a character. Hold Ctrl to see every button's shortcut."
    };

    public Rect2D PanelBounds(PanelId id) => AnchorRing.BoundingBox(Working.Panels[id].Shape.Anchors);

    private Rect2D CommittedPanelBounds(PanelId id) => AnchorRing.BoundingBox(Committed.Panels[id].Shape.Anchors);

    private IEnumerable<Rect2D> CommittedBoundsExcept(IReadOnlyCollection<PanelId> excluded) =>
        Committed.PanelOrder
            .Where(id => !excluded.Contains(id) && Committed.Panels.ContainsKey(id))
            .Select(CommittedPanelBounds);

    /// <summary>
    /// Ends the current gesture and drops snap guides. Every <c>Update*</c> below is
    /// computed from <see cref="EditorViewModel{T}.Committed"/> (the gesture's baseline),
    /// never from <see cref="EditorViewModel{T}.Working"/>, so a drag is a pure function
    /// of where the pointer is now - shrinking a panel until its bubbles are squeezed and
    /// then growing it back restores them exactly.
    /// </summary>
    public void EndGesture(bool commit)
    {
        var duplicateCancelled = commit ? null : _duplicateCancelled;
        _moveBase = null;
        _duplicateCancelled = null;
        if (commit)
            CommitGesture();
        else
            CancelGesture();
        ActiveGuides = [];
        duplicateCancelled?.Invoke();
    }

    // ---------------------------------------------------------------- panel layout

    public void BeginResizePanel(PanelId id)
    {
        if (Working.LayoutLocked)
            return;
        BeginGesture();
    }

    public void UpdateResizePanel(PanelId id, Rect2D newBounds) =>
        UpdateGesture(EditPanel(Committed, id, p => PanelLayoutEditing.Resize(p, newBounds, PageBounds)));

    /// <summary>Resize with snapping: only <paramref name="movingEdges"/> snap, the rest stay exactly where they were.</summary>
    public void UpdateResizePanel(PanelId id, Rect2D rawBounds, RectEdges movingEdges, double snapTolerance)
    {
        var bounds = rawBounds;
        if (SnapEnabled && snapTolerance > 0)
        {
            var snap = PanelSnapping.SnapResize(rawBounds, movingEdges, PageBounds, CommittedBoundsExcept([id]), Grid, snapTolerance);
            bounds = snap.Bounds;
            ActiveGuides = snap.Guides;
        }
        UpdateResizePanel(id, bounds);
    }

    public void BeginMovePanel(PanelId id)
    {
        if (Working.LayoutLocked)
            return;
        BeginGesture();
    }

    public void UpdateMovePanel(PanelId id, double dx, double dy, double snapTolerance)
    {
        var from = MoveBase;
        if (!from.Panels.TryGetValue(id, out var panel))
            return;

        if (SnapEnabled && snapTolerance > 0)
        {
            var start = Bounds(panel);
            var raw = start with { X = start.X + dx, Y = start.Y + dy };
            var snap = PanelSnapping.SnapMove(raw, PageBounds, CommittedBoundsExcept([id]), Grid, snapTolerance);
            dx = snap.Bounds.X - start.X;
            dy = snap.Bounds.Y - start.Y;
            ActiveGuides = snap.Guides;
        }
        UpdateGesture(EditPanel(from, id, p => PanelLayoutEditing.Move(p, dx, dy, PageBounds)));
    }

    public void BeginDragBoundary(PanelBoundaryDrag boundary)
    {
        if (Working.LayoutLocked)
            return;
        BeginGesture();
    }

    public void UpdateDragBoundary(PanelBoundaryDrag boundary, double newPosition)
    {
        var result = PanelLayoutEditing.DragBoundary(Committed.Panels, boundary, newPosition, PageBounds);
        UpdateGesture(result.IsValid
            ? EditResult<PageDocument>.Success(Committed with { Panels = result.Value })
            : EditResult<PageDocument>.Failure(result.Error!));
    }

    /// <summary>Gutter drag with snapping: lines up with other panels' edges, and with the spot that splits the two sides evenly.</summary>
    public void UpdateDragBoundary(PanelBoundaryDrag boundary, double newPosition, double snapTolerance)
    {
        if (SnapEnabled && snapTolerance > 0)
        {
            var vertical = boundary.Orientation == BoundaryOrientation.Vertical;
            var involved = boundary.PanelsBefore.Concat(boundary.PanelsAfter).ToList();
            var targets = new List<double>();
            foreach (var other in CommittedBoundsExcept(involved))
                targets.Add(vertical ? other.Right : other.Bottom);

            // The even split: before-side start and after-side end, gap shared out.
            var start = boundary.PanelsBefore.Where(Committed.Panels.ContainsKey).Select(CommittedPanelBounds).Select(b => vertical ? b.Left : b.Top).DefaultIfEmpty(0).Max();
            var end = boundary.PanelsAfter.Where(Committed.Panels.ContainsKey).Select(CommittedPanelBounds).Select(b => vertical ? b.Right : b.Bottom).DefaultIfEmpty(0).Min();
            targets.Add((start + end - boundary.Gap) / 2);

            var guides = new List<SnapGuide>();
            newPosition = PanelSnapping.SnapValue(newPosition, targets, snapTolerance, boundary.Orientation, guides);
            ActiveGuides = guides;
        }
        UpdateDragBoundary(boundary, newPosition);
    }

    /// <summary>Starts drawing a brand-new panel; <see cref="UpdateCreatePanel"/> sizes it, commit adds it and selects it.</summary>
    public void BeginCreatePanel()
    {
        if (Working.LayoutLocked)
            return;
        _pendingPanelId = PanelId.New();
        BeginGesture();
    }

    public void UpdateCreatePanel(Rect2D rawBounds, double snapTolerance)
    {
        var bounds = rawBounds;
        if (SnapEnabled && snapTolerance > 0)
        {
            var snap = PanelSnapping.SnapResize(rawBounds, RectEdges.All, PageBounds, CommittedBoundsExcept([]), Grid, snapTolerance);
            bounds = snap.Bounds;
            ActiveGuides = snap.Guides;
        }

        var panel = new Panel(_pendingPanelId, PanelShapes.Rectangle(bounds), Background: null, CharacterInstances: [], Bubbles: []);
        var validated = PanelLayoutEditing.Resize(panel, bounds, PageBounds);
        if (!validated.IsValid)
        {
            UpdateGesture(EditResult<PageDocument>.Failure(validated.Error!));
            return;
        }

        var panels = new Dictionary<PanelId, Panel>(Committed.Panels) { [_pendingPanelId] = validated.Value };
        UpdateGesture(EditResult<PageDocument>.Success(new PageDocument(ReadingOrder(panels), panels, Committed.LayoutLocked)));
    }

    /// <summary>Commits a panel drawn with <see cref="BeginCreatePanel"/> and selects it; returns false if it ended up too small to keep.</summary>
    public bool CommitCreatePanel()
    {
        EndGesture(commit: true);
        if (Working.LayoutLocked || !Working.Panels.ContainsKey(_pendingPanelId))
            return false;
        Select(_pendingPanelId);
        return true;
    }

    public void SplitPanel(PanelId id, BoundaryOrientation orientation, double fraction)
    {
        if (Working.LayoutLocked)
        {
            Apply(EditResult<PageDocument>.Failure("Layout is locked."));
            return;
        }

        if (!Working.Panels.TryGetValue(id, out var panel))
        {
            Apply(EditResult<PageDocument>.Failure($"Unknown panel '{id}'."));
            return;
        }

        var splitResult = PanelLayoutEditing.Split(panel, orientation, fraction, Grid.GutterMm);
        if (!splitResult.IsValid)
        {
            Apply(EditResult<PageDocument>.Failure(splitResult.Error!));
            return;
        }

        var (first, second) = splitResult.Value;
        var newOrder = Working.PanelOrder.ToList();
        var panelIndex = newOrder.IndexOf(id);
        newOrder[panelIndex] = first.Id;
        newOrder.Insert(panelIndex + 1, second.Id);

        var newPanels = new Dictionary<PanelId, Panel>();
        foreach (var kvp in Working.Panels)
        {
            if (!kvp.Key.Equals(id))
                newPanels[kvp.Key] = kvp.Value;
        }
        newPanels[first.Id] = first;
        newPanels[second.Id] = second;

        Apply(EditResult<PageDocument>.Success(new PageDocument(newOrder, newPanels, Working.LayoutLocked)));
        if (_selectedPanelId is { } selected && selected.Equals(id))
            Select(first.Id);
    }

    public void DeletePanel(PanelId id)
    {
        if (Working.LayoutLocked || !Working.Panels.ContainsKey(id))
            return;

        var panels = Working.Panels.Where(kvp => !kvp.Key.Equals(id)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        Apply(EditResult<PageDocument>.Success(new PageDocument(Working.PanelOrder.Where(p => !p.Equals(id)).ToList(), panels, Working.LayoutLocked)));
    }

    /// <summary>
    /// Re-tiles the page into <paramref name="preset"/>'s grid. Existing panels are reused
    /// in reading order - so their bubbles come along into the new slots - and any extra
    /// panels are removed (one undo step brings them back).
    /// </summary>
    public void ApplyLayoutPreset(PanelLayoutPreset preset)
    {
        if (Working.LayoutLocked)
        {
            Apply(EditResult<PageDocument>.Failure("Layout is locked."));
            return;
        }

        var layout = PanelLayoutEditing.GridLayout(PageBounds, Grid, preset.ColumnsPerRow);
        if (!layout.IsValid)
        {
            Apply(EditResult<PageDocument>.Failure(layout.Error!));
            return;
        }

        var existing = ReadingOrder(Working.Panels);
        var panels = new Dictionary<PanelId, Panel>();
        var order = new List<PanelId>();
        for (var i = 0; i < layout.Value.Count; i++)
        {
            var rect = layout.Value[i];
            Panel panel;
            if (i < existing.Count)
            {
                var resized = PanelLayoutEditing.Resize(Working.Panels[existing[i]], rect, PageBounds);
                if (!resized.IsValid)
                {
                    Apply(EditResult<PageDocument>.Failure(resized.Error!));
                    return;
                }
                panel = resized.Value;
            }
            else
            {
                panel = new Panel(PanelId.New(), PanelShapes.Rectangle(rect), Background: null, CharacterInstances: [], Bubbles: []);
            }
            panels[panel.Id] = panel;
            order.Add(panel.Id);
        }

        Apply(EditResult<PageDocument>.Success(new PageDocument(order, panels, Working.LayoutLocked)));
    }

    /// <summary>Western reading order: rows top to bottom (panels whose tops are within a few mm share a row), left to right within a row.</summary>
    private static List<PanelId> ReadingOrder(IReadOnlyDictionary<PanelId, Panel> panels)
    {
        var items = panels.Values.Select(p => (p.Id, Bounds: AnchorRing.BoundingBox(p.Shape.Anchors))).OrderBy(i => i.Bounds.Top).ToList();
        var result = new List<PanelId>();
        var row = new List<(PanelId Id, Rect2D Bounds)>();
        foreach (var item in items)
        {
            if (row.Count > 0 && item.Bounds.Top > row[0].Bounds.Top + 5)
            {
                result.AddRange(row.OrderBy(r => r.Bounds.Left).Select(r => r.Id));
                row.Clear();
            }
            row.Add(item);
        }
        result.AddRange(row.OrderBy(r => r.Bounds.Left).Select(r => r.Id));
        return result;
    }

    // ---------------------------------------------------------------- bubbles

    public void InsertBubble(PanelId panelId, Rect2D bounds, BubbleStylePreset style)
    {
        var bubbleResult = BubbleEditing.Create(bounds, style);
        if (!bubbleResult.IsValid)
        {
            Apply(EditResult<PageDocument>.Failure(bubbleResult.Error!));
            return;
        }

        var bubble = GrowBubbleToFit(bubbleResult.Value);
        Apply(EditPanel(Working, panelId, p =>
            EditResult<Panel>.Success(p with { Bubbles = [.. p.Bubbles, BubbleEditing.KeepInside(bubble, Bounds(p))] })));
    }

    /// <summary>
    /// The one-click path: a bubble of the current style, centred on <paramref name="center"/>
    /// (or sized to <paramref name="bounds"/> when dragged out), kept inside the panel, with
    /// a tail already aimed into the panel, and selected - ready to type into.
    /// Returns the new bubble's index, or -1 if nothing was created.
    /// </summary>
    public int CreateBubble(PanelId panelId, Point2D center, Rect2D? bounds = null)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel))
            return -1;

        var panelBounds = Bounds(panel);
        var rect = bounds ?? new Rect2D(
            center.X - DefaultBubbleWidthMm / 2,
            center.Y - DefaultBubbleHeightMm / 2,
            Math.Min(DefaultBubbleWidthMm, panelBounds.Width),
            Math.Min(DefaultBubbleHeightMm, panelBounds.Height));

        var created = BubbleEditing.Create(rect, _newBubbleStyle);
        if (!created.IsValid)
        {
            Apply(EditResult<PageDocument>.Failure(created.Error!));
            return -1;
        }

        var bubble = BubbleEditing.KeepInside(GrowBubbleToFit(_newBubbleLettering.ApplyTo(created.Value)), panelBounds);
        if (bounds is null)
        {
            // Not on top of one already there: adding two in a row would hide the first.
            var at = AnchorRing.BoundingBox(bubble.Shape.Anchors);
            var free = BubbleEditing.OutOfTheWay(at, panel.Bubbles.Select(b => AnchorRing.BoundingBox(b.Shape.Anchors)).ToList(), panelBounds);
            bubble = BubbleEditing.Move(bubble, free.Left - at.Left, free.Top - at.Top).Value;
        }
        bubble = WithDefaultTail(bubble, panelBounds);
        var index = panel.Bubbles.Count;
        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with { Bubbles = [.. p.Bubbles, bubble] })));
        if (Working.Panels[panelId].Bubbles.Count <= index)
            return -1;

        Select(panelId, index);
        return index;
    }

    public void BeginResizeBubble(PanelId panelId, int bubbleIndex) => BeginGesture();

    public void UpdateResizeBubble(PanelId panelId, int bubbleIndex, Rect2D newBounds)
    {
        UpdateGesture(EditBubbleInPanel(Committed, panelId, bubbleIndex, (b, panelBounds) =>
        {
            var clamped = Rect2D.FromEdges(
                Math.Max(newBounds.Left, panelBounds.Left),
                Math.Max(newBounds.Top, panelBounds.Top),
                Math.Min(newBounds.Right, panelBounds.Right),
                Math.Min(newBounds.Bottom, panelBounds.Bottom));
            return BubbleEditing.Resize(b, clamped);
        }));
    }

    public void BeginMoveBubble(PanelId panelId, int bubbleIndex) => BeginGesture();

    /// <summary>
    /// Moves by (<paramref name="dx"/>, <paramref name="dy"/>) from where the bubble was when
    /// the drag began; it stops at its panel's edge. Its tails keep pointing where they did -
    /// at the speaker - unless <paramref name="withTails"/> (Ctrl held), which moves the whole bubble, tails and all.
    /// </summary>
    public void UpdateMoveBubble(PanelId panelId, int bubbleIndex, double dx, double dy, bool withTails = false) =>
        UpdateGesture(EditBubbleInPanel(MoveBase, panelId, bubbleIndex, (b, _) => BubbleEditing.Move(b, dx, dy, withTails)));

    /// <summary>Nudges every selected item (or, with none, the selected panel) by a fixed amount, as one undo step - the arrow-key path.</summary>
    public void NudgeSelection(double dx, double dy)
    {
        if (_selectedPanelId is not { } panelId)
            return;

        if (HasMultiSelection)
            Apply(MoveSelectedItems(Working, panelId, dx, dy));
        else if (HasSelectedBubble)
            Apply(EditBubbleInPanel(Working, panelId, _selectedBubbleIndex, (b, _) => BubbleEditing.Move(b, dx, dy)));
        else if (HasSelectedElement)
            Apply(EditElementInPanel(Working, panelId, _selectedElementIndex, e => EditResult<PanelElement>.Success(ElementEditing.Move(e, dx, dy))));
        else if (HasSelectedCharacter)
            Apply(EditCharacterInPanel(Working, panelId, _selectedCharacterIndex, c => CharacterPlacementEditing.Move(c, dx, dy)));
        else if (!Working.LayoutLocked)
            Apply(EditPanel(Working, panelId, p => PanelLayoutEditing.Move(p, dx, dy, PageBounds)));
    }

    /// <summary>Restyles the bubble; it grows if the new outline leaves less room for its text than the old one did.</summary>
    public void SetBubbleStyle(PanelId panelId, int bubbleIndex, BubbleStylePreset style) =>
        Apply(EditBubbleInPanel(Working, panelId, bubbleIndex, (b, _) =>
            EditResult<Bubble>.Success(GrowBubbleToFit(BubbleEditing.SetStyle(b, style).Value))));

    public void AddBubbleTail(PanelId panelId, int bubbleIndex, Point2D target) =>
        Apply(EditBubbleInPanel(Working, panelId, bubbleIndex, (b, panelBounds) =>
        {
            var clamped = BubbleEditing.Clamp(target, panelBounds);
            var added = BubbleEditing.AddTail(b, clamped).Value;
            return BubbleEditing.SlideTailAttachment(added, added.Tails.Count - 1, clamped);
        }));

    /// <summary>Adds a tail aimed somewhere sensible without asking where (see <see cref="DefaultTailTarget"/>); the user then drags its tip onto the speaker.</summary>
    public void AddBubbleTail(PanelId panelId, int bubbleIndex)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || bubbleIndex < 0 || bubbleIndex >= panel.Bubbles.Count)
            return;

        var bubble = panel.Bubbles[bubbleIndex];
        AddBubbleTail(panelId, bubbleIndex, DefaultTailTarget(bubble, Bounds(panel)));
    }

    public void RemoveBubbleTail(PanelId panelId, int bubbleIndex, int tailIndex) =>
        Apply(EditBubbleInPanel(Working, panelId, bubbleIndex, (b, _) => BubbleEditing.RemoveTail(b, tailIndex)));

    public void BeginMoveBubbleTail(PanelId panelId, int bubbleIndex, int tailIndex) => BeginGesture();

    public void UpdateMoveBubbleTail(PanelId panelId, int bubbleIndex, int tailIndex, Point2D newTarget) =>
        UpdateGesture(EditBubbleInPanel(Committed, panelId, bubbleIndex, (b, panelBounds) =>
            BubbleEditing.MoveTailTarget(b, tailIndex, BubbleEditing.Clamp(newTarget, panelBounds))));

    public void BeginSlideBubbleTailAttachment(PanelId panelId, int bubbleIndex, int tailIndex) => BeginGesture();

    public void UpdateSlideBubbleTailAttachment(PanelId panelId, int bubbleIndex, int tailIndex, Point2D pointer) =>
        UpdateGesture(EditBubbleInPanel(Committed, panelId, bubbleIndex, (b, _) =>
            BubbleEditing.SlideTailAttachment(b, tailIndex, pointer)));

    /// <summary>Sets a bubble's words (one undo step), growing it to fit them.</summary>
    public void SetBubbleText(PanelId panelId, int bubbleIndex, string text) =>
        Apply(EditBubbleInPanel(Working, panelId, bubbleIndex, (b, _) =>
        {
            var set = BubbleEditing.SetText(b, text);
            return set.IsValid ? GrownBubbleEdit(set.Value) : set;
        }));

    public void DeleteBubble(PanelId panelId, int bubbleIndex)
    {
        Apply(EditPanel(Working, panelId, p =>
            bubbleIndex < 0 || bubbleIndex >= p.Bubbles.Count
                ? EditResult<Panel>.Failure("No such bubble.")
                : EditResult<Panel>.Success(p with { Bubbles = p.Bubbles.Where((_, i) => i != bubbleIndex).ToList() })));
        if (Equals(_selectedPanelId, panelId) && _selectedBubbleIndex == bubbleIndex)
            Select(panelId);
    }

    /// <summary>Bubbles draw in list order, so the front is the end of the list.</summary>
    public void BringBubbleToFront(PanelId panelId, int bubbleIndex) => ReorderBubble(panelId, bubbleIndex, toFront: true);

    public void SendBubbleToBack(PanelId panelId, int bubbleIndex) => ReorderBubble(panelId, bubbleIndex, toFront: false);

    private void ReorderBubble(PanelId panelId, int bubbleIndex, bool toFront)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || bubbleIndex < 0 || bubbleIndex >= panel.Bubbles.Count)
            return;

        var bubbles = panel.Bubbles.ToList();
        var bubble = bubbles[bubbleIndex];
        bubbles.RemoveAt(bubbleIndex);
        var newIndex = toFront ? bubbles.Count : 0;
        bubbles.Insert(newIndex, bubble);
        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with { Bubbles = bubbles })));
        if (Equals(_selectedPanelId, panelId) && _selectedBubbleIndex == bubbleIndex)
            Select(panelId, newIndex);
    }

    /// <summary>Deletes every selected bubble, character and element (one undo step) if there's more than one, else the selected bubble, character or element if there is one, otherwise the selected panel.</summary>
    public void DeleteSelection()
    {
        if (_selectedPanelId is not { } panelId)
            return;

        if (HasMultiSelection)
            DeleteSelectedItems(panelId);
        else if (HasSelectedBubble)
            DeleteBubble(panelId, _selectedBubbleIndex);
        else if (HasSelectedElement)
            DeleteElement(panelId, _selectedElementIndex);
        else if (HasSelectedCharacter)
            DeleteCharacter(panelId, _selectedCharacterIndex);
        else
            DeletePanel(panelId);
    }

    /// <summary>Removes every selected bubble, character and element from <paramref name="panelId"/> in one undo step, then selects just the panel. Filters each list by index rather than removing one at a time, so which order the indices come off the selection in doesn't matter.</summary>
    private void DeleteSelectedItems(PanelId panelId)
    {
        var bubbleIndices = new HashSet<int>();
        var characterIndices = new HashSet<int>();
        var elementIndices = new HashSet<int>();
        foreach (var item in AllSelected())
        {
            var set = item.Kind switch
            {
                SelectionKind.Bubble => bubbleIndices,
                SelectionKind.Character => characterIndices,
                _ => elementIndices
            };
            set.Add(item.Index);
        }

        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with
        {
            Bubbles = p.Bubbles.Where((_, i) => !bubbleIndices.Contains(i)).ToList(),
            CharacterInstances = p.CharacterInstances.Where((_, i) => !characterIndices.Contains(i)).ToList(),
            Elements = p.Elements.Where((_, i) => !elementIndices.Contains(i)).ToList()
        })));
        Select(panelId);
    }

    private static Bubble WithDefaultTail(Bubble bubble, Rect2D panelBounds)
    {
        var target = DefaultTailTarget(bubble, panelBounds);
        var added = BubbleEditing.AddTail(bubble, target).Value;
        return BubbleEditing.SlideTailAttachment(added, added.Tails.Count - 1, target).Value;
    }

    /// <summary>
    /// Aims a new tail where there's room for it: tries sixteen directions around the
    /// bubble and picks the one with the most space inside the panel, preferring
    /// downwards (speakers are usually below their dialogue) and steering clear of the
    /// bubble's existing tails so a second tail doesn't stack on the first.
    /// </summary>
    private static Point2D DefaultTailTarget(Bubble bubble, Rect2D panelBounds)
    {
        var b = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        var center = new Point2D(b.MidX, b.MidY);
        var reach = Math.Max(b.Height * 0.9, 12);
        const double inset = 3;
        var area = panelBounds.Width > inset * 4 && panelBounds.Height > inset * 4
            ? Rect2D.FromEdges(panelBounds.Left + inset, panelBounds.Top + inset, panelBounds.Right - inset, panelBounds.Bottom - inset)
            : panelBounds;
        var existing = bubble.Tails.Select(t => Math.Atan2(t.Target.Y - center.Y, t.Target.X - center.X)).ToList();

        var best = new Point2D(center.X, b.Bottom + reach);
        var bestScore = double.MinValue;
        for (var k = 0; k < 16; k++)
        {
            var angle = k * Math.PI / 8;
            var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
            // Distance from the centre to the (elliptical) outline in this direction.
            var rx = b.Width / 2;
            var ry = b.Height / 2;
            var radius = rx * ry / Math.Sqrt(Math.Pow(ry * cos, 2) + Math.Pow(rx * sin, 2));
            var ideal = new Point2D(center.X + cos * (radius + reach), center.Y + sin * (radius + reach));
            var clamped = BubbleEditing.Clamp(ideal, area);
            var length = Math.Sqrt(Math.Pow(clamped.X - center.X, 2) + Math.Pow(clamped.Y - center.Y, 2)) - radius;

            var score = Math.Min(length, reach) + sin * reach * 0.3;
            foreach (var other in existing)
            {
                var diff = Math.Abs(Math.IEEERemainder(angle - other, Math.PI * 2));
                if (diff < Math.PI / 3)
                    score -= reach * (1 - diff / (Math.PI / 3)) * 2;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = clamped;
            }
        }
        return best;
    }

    // ---------------------------------------------------------------- characters

    /// <summary>Where the comic's characters come from (the Characters pane); null for a page edited on its own, which then draws placeholders and can't insert characters.</summary>
    public ICharacterCatalog? Characters
    {
        get => _catalog;
        set
        {
            if (_catalog != null)
                _catalog.CharactersChanged -= RaiseCharactersChanged;
            _catalog = value;
            if (_catalog != null)
                _catalog.CharactersChanged += RaiseCharactersChanged;
            RaiseCharactersChanged();
        }
    }

    public bool HasCharacterCatalog => _catalog != null;

    /// <summary>The characters to draw with, as they look right now (a live slider drag in a character editor included).</summary>
    public IReadOnlyDictionary<CharacterId, CharacterDefinition> CharacterSnapshot =>
        _catalog?.Characters ?? EmptyCharacters;

    /// <summary>The Insert tab's character gallery.</summary>
    public IReadOnlyList<CharacterDefinition> CharacterChoices => _catalog?.InOrder ?? [];

    public bool HasCharacterChoices => CharacterChoices.Count > 0;

    private static readonly IReadOnlyDictionary<CharacterId, CharacterDefinition> EmptyCharacters = new Dictionary<CharacterId, CharacterDefinition>();

    private void RaiseCharactersChanged()
    {
        OnPropertyChanged(nameof(HasCharacterCatalog));
        OnPropertyChanged(nameof(CharacterSnapshot));
        OnPropertyChanged(nameof(CharacterChoices));
        OnPropertyChanged(nameof(HasCharacterChoices));
        OnPropertyChanged(nameof(SelectedCharacterName));
        OnPropertyChanged(nameof(PoseChoices));
        RaiseFaceChoicesChanged();
        RaiseLookChoicesChanged();
        NotifyCommands();
    }

    /// <summary>A character's figure-space bounding box; a default body's for a character missing from the catalog (it draws as a placeholder that size).</summary>
    public Rect2D FigureExtent(CharacterId id, ViewAngle angle = ViewAngle.Front) =>
        CharacterSnapshot.TryGetValue(id, out var character)
            ? BodyRig.Extent(character.Body, angle, character.Skeleton)
            : BodyRig.Extent(BodyShape.Default, angle);

    /// <summary>A placed character's bounding box on the page.</summary>
    public Rect2D CharacterBounds(CharacterInstance instance) => instance.Placement.ToPage(InstanceExtent(instance));

    /// <summary>An instance's figure-space bounding box as it stands - view, pose and what it wears included.</summary>
    private Rect2D InstanceExtent(CharacterInstance instance) =>
        CharacterSnapshot.TryGetValue(instance.CharacterId, out var character)
            ? Rendering.CharacterRenderers.Default.Extent(character, instance.Pose.ViewAngle, instance.Pose, instance.Overrides, LookOf(instance))
            : BodyRig.Extent(BodyShape.Default, instance.Pose.ViewAngle);

    /// <summary>
    /// Places <paramref name="characterId"/> in <paramref name="panelId"/> (default: the
    /// selected panel, else the first) at the panel's scale, beside the characters already
    /// there - or, with <paramref name="ground"/>, standing there - and selects it.
    /// Returns its index, or -1.
    /// </summary>
    public int InsertCharacter(CharacterId characterId, PanelId? panelId = null, Point2D? ground = null)
    {
        var target = panelId ?? _selectedPanelId;
        if (target is not { } id || !Working.Panels.ContainsKey(id))
        {
            if (Working.PanelOrder.Count == 0)
                return -1;
            id = Working.PanelOrder[0];
        }

        var panel = Working.Panels[id];
        var bounds = Bounds(panel);
        var figure = FigureExtent(characterId);
        var others = panel.CharacterInstances.Select(c => (c, CharacterBounds(c))).ToList();
        var placement = CharacterPlacementEditing.DefaultPlacement(bounds, figure, others);
        if (ground is { } at)
            placement = placement with { Ground = at };
        var instance = CharacterPlacementEditing.KeepReachable(
            new CharacterInstance(characterId, placement, RevisionOverride: null, new ProjectModel.Poses.PoseData(ViewAngle.Front, [], new SortedDictionary<string, string>()), Overrides: null),
            figure, bounds);

        var index = panel.CharacterInstances.Count;
        Apply(EditPanel(Working, id, p => EditResult<Panel>.Success(p with { CharacterInstances = [.. p.CharacterInstances, instance] })));
        if (Working.Panels[id].CharacterInstances.Count <= index)
            return -1;
        SelectCharacter(id, index);
        return index;
    }

    public void BeginMoveCharacter(PanelId panelId, int index) => BeginGesture();

    /// <summary>Moves by (<paramref name="dx"/>, <paramref name="dy"/>) from where the character stood when the drag began. Its feet snap onto the floor line of the panel's other characters.</summary>
    public void UpdateMoveCharacter(PanelId panelId, int index, double dx, double dy, double snapTolerance)
    {
        var from = MoveBase;
        if (!from.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count)
            return;

        if (SnapEnabled && snapTolerance > 0)
        {
            var ground = panel.CharacterInstances[index].Placement.Ground;
            var floors = panel.CharacterInstances.Where((_, i) => i != index).Select(c => c.Placement.Ground.Y);
            var guides = new List<SnapGuide>();
            dy = PanelSnapping.SnapValue(ground.Y + dy, floors, snapTolerance, BoundaryOrientation.Horizontal, guides) - ground.Y;
            ActiveGuides = guides;
        }
        UpdateGesture(EditCharacterInPanel(from, panelId, index, c => CharacterPlacementEditing.Move(c, dx, dy)));
    }

    public void BeginResizeCharacter(PanelId panelId, int index) => BeginGesture();

    /// <summary>
    /// Sets the character's size (<see cref="CharacterPlacement.UnitHeightMm"/>), scaling about
    /// its feet. <paramref name="together"/> (the default drag) resizes every character sharing
    /// its scale, so the panel's relative heights hold; alone, it snaps to the panel's scale
    /// when close, so it's easy to line back up.
    /// </summary>
    public void UpdateResizeCharacter(PanelId panelId, int index, double unitHeightMm, bool together)
    {
        if (!Committed.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count)
            return;

        if (!together && SnapEnabled && CharacterPlacementEditing.PanelScale(panel.CharacterInstances, index) is { } shared
            && Math.Abs(unitHeightMm - shared) <= shared * 0.04)
            unitHeightMm = shared;
        UpdateGesture(EditCharacters(Committed, panelId, list => CharacterPlacementEditing.Resize(list, index, unitHeightMm, together)));
    }

    /// <summary>Bigger/Smaller on the ribbon: resizes the character and everyone sharing its scale.</summary>
    public void ScaleCharacter(PanelId panelId, int index, double factor)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count)
            return;
        var unit = panel.CharacterInstances[index].Placement.UnitHeightMm * factor;
        Apply(EditCharacters(Working, panelId, list => CharacterPlacementEditing.Resize(list, index, unit, together: true)));
    }

    /// <summary>Whether the selected character has been sized apart from the rest of its panel (so "Match size" has something to do).</summary>
    public bool SelectedCharacterHasOddScale =>
        SelectedPanel is { } panel && SelectedCharacter is { } instance
        && CharacterPlacementEditing.PanelScale(panel.CharacterInstances, _selectedCharacterIndex) is { } shared
        && !CharacterPlacementEditing.SameScale(shared, instance.Placement.UnitHeightMm);

    /// <summary>Back to the panel's shared scale, after it was resized on its own.</summary>
    public void MatchCharacterSize(PanelId panelId, int index)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || CharacterPlacementEditing.PanelScale(panel.CharacterInstances, index) is not { } shared)
            return;
        Apply(EditCharacters(Working, panelId, list => CharacterPlacementEditing.Resize(list, index, shared, together: false)));
    }

    public void FlipCharacter(PanelId panelId, int index) =>
        Apply(EditCharacterInPanel(Working, panelId, index, CharacterPlacementEditing.Flip));

    /// <summary>Front or side view (<see cref="ViewAngle.Profile"/>); the character keeps standing where it was. Flip then turns a side view to face the other way.</summary>
    public void SetCharacterView(PanelId panelId, int index, ViewAngle angle) =>
        Apply(EditCharacterInPanel(Working, panelId, index, c => CharacterPlacementEditing.Turn(c, angle)));

    // ---------------------------------------------------------------- posing (drag a hand or foot)

    private int _bendSign = 1;
    private (CharacterId, ViewAngle) _poseChoicesKey;
    private string _expressionChoicesKey = "";

    /// <summary>The hand and foot handles of a placed character, on the page - none for a character missing from the catalog.</summary>
    public IReadOnlyList<(Limb Limb, Point2D Point)> LimbHandles(CharacterInstance instance) =>
        CharacterSnapshot.TryGetValue(instance.CharacterId, out var character)
            ? Enum.GetValues<Limb>().Select(limb => (limb, CharacterPosing.EndPoint(character, instance, limb))).ToList()
            : [];

    /// <summary>Starts dragging a hand or foot; the elbow/knee keeps bending the way it bends now for the whole drag.</summary>
    public void BeginPoseLimb(PanelId panelId, int index, Limb limb)
    {
        BeginGesture();
        if (Committed.Panels.TryGetValue(panelId, out var panel) && index >= 0 && index < panel.CharacterInstances.Count
            && CharacterSnapshot.TryGetValue(panel.CharacterInstances[index].CharacterId, out var character))
            _bendSign = CharacterPosing.BendSign(character, panel.CharacterInstances[index], limb);
    }

    /// <summary>The elbow/knee handles of a placed character, on the page - a second drag handle per limb that swings the upper arm or thigh.</summary>
    public IReadOnlyList<(Limb Limb, Point2D Point)> BendHandles(CharacterInstance instance) =>
        CharacterSnapshot.TryGetValue(instance.CharacterId, out var character)
            ? Enum.GetValues<Limb>().Select(limb => (limb, CharacterPosing.BendPoint(character, instance, limb))).ToList()
            : [];

    public void BeginPoseBend(PanelId panelId, int index, Limb limb) => BeginGesture();

    /// <summary>Drags a limb's elbow/knee handle: the joint follows the pointer (the upper bone reaches towards it); the forearm or shin keeps its bend.</summary>
    public void UpdatePoseBend(PanelId panelId, int index, Limb limb, Point2D target)
    {
        if (!Committed.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count
            || !CharacterSnapshot.TryGetValue(panel.CharacterInstances[index].CharacterId, out var character))
            return;
        UpdateGesture(EditCharacterInPanel(Committed, panelId, index, c => CharacterPosing.Bend(character, c, limb, target)));
    }

    /// <summary>The hips, chest and head handles of a placed character, on the page.</summary>
    public IReadOnlyList<(TrunkPart Part, Point2D Point)> TrunkHandles(CharacterInstance instance) =>
        CharacterSnapshot.TryGetValue(instance.CharacterId, out var character)
            ? Enum.GetValues<TrunkPart>().Select(part => (part, CharacterPosing.TrunkPoint(character, instance, part))).ToList()
            : [];

    public void BeginPoseTrunk(PanelId panelId, int index, TrunkPart part) => BeginGesture();

    /// <summary>
    /// Drags a trunk handle, computed from the drag's starting pose: the hips by
    /// (<paramref name="pointer"/> - <paramref name="pressedAt"/>) with the feet staying
    /// planted; the chest and head towards <paramref name="pointer"/> (lean, tilt).
    /// </summary>
    public void UpdatePoseTrunk(PanelId panelId, int index, TrunkPart part, Point2D pointer, Point2D pressedAt)
    {
        if (!Committed.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count
            || !CharacterSnapshot.TryGetValue(panel.CharacterInstances[index].CharacterId, out var character))
            return;
        UpdateGesture(EditCharacterInPanel(Committed, panelId, index, c => part switch
        {
            TrunkPart.Hips => CharacterPosing.MoveHips(character, c, new Point2D(pointer.X - pressedAt.X, pointer.Y - pressedAt.Y)),
            TrunkPart.Chest => CharacterPosing.Lean(character, c, pointer),
            _ => CharacterPosing.TiltHead(character, c, pointer)
        }));
    }

    public void MirrorCharacterPose(PanelId panelId, int index) =>
        Apply(EditCharacterInPanel(Working, panelId, index, CharacterPosing.MirrorPose));

    /// <summary>Poses the character as <paramref name="preset"/> (turning it side on if the preset needs that), in one undo step.</summary>
    public void ApplyPosePreset(PanelId panelId, int index, PosePresetDefinition preset)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count
            || !CharacterSnapshot.TryGetValue(panel.CharacterInstances[index].CharacterId, out var character))
            return;
        Apply(EditCharacterInPanel(Working, panelId, index, c => PosePresets.Apply(character, c, preset)));
        RaiseCharacterViewChanged();
    }

    // ---------------------------------------------------------------- looks: per panel, per issue, and one panel's own changes

    /// <summary>Whether the selected character has named looks to pick from.</summary>
    public bool SelectedCharacterHasLooks => SelectedCharacterDefinition is { Revisions.Count: > 0 };

    private CharacterDefinition? SelectedCharacterDefinition =>
        SelectedCharacter is { } instance && CharacterSnapshot.TryGetValue(instance.CharacterId, out var character) ? character : null;

    /// <summary>The selected character's look, for the Look button: its name, and whether it's the panel's own or the issue's.</summary>
    public string SelectedLookName
    {
        get
        {
            if (SelectedCharacter is not { } instance || SelectedCharacterDefinition is not { } character)
                return "";
            var name = CharacterLooks.Revision(character, LookOf(instance))?.Name ?? "Default";
            return instance.RevisionOverride is null ? name : name + " (panel)";
        }
    }

    /// <summary>The Look dropdown's "This panel" choices: the issue's look, the default, each named look.</summary>
    public IReadOnlyList<LookChoice> PanelLookChoices
    {
        get
        {
            if (SelectedCharacter is not { } instance || SelectedCharacterDefinition is not { } character)
                return [];
            var issueLook = IssueLooks.TryGetValue(character.Id, out var l) ? l : (CharacterRevisionId?)null;
            var choices = new List<LookChoice>
            {
                new($"Issue: {CharacterLooks.Revision(character, issueLook)?.Name ?? "Default"}", null, instance.RevisionOverride is null,
                    LookEditing.Project(character, CharacterLooks.Revision(character, issueLook))),
                new("Default", CharacterLooks.DefaultLook, instance.RevisionOverride == CharacterLooks.DefaultLook, character),
            };
            choices.AddRange(character.Revisions.Values.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => new LookChoice(r.Name, r.Id, instance.RevisionOverride == r.Id, LookEditing.Project(character, r))));
            return choices;
        }
    }

    /// <summary>The Look dropdown's "Whole issue" choices: the default, each named look.</summary>
    public IReadOnlyList<LookChoice> IssueLookChoices
    {
        get
        {
            if (SelectedCharacterDefinition is not { } character)
                return [];
            var current = IssueLooks.TryGetValue(character.Id, out var l) ? l : (CharacterRevisionId?)null;
            return new[] { new LookChoice("Default", null, current is null || !character.Revisions.ContainsKey(current.Value), character) }
                .Concat(character.Revisions.Values.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(r => new LookChoice(r.Name, r.Id, current == r.Id, LookEditing.Project(character, r))))
                .ToList();
        }
    }

    public IRelayCommand<LookChoice> SetPanelLookCommand { get; private set; } = null!;
    public IRelayCommand<LookChoice> SetIssueLookCommand { get; private set; } = null!;

    /// <summary>Shows the character in <paramref name="look"/> in this panel (null = as the issue says), in one undo step.</summary>
    public void SetPanelLook(PanelId panelId, int index, CharacterRevisionId? look)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count
            || panel.CharacterInstances[index].RevisionOverride == look)
            return;
        Apply(EditCharacterInPanel(Working, panelId, index, c => c with { RevisionOverride = look }));
        RaiseLookChoicesChanged();
    }

    /// <summary>
    /// One panel's own change to what the character wears or its colours (sunglasses for
    /// one shot): <paramref name="edit"/> works on the character as the panel shows it, and
    /// the panel keeps only where the result differs from its look. One undo step.
    /// </summary>
    public void EditPanelLook(PanelId panelId, int index, Func<CharacterDefinition, CharacterDefinition> edit)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count
            || !CharacterSnapshot.TryGetValue(panel.CharacterInstances[index].CharacterId, out var character))
            return;
        var instance = panel.CharacterInstances[index];
        var revision = CharacterLooks.Revision(character, LookOf(instance));
        var edited = edit(LookEditing.Project(character, revision, instance.Overrides));
        var updated = LookEditing.StorePanel(character, revision, instance, edited);
        if (updated == instance || Equals(updated.Overrides, instance.Overrides))
            return;
        Apply(EditCharacterInPanel(Working, panelId, index, _ => updated));
        RaiseLookChoicesChanged();
    }

    /// <summary>The character as one panel shows it (its look, then the panel's own changes), flattened - what "this panel only" edits start from.</summary>
    public CharacterDefinition? PanelView(PanelId panelId, int index) =>
        Working.Panels.TryGetValue(panelId, out var panel) && index >= 0 && index < panel.CharacterInstances.Count
        && CharacterSnapshot.TryGetValue(panel.CharacterInstances[index].CharacterId, out var character)
            ? LookEditing.Project(character, CharacterLooks.Revision(character, LookOf(panel.CharacterInstances[index])), panel.CharacterInstances[index].Overrides)
            : null;

    /// <summary>Drops one panel's own changes, back to its look.</summary>
    public void ClearPanelLook(PanelId panelId, int index)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count
            || panel.CharacterInstances[index].Overrides is null)
            return;
        Apply(EditCharacterInPanel(Working, panelId, index, c => c with { Overrides = null }));
    }

    private object? _lookChoicesKey;

    private void RaiseLookChoicesChanged()
    {
        // Only when something the choices show changed - not on every pointer move of a drag.
        var instance = SelectedCharacter;
        var character = SelectedCharacterDefinition;
        var key = (instance?.CharacterId, instance?.RevisionOverride, instance?.Overrides, character,
            character is not null && IssueLooks.TryGetValue(character.Id, out var issueLook) ? issueLook : (CharacterRevisionId?)null);
        if (Equals(key, _lookChoicesKey))
            return;
        _lookChoicesKey = key;
        OnPropertyChanged(nameof(SelectedCharacterHasLooks));
        OnPropertyChanged(nameof(SelectedLookName));
        OnPropertyChanged(nameof(PanelLookChoices));
        OnPropertyChanged(nameof(IssueLookChoices));
        RaiseFaceChoicesChanged(); // another look can mean another face to mix from
    }

    /// <summary>Gives the character <paramref name="preset"/>'s face, in one undo step. The pose is untouched.</summary>
    public void ApplyExpression(PanelId panelId, int index, ExpressionPresetDefinition preset)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count)
            return;
        if (ExpressionPresets.Of(panel.CharacterInstances[index].Pose) == preset)
            return;
        Apply(EditCharacterInPanel(Working, panelId, index, c => ExpressionPresets.Apply(c, preset)));
        RaiseCharacterViewChanged();
    }

    /// <summary>The Character tab's expression gallery: every preset, as a close-up of the selected character (dressed as this panel shows it).</summary>
    public IReadOnlyList<ExpressionPresetChoice> ExpressionChoices
    {
        get
        {
            if (SelectedCharacter is not { } instance || SelectedPanelView is not { } character)
                return [];
            var current = ExpressionPresets.Of(instance.Pose);
            var standing = new ProjectModel.Poses.PoseData(instance.Pose.ViewAngle, [], new SortedDictionary<string, string>());
            return ExpressionPresets.All
                .Select(p => new ExpressionPresetChoice(p, character, ExpressionPresets.Apply(standing, p), p == current))
                .ToList();
        }
    }

    /// <summary>The selected character's expression, for the gallery button: a preset's name, a saved face's, or "Custom" for a mix.</summary>
    public string SelectedExpressionName => SelectedCharacter is { } instance
        ? ExpressionPresets.Of(instance.Pose)?.Name ?? (SelectedCharacterDefinition is { } character ? SavedFaces.Of(character, instance.Pose)?.Name : null) ?? "Custom"
        : "";

    public IRelayCommand<ExpressionPresetChoice> ApplyExpressionCommand { get; }

    /// <summary>The Character tab's pose gallery: every preset, previewed on the selected character.</summary>
    public IReadOnlyList<PosePresetChoice> PoseChoices
    {
        get
        {
            if (SelectedCharacter is not { } instance || !CharacterSnapshot.TryGetValue(instance.CharacterId, out var character))
                return [];
            var standing = new CharacterInstance(character.Id, new CharacterPlacement(default, 1, false), null,
                new ProjectModel.Poses.PoseData(instance.Pose.ViewAngle, [], new SortedDictionary<string, string>()), null);
            return PosePresets.All
                .Select(p => new PosePresetChoice(p, character, PosePresets.Apply(character, standing, p).Pose))
                .ToList();
        }
    }

    /// <summary>Reaches the hand or foot towards <paramref name="target"/> (page mm) - inverse kinematics, computed from the drag's starting pose.</summary>
    public void UpdatePoseLimb(PanelId panelId, int index, Limb limb, Point2D target)
    {
        if (!Committed.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count
            || !CharacterSnapshot.TryGetValue(panel.CharacterInstances[index].CharacterId, out var character))
            return;
        UpdateGesture(EditCharacterInPanel(Committed, panelId, index, c => CharacterPosing.Reach(character, c, limb, target, _bendSign)));
    }

    public void ResetCharacterPose(PanelId panelId, int index) =>
        Apply(EditCharacterInPanel(Working, panelId, index, CharacterPosing.ResetPose));

    public bool SelectedCharacterIsPosed => SelectedCharacter is { } instance && CharacterPosing.IsPosed(instance.Pose);

    public IRelayCommand<PosePresetChoice> ApplyPosePresetCommand { get; }
    public IRelayCommand MirrorPoseCommand { get; }

    /// <summary>The selected character's view, for the ribbon's Front/Side toggle.</summary>
    public ViewAngle? SelectedCharacterView => SelectedCharacter?.Pose.ViewAngle;

    public bool IsSelectedCharacterFront
    {
        get => SelectedCharacterView == ViewAngle.Front;
        set => SetSelectedView(ViewAngle.Front, value);
    }

    public bool IsSelectedCharacterSide
    {
        get => SelectedCharacterView == ViewAngle.Profile;
        set => SetSelectedView(ViewAngle.Profile, value);
    }

    private void SetSelectedView(ViewAngle angle, bool value)
    {
        if (value && SelectedCharacter is { } instance && instance.Pose.ViewAngle != angle)
            SetCharacterView(_selectedPanelId!.Value, _selectedCharacterIndex, angle);
        RaiseCharacterViewChanged(); // a toggle that flipped itself off locally hears "no, you're still on"
    }

    private void RaiseCharacterViewChanged()
    {
        OnPropertyChanged(nameof(SelectedCharacterIsPosed));
        ResetPoseCommand.NotifyCanExecuteChanged();
        MirrorPoseCommand.NotifyCanExecuteChanged();
        var key = SelectedCharacter is { } selected ? (selected.CharacterId, selected.Pose.ViewAngle) : default;
        if (!Equals(key, _poseChoicesKey))
        {
            _poseChoicesKey = key;
            OnPropertyChanged(nameof(PoseChoices));
        }
        var expressionKey = SelectedCharacter is { } face
            ? $"{face.CharacterId}:{face.Pose.ViewAngle}:{string.Join(";", (face.Pose.Expression ?? []).Select(e => e.Key + "=" + e.Value))}"
            : "";
        RaiseLookChoicesChanged();
        if (expressionKey != _expressionChoicesKey)
        {
            _expressionChoicesKey = expressionKey;
            RaiseFaceChoicesChanged();
        }
        OnPropertyChanged(nameof(SelectedCharacterView));
        OnPropertyChanged(nameof(IsSelectedCharacterFront));
        OnPropertyChanged(nameof(IsSelectedCharacterSide));
    }

    public void DeleteCharacter(PanelId panelId, int index)
    {
        Apply(EditPanel(Working, panelId, p =>
            index < 0 || index >= p.CharacterInstances.Count
                ? EditResult<Panel>.Failure("No such character.")
                : EditResult<Panel>.Success(p with { CharacterInstances = p.CharacterInstances.Where((_, i) => i != index).ToList() })));
        if (Equals(_selectedPanelId, panelId) && _selectedCharacterIndex == index)
            Select(panelId);
    }

    /// <summary>Characters draw in list order (the end is in front) - and always behind the panel's bubbles.</summary>
    public void ReorderCharacter(PanelId panelId, int index, bool toFront)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel) || index < 0 || index >= panel.CharacterInstances.Count)
            return;
        var (list, newIndex) = CharacterPlacementEditing.Reorder(panel.CharacterInstances, index, toFront);
        Apply(EditCharacters(Working, panelId, _ => list));
        if (Equals(_selectedPanelId, panelId) && _selectedCharacterIndex == index)
            SelectCharacter(panelId, newIndex);
    }

    private void ReorderSelection(bool toFront)
    {
        if (_selectedPanelId is not { } panelId)
            return;
        if (HasSelectedBubble)
            ReorderBubble(panelId, _selectedBubbleIndex, toFront);
        else if (HasSelectedCharacter)
            ReorderCharacter(panelId, _selectedCharacterIndex, toFront);
        else if (HasSelectedElement)
            ReorderElement(panelId, _selectedElementIndex, toFront);
    }

    /// <summary>How many panels on this page show <paramref name="id"/>.</summary>
    public int CountPanelsShowing(CharacterId id) =>
        Committed.Panels.Values.Count(p => p.CharacterInstances.Any(c => c.CharacterId == id));

    /// <summary>Applies <paramref name="edit"/> to one character, then keeps it reachable inside its panel - the single place that enforces "a character belongs to its panel".</summary>
    private EditResult<PageDocument> EditCharacterInPanel(PageDocument document, PanelId panelId, int index, Func<CharacterInstance, CharacterInstance> edit) =>
        EditPanel(document, panelId, panel =>
        {
            if (index < 0 || index >= panel.CharacterInstances.Count)
                return EditResult<Panel>.Failure("No such character.");
            var list = panel.CharacterInstances.ToList();
            var edited = edit(list[index]);
            list[index] = CharacterPlacementEditing.KeepReachable(edited, InstanceExtent(edited), Bounds(panel));
            return EditResult<Panel>.Success(panel with { CharacterInstances = list });
        });

    private static EditResult<PageDocument> EditCharacters(PageDocument document, PanelId panelId, Func<IReadOnlyList<CharacterInstance>, IReadOnlyList<CharacterInstance>> edit) =>
        EditPanel(document, panelId, panel => EditResult<Panel>.Success(panel with { CharacterInstances = edit(panel.CharacterInstances) }));

    // ---------------------------------------------------------------- helpers

    private static Rect2D Bounds(Panel panel) => AnchorRing.BoundingBox(panel.Shape.Anchors);

    /// <summary>
    /// Grows a bubble to fit its own text at its own lettering (never shrinks it) - the same
    /// measurement <see cref="Stanley.Rendering.PageRenderer"/> draws with, so nothing on
    /// screen, in export or in the in-place text editor ever has to shrink to fit (issue #66).
    /// Callers that need it wrapped back up as a successful edit use <see cref="GrownBubbleEdit"/>.
    /// </summary>
    private static Bubble GrowBubbleToFit(Bubble bubble) =>
        BubbleEditing.GrowToFit(bubble, BubbleTextRenderer.NeededScale(bubble, PageRenderer.FontSizeMm));

    private static EditResult<Bubble> GrownBubbleEdit(Bubble bubble) => EditResult<Bubble>.Success(GrowBubbleToFit(bubble));

    private static EditResult<PageDocument> EditPanel(PageDocument document, PanelId id, Func<Panel, EditResult<Panel>> edit)
    {
        if (!document.Panels.TryGetValue(id, out var panel))
            return EditResult<PageDocument>.Failure($"Unknown panel '{id}'.");

        var result = edit(panel);
        if (!result.IsValid)
            return EditResult<PageDocument>.Failure(result.Error!);

        var newPanels = new Dictionary<PanelId, Panel>(document.Panels) { [id] = result.Value };
        return EditResult<PageDocument>.Success(document with { Panels = newPanels });
    }

    /// <summary>Applies <paramref name="edit"/> to one bubble, then pulls the result back inside its panel - the single place that enforces "a bubble belongs to its panel".</summary>
    private static EditResult<PageDocument> EditBubbleInPanel(
        PageDocument document,
        PanelId panelId,
        int bubbleIndex,
        Func<Bubble, Rect2D, EditResult<Bubble>> edit)
    {
        return EditPanel(document, panelId, panel =>
        {
            if (bubbleIndex < 0 || bubbleIndex >= panel.Bubbles.Count)
                return EditResult<Panel>.Failure("No such bubble.");

            var panelBounds = Bounds(panel);
            var result = edit(panel.Bubbles[bubbleIndex], panelBounds);
            if (!result.IsValid)
                return EditResult<Panel>.Failure(result.Error!);

            var bubbles = panel.Bubbles.ToList();
            bubbles[bubbleIndex] = BubbleEditing.KeepInside(result.Value, panelBounds);
            return EditResult<Panel>.Success(panel with { Bubbles = bubbles });
        });
    }
}
