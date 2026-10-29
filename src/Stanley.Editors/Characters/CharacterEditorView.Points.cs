using Avalonia.Controls;
using Avalonia.Input;
using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>
/// Edit Points and Edit sockets on the stage (docs/shape-characters.md §9.2), with the page's
/// gestures (docs/shape-editing.md): double-click a head or torso to edit its points; drag a
/// point (Shift keeps it level, upright or at 45°), press the outline to add one and keep
/// dragging to pull it out, click a point for its handles and drag them (Alt bends one side),
/// double-click a point to make it smooth or sharp; Delete, the arrows, Esc and Enter; a drag
/// of a socket's dot. Every drag maps the pointer through the placement as it was pressed.
/// A press anywhere else ends the editing and then does what it always does.
/// </summary>
public partial class CharacterEditorView
{
    private enum EditDrag
    {
        None,
        PendingPoint, // pressed on a point: a click only selects it
        Point,
        Handle,
        InsertPoint,
        Socket
    }

    private EditDrag _editDrag;
    private CharacterPlacement? _editPlacement;
    private Avalonia.Point _editPressed;
    private Point2D _editPressFigure;
    private Point2D _editStart; // where the dragged point, handle or socket was (figure space)
    private Point2D _editAnchor; // a dragged handle's point
    private int _editIndex = -1;
    private bool _editMoved;

    // A press on the outline added this point; a double-click there shouldn't then toggle it.
    private int _pointAddedByLastClick = -1;

    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);

    /// <summary>How far (figure units) the arrows move a point: half a percent of an adult's height, or 2.5% with Shift.</summary>
    private const double NudgeStep = 0.005;

    private void InitializeEditing()
    {
        // Right-click: the stage's menu (handled on the way down, before the left-button handler bails).
        Stage.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (DataContext is CharacterEditorViewModel vm && e.GetCurrentPoint(Stage).Properties.IsRightButtonPressed && StageMenuItems(e.GetPosition(Stage)) is { Count: > 0 } items)
            {
                new ContextMenu { ItemsSource = items }.Open(Stage);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        KeyDown += OnEditingKeyDown;
    }

    /// <summary>
    /// A left press while a part's points or sockets are edited, or a double-click on a head or
    /// torso that starts editing its points. True when handled here; false lets the stage's
    /// usual press handling go on (after ending the editing, for a press elsewhere).
    /// </summary>
    private bool PressEditing(CharacterEditorViewModel vm, Avalonia.Point p, PointerPressedEventArgs e)
    {
        Stage.Focus();
        var justAdded = _pointAddedByLastClick;
        _pointAddedByLastClick = -1;
        if (vm.IsEditingPoints || vm.IsEditingSockets)
        {
            if (Stage.MainPlacement is { } placement && Stage.EditMarkAt(p) is { } hit)
            {
                PressEditMark(vm, hit, placement, p, e, justAdded);
                e.Handled = true;
                return true;
            }
            // Inside the part being edited: still editing it, nothing else to do.
            if (vm.IsEditingPoints ? Stage.IsInsideEditedOutline(p) : Stage.ModuleAt(p) == vm.SelectedModule)
            {
                e.Handled = true;
                return true;
            }
            vm.StopEditing();
        }
        // Like Figma: double-click a head or torso to edit its points.
        if (e.ClickCount >= 2 && vm.IsModular && Stage.ModuleAt(p) is { } part && part.IsShape())
        {
            vm.SelectModule(part);
            vm.IsEditingPoints = true;
            e.Handled = true;
            return true;
        }
        return false;
    }

    private void PressEditMark(CharacterEditorViewModel vm, ModuleEditHit hit, CharacterPlacement placement, Avalonia.Point p, PointerPressedEventArgs e, int justAdded)
    {
        var outline = vm.EditedOutline;
        switch (hit.Kind)
        {
            case ModuleEditHitKind.Point when e.ClickCount >= 2:
                if (hit.Index != justAdded) // a double-click on the outline: its first click added this point, which is all it wanted
                    vm.TogglePointKind(hit.Index);
                return;
            case ModuleEditHitKind.Point when outline is not null:
                vm.SelectPoint(hit.Index);
                StartEditDrag(EditDrag.PendingPoint, placement, p, outline.Anchors[hit.Index].Point, hit.Index, e);
                return;
            case ModuleEditHitKind.Handle when outline is not null:
                var anchor = outline.Anchors[hit.Index];
                if (!vm.BeginMoveHandle(hit.Index, hit.Side))
                    return;
                _editAnchor = anchor.Point;
                StartEditDrag(EditDrag.Handle, placement, p, hit.Side == HandleSide.In ? anchor.InHandle : anchor.OutHandle, hit.Index, e);
                return;
            case ModuleEditHitKind.Outline:
                // A new point right there, which the drag then pulls (released straight away, it's just added).
                var added = vm.BeginInsertPoint(hit.Segment, hit.T);
                if (added >= 0 && vm.SelectedPoint is { } inserted)
                    StartEditDrag(EditDrag.InsertPoint, placement, p, inserted.Point, added, e);
                return;
            case ModuleEditHitKind.Socket when hit.Socket is { } socket && vm.EditedSockets.FirstOrDefault(s => s.Name == socket) is { } spot:
                if (vm.BeginMoveSocket(socket))
                    StartEditDrag(EditDrag.Socket, placement, p, spot.At, -1, e);
                return;
        }
    }

    private void StartEditDrag(EditDrag kind, CharacterPlacement placement, Avalonia.Point p, Point2D start, int index, PointerPressedEventArgs e)
    {
        _editDrag = kind;
        _editPlacement = placement;
        _editPressed = p;
        _editPressFigure = placement.ToFigure(new(p.X, p.Y));
        _editStart = start;
        _editIndex = index;
        _editMoved = false;
        Stage.PointGhost = null;
        e.Pointer.Capture(Stage);
    }

    /// <summary>A pointer move: an edit drag goes on, or - hovering while editing - the cursor and the "add a point here" dot follow what's under it. True when handled here.</summary>
    private bool MoveEditing(CharacterEditorViewModel vm, Avalonia.Point p, PointerEventArgs e)
    {
        if (_editDrag == EditDrag.None)
        {
            if (!vm.IsEditingPoints && !vm.IsEditingSockets)
            {
                Stage.PointGhost = null;
                return false;
            }
            var hit = Stage.EditMarkAt(p);
            Stage.PointGhost = hit is { Kind: ModuleEditHitKind.Outline } spot && vm.EditedOutline is { } outline ? ShapePointEditing.PointOn(outline, spot.Segment, spot.T) : null;
            Stage.Cursor = hit is null ? Cursor.Default : hit.Kind == ModuleEditHitKind.Outline ? Cursor.Default : MoveCursor;
            return hit is not null;
        }
        if (!_editMoved && Math.Abs(p.X - _editPressed.X) < DragThreshold && Math.Abs(p.Y - _editPressed.Y) < DragThreshold)
            return true;
        _editMoved = true;
        var now = _editPlacement!.ToFigure(new(p.X, p.Y));
        var to = new Point2D(_editStart.X + now.X - _editPressFigure.X, _editStart.Y + now.Y - _editPressFigure.Y);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (_editDrag)
        {
            case EditDrag.PendingPoint:
                if (!vm.BeginMovePoint(_editIndex))
                {
                    _editDrag = EditDrag.None;
                    return true;
                }
                _editDrag = EditDrag.Point;
                vm.UpdateMovePoint(shift ? ShapePointEditing.SnapAngle(_editStart, to) : to);
                break;
            case EditDrag.Point or EditDrag.InsertPoint:
                vm.UpdateMovePoint(shift ? ShapePointEditing.SnapAngle(_editStart, to) : to);
                break;
            case EditDrag.Handle:
                vm.UpdateMoveHandle(shift ? ShapePointEditing.SnapAngle(_editAnchor, to) : to, independent: e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                break;
            case EditDrag.Socket:
                vm.UpdateMoveSocket(shift ? ShapePointEditing.SnapAngle(_editStart, to) : to);
                break;
        }
        return true;
    }

    /// <summary>The end of an edit drag: one undo step if it changed anything (<paramref name="commit"/> false throws it away). True if there was one.</summary>
    private bool EndEditing(IPointer? pointer, bool commit = true)
    {
        if (_editDrag == EditDrag.None)
            return false;
        var kind = _editDrag;
        _editDrag = EditDrag.None;
        pointer?.Capture(null);
        if (DataContext is not CharacterEditorViewModel vm)
            return true;
        switch (kind)
        {
            case EditDrag.Point or EditDrag.Handle or EditDrag.InsertPoint:
                vm.EndPointDrag(commit);
                if (kind == EditDrag.InsertPoint && commit && !_editMoved)
                    _pointAddedByLastClick = _editIndex;
                break;
            case EditDrag.Socket:
                vm.EndSocketDrag(commit);
                break;
        }
        return true;
    }

    /// <summary>While editing points: Esc or Enter is done, Delete removes the selected point (never the shape), the arrows move it. Enter on a picked head or torso starts.</summary>
    private void OnEditingKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CharacterEditorViewModel vm || e.Source is TextBox)
            return;
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5 * NudgeStep : NudgeStep;
        switch (e.Key)
        {
            case Key.Escape when _editDrag != EditDrag.None:
                EndEditing(null, commit: false);
                break;
            case Key.Escape or Key.Enter when vm.IsEditingPoints || vm.IsEditingSockets:
                vm.StopEditing();
                break;
            case Key.Delete or Key.Back when _editDrag == EditDrag.None && vm.IsEditingPoints:
                vm.DeleteSelectedPoint();
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down when _editDrag == EditDrag.None && vm.HasSelectedPoint:
                vm.NudgeSelectedPoint(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0);
                break;
            case Key.Enter when vm.CanEditPoints:
                vm.IsEditingPoints = true;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>
    /// The stage's one right-click menu at <paramref name="p"/> (control coordinates), picking
    /// what's there first: on a point being edited, Smooth / Corner / Delete point; on the
    /// outline, Add point; on a head or torso, Edit points and Edit sockets (and Reset sockets);
    /// then, for any picked part, a shape of your own for it (<see cref="OwnShapeItems"/>).
    /// Empty for anything else. Public for headless UI tests.
    /// </summary>
    public IReadOnlyList<Control> StageMenuItems(Avalonia.Point p)
    {
        if (DataContext is not CharacterEditorViewModel vm || !vm.IsModular || vm.IsDrawingShape)
            return [];
        var items = new List<Control>();
        var hit = vm.IsEditingPoints ? Stage.EditMarkAt(p) : null;
        if (hit is { Kind: ModuleEditHitKind.Point })
        {
            var index = hit.Index;
            vm.SelectPoint(index);
            items.Add(Item("Smooth point", () => vm.SetPointKind(index, AnchorHandleKind.Smooth)));
            items.Add(Item("Corner point", () => vm.SetPointKind(index, AnchorHandleKind.Corner)));
            var delete = Item("Delete point", vm.DeleteSelectedPoint, "Delete");
            delete.IsEnabled = vm.CanDeleteSelectedPoint;
            items.Add(delete);
        }
        else if (hit is { Kind: ModuleEditHitKind.Outline })
        {
            var (segment, t) = (hit.Segment, hit.T);
            items.Add(Item("Add point", () =>
            {
                if (vm.BeginInsertPoint(segment, t) >= 0)
                    vm.EndPointDrag();
            }));
        }
        else
        {
            var part = vm.IsEditingPoints || vm.IsEditingSockets ? vm.SelectedModule : Stage.StickerAt(p) is null ? Stage.ModuleAt(p) : null;
            if (part is not { } picked)
                return [];
            vm.SelectModule(picked);
            if (vm.SelectedModule != picked)
                return [];
            if (!picked.IsShape())
                return [.. OwnShapeItems(vm)];
        }
        if (items.Count > 0)
            items.Add(new Separator());
        items.Add(vm.IsEditingPoints
            ? Item("Done editing points", () => vm.IsEditingPoints = false, "Escape")
            : Item("Edit points", () => vm.IsEditingPoints = true, "Enter"));
        items.Add(vm.IsEditingSockets
            ? Item("Done editing sockets", () => vm.IsEditingSockets = false, "Escape")
            : Item("Edit sockets", () => vm.IsEditingSockets = true));
        if (vm.CanResetSockets)
            items.Add(Item("Reset sockets", () => vm.ResetSocketsCommand.Execute(null)));
        if (!vm.IsEditingPoints && !vm.IsEditingSockets)
        {
            items.Add(new Separator());
            items.AddRange(OwnShapeItems(vm));
        }
        return items;
    }

    private static MenuItem Item(string header, Action action, string? gesture = null)
    {
        var item = new MenuItem { Header = header };
        if (gesture != null)
            item.InputGesture = KeyGesture.Parse(gesture);
        item.Click += (_, _) => action();
        return item;
    }
}
