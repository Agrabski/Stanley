using System.ComponentModel;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>What a press on the page canvas does. <see cref="Select"/> is the default and already covers moving/resizing everything; the others are one-purpose shortcuts for creating things.</summary>
public enum PageEditorTool
{
    Select,
    Panel,
    Bubble,
    Pan
}

public sealed class PageEditorViewModel : EditorViewModel<PageDocument>
{
    /// <summary>Default size for a bubble created with a single click, in mm - roughly two short lines of lettering.</summary>
    public const double DefaultBubbleWidthMm = 42;
    public const double DefaultBubbleHeightMm = 26;

    private PageEditorTool _tool = PageEditorTool.Select;
    private PanelId? _selectedPanelId;
    private int _selectedBubbleIndex = -1;
    private BubbleStylePreset _newBubbleStyle = BubbleStylePreset.Speech;
    private PanelGrid _grid = PanelGrid.Default;
    private bool _snapEnabled = true;
    private IReadOnlyList<SnapGuide> _activeGuides = [];
    private PanelId _pendingPanelId;

    public Rect2D PageBounds { get; }

    public PageEditorViewModel(EditorHistory history, Rect2D pageBounds, PageDocument initial)
        : base(history, "Page", initial)
    {
        PageBounds = pageBounds;
        PropertyChanged += OnSelfPropertyChanged;
    }

    // ---------------------------------------------------------------- tool & settings

    public PageEditorTool Tool
    {
        get => _tool;
        set
        {
            SetProperty(ref _tool, value);
            // Always re-raise every flag: a toggle button bound to one of them may have
            // flipped itself off locally, and needs to hear "no, you're still on".
            OnPropertyChanged(nameof(IsSelectTool));
            OnPropertyChanged(nameof(IsPanelTool));
            OnPropertyChanged(nameof(IsBubbleTool));
            OnPropertyChanged(nameof(IsPanTool));
            OnPropertyChanged(nameof(Hint));
        }
    }

    public bool IsSelectTool { get => Tool == PageEditorTool.Select; set => SetToolFlag(PageEditorTool.Select, value); }
    public bool IsPanelTool { get => Tool == PageEditorTool.Panel; set => SetToolFlag(PageEditorTool.Panel, value); }
    public bool IsBubbleTool { get => Tool == PageEditorTool.Bubble; set => SetToolFlag(PageEditorTool.Bubble, value); }
    public bool IsPanTool { get => Tool == PageEditorTool.Pan; set => SetToolFlag(PageEditorTool.Pan, value); }

    private void SetToolFlag(PageEditorTool tool, bool value) => Tool = value ? tool : Tool;

    /// <summary>Margin and gutter used for snapping, splitting and layout presets.</summary>
    public PanelGrid Grid
    {
        get => _grid;
        set => SetProperty(ref _grid, value);
    }

    public double MarginMm
    {
        get => Grid.MarginMm;
        set
        {
            Grid = Grid with { MarginMm = Math.Max(0, value) };
            OnPropertyChanged();
        }
    }

    public double GutterMm
    {
        get => Grid.GutterMm;
        set
        {
            Grid = Grid with { GutterMm = Math.Max(0, value) };
            OnPropertyChanged();
        }
    }

    public bool SnapEnabled
    {
        get => _snapEnabled;
        set => SetProperty(ref _snapEnabled, value);
    }

    /// <summary>The lines the current drag snapped to, for the canvas to draw. Empty outside a snapping drag.</summary>
    public IReadOnlyList<SnapGuide> ActiveGuides
    {
        get => _activeGuides;
        private set => SetProperty(ref _activeGuides, value);
    }

    // ---------------------------------------------------------------- selection

    public PanelId? SelectedPanelId => _selectedPanelId;

    /// <summary>Index into the selected panel's <see cref="Panel.Bubbles"/>, or -1.</summary>
    public int SelectedBubbleIndex => _selectedBubbleIndex;

    public bool HasSelectedPanel => _selectedPanelId is not null;
    public bool HasSelectedBubble => SelectedBubble is not null;
    public bool HasSelection => HasSelectedPanel;
    public bool SelectedBubbleHasTails => SelectedBubble is { Tails.Count: > 0 };

    public Panel? SelectedPanel =>
        _selectedPanelId is { } id && Working.Panels.TryGetValue(id, out var panel) ? panel : null;

    public Bubble? SelectedBubble =>
        SelectedPanel is { } panel && _selectedBubbleIndex >= 0 && _selectedBubbleIndex < panel.Bubbles.Count
            ? panel.Bubbles[_selectedBubbleIndex]
            : null;

    public event Action? SelectionChanged;

    public void Select(PanelId? panelId, int bubbleIndex = -1)
    {
        if (panelId is null)
            bubbleIndex = -1;
        if (Equals(_selectedPanelId, panelId) && _selectedBubbleIndex == bubbleIndex)
            return;

        _selectedPanelId = panelId;
        _selectedBubbleIndex = bubbleIndex;
        RaiseSelectionChanged();
    }

    public void ClearSelection() => Select(null);

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedPanelId));
        OnPropertyChanged(nameof(SelectedBubbleIndex));
        OnPropertyChanged(nameof(HasSelectedPanel));
        OnPropertyChanged(nameof(HasSelectedBubble));
        OnPropertyChanged(nameof(HasSelection));
        RaiseBubbleDerivedChanged();
        OnPropertyChanged(nameof(Hint));
        SelectionChanged?.Invoke();
    }

    private void RaiseBubbleDerivedChanged()
    {
        OnPropertyChanged(nameof(SelectedBubbleHasTails));
        OnPropertyChanged(nameof(CurrentBubbleStyle));
        OnPropertyChanged(nameof(IsSpeechStyle));
        OnPropertyChanged(nameof(IsShoutStyle));
        OnPropertyChanged(nameof(IsWhisperStyle));
    }

    /// <summary>Undo/redo or a delete can remove what's selected out from under us; drop whatever no longer exists instead of pointing at a stale index.</summary>
    private void OnSelfPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Working))
            return;

        if (_selectedPanelId is { } id)
        {
            if (!Working.Panels.TryGetValue(id, out var panel))
                Select(null);
            else if (_selectedBubbleIndex >= panel.Bubbles.Count)
                Select(id);
        }
        RaiseBubbleDerivedChanged();
    }

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
    public string Hint => Tool switch
    {
        PageEditorTool.Panel => "Drag on the page to draw a panel. Edges snap to the margins and a gutter away from other panels (hold Alt to place freely).",
        PageEditorTool.Bubble => "Click inside a panel to add a bubble there, or drag to size it. The bubble stays inside that panel.",
        PageEditorTool.Pan => "Drag to move around the page. Ctrl+scroll zooms.",
        _ when HasSelectedBubble => "Drag to move the bubble · drag the orange dot to aim a tail · double-click or Enter to edit text · Delete removes it.",
        _ when HasSelectedPanel => "Drag to move the panel · drag an edge, corner or gutter to resize · split it or pick a layout from the ribbon · Delete removes it.",
        _ => "Pick a page layout from the ribbon, or click a panel to select it. Double-click inside a panel to add a speech bubble."
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
        if (commit)
            CommitGesture();
        else
            CancelGesture();
        ActiveGuides = [];
    }

    // ---------------------------------------------------------------- panel layout

    public void BeginResizePanel(PanelId id) => BeginGesture();

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

    public void BeginMovePanel(PanelId id) => BeginGesture();

    public void UpdateMovePanel(PanelId id, double dx, double dy, double snapTolerance)
    {
        if (!Committed.Panels.ContainsKey(id))
            return;

        if (SnapEnabled && snapTolerance > 0)
        {
            var start = CommittedPanelBounds(id);
            var raw = start with { X = start.X + dx, Y = start.Y + dy };
            var snap = PanelSnapping.SnapMove(raw, PageBounds, CommittedBoundsExcept([id]), Grid, snapTolerance);
            dx = snap.Bounds.X - start.X;
            dy = snap.Bounds.Y - start.Y;
            ActiveGuides = snap.Guides;
        }
        UpdateGesture(EditPanel(Committed, id, p => PanelLayoutEditing.Move(p, dx, dy, PageBounds)));
    }

    public void BeginDragBoundary(PanelBoundaryDrag boundary) => BeginGesture();

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
        UpdateGesture(EditResult<PageDocument>.Success(new PageDocument(ReadingOrder(panels), panels)));
    }

    /// <summary>Commits a panel drawn with <see cref="BeginCreatePanel"/> and selects it; returns false if it ended up too small to keep.</summary>
    public bool CommitCreatePanel()
    {
        EndGesture(commit: true);
        if (!Working.Panels.ContainsKey(_pendingPanelId))
            return false;
        Select(_pendingPanelId);
        return true;
    }

    public void SplitPanel(PanelId id, BoundaryOrientation orientation, double fraction)
    {
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

        Apply(EditResult<PageDocument>.Success(new PageDocument(newOrder, newPanels)));
        if (_selectedPanelId is { } selected && selected.Equals(id))
            Select(first.Id);
    }

    public void DeletePanel(PanelId id)
    {
        if (!Working.Panels.ContainsKey(id))
            return;

        var panels = Working.Panels.Where(kvp => !kvp.Key.Equals(id)).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        Apply(EditResult<PageDocument>.Success(new PageDocument(Working.PanelOrder.Where(p => !p.Equals(id)).ToList(), panels)));
    }

    /// <summary>
    /// Re-tiles the page into <paramref name="preset"/>'s grid. Existing panels are reused
    /// in reading order - so their bubbles come along into the new slots - and any extra
    /// panels are removed (one undo step brings them back).
    /// </summary>
    public void ApplyLayoutPreset(PanelLayoutPreset preset)
    {
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

        Apply(EditResult<PageDocument>.Success(new PageDocument(order, panels)));
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

        var bubble = bubbleResult.Value;
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

        var bubble = BubbleEditing.KeepInside(created.Value, panelBounds);
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

    /// <summary>Moves by (<paramref name="dx"/>, <paramref name="dy"/>) from where the bubble was when the drag began; it stops at its panel's edge.</summary>
    public void UpdateMoveBubble(PanelId panelId, int bubbleIndex, double dx, double dy) =>
        UpdateGesture(EditBubbleInPanel(Committed, panelId, bubbleIndex, (b, _) => BubbleEditing.Move(b, dx, dy)));

    /// <summary>Nudges the selected bubble (or, with none, the selected panel) by a fixed amount - the arrow-key path.</summary>
    public void NudgeSelection(double dx, double dy)
    {
        if (_selectedPanelId is not { } panelId)
            return;

        if (HasSelectedBubble)
            Apply(EditBubbleInPanel(Working, panelId, _selectedBubbleIndex, (b, _) => BubbleEditing.Move(b, dx, dy)));
        else
            Apply(EditPanel(Working, panelId, p => PanelLayoutEditing.Move(p, dx, dy, PageBounds)));
    }

    public void SetBubbleStyle(PanelId panelId, int bubbleIndex, BubbleStylePreset style) =>
        Apply(EditBubbleInPanel(Working, panelId, bubbleIndex, (b, _) => BubbleEditing.SetStyle(b, style)));

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

    public void SetBubbleText(PanelId panelId, int bubbleIndex, string text) =>
        Apply(EditBubbleInPanel(Working, panelId, bubbleIndex, (b, _) => BubbleEditing.SetText(b, text)));

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

    /// <summary>Deletes the selected bubble if there is one, otherwise the selected panel.</summary>
    public void DeleteSelection()
    {
        if (_selectedPanelId is not { } panelId)
            return;

        if (HasSelectedBubble)
            DeleteBubble(panelId, _selectedBubbleIndex);
        else
            DeletePanel(panelId);
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

    // ---------------------------------------------------------------- helpers

    private static Rect2D Bounds(Panel panel) => AnchorRing.BoundingBox(panel.Shape.Anchors);

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
