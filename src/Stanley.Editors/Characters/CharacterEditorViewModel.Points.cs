using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>
/// Edit Points and Edit sockets on the picked head or torso of a body built from shapes
/// (docs/shape-characters.md §9.2). Edit Points shows the part's outline points on the stage
/// to drag, add, delete and make smooth or sharp, and the selected point's curve handles -
/// exactly the page's gestures (docs/shape-editing.md), run by <see cref="ShapePointEditing"/>
/// on the outline in figure space and written back into the unit box by
/// <see cref="ModulePointEditing"/>. Edit sockets (the escape hatch) shows where the other parts
/// attach - neck, shoulders, hips, face - as dots to drag. Each drag and each click is one undo
/// step; the stage stands at rest meanwhile (the stage never poses), so the mapping is exact.
/// Picking anything else, or another part, ends either.
/// </summary>
public sealed partial class CharacterEditorViewModel
{
    /// <summary>The part whose points are being edited (only while it's still the picked one).</summary>
    private ModulePart? _editingPointsPart;
    private int _selectedPointIndex = -1;

    /// <summary>The part whose sockets are shown to drag.</summary>
    private ModulePart? _editingSocketsPart;

    // A drag of a point or a curve handle: the outline as it was pressed, the frame it was
    // placed through then and the point it's about. Held for the whole drag, so the body
    // changing under it can't feed back into where the point goes.
    private (ShapeElement Outline, ModuleFrame Frame, int Index, HandleSide? Side)? _pointDrag;

    // The drag is of a point its press added: cancelled, the point goes, so it's let go of too.
    private bool _pointDragAdded;

    // A drag of a socket: its name and the frame it was shown through when pressed.
    private (string Name, ModuleFrame Frame)? _socketDrag;

    /// <summary>Module tab › Points › Delete Point (or Delete on the stage): removes the selected point.</summary>
    public IRelayCommand DeletePointCommand { get; private set; } = null!;

    /// <summary>Module tab › Sockets › Reset Sockets: every socket placed by hand goes back to where the outline puts it.</summary>
    public IRelayCommand ResetSocketsCommand { get; private set; } = null!;

    private void InitializePoints()
    {
        DeletePointCommand = new RelayCommand(DeleteSelectedPoint, () => CanDeleteSelectedPoint);
        ResetSocketsCommand = new RelayCommand(ResetSockets, () => CanResetSockets);
        PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(SelectedModule):
                    OnSelectedModuleChangedForPoints();
                    break;
                case nameof(Working):
                    DropStalePointEditing();
                    break;
                case nameof(PreviewAngle):
                    RaisePointsChanged(); // the outline and sockets as seen from the other side
                    break;
            }
        };
    }

    // ---------------------------------------------------------------- the modes

    /// <summary>A head or torso is picked, so its points (and sockets) can be edited.</summary>
    public bool CanEditPoints => SelectedModuleIsShape && SelectedShape is not null;

    /// <summary>
    /// Edit Points (double-click the part, or the button): the picked head's or torso's outline
    /// shows its points. Esc or Enter is done; so is picking anything else.
    /// </summary>
    public bool IsEditingPoints
    {
        get => _editingPointsPart is { } part && part == _selectedModule && CanEditPoints;
        set
        {
            if (value == IsEditingPoints)
            {
                RaisePointsChanged(); // a toggle that flipped itself needs to hear it didn't
                return;
            }
            if (value && CanEditPoints)
            {
                _editingPointsPart = _selectedModule;
                _editingSocketsPart = null;
                _selectedPointIndex = -1;
                ShowMessage(null);
            }
            else
                StopEditingPoints();
            RaisePointsChanged();
        }
    }

    /// <summary>Edit sockets (the escape hatch): the picked head's or torso's sockets show as dots, to drag where the other parts should attach.</summary>
    public bool IsEditingSockets
    {
        get => _editingSocketsPart is { } part && part == _selectedModule && CanEditPoints;
        set
        {
            if (value == IsEditingSockets)
            {
                RaisePointsChanged();
                return;
            }
            if (value && CanEditPoints)
            {
                _editingSocketsPart = _selectedModule;
                StopEditingPoints();
                ShowMessage(null);
            }
            else
                _editingSocketsPart = null;
            RaisePointsChanged();
        }
    }

    /// <summary>Ends Edit Points (Esc, Enter, a click elsewhere).</summary>
    public void StopEditingPoints()
    {
        _editingPointsPart = null;
        _selectedPointIndex = -1;
    }

    /// <summary>Ends Edit Points and Edit sockets, whichever is on; true if one was.</summary>
    public bool StopEditing()
    {
        if (!IsEditingPoints && !IsEditingSockets)
            return false;
        StopEditingPoints();
        _editingSocketsPart = null;
        RaisePointsChanged();
        return true;
    }

    /// <summary>A different part picked (or none): editing points or sockets ends unless it's still the same part.</summary>
    private void OnSelectedModuleChangedForPoints()
    {
        if (_editingPointsPart is { } editing && editing != _selectedModule)
            StopEditingPoints();
        if (_editingSocketsPart is { } sockets && sockets != _selectedModule)
            _editingSocketsPart = null;
        RaisePointsChanged();
    }

    /// <summary>The body changed (an edit, an undo): let go of a point that isn't there any more.</summary>
    private void DropStalePointEditing()
    {
        if (_selectedPointIndex >= 0 && (EditedOutline is not { } outline || _selectedPointIndex >= outline.Anchors.Count))
            _selectedPointIndex = -1;
        if (_editingPointsPart is not null && !IsEditingPoints)
            StopEditingPoints();
        RaisePointsChanged();
    }

    // ---------------------------------------------------------------- what the stage shows

    /// <summary>The outline being edited, in figure space as the stage stands it (null unless editing points).</summary>
    public ShapeElement? EditedOutline
    {
        get
        {
            if (!IsEditingPoints || Working.Modules is not { } body || _selectedModule is not { } part)
                return null;
            // Asked for by most of the points' properties at once: built once per body.
            if (_outlineCache is not { } cached || !ReferenceEquals(cached.Body, body) || cached.Part != part || cached.Angle != PreviewAngle)
                _outlineCache = cached = (body, part, PreviewAngle, ModulePointEditing.Outline(body, part, PreviewAngle));
            return cached.Outline;
        }
    }

    private (ModularBody Body, ModulePart Part, ViewAngle Angle, ShapeElement? Outline)? _outlineCache;

    /// <summary>The sockets being edited, in figure space as the stage stands it (empty unless editing sockets).</summary>
    public IReadOnlyList<ModuleSocketSpot> EditedSockets =>
        IsEditingSockets && Working.Modules is { } body ? ModulePointEditing.Sockets(body, _selectedModule!.Value, PreviewAngle) : [];

    /// <summary>What the stage draws over the picked part while editing its points or sockets, or null.</summary>
    public ModuleEditMarks? StageEditMarks =>
        IsEditingPoints ? new ModuleEditMarks(EditedOutline, SelectedPointIndex, []) :
        IsEditingSockets ? new ModuleEditMarks(null, -1, EditedSockets) : null;

    // ---------------------------------------------------------------- the selected point

    /// <summary>The selected point of the outline being edited, or -1.</summary>
    public int SelectedPointIndex => IsEditingPoints && EditedOutline is { } outline && _selectedPointIndex < outline.Anchors.Count ? _selectedPointIndex : -1;

    public bool HasSelectedPoint => SelectedPointIndex >= 0;

    public ShapeAnchor? SelectedPoint => HasSelectedPoint ? EditedOutline!.Anchors[SelectedPointIndex] : null;

    /// <summary>Picks a point of the outline being edited (-1 for none): it shows its curve handles, and the point buttons act on it.</summary>
    public void SelectPoint(int index)
    {
        if (!IsEditingPoints)
            return;
        index = index >= 0 && index < EditedOutline!.Anchors.Count ? index : -1;
        if (index == _selectedPointIndex)
            return;
        _selectedPointIndex = index;
        RaisePointsChanged();
    }

    /// <summary>Points › Smooth: the selected point is a smooth curve.</summary>
    public bool IsSelectedPointSmooth
    {
        get => SelectedPoint?.HandleKind == AnchorHandleKind.Smooth;
        set => SetSelectedPointKindFlag(AnchorHandleKind.Smooth, value);
    }

    /// <summary>Points › Corner: the selected point is a sharp corner.</summary>
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

    public bool CanDeleteSelectedPoint => HasSelectedPoint && EditedOutline!.Anchors.Count > ShapePointEditing.MinPoints(closed: true);

    // ---------------------------------------------------------------- dragging points and handles

    /// <summary>Starts dragging point <paramref name="index"/> of the outline being edited (it becomes the selected point). False when nothing's being edited.</summary>
    public bool BeginMovePoint(int index) => BeginPointDrag(index, null);

    /// <summary>Starts dragging one of point <paramref name="index"/>'s curve handles.</summary>
    public bool BeginMoveHandle(int index, HandleSide side) => BeginPointDrag(index, side);

    private bool BeginPointDrag(int index, HandleSide? side)
    {
        if (!IsEditingPoints || Committed.Modules is not { } body || _selectedModule is not { } part
            || ModuleEditing.ShapeOf(body, part) is not { } module || ModularRig.FrameOf(body, part, PreviewAngle) is not { } frame)
            return false;
        var outline = ModulePointEditing.Outline(module, frame);
        if (index < 0 || index >= outline.Anchors.Count)
            return false;
        SelectPoint(index);
        BeginGesture();
        _pointDrag = (outline, frame, index, side);
        _pointDragAdded = false;
        return true;
    }

    /// <summary>Puts the dragged point at <paramref name="to"/> (figure space), its handles following.</summary>
    public void UpdateMovePoint(Point2D to)
    {
        if (_pointDrag is { Side: null } drag)
            EditOutlineInDrag(drag, ShapePointEditing.MovePoint(drag.Outline, drag.Index, to));
    }

    /// <summary>Puts the dragged curve handle at <paramref name="to"/>; on a smooth point the other handle stays in line unless <paramref name="independent"/> (Alt).</summary>
    public void UpdateMoveHandle(Point2D to, bool independent)
    {
        if (_pointDrag is { Side: { } side } drag)
            EditOutlineInDrag(drag, ShapePointEditing.MoveHandle(drag.Outline, drag.Index, side, to, independent));
    }

    /// <summary>
    /// A press on the outline being edited: a new point there, selected, which the drag that
    /// follows moves (<see cref="UpdateMovePoint"/>) - pressed and let go, it's just added. One
    /// undo step either way. Returns the new point's index, or -1.
    /// </summary>
    public int BeginInsertPoint(int segment, double t)
    {
        if (!IsEditingPoints || Committed.Modules is not { } body || _selectedModule is not { } part
            || ModuleEditing.ShapeOf(body, part) is not { } module || ModularRig.FrameOf(body, part, PreviewAngle) is not { } frame
            || ShapePointEditing.InsertPoint(ModulePointEditing.Outline(module, frame), segment, t) is not { IsValid: true } inserted)
            return -1;
        var (outline, index) = inserted.Value;
        BeginGesture();
        _pointDrag = (outline, frame, index, null);
        _pointDragAdded = true;
        EditOutlineInDrag(_pointDrag.Value, EditResult<ShapeElement>.Success(outline));
        _selectedPointIndex = index;
        RaisePointsChanged();
        return index;
    }

    /// <summary>The end of a point, handle or added point's drag: one undo step (or none, cancelled - an added point goes again).</summary>
    public void EndPointDrag(bool commit = true)
    {
        if (_pointDrag is null)
            return;
        _pointDrag = null;
        if (commit)
            CommitGesture();
        else
        {
            CancelGesture();
            if (_pointDragAdded)
                _selectedPointIndex = -1;
            DropStalePointEditing();
        }
        OnPropertyChanged(nameof(CreatureChoices));
    }

    private void EditOutlineInDrag((ShapeElement Outline, ModuleFrame Frame, int Index, HandleSide? Side) drag, EditResult<ShapeElement> edited)
    {
        if (!IsGestureActive || !edited.IsValid || Committed.Modules is not { } baseline || _selectedModule is not { } part)
            return;
        EditModules(ModulePointEditing.SetOutline(baseline, part, PreviewAngle, drag.Frame, edited.Value.Anchors), baseline);
    }

    // ---------------------------------------------------------------- one-step point edits

    /// <summary>Arrow keys while editing points: moves the selected point by (<paramref name="dx"/>, <paramref name="dy"/>) figure units (one undo step).</summary>
    public void NudgeSelectedPoint(double dx, double dy)
    {
        if (SelectedPoint is not { } point)
            return;
        var index = SelectedPointIndex;
        EditOutline(s => ShapePointEditing.MovePoint(s, index, new Point2D(point.Point.X + dx, point.Point.Y + dy)));
    }

    /// <summary>Makes point <paramref name="index"/> of the outline being edited smooth or sharp.</summary>
    public void SetPointKind(int index, AnchorHandleKind kind)
    {
        if (EditedOutline is not { } outline || index < 0 || index >= outline.Anchors.Count || outline.Anchors[index].HandleKind == kind && kind == AnchorHandleKind.Smooth)
            return;
        SelectPoint(index);
        EditOutline(s => ShapePointEditing.SetPointKind(s, index, kind));
    }

    /// <summary>Double-click on a point: smooth becomes sharp, sharp becomes smooth.</summary>
    public void TogglePointKind(int index)
    {
        if (EditedOutline is not { } outline || index < 0 || index >= outline.Anchors.Count)
            return;
        SetPointKind(index, outline.Anchors[index].HandleKind == AnchorHandleKind.Smooth ? AnchorHandleKind.Corner : AnchorHandleKind.Smooth);
    }

    /// <summary>Delete or Backspace while editing points: takes the selected point out - never below three, so it's always a shape.</summary>
    public void DeleteSelectedPoint()
    {
        if (!HasSelectedPoint)
            return;
        var index = SelectedPointIndex;
        if (EditOutline(s => ShapePointEditing.DeletePoint(s, index)))
            SelectPoint(-1);
    }

    /// <summary>
    /// One click's edit of the outline being edited, one undo step - unless it changes nothing
    /// (a corner made a corner again), which would leave an empty one. A refused edit says why.
    /// </summary>
    private bool EditOutline(Func<ShapeElement, EditResult<ShapeElement>> edit)
    {
        if (!IsEditingPoints || Working.Modules is not { } body || _selectedModule is not { } part
            || ModuleEditing.ShapeOf(body, part) is not { } module || ModularRig.FrameOf(body, part, PreviewAngle) is not { } frame)
            return false;
        var outline = ModulePointEditing.Outline(module, frame);
        var edited = edit(outline);
        if (!edited.IsValid)
        {
            ShowMessage(edited.Error);
            return false;
        }
        if (edited.Value.Anchors.SequenceEqual(outline.Anchors))
            return false;
        return EditModules(ModulePointEditing.SetOutline(body, part, PreviewAngle, frame, edited.Value.Anchors));
    }

    // ---------------------------------------------------------------- sockets

    /// <summary>Starts dragging socket <paramref name="name"/> of the picked part. False when sockets aren't being edited.</summary>
    public bool BeginMoveSocket(string name)
    {
        if (!IsEditingSockets || Committed.Modules is not { } body || _selectedModule is not { } part || ModularRig.FrameOf(body, part, PreviewAngle) is not { } frame)
            return false;
        BeginGesture();
        _socketDrag = (name, frame);
        return true;
    }

    /// <summary>Puts the dragged socket at <paramref name="to"/> (figure space): placed by hand from now on.</summary>
    public void UpdateMoveSocket(Point2D to)
    {
        if (_socketDrag is { } drag && IsGestureActive && Committed.Modules is { } baseline && _selectedModule is { } part)
            EditModules(ModulePointEditing.MoveSocket(baseline, part, drag.Frame, drag.Name, to), baseline);
    }

    public void EndSocketDrag(bool commit = true)
    {
        if (_socketDrag is null)
            return;
        _socketDrag = null;
        if (commit)
            CommitGesture();
        else
            CancelGesture();
        OnPropertyChanged(nameof(CreatureChoices));
    }

    /// <summary>Some of the picked part's sockets were placed by hand, so Reset Sockets has something to do.</summary>
    public bool CanResetSockets => _selectedModule is { } part && part.IsShape() && Working.Modules is { } body && ModulePointEditing.HasHandSetSockets(body, part);

    private void ResetSockets()
    {
        if (_selectedModule is { } part && Working.Modules is { } body)
            EditModules(ModulePointEditing.ResetSockets(body, part));
    }

    /// <summary>What a socket is called on the stage: "Neck", "Left shoulder" (the character's own left)...</summary>
    public static string SocketLabel(string socket) => socket switch
    {
        ModuleSockets.Neck => "Neck",
        ModuleSockets.LeftShoulder => "Left shoulder",
        ModuleSockets.RightShoulder => "Right shoulder",
        ModuleSockets.LeftHip => "Left hip",
        ModuleSockets.RightHip => "Right hip",
        ModuleSockets.Face => "Face",
        _ => socket
    };

    // ---------------------------------------------------------------- status bar

    /// <summary>The status bar's hint while editing points or sockets, or null.</summary>
    private string? PointsHint =>
        IsEditingPoints
            ? $"Editing the {SelectedModuleName.ToLowerInvariant()}'s points - drag a point to move it, or the outline to add one there · drag a round handle to bend the curve (Alt bends just that side) · " +
              "double-click a point to make it smooth or sharp · Delete removes the point · Enter or Esc when done."
            : IsEditingSockets
                ? $"Editing the {SelectedModuleName.ToLowerInvariant()}'s sockets - drag a dot to change where the other parts attach (left and right are the character's own) · " +
                  $"Reset Sockets puts them back where the shape puts them · Esc when done."
                : null;

    private void RaisePointsChanged()
    {
        OnPropertyChanged(nameof(CanEditPoints));
        OnPropertyChanged(nameof(IsEditingPoints));
        OnPropertyChanged(nameof(IsEditingSockets));
        OnPropertyChanged(nameof(EditedOutline));
        OnPropertyChanged(nameof(EditedSockets));
        OnPropertyChanged(nameof(StageEditMarks));
        OnPropertyChanged(nameof(SelectedPointIndex));
        OnPropertyChanged(nameof(HasSelectedPoint));
        OnPropertyChanged(nameof(SelectedPoint));
        OnPropertyChanged(nameof(IsSelectedPointSmooth));
        OnPropertyChanged(nameof(IsSelectedPointCorner));
        OnPropertyChanged(nameof(CanDeleteSelectedPoint));
        OnPropertyChanged(nameof(CanResetSockets));
        OnPropertyChanged(nameof(Hint));
        DeletePointCommand?.NotifyCanExecuteChanged(); // null while the constructor is still setting up
        ResetSocketsCommand?.NotifyCanExecuteChanged();
    }
}

/// <summary>What the character editor's stage draws over the picked part while its points or sockets are edited: the outline (figure space) with its selected point, or its sockets.</summary>
public sealed record ModuleEditMarks(ShapeElement? Outline, int SelectedPoint, IReadOnlyList<ModuleSocketSpot> Sockets);
