using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>
/// Edit Points (issue #84, docs/shape-editing.md): the selected shape shows its anchor points,
/// to drag, add, delete and make smooth or sharp, and its curve handles, to bend the edges -
/// Figma's vector editing, PowerPoint's Edit Points. Also the Freeform tool's last step, which
/// turns the points it placed into a shape.
/// </summary>
public sealed partial class PageEditorViewModel
{
    /// <summary>The shape whose points are being edited - by id, so moving it in the stack (a new index) keeps the editing on.</summary>
    private ElementId? _editingPointsId;
    private int _selectedPointIndex = -1;

    private void InitializePointCommands()
    {
        DeletePointCommand = new RelayCommand(DeleteSelectedPoint, () => CanDeleteSelectedPoint);
        CloseShapeCommand = new RelayCommand(CloseEditedShape, () => CanCloseShape);
        OpenShapeCommand = new RelayCommand(OpenEditedShapeAtSelectedPoint, () => CanOpenShape);
    }

    private void NotifyPointCommands()
    {
        DeletePointCommand.NotifyCanExecuteChanged();
        CloseShapeCommand.NotifyCanExecuteChanged();
        OpenShapeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Shape tab › Delete Point (or Delete on the page): removes the selected point.</summary>
    public IRelayCommand DeletePointCommand { get; private set; } = null!;

    /// <summary>Shape tab › Close: joins a line's ends into a shape the fill fills.</summary>
    public IRelayCommand CloseShapeCommand { get; private set; } = null!;

    /// <summary>Shape tab › Open: cuts the shape at the selected point into a line.</summary>
    public IRelayCommand OpenShapeCommand { get; private set; } = null!;

    // ---------------------------------------------------------------- the mode

    /// <summary>One shape - not a multi-selection - is selected, so its points can be edited.</summary>
    public bool CanEditPoints => SelectedShape is not null && !HasMultiSelection;

    /// <summary>Shape tab › Edit Points (double-click the shape, or Enter): the selected shape shows its points to drag, add and delete. Selecting anything else - or another tool - ends it.</summary>
    public bool IsEditingPoints
    {
        get => _editingPointsId is { } id && CanEditPoints && SelectedShape!.Id == id;
        set
        {
            if (value == IsEditingPoints)
            {
                RaisePointsChanged(); // a toggle that flipped itself needs to hear it didn't
                return;
            }
            if (value)
                StartEditingPoints();
            else
                StopEditingPoints();
        }
    }

    private void StartEditingPoints()
    {
        if (!CanEditPoints)
            return;
        if (Tool != PageEditorTool.Select)
            Tool = PageEditorTool.Select;
        _editingPointsId = SelectedShape!.Id;
        _selectedPointIndex = -1;
        _notice = null;
        RaisePointsChanged();
    }

    public void StopEditingPoints()
    {
        if (_editingPointsId is null)
            return;
        _editingPointsId = null;
        _selectedPointIndex = -1;
        RaisePointsChanged();
    }

    /// <summary>A new selection: point editing ends unless it's still the same one shape, and the ribbon's Edit Points follows what's selected now.</summary>
    private void OnSelectionChangedForPoints()
    {
        if (_editingPointsId is null)
            RaisePointsChanged();
        else
            DropStalePointEditing(); // which raises them itself
    }

    /// <summary>Ends point editing once what it was for is gone - another selection, a multi-selection, the shape deleted - and lets go of a point that no longer exists (an undo took it away).</summary>
    private void DropStalePointEditing()
    {
        if (_editingPointsId is not { } id)
            return;
        if (SelectedShape?.Id != id || HasMultiSelection)
        {
            _editingPointsId = null;
            _selectedPointIndex = -1;
        }
        else if (_selectedPointIndex >= SelectedShape.Anchors.Count)
        {
            _selectedPointIndex = -1;
        }
        RaisePointsChanged();
    }

    /// <summary>The selected point of the shape being edited, or -1.</summary>
    public int SelectedPointIndex => IsEditingPoints && _selectedPointIndex < SelectedShape!.Anchors.Count ? _selectedPointIndex : -1;

    public bool HasSelectedPoint => SelectedPointIndex >= 0;

    public ShapeAnchor? SelectedPoint => HasSelectedPoint ? SelectedShape!.Anchors[SelectedPointIndex] : null;

    /// <summary>Picks a point of the shape being edited (-1 for none): it shows its curve handles, and the ribbon's point buttons act on it.</summary>
    public void SelectPoint(int index)
    {
        if (!IsEditingPoints)
            return;
        index = index >= 0 && index < SelectedShape!.Anchors.Count ? index : -1;
        if (index == _selectedPointIndex)
            return;
        _selectedPointIndex = index;
        RaisePointsChanged();
    }

    /// <summary>Shape tab › Smooth: the selected point is a smooth curve.</summary>
    public bool IsSelectedPointSmooth
    {
        get => SelectedPoint?.HandleKind == AnchorHandleKind.Smooth;
        set => SetSelectedPointKindFlag(AnchorHandleKind.Smooth, value);
    }

    /// <summary>Shape tab › Corner: the selected point is a sharp corner.</summary>
    public bool IsSelectedPointCorner
    {
        get => SelectedPoint?.HandleKind == AnchorHandleKind.Corner;
        set => SetSelectedPointKindFlag(AnchorHandleKind.Corner, value);
    }

    private void SetSelectedPointKindFlag(AnchorHandleKind kind, bool value)
    {
        if (value && HasSelectedPoint)
            SetPointKind(SelectedPointIndex, kind);
        RaisePointsChanged();
    }

    public bool CanDeleteSelectedPoint => SelectedShape is { } shape && HasSelectedPoint && shape.Anchors.Count > ShapePointEditing.MinPoints(shape.Closed);

    public bool CanCloseShape => IsEditingPoints && SelectedShape is { Closed: false } shape && shape.Anchors.Count >= ShapePointEditing.MinPoints(closed: true);

    public bool CanOpenShape => IsEditingPoints && SelectedShape is { Closed: true } && HasSelectedPoint;

    private void RaisePointsChanged()
    {
        OnPropertyChanged(nameof(CanEditPoints));
        OnPropertyChanged(nameof(IsEditingPoints));
        OnPropertyChanged(nameof(SelectedPointIndex));
        OnPropertyChanged(nameof(HasSelectedPoint));
        OnPropertyChanged(nameof(IsSelectedPointSmooth));
        OnPropertyChanged(nameof(IsSelectedPointCorner));
        OnPropertyChanged(nameof(CanDeleteSelectedPoint));
        OnPropertyChanged(nameof(CanCloseShape));
        OnPropertyChanged(nameof(CanOpenShape));
        OnPropertyChanged(nameof(Hint));
        if (DeletePointCommand != null) // null while the constructor is still setting up
            NotifyPointCommands();
    }

    // ---------------------------------------------------------------- dragging points and handles

    /// <summary>Starts dragging point <paramref name="pointIndex"/> of the shape being edited (it becomes the selected point).</summary>
    public void BeginMovePoint(PanelId panelId, int elementIndex, int pointIndex)
    {
        SelectPoint(pointIndex);
        BeginGesture();
    }

    /// <summary>Puts the dragged point at <paramref name="to"/>, its handles following - one undo step when the drag ends.</summary>
    public void UpdateMovePoint(PanelId panelId, int elementIndex, int pointIndex, Point2D to) =>
        UpdateGesture(EditShapeInPanel(MoveBase, panelId, elementIndex, s => ShapePointEditing.MovePoint(s, pointIndex, to)));

    public void BeginMoveHandle(PanelId panelId, int elementIndex, int pointIndex, HandleSide side)
    {
        SelectPoint(pointIndex);
        BeginGesture();
    }

    /// <summary>Puts the dragged curve handle at <paramref name="to"/>; on a smooth point the other handle stays in line unless <paramref name="independent"/> (Alt).</summary>
    public void UpdateMoveHandle(PanelId panelId, int elementIndex, int pointIndex, HandleSide side, Point2D to, bool independent) =>
        UpdateGesture(EditShapeInPanel(Committed, panelId, elementIndex, s => ShapePointEditing.MoveHandle(s, pointIndex, side, to, independent)));

    /// <summary>
    /// A press on the outline of the shape being edited: a new point there, selected, which the
    /// drag that follows moves (<see cref="UpdateMovePoint"/>) - pressed and let go, it's just
    /// added. One undo step either way. Returns the new point's index, or -1.
    /// </summary>
    public int BeginInsertPoint(PanelId panelId, int elementIndex, int segment, double t)
    {
        if (!Committed.Panels.TryGetValue(panelId, out var panel) || elementIndex < 0 || elementIndex >= panel.Elements.Count
            || panel.Elements[elementIndex] is not ShapeElement shape || ShapePointEditing.InsertPoint(shape, segment, t) is not { IsValid: true } inserted)
            return -1;
        var (withPoint, index) = inserted.Value;
        var elements = panel.Elements.ToList();
        elements[elementIndex] = withPoint;
        var document = Committed with { Panels = new Dictionary<PanelId, Panel>(Committed.Panels) { [panelId] = panel with { Elements = elements } } };

        BeginGesture();
        _moveBase = document;
        // Let go of the point again if the drag is cancelled and it never gets added (the same hook an Alt+drag's copy uses).
        _duplicateCancelled = () => SelectPoint(-1);
        UpdateGesture(EditResult<PageDocument>.Success(document));
        SelectPoint(index);
        return index;
    }

    // ---------------------------------------------------------------- one-step point edits

    /// <summary>Arrow keys while editing points: moves the selected point (one undo step).</summary>
    public void NudgeSelectedPoint(double dx, double dy)
    {
        if (SelectedPoint is not { } point)
            return;
        var index = SelectedPointIndex;
        ApplyToEditedShape(s => ShapePointEditing.MovePoint(s, index, new Point2D(point.Point.X + dx, point.Point.Y + dy)));
    }

    /// <summary>Makes point <paramref name="index"/> of the shape being edited smooth or sharp.</summary>
    public void SetPointKind(int index, AnchorHandleKind kind)
    {
        if (SelectedShape is not { } shape || index < 0 || index >= shape.Anchors.Count || shape.Anchors[index].HandleKind == AnchorHandleKind.Smooth && kind == AnchorHandleKind.Smooth)
            return;
        SelectPoint(index);
        ApplyToEditedShape(s => ShapePointEditing.SetPointKind(s, index, kind));
    }

    /// <summary>Double-click on a point: smooth becomes sharp, sharp becomes smooth.</summary>
    public void TogglePointKind(int index)
    {
        if (SelectedShape is not { } shape || index < 0 || index >= shape.Anchors.Count)
            return;
        SetPointKind(index, shape.Anchors[index].HandleKind == AnchorHandleKind.Smooth ? AnchorHandleKind.Corner : AnchorHandleKind.Smooth);
    }

    public void DeleteSelectedPoint()
    {
        if (!HasSelectedPoint)
            return;
        var index = SelectedPointIndex;
        ApplyToEditedShape(s => ShapePointEditing.DeletePoint(s, index));
        if (LastError is null)
            SelectPoint(-1);
    }

    public void CloseEditedShape()
    {
        ApplyToEditedShape(ShapePointEditing.Close);
        SelectPoint(-1);
    }

    /// <summary>Cuts the shape open at the selected point - or at point <paramref name="index"/> - selecting the new loose end, ready to pull away.</summary>
    public void OpenEditedShapeAtSelectedPoint() => OpenEditedShapeAt(SelectedPointIndex);

    public void OpenEditedShapeAt(int index)
    {
        if (!IsEditingPoints || SelectedShape is not { } shape || ShapePointEditing.OpenAt(shape, index) is not { IsValid: true } opened)
            return;
        var (line, end) = opened.Value;
        ApplyToEditedShape(_ => EditResult<ShapeElement>.Success(line));
        SelectPoint(end);
    }

    /// <summary>Adds a point <paramref name="t"/> of the way along edge <paramref name="segment"/> of the shape being edited (the right-click menu's Add Point), selected.</summary>
    public void AddPoint(int segment, double t)
    {
        if (!IsEditingPoints || SelectedShape is not { } shape || ShapePointEditing.InsertPoint(shape, segment, t) is not { IsValid: true } inserted)
            return;
        var (withPoint, index) = inserted.Value;
        ApplyToEditedShape(_ => EditResult<ShapeElement>.Success(withPoint));
        SelectPoint(index);
    }

    private void ApplyToEditedShape(Func<ShapeElement, EditResult<ShapeElement>> edit)
    {
        if (!IsEditingPoints || _selectedPanelId is not { } panelId || SelectedShape is not { } before)
            return;
        // Nothing to undo when the point already was what it's being made (a corner made sharp again).
        if (edit(before) is { IsValid: true } after && after.Value.Closed == before.Closed && after.Value.Anchors.SequenceEqual(before.Anchors))
            return;
        Apply(EditShapeInPanel(Working, panelId, _selectedElementIndex, edit));
        RaisePointsChanged();
    }

    private static EditResult<PageDocument> EditShapeInPanel(PageDocument document, PanelId panelId, int elementIndex, Func<ShapeElement, EditResult<ShapeElement>> edit) =>
        EditElementInPanel(document, panelId, elementIndex, e =>
        {
            if (e is not ShapeElement shape)
                return EditResult<PanelElement>.Failure("That isn't a shape.");
            var edited = edit(shape);
            return edited.IsValid ? EditResult<PanelElement>.Success(edited.Value) : EditResult<PanelElement>.Failure(edited.Error!);
        });

    // ---------------------------------------------------------------- the Freeform tool

    /// <summary>
    /// The Freeform tool's points made into a shape in <paramref name="panelId"/> (one undo step):
    /// a line through them, or <paramref name="closed"/> - the first point clicked again - a
    /// shape the fill fills, drawn in the current pen. Like the line, rectangle and ellipse
    /// tools, it then hands back to Select with the new shape selected, unless
    /// <paramref name="select"/> is false (the tool was switched away mid-shape). Returns its
    /// index in the panel, or -1 if the points don't make a shape yet.
    /// </summary>
    public int AddFreeformShape(PanelId panelId, IReadOnlyList<ShapeAnchor> anchors, bool closed, bool select = true)
    {
        var made = ShapePointEditing.Freeform(anchors, closed, _newShapeStyle, _newShapeLayer);
        if (!made.IsValid || !Working.Panels.TryGetValue(panelId, out var panel))
            return -1;
        var shape = made.Value;
        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with { Elements = [.. p.Elements, ElementEditing.KeepReachable(shape, Bounds(panel))] })));
        var index = IndexOfElement(panelId, shape.Id);
        if (index >= 0 && select)
        {
            Tool = PageEditorTool.Select;
            SelectElement(panelId, index);
        }
        return index;
    }
}
