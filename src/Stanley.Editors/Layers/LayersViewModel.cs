using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Stanley.Editing;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>
/// The Layers side pane (a dock <see cref="Tool"/> on the right of the editors): what the
/// current page's panels hold, front to back as it's drawn (<see cref="PanelStack.Order"/>),
/// so overlapping things can be picked from a list. Like the other side panes it isn't an
/// editor - the ribbon keeps following the page being worked in; the pane follows the page
/// editor it's pointed at (<see cref="Editor"/>), and its highlight follows what's selected
/// there, both ways.
/// </summary>
public sealed class LayersViewModel : Tool
{
    // The panels whose layers are showing. Whatever gets selected opens its panel, so the
    // list grows to where you've been working; the arrow on a panel's row closes it again.
    private readonly HashSet<PanelId> _expanded = [];
    private PageEditorViewModel? _editor;

    // Where the selected layer sits in its panel's stack (0 is the back), and how many layers the panel stacks; null when no layer is selected.
    private int? _slot;
    private int _count;

    // The row being dragged to another place in its panel's stack, and the gap it would be dropped
    // into: the index of the row it would land just above (-1 until the pointer's been over one).
    private LayerRow? _dragged;
    private int _dropGap = -1;

    public LayersViewModel()
    {
        Id = "Layers";
        Title = "Layers";
        CanClose = false;
        CanFloat = false;
        // It lives in its own column: no pinning it away, dragging it elsewhere or turning it into a tab among the pages.
        CanPin = false;
        CanDrag = false;
        CanDockAsDocument = false;

        BringToFrontCommand = new RelayCommand(() => MoveSelected(StackMove.ToFront), () => _slot is { } slot && slot < _count - 1);
        BringForwardCommand = new RelayCommand(() => MoveSelected(StackMove.Forward), () => _slot is { } slot && slot < _count - 1);
        SendBackwardCommand = new RelayCommand(() => MoveSelected(StackMove.Backward), () => _slot is > 0);
        SendToBackCommand = new RelayCommand(() => MoveSelected(StackMove.ToBack), () => _slot is > 0);
    }

    // The four buttons above the list, ComiPo's way: all the way to the front, one place forward, one place back, all the way to the back.
    public IRelayCommand BringToFrontCommand { get; }
    public IRelayCommand BringForwardCommand { get; }
    public IRelayCommand SendBackwardCommand { get; }
    public IRelayCommand SendToBackCommand { get; }

    // What each button says it does - or why it can't just now (disabled buttons show it too).
    public string BringToFrontTip => MoveTip("Bring to front", towardsFront: true);
    public string BringForwardTip => MoveTip("Bring forward", towardsFront: true);
    public string SendBackwardTip => MoveTip("Send backward", towardsFront: false);
    public string SendToBackTip => MoveTip("Send to back", towardsFront: false);

    private string MoveTip(string action, bool towardsFront)
    {
        if (_slot is not { } slot)
            return "Select a layer - in the list or on the page - to move it";
        if (towardsFront && slot == _count - 1)
            return "Already in front of everything else in its panel";
        if (!towardsFront && slot == 0)
            return "Already behind everything else in its panel (only the background is further back)";
        return action;
    }

    /// <summary>The rows, top to bottom: each panel in reading order, and under an open one its layers from the front to the back, then its background.</summary>
    public ObservableCollection<LayerRow> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>The page editor whose panels are listed - the current page's; null when there's none.</summary>
    public PageEditorViewModel? Editor
    {
        get => _editor;
        set
        {
            if (ReferenceEquals(value, _editor))
                return;
            if (_editor != null)
                _editor.PropertyChanged -= OnEditorPropertyChanged;
            _editor = value;
            if (_editor != null)
            {
                _editor.PropertyChanged += OnEditorPropertyChanged;
                if (_editor.SelectedPanelId is { } selected)
                    _expanded.Add(selected);
            }
            Rebuild();
        }
    }

    /// <summary>Clicking a row selects what it stands for on the page; Shift+click adds a layer to the selection (or drops it), as on the page itself.</summary>
    public void Pick(LayerRow row, bool additive)
    {
        if (_editor is not { } editor)
            return;
        switch (row.Kind)
        {
            case LayerRowKind.Bubble when additive:
                editor.ToggleSelect(row.PanelId, bubbleIndex: row.Index);
                break;
            case LayerRowKind.Bubble:
                editor.Select(row.PanelId, bubbleIndex: row.Index);
                break;
            case LayerRowKind.Character when additive:
                editor.ToggleSelect(row.PanelId, characterIndex: row.Index);
                break;
            case LayerRowKind.Character:
                editor.SelectCharacter(row.PanelId, row.Index);
                break;
            case LayerRowKind.Element when additive:
                editor.ToggleSelect(row.PanelId, elementIndex: row.Index);
                break;
            case LayerRowKind.Element:
                editor.SelectElement(row.PanelId, row.Index);
                break;
            default: // a panel, or its background: the panel itself
                editor.Select(row.PanelId);
                break;
        }
        // The keyboard goes to the page, so Delete and the arrow keys act on what was just picked.
        editor.FocusPage();
    }

    /// <summary>Whether a row is being dragged to another place in the list.</summary>
    public bool IsDragging => _dragged != null;

    /// <summary>
    /// Starts dragging <paramref name="row"/> to another place among its panel's layers. False -
    /// nothing to drag - for a panel or background row, or one no longer in the list.
    /// </summary>
    public bool BeginDrag(LayerRow row)
    {
        CancelDrag();
        if (_editor == null || !row.IsLayer || !Rows.Contains(row))
            return false;
        _dragged = row;
        row.IsDragged = true;
        return true;
    }

    /// <summary>
    /// The pointer is over the gap above <c>Rows[gap]</c> (<c>Rows.Count</c>: below the last row).
    /// The layer stays among its own panel's, so a gap beyond them means the nearest end: in front
    /// of the panel's frontmost layer, or just above its background. The drop line shows there -
    /// unless dropping would leave the layer where it is.
    /// </summary>
    public void DragOver(int gap)
    {
        if (_dragged is not { } dragged || LayerRowsOf(dragged.PanelId) is not { } range)
            return;
        gap = Math.Clamp(gap, range.First, range.Background);
        if (gap == _dropGap)
            return;
        if (_dropGap >= 0 && _dropGap < Rows.Count)
            Rows[_dropGap].ShowDropLine = false;
        _dropGap = gap;
        var at = Rows.IndexOf(dragged);
        Rows[gap].ShowDropLine = gap != at && gap != at + 1;
    }

    /// <summary>Puts the dragged layer where the drop line is - one undo step - and selects it. Nothing moves if it's dropped where it was.</summary>
    public void Drop()
    {
        var dragged = _dragged;
        var gap = _dropGap;
        CancelDrag();
        if (dragged is null || gap < 0 || _editor is not { } editor || LayerRowsOf(dragged.PanelId) is not { } range)
            return;

        // The rows list the panel's layers front first; the stack counts from the back.
        var layers = range.Background - range.First;
        var from = Rows.IndexOf(dragged) - range.First;
        if (from < 0)
            return;
        var to = gap - range.First;
        if (to > from)
            to--; // the gap below the dragged row's own place closes up when it's taken out
        if (to != from)
            editor.MoveInStackTo(dragged.PanelId, StackItemOf(dragged), layers - 1 - to);
        Pick(dragged, additive: false);
    }

    /// <summary>Lets go of a drag without moving anything.</summary>
    public void CancelDrag()
    {
        if (_dragged != null)
            _dragged.IsDragged = false;
        if (_dropGap >= 0 && _dropGap < Rows.Count)
            Rows[_dropGap].ShowDropLine = false;
        _dragged = null;
        _dropGap = -1;
    }

    /// <summary>Where a panel's layers are in <see cref="Rows"/>: the first (frontmost) one's row, and its background's row just after the last; null if the panel isn't open.</summary>
    private (int First, int Background)? LayerRowsOf(PanelId panel)
    {
        var header = -1;
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].PanelId != panel)
                continue;
            if (Rows[i].Kind == LayerRowKind.Panel)
                header = i;
            else if (Rows[i].Kind == LayerRowKind.Background && header >= 0)
                return (header + 1, i);
        }
        return null;
    }

    private static StackItem StackItemOf(LayerRow row) => row.Kind switch
    {
        LayerRowKind.Bubble => new StackItem(StackKind.Bubble, row.Index),
        LayerRowKind.Character => new StackItem(StackKind.Character, row.Index),
        _ => new StackItem(StackKind.Element, row.Index)
    };

    /// <summary>The bubble, character or drawing that's selected on the page (the primary one, if several are), with its panel.</summary>
    private (PanelId Panel, StackItem Item)? SelectedLayer()
    {
        if (_editor is not { SelectedPanelId: { } panel } editor)
            return null;
        if (editor.HasSelectedBubble)
            return (panel, new StackItem(StackKind.Bubble, editor.SelectedBubbleIndex));
        if (editor.HasSelectedCharacter)
            return (panel, new StackItem(StackKind.Character, editor.SelectedCharacterIndex));
        if (editor.HasSelectedElement)
            return (panel, new StackItem(StackKind.Element, editor.SelectedElementIndex));
        return null;
    }

    private void MoveSelected(StackMove move)
    {
        if (_editor != null && SelectedLayer() is { } layer)
            _editor.MoveInStack(layer.Panel, layer.Item, move);
    }

    /// <summary>Works out where the selected layer sits in its panel's stack, and so which of the four buttons can move it.</summary>
    private void RefreshMoves()
    {
        _slot = null;
        _count = 0;
        if (_editor != null && SelectedLayer() is { } layer && _editor.Committed.Panels.TryGetValue(layer.Panel, out var panel))
        {
            var order = PanelStack.Order(panel);
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i] == layer.Item)
                    _slot = i;
            }
            _count = order.Count;
        }
        BringToFrontCommand.NotifyCanExecuteChanged();
        BringForwardCommand.NotifyCanExecuteChanged();
        SendBackwardCommand.NotifyCanExecuteChanged();
        SendToBackCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BringToFrontTip));
        OnPropertyChanged(nameof(BringForwardTip));
        OnPropertyChanged(nameof(SendBackwardTip));
        OnPropertyChanged(nameof(SendToBackTip));
    }

    private void ToggleExpanded(PanelId panel)
    {
        if (!_expanded.Remove(panel))
            _expanded.Add(panel);
        Rebuild();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // Structure: only when an edit is committed, not on every frame of a drag.
            case nameof(PageEditorViewModel.Committed):
            case nameof(PageEditorViewModel.CharacterSnapshot):
                Rebuild();
                break;
            case nameof(PageEditorViewModel.SelectedPanelId):
                if (_editor?.SelectedPanelId is { } panel && _expanded.Add(panel))
                    Rebuild();
                else
                    RefreshSelection();
                break;
            case nameof(PageEditorViewModel.SelectedBubbleIndex):
            case nameof(PageEditorViewModel.SelectedCharacterIndex):
            case nameof(PageEditorViewModel.SelectedElementIndex):
            case nameof(PageEditorViewModel.SelectionCount):
                RefreshSelection();
                break;
        }
    }

    private void Rebuild()
    {
        CancelDrag(); // the rows it pointed at are going
        Rows.Clear();
        if (_editor is { } editor)
        {
            var document = editor.Committed;
            var clouds = document.PanelOrder.Count(id => document.Panels.TryGetValue(id, out var p) && p.Kind == PanelKind.Cloud);
            int panelNumber = 0, cloudNumber = 0;
            foreach (var id in document.PanelOrder)
            {
                if (!document.Panels.TryGetValue(id, out var panel))
                    continue;
                var isCloud = panel.Kind == PanelKind.Cloud;
                var order = PanelStack.Order(panel);
                var expanded = _expanded.Contains(id);
                var panelId = id;
                Rows.Add(new LayerRow(LayerRowKind.Panel, id, -1,
                    isCloud ? LayerLabels.Cloud(++cloudNumber, clouds) : LayerLabels.Panel(++panelNumber),
                    LayerLabels.PanelDetail(order.Count), isCloud ? "ThoughtCloudIcon" : "PanelBorderIcon")
                {
                    IsExpanded = expanded,
                    ToggleCommand = new RelayCommand(() => ToggleExpanded(panelId))
                });
                if (!expanded)
                    continue;
                for (var i = order.Count - 1; i >= 0; i--) // the front first, as lists of layers read
                    Rows.Add(RowFor(editor, id, panel, order[i]));
                Rows.Add(new LayerRow(LayerRowKind.Background, id, -1, "Background", LayerLabels.BackgroundDetail(panel.Background), "BackgroundIcon"));
            }
        }
        OnPropertyChanged(nameof(HasRows));
        RefreshSelection();
    }

    private static LayerRow RowFor(PageEditorViewModel editor, PanelId panelId, Panel panel, StackItem item)
    {
        switch (item.Kind)
        {
            case StackKind.Bubble:
                var bubble = panel.Bubbles[item.Index];
                return new LayerRow(LayerRowKind.Bubble, panelId, item.Index, LayerLabels.BubbleName(bubble.Style), LayerLabels.Preview(bubble.Text),
                    LayerLabels.BubbleIcon(bubble.Style));
            case StackKind.Character:
                var instance = panel.CharacterInstances[item.Index];
                var name = editor.CharacterSnapshot.TryGetValue(instance.CharacterId, out var character) ? character.Name : "Missing character";
                return new LayerRow(LayerRowKind.Character, panelId, item.Index, name, "", "CharacterIcon");
            default:
                var element = panel.Elements[item.Index];
                return new LayerRow(LayerRowKind.Element, panelId, item.Index, LayerLabels.ElementName(element), LayerLabels.ElementDetail(element),
                    LayerLabels.ElementIcon(element));
        }
    }

    private void RefreshSelection()
    {
        RefreshMoves();
        if (_editor is not { } editor)
            return;
        var itemSelected = editor.HasSelectedBubble || editor.HasSelectedCharacter || editor.HasSelectedElement;
        foreach (var row in Rows)
        {
            row.IsSelected = row.Kind switch
            {
                LayerRowKind.Panel => !itemSelected && Equals(editor.SelectedPanelId, row.PanelId),
                LayerRowKind.Bubble => editor.IsPartOfSelection(row.PanelId, bubbleIndex: row.Index),
                LayerRowKind.Character => editor.IsPartOfSelection(row.PanelId, characterIndex: row.Index),
                LayerRowKind.Element => editor.IsPartOfSelection(row.PanelId, elementIndex: row.Index),
                _ => false
            };
        }
    }
}
