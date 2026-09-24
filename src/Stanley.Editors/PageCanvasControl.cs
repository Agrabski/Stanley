using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Stanley.Editing;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>
/// The page itself: draws the document at the current zoom and turns pointer input
/// into editor gestures. The document is in millimetres; this control owns the
/// millimetre-to-screen transform (<see cref="Zoom"/> pixels per mm plus a pan offset),
/// so every handle and hit radius is a constant size on screen whatever the zoom.
/// </summary>
public sealed class PageCanvasControl : Control
{
    /// <summary>Pixels per millimetre at 100% - the page on screen at its real printed size on a standard 96 DPI display.</summary>
    public const double ActualSizeZoom = 96.0 / 25.4;
    public const double MinZoom = ActualSizeZoom * 0.1;
    public const double MaxZoom = ActualSizeZoom * 16;

    private const double HitRadiusPx = 9;
    private const double EdgeBandPx = 6;
    private const double SnapDistancePx = 8;
    private const double DragThresholdPx = 4;
    private const double FitPaddingPx = 28;

    private PageEditorViewModel? _viewModel;
    private double _zoom = 2;
    private Vector _offset;
    private bool _fitMode = true;
    private bool _spaceHeld;

    private DragKind _drag = DragKind.None;
    private Point _pressScreen;
    private Point2D _pressPage;
    private PanelId? _dragPanelId;
    private int _dragBubbleIndex = -1;
    private int _dragCharacterIndex = -1;
    private double _dragStartUnit;
    private Limb _dragLimb;
    private TrunkPart _dragTrunk;
    private double _dragStartGroundY;
    private int _dragTailIndex = -1;
    private RectEdges _dragEdges;
    private Rect2D _dragStartBounds;
    private GutterHit? _dragGutter;
    private Vector _panStartOffset;
    private Rect2D? _rubberBand;

    private GutterHit? _hoverGutter;
    private PanelId? _hoverPanelId;

    private readonly Dictionary<StandardCursorType, Cursor> _cursors = new();

    public PageCanvasControl()
    {
        ClipToBounds = true;
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();

        // A character dragged out of the Characters pane is placed where it's dropped.
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var page = ControlToPage(e.GetPosition(this));
        e.DragEffects = _viewModel != null && e.DataTransfer.Contains(CharacterDrag.Format) && PanelAt(page) != null
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (_viewModel is not { } vm || e.DataTransfer.TryGetValue(CharacterDrag.Format) is not { } value
            || !ProjectModel.Ids.CharacterId.TryParse(value, null, out var id))
            return;
        var page = ControlToPage(e.GetPosition(this));
        if (PanelAt(page) is { } panelId)
        {
            vm.InsertCharacter(id, panelId, page);
            Focus();
        }
        e.Handled = true;
    }

    /// <summary>Raised when zoom or pan changes, so the ribbon's zoom readout and any overlay (the inline text editor) can follow.</summary>
    public event Action? ViewChanged;

    public PageEditorViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = value;
            if (_viewModel != null)
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _fitMode = true;
            InvalidateArrange();
            InvalidateVisual();
        }
    }

    // ---------------------------------------------------------------- view transform

    public double Zoom => _zoom;

    /// <summary>Zoom relative to the page's real printed size (100% = actual size).</summary>
    public double ZoomPercent => _zoom / ActualSizeZoom * 100;

    public Point PageToControl(Point2D p) => new(_offset.X + p.X * _zoom, _offset.Y + p.Y * _zoom);

    public Point2D ControlToPage(Point p) => new((p.X - _offset.X) / _zoom, (p.Y - _offset.Y) / _zoom);

    public Rect PageToControl(Rect2D r) => new(PageToControl(new Point2D(r.Left, r.Top)), PageToControl(new Point2D(r.Right, r.Bottom)));

    public void ZoomIn() => ZoomAt(ViewCenter, _zoom * 1.25);

    public void ZoomOut() => ZoomAt(ViewCenter, _zoom / 1.25);

    public void ActualSize() => ZoomAt(ViewCenter, ActualSizeZoom);

    /// <summary>Keeps the page point under <paramref name="anchor"/> fixed while zooming, so Ctrl+scroll zooms towards the cursor.</summary>
    public void ZoomAt(Point anchor, double newZoom)
    {
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        var pagePoint = ControlToPage(anchor);
        _zoom = newZoom;
        _offset = new Vector(anchor.X - pagePoint.X * _zoom, anchor.Y - pagePoint.Y * _zoom);
        _fitMode = false;
        OnViewChanged();
    }

    /// <summary>Whole page visible and centred. Stays in effect (re-fitting as the pane resizes) until the user zooms or pans.</summary>
    public void FitPage()
    {
        _fitMode = true;
        FitTo(Bounds.Size);
        OnViewChanged();
    }

    private Point ViewCenter => new(Bounds.Width / 2, Bounds.Height / 2);

    private void FitTo(Size size)
    {
        if (_viewModel == null || size.Width <= 0 || size.Height <= 0)
            return;

        var page = _viewModel.PageBounds;
        var zoomX = (size.Width - FitPaddingPx * 2) / page.Width;
        var zoomY = (size.Height - FitPaddingPx * 2) / page.Height;
        _zoom = Math.Clamp(Math.Min(zoomX, zoomY), MinZoom, MaxZoom);
        _offset = new Vector(
            (size.Width - page.Width * _zoom) / 2 - page.Left * _zoom,
            (size.Height - page.Height * _zoom) / 2 - page.Top * _zoom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        if (_fitMode)
        {
            FitTo(finalSize);
            OnViewChanged();
        }
        return result;
    }

    private void OnViewChanged()
    {
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PageEditorViewModel.Working) or nameof(PageEditorViewModel.Committed)
            or nameof(PageEditorViewModel.SelectedPanelId) or nameof(PageEditorViewModel.SelectedBubbleIndex)
            or nameof(PageEditorViewModel.ActiveGuides) or nameof(PageEditorViewModel.Grid)
            or nameof(PageEditorViewModel.ShowMarginGuides) or nameof(PageEditorViewModel.Folio)
            or nameof(PageEditorViewModel.SelectedCharacterIndex) or nameof(PageEditorViewModel.CharacterSnapshot))
            InvalidateVisual();
        if (e.PropertyName == nameof(PageEditorViewModel.Tool))
            UpdateCursor(null);
    }

    // ---------------------------------------------------------------- rendering

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_viewModel == null)
            return;

        context.Custom(new PageCanvasDrawOperation(new Rect(Bounds.Size), new PageCanvasScene(
            _viewModel.Working,
            _viewModel.PageBounds,
            _viewModel.Grid,
            _zoom,
            new Point2D(_offset.X, _offset.Y),
            _viewModel.SelectedPanelId,
            _viewModel.SelectedBubbleIndex,
            _drag == DragKind.None ? _hoverPanelId : null,
            _drag == DragKind.DragGutter ? CurrentDragGutter() : _drag == DragKind.None ? _hoverGutter : null,
            _viewModel.ActiveGuides,
            _rubberBand,
            _drag == DragKind.CreateBubble,
            _viewModel.ShowMarginGuides,
            _viewModel.Folio,
            ActualThemeVariant == ThemeVariant.Dark,
            _viewModel.CharacterSnapshot,
            _viewModel.SelectedCharacterIndex,
            _viewModel.SelectedCharacter is { } selectedCharacter ? _viewModel.CharacterBounds(selectedCharacter) : null,
            _viewModel.SelectedCharacter is { } posed ? _viewModel.LimbHandles(posed).Select(h => h.Point).ToList() : null,
            _viewModel.SelectedCharacter is { } bent ? _viewModel.BendHandles(bent).Select(h => h.Point).ToList() : null,
            _viewModel.SelectedCharacter is { } trunk ? _viewModel.TrunkHandles(trunk).Select(h => h.Point).ToList() : null)));
    }

    /// <summary>The gutter being dragged, re-read from the live document so the highlight follows it.</summary>
    private GutterHit? CurrentDragGutter()
    {
        if (_dragGutter == null || _viewModel == null || _dragGutter.Drag.PanelsBefore.Count == 0)
            return _dragGutter;
        var id = _dragGutter.Drag.PanelsBefore[0];
        if (!_viewModel.Working.Panels.ContainsKey(id))
            return _dragGutter;
        var bounds = _viewModel.PanelBounds(id);
        var position = _dragGutter.Drag.Orientation == BoundaryOrientation.Vertical ? bounds.Right : bounds.Bottom;
        return _dragGutter with { Position = position };
    }

    // ---------------------------------------------------------------- hit testing

    private enum HitKind
    {
        None,
        TailTarget,
        TailBase,
        BubbleHandle,
        LimbHandle,
        BendHandle,
        TrunkHandle,
        CharacterHandle,
        PanelCorner,
        Gutter,
        BubbleBody,
        CharacterBody,
        PanelEdge,
        PanelBody
    }

    private sealed record Hit(
        HitKind Kind,
        PanelId? PanelId = null,
        int BubbleIndex = -1,
        int TailIndex = -1,
        int CharacterIndex = -1,
        Limb Limb = Limb.LeftArm,
        TrunkPart Trunk = TrunkPart.Hips,
        RectEdges Edges = RectEdges.None,
        GutterHit? Gutter = null);

    /// <summary>
    /// What's under a point, in priority order: the selected bubble's own handles first
    /// (they sit on top), then the selected character's pose handles (hand/foot, then
    /// elbow/knee, then trunk) and resize handles, panel corners and gutters, then bubble
    /// bodies (so a bubble flush against a panel edge is still grabbable), then characters
    /// (bubbles draw over them), then panel edges and bodies.
    /// </summary>
    private Hit HitTest(Point2D p)
    {
        var vm = _viewModel!;
        var doc = vm.Working;
        var tol = HitRadiusPx / _zoom;

        if (vm.SelectedBubble is { } selected && vm.SelectedPanelId is { } selectedPanel)
        {
            for (var t = selected.Tails.Count - 1; t >= 0; t--)
            {
                if (Dist(selected.Tails[t].Target, p) <= tol)
                    return new Hit(HitKind.TailTarget, selectedPanel, vm.SelectedBubbleIndex, t);
            }
            for (var t = selected.Tails.Count - 1; t >= 0; t--)
            {
                if (Dist(AnchorRing.PointAt(selected.Shape.Anchors, selected.Tails[t].AttachmentT), p) <= tol)
                    return new Hit(HitKind.TailBase, selectedPanel, vm.SelectedBubbleIndex, t);
            }
            var handleEdges = HitHandles(AnchorRing.BoundingBox(selected.Shape.Anchors), p, tol, includeMidpoints: true);
            if (handleEdges != RectEdges.None)
                return new Hit(HitKind.BubbleHandle, selectedPanel, vm.SelectedBubbleIndex, Edges: handleEdges);
        }

        if (vm.SelectedCharacter is { } character && vm.SelectedPanelId is { } characterPanel)
        {
            foreach (var (limb, point) in vm.LimbHandles(character))
            {
                if (Dist(point, p) <= tol)
                    return new Hit(HitKind.LimbHandle, characterPanel, CharacterIndex: vm.SelectedCharacterIndex, Limb: limb);
            }
            foreach (var (limb, point) in vm.BendHandles(character))
            {
                if (Dist(point, p) <= tol)
                    return new Hit(HitKind.BendHandle, characterPanel, CharacterIndex: vm.SelectedCharacterIndex, Limb: limb);
            }
            foreach (var (part, point) in vm.TrunkHandles(character))
            {
                if (Dist(point, p) <= tol)
                    return new Hit(HitKind.TrunkHandle, characterPanel, CharacterIndex: vm.SelectedCharacterIndex, Trunk: part);
            }
            var box = vm.CharacterBounds(character);
            if (Dist(new Point2D(box.Left, box.Top), p) <= tol)
                return new Hit(HitKind.CharacterHandle, characterPanel, CharacterIndex: vm.SelectedCharacterIndex, Edges: RectEdges.Left | RectEdges.Top);
            if (Dist(new Point2D(box.Right, box.Top), p) <= tol)
                return new Hit(HitKind.CharacterHandle, characterPanel, CharacterIndex: vm.SelectedCharacterIndex, Edges: RectEdges.Right | RectEdges.Top);
        }

        foreach (var id in doc.PanelOrder)
        {
            if (!doc.Panels.TryGetValue(id, out var panel))
                continue;
            var edges = HitHandles(AnchorRing.BoundingBox(panel.Shape.Anchors), p, tol, includeMidpoints: false);
            if (edges != RectEdges.None)
                return new Hit(HitKind.PanelCorner, id, Edges: edges);
        }

        if (PanelGutters.FindAt(doc.Panels.Values, p, EdgeBandPx / _zoom) is { } gutter)
            return new Hit(HitKind.Gutter, Gutter: gutter);

        for (var i = doc.PanelOrder.Count - 1; i >= 0; i--)
        {
            var id = doc.PanelOrder[i];
            if (!doc.Panels.TryGetValue(id, out var panel) || !Contains(AnchorRing.BoundingBox(panel.Shape.Anchors), p))
                continue;
            for (var b = panel.Bubbles.Count - 1; b >= 0; b--)
            {
                using var path = BubbleRenderer.BuildRenderPath(panel.Bubbles[b], PageCanvasDrawOperation.TailBaseHalfWidthMm);
                if (path.Contains((float)p.X, (float)p.Y))
                    return new Hit(HitKind.BubbleBody, id, b);
            }
        }

        for (var i = doc.PanelOrder.Count - 1; i >= 0; i--)
        {
            var id = doc.PanelOrder[i];
            if (!doc.Panels.TryGetValue(id, out var panel) || !Contains(AnchorRing.BoundingBox(panel.Shape.Anchors), p))
                continue;
            for (var c = panel.CharacterInstances.Count - 1; c >= 0; c--)
            {
                if (HitsCharacter(panel.CharacterInstances[c], p))
                    return new Hit(HitKind.CharacterBody, id, CharacterIndex: c);
            }
        }

        var band = EdgeBandPx / _zoom;
        for (var i = doc.PanelOrder.Count - 1; i >= 0; i--)
        {
            var id = doc.PanelOrder[i];
            if (!doc.Panels.TryGetValue(id, out var panel))
                continue;
            var bounds = AnchorRing.BoundingBox(panel.Shape.Anchors);
            if (!Contains(bounds, p))
                continue;

            var edges = RectEdges.None;
            if (p.X - bounds.Left <= band) edges |= RectEdges.Left;
            if (bounds.Right - p.X <= band) edges |= RectEdges.Right;
            if (p.Y - bounds.Top <= band) edges |= RectEdges.Top;
            if (bounds.Bottom - p.Y <= band) edges |= RectEdges.Bottom;
            return edges != RectEdges.None ? new Hit(HitKind.PanelEdge, id, Edges: edges) : new Hit(HitKind.PanelBody, id);
        }

        return new Hit(HitKind.None);
    }

    /// <summary>The figure's actual silhouette, not its box - characters stand close together and overlap.</summary>
    private bool HitsCharacter(ProjectModel.Issues.CharacterInstance instance, Point2D p)
    {
        var vm = _viewModel!;
        if (!Contains(vm.CharacterBounds(instance), p))
            return false;
        if (!vm.CharacterSnapshot.TryGetValue(instance.CharacterId, out var character))
            return true; // a missing character's placeholder box
        using var path = CharacterRenderers.Default.BuildSilhouette(character, instance.Placement, instance.Pose.ViewAngle);
        return path.Contains((float)p.X, (float)p.Y);
    }

    private PanelId? PanelAt(Point2D p)
    {
        var doc = _viewModel!.Working;
        for (var i = doc.PanelOrder.Count - 1; i >= 0; i--)
        {
            if (doc.Panels.TryGetValue(doc.PanelOrder[i], out var panel) && Contains(AnchorRing.BoundingBox(panel.Shape.Anchors), p))
                return doc.PanelOrder[i];
        }
        return null;
    }

    /// <summary>Which edges a handle at <paramref name="p"/> drags: two for a corner, one for an edge midpoint.</summary>
    private static RectEdges HitHandles(Rect2D r, Point2D p, double tol, bool includeMidpoints)
    {
        (Point2D Point, RectEdges Edges)[] handles =
        [
            (new(r.Left, r.Top), RectEdges.Left | RectEdges.Top),
            (new(r.Right, r.Top), RectEdges.Right | RectEdges.Top),
            (new(r.Right, r.Bottom), RectEdges.Right | RectEdges.Bottom),
            (new(r.Left, r.Bottom), RectEdges.Left | RectEdges.Bottom),
        ];
        foreach (var (point, edges) in handles)
        {
            if (Dist(point, p) <= tol)
                return edges;
        }

        if (includeMidpoints)
        {
            (Point2D Point, RectEdges Edges)[] mids =
            [
                (new(r.MidX, r.Top), RectEdges.Top),
                (new(r.Right, r.MidY), RectEdges.Right),
                (new(r.MidX, r.Bottom), RectEdges.Bottom),
                (new(r.Left, r.MidY), RectEdges.Left),
            ];
            foreach (var (point, edges) in mids)
            {
                if (Dist(point, p) <= tol)
                    return edges;
            }
        }
        return RectEdges.None;
    }

    // ---------------------------------------------------------------- pointer input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_viewModel == null || _drag != DragKind.None)
            return;

        var point = e.GetCurrentPoint(this);
        var pos = point.Position;
        var page = ControlToPage(pos);
        _pressScreen = pos;
        _pressPage = page;
        e.Handled = true;

        if (point.Properties.IsMiddleButtonPressed || _spaceHeld || (_viewModel.Tool == PageEditorTool.Pan && point.Properties.IsLeftButtonPressed))
        {
            StartDrag(e, DragKind.Pan);
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            ShowContextMenu(page);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        switch (_viewModel.Tool)
        {
            case PageEditorTool.Panel:
                _viewModel.ClearSelection();
                _viewModel.BeginCreatePanel();
                _rubberBand = new Rect2D(page.X, page.Y, 0, 0);
                StartDrag(e, DragKind.CreatePanel);
                return;

            case PageEditorTool.Bubble:
                if (PanelAt(page) is { } targetPanel)
                {
                    _dragPanelId = targetPanel;
                    StartDrag(e, DragKind.CreateBubble);
                }
                return;
        }

        PressWithSelectTool(e, page);
    }

    private void PressWithSelectTool(PointerPressedEventArgs e, Point2D page)
    {
        var vm = _viewModel!;
        var hit = HitTest(page);

        if (e.ClickCount >= 2)
        {
            switch (hit.Kind)
            {
                case HitKind.BubbleBody or HitKind.BubbleHandle when hit.PanelId is { } bubblePanel:
                    vm.Select(bubblePanel, hit.BubbleIndex);
                    _viewModel!.RequestTextEdit(bubblePanel, hit.BubbleIndex);
                    return;
                case HitKind.CharacterBody when hit.PanelId is { } characterPanel:
                    vm.SelectCharacter(characterPanel, hit.CharacterIndex);
                    if (vm.EditCharacterCommand.CanExecute(null))
                        vm.EditCharacterCommand.Execute(null);
                    return;
                case HitKind.PanelBody when hit.PanelId is { } emptyPanel:
                    var index = vm.CreateBubble(emptyPanel, page);
                    if (index >= 0)
                        _viewModel!.RequestTextEdit(emptyPanel, index);
                    return;
            }
        }

        _dragPanelId = hit.PanelId;
        _dragBubbleIndex = hit.BubbleIndex;
        _dragTailIndex = hit.TailIndex;
        _dragCharacterIndex = hit.CharacterIndex;
        _dragEdges = hit.Edges;

        switch (hit.Kind)
        {
            case HitKind.TailTarget:
                vm.BeginMoveBubbleTail(hit.PanelId!.Value, hit.BubbleIndex, hit.TailIndex);
                StartDrag(e, DragKind.MoveTailTarget);
                break;

            case HitKind.TailBase:
                vm.BeginSlideBubbleTailAttachment(hit.PanelId!.Value, hit.BubbleIndex, hit.TailIndex);
                StartDrag(e, DragKind.SlideTailAttachment);
                break;

            case HitKind.BubbleHandle:
                _dragStartBounds = AnchorRing.BoundingBox(vm.Working.Panels[hit.PanelId!.Value].Bubbles[hit.BubbleIndex].Shape.Anchors);
                vm.BeginResizeBubble(hit.PanelId.Value, hit.BubbleIndex);
                StartDrag(e, DragKind.ResizeBubble);
                break;

            case HitKind.PanelCorner or HitKind.PanelEdge:
                vm.Select(hit.PanelId);
                _dragStartBounds = vm.PanelBounds(hit.PanelId!.Value);
                vm.BeginResizePanel(hit.PanelId.Value);
                StartDrag(e, DragKind.ResizePanel);
                break;

            case HitKind.Gutter:
                _dragGutter = hit.Gutter;
                vm.BeginDragBoundary(hit.Gutter!.Drag);
                StartDrag(e, DragKind.DragGutter);
                break;

            case HitKind.BubbleBody:
                vm.Select(hit.PanelId, hit.BubbleIndex);
                StartDrag(e, DragKind.PendingMoveBubble);
                break;

            case HitKind.LimbHandle:
                _dragLimb = hit.Limb;
                vm.BeginPoseLimb(hit.PanelId!.Value, hit.CharacterIndex, hit.Limb);
                StartDrag(e, DragKind.PoseLimb);
                break;

            case HitKind.BendHandle:
                _dragLimb = hit.Limb;
                vm.BeginPoseBend(hit.PanelId!.Value, hit.CharacterIndex, hit.Limb);
                StartDrag(e, DragKind.PoseBend);
                break;

            case HitKind.TrunkHandle:
                _dragTrunk = hit.Trunk;
                vm.BeginPoseTrunk(hit.PanelId!.Value, hit.CharacterIndex, hit.Trunk);
                StartDrag(e, DragKind.PoseTrunk);
                break;

            case HitKind.CharacterHandle:
                var placement = vm.Working.Panels[hit.PanelId!.Value].CharacterInstances[hit.CharacterIndex].Placement;
                _dragStartUnit = placement.UnitHeightMm;
                _dragStartGroundY = placement.Ground.Y;
                _dragStartBounds = vm.CharacterBounds(vm.Working.Panels[hit.PanelId.Value].CharacterInstances[hit.CharacterIndex]);
                vm.BeginResizeCharacter(hit.PanelId.Value, hit.CharacterIndex);
                StartDrag(e, DragKind.ResizeCharacter);
                break;

            case HitKind.CharacterBody:
                vm.SelectCharacter(hit.PanelId!.Value, hit.CharacterIndex);
                StartDrag(e, DragKind.PendingMoveCharacter);
                break;

            case HitKind.PanelBody:
                vm.Select(hit.PanelId);
                StartDrag(e, DragKind.PendingMovePanel);
                break;

            default:
                // Empty pasteboard: deselect, and let a drag pan the view.
                vm.ClearSelection();
                StartDrag(e, DragKind.Pan);
                break;
        }
    }

    private void StartDrag(PointerPressedEventArgs e, DragKind kind)
    {
        _drag = kind;
        _panStartOffset = _offset;
        e.Pointer.Capture(this);
        UpdateCursor(null);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_viewModel == null)
            return;

        var pos = e.GetPosition(this);
        var page = ControlToPage(pos);

        if (_drag == DragKind.None)
        {
            UpdateHover(page);
            return;
        }

        var dx = page.X - _pressPage.X;
        var dy = page.Y - _pressPage.Y;
        var snap = e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 0 : SnapDistancePx / _zoom;
        var beyondThreshold = Math.Abs(pos.X - _pressScreen.X) > DragThresholdPx || Math.Abs(pos.Y - _pressScreen.Y) > DragThresholdPx;

        switch (_drag)
        {
            case DragKind.PendingMoveBubble when beyondThreshold:
                _viewModel.BeginMoveBubble(_dragPanelId!.Value, _dragBubbleIndex);
                _drag = DragKind.MoveBubble;
                goto case DragKind.MoveBubble;

            case DragKind.MoveBubble:
                _viewModel.UpdateMoveBubble(_dragPanelId!.Value, _dragBubbleIndex, dx, dy);
                break;

            case DragKind.PendingMoveCharacter when beyondThreshold:
                _viewModel.BeginMoveCharacter(_dragPanelId!.Value, _dragCharacterIndex);
                _drag = DragKind.MoveCharacter;
                goto case DragKind.MoveCharacter;

            case DragKind.MoveCharacter:
                _viewModel.UpdateMoveCharacter(_dragPanelId!.Value, _dragCharacterIndex, dx, dy, snap);
                break;

            case DragKind.PoseLimb:
                _viewModel.UpdatePoseLimb(_dragPanelId!.Value, _dragCharacterIndex, _dragLimb, page);
                break;

            case DragKind.PoseBend:
                _viewModel.UpdatePoseBend(_dragPanelId!.Value, _dragCharacterIndex, _dragLimb, page);
                break;

            case DragKind.PoseTrunk:
                _viewModel.UpdatePoseTrunk(_dragPanelId!.Value, _dragCharacterIndex, _dragTrunk, page, _pressPage);
                break;

            case DragKind.ResizeCharacter:
                // Scales about the feet: the handle's height above the ground sets the size.
                var startHeight = _dragStartGroundY - _dragStartBounds.Top;
                var newHeight = _dragStartGroundY - (_dragStartBounds.Top + dy);
                if (startHeight > 1e-6 && newHeight > 1)
                    _viewModel.UpdateResizeCharacter(_dragPanelId!.Value, _dragCharacterIndex, _dragStartUnit * newHeight / startHeight,
                        together: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                break;

            case DragKind.PendingMovePanel when beyondThreshold:
                _viewModel.BeginMovePanel(_dragPanelId!.Value);
                _drag = DragKind.MovePanel;
                goto case DragKind.MovePanel;

            case DragKind.MovePanel:
                _viewModel.UpdateMovePanel(_dragPanelId!.Value, dx, dy, snap);
                break;

            case DragKind.Pan:
                _offset = _panStartOffset + (pos - _pressScreen);
                _fitMode = false;
                OnViewChanged();
                break;

            case DragKind.ResizePanel:
                _viewModel.UpdateResizePanel(_dragPanelId!.Value, MoveEdges(_dragStartBounds, _dragEdges, dx, dy), _dragEdges, snap);
                break;

            case DragKind.ResizeBubble:
                _viewModel.UpdateResizeBubble(_dragPanelId!.Value, _dragBubbleIndex, MoveEdges(_dragStartBounds, _dragEdges, dx, dy));
                break;

            case DragKind.MoveTailTarget:
                _viewModel.UpdateMoveBubbleTail(_dragPanelId!.Value, _dragBubbleIndex, _dragTailIndex, page);
                break;

            case DragKind.SlideTailAttachment:
                _viewModel.UpdateSlideBubbleTailAttachment(_dragPanelId!.Value, _dragBubbleIndex, _dragTailIndex, page);
                break;

            case DragKind.DragGutter:
                var along = _dragGutter!.Drag.Orientation == BoundaryOrientation.Vertical ? dx : dy;
                _viewModel.UpdateDragBoundary(_dragGutter.Drag, _dragGutter.Position + along, snap);
                break;

            case DragKind.CreatePanel:
                _rubberBand = RectFromPoints(_pressPage, page);
                _viewModel.UpdateCreatePanel(_rubberBand.Value, snap);
                break;

            case DragKind.CreateBubble when beyondThreshold:
                _rubberBand = RectFromPoints(_pressPage, page);
                break;
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == DragKind.None || _viewModel == null)
            return;

        FinishDrag(commit: true);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag != DragKind.None)
            FinishDrag(commit: true);
    }

    private void FinishDrag(bool commit)
    {
        var vm = _viewModel!;
        var kind = _drag;
        _drag = DragKind.None;

        switch (kind)
        {
            case DragKind.CreatePanel:
                if (commit)
                    vm.CommitCreatePanel();
                else
                    vm.EndGesture(commit: false);
                break;

            case DragKind.CreateBubble when commit && _dragPanelId is { } panelId:
                var band = _rubberBand;
                var index = band is { Width: >= BubbleEditing.MinWidthMm, Height: >= BubbleEditing.MinHeightMm }
                    ? vm.CreateBubble(panelId, new Point2D(band.Value.MidX, band.Value.MidY), band)
                    : vm.CreateBubble(panelId, _pressPage);
                if (index >= 0)
                {
                    vm.Tool = PageEditorTool.Select;
                    _viewModel!.RequestTextEdit(panelId, index);
                }
                break;

            case DragKind.MoveBubble or DragKind.MovePanel or DragKind.ResizePanel or DragKind.ResizeBubble
                or DragKind.MoveTailTarget or DragKind.SlideTailAttachment or DragKind.DragGutter
                or DragKind.MoveCharacter or DragKind.ResizeCharacter or DragKind.PoseLimb or DragKind.PoseBend or DragKind.PoseTrunk:
                vm.EndGesture(commit);
                break;
        }

        _dragPanelId = null;
        _dragBubbleIndex = -1;
        _dragTailIndex = -1;
        _dragCharacterIndex = -1;
        _dragEdges = RectEdges.None;
        _dragGutter = null;
        _rubberBand = null;
        UpdateCursor(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos = e.GetPosition(this);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ZoomAt(pos, _zoom * Math.Pow(1.15, e.Delta.Y));
        }
        else
        {
            const double step = 48;
            var delta = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? new Vector(e.Delta.Y, e.Delta.X) : e.Delta;
            _offset += delta * step;
            _fitMode = false;
            OnViewChanged();
        }
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverGutter != null || _hoverPanelId != null)
        {
            _hoverGutter = null;
            _hoverPanelId = null;
            InvalidateVisual();
        }
    }

    private void UpdateHover(Point2D page)
    {
        var vm = _viewModel!;
        Hit? hit = vm.Tool == PageEditorTool.Select && !_spaceHeld ? HitTest(page) : null;
        var gutter = hit?.Gutter;
        var panel = vm.Tool is PageEditorTool.Select or PageEditorTool.Bubble ? PanelAt(page) : null;
        if (!Equals(gutter, _hoverGutter) || !Equals(panel, _hoverPanelId))
        {
            _hoverGutter = gutter;
            _hoverPanelId = panel;
            InvalidateVisual();
        }
        UpdateCursor(hit);
    }

    // ---------------------------------------------------------------- keyboard

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var vm = _viewModel;
        if (vm == null)
            return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 5.0 : 1.0;

        switch (e.Key)
        {
            case Key.Escape when _drag != DragKind.None:
                FinishDrag(commit: false);
                break;
            case Key.Escape when vm.Tool != PageEditorTool.Select:
                vm.Tool = PageEditorTool.Select;
                break;
            case Key.Escape:
                vm.ClearSelection();
                break;
            case Key.Delete or Key.Back when _drag == DragKind.None:
                vm.DeleteSelection();
                break;
            case Key.Enter or Key.F2 when vm.HasSelectedBubble:
                vm.RequestTextEdit(vm.SelectedPanelId!.Value, vm.SelectedBubbleIndex);
                break;
            case Key.Left when _drag == DragKind.None:
                vm.NudgeSelection(-step, 0);
                break;
            case Key.Right when _drag == DragKind.None:
                vm.NudgeSelection(step, 0);
                break;
            case Key.Up when _drag == DragKind.None:
                vm.NudgeSelection(0, -step);
                break;
            case Key.Down when _drag == DragKind.None:
                vm.NudgeSelection(0, step);
                break;
            case Key.Space:
                _spaceHeld = true;
                UpdateCursor(null);
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                FitPage();
                break;
            case Key.D1 or Key.NumPad1 when ctrl:
                ActualSize();
                break;
            case Key.OemPlus or Key.Add when ctrl:
                ZoomIn();
                break;
            case Key.OemMinus or Key.Subtract when ctrl:
                ZoomOut();
                break;
            case Key.V when !ctrl:
                vm.Tool = PageEditorTool.Select;
                break;
            case Key.P when !ctrl:
                vm.Tool = PageEditorTool.Panel;
                break;
            case Key.B when !ctrl:
                vm.Tool = PageEditorTool.Bubble;
                break;
            case Key.H when !ctrl:
                vm.Tool = PageEditorTool.Pan;
                break;
            case Key.S when !ctrl && vm.HasSelectedCharacter:
                vm.IsSelectedCharacterSide = true;
                break;
            case Key.F when !ctrl && vm.HasSelectedCharacter:
                vm.IsSelectedCharacterFront = true;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _spaceHeld = false;
            UpdateCursor(null);
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- context menu

    private void ShowContextMenu(Point2D page)
    {
        var vm = _viewModel!;
        var hit = HitTest(page);
        var items = new List<Control>();

        if (hit.Kind is HitKind.BubbleBody or HitKind.BubbleHandle or HitKind.TailBase or HitKind.TailTarget && hit.PanelId is { } bubblePanel)
        {
            var index = hit.BubbleIndex;
            vm.Select(bubblePanel, index);
            items.Add(Item("Edit text", () => _viewModel!.RequestTextEdit(bubblePanel, index), "Enter"));
            var styles = new MenuItem { Header = "Style" };
            styles.ItemsSource = Enum.GetValues<BubbleStylePreset>()
                .Select(style => Item(style.ToString(), () => vm.SetBubbleStyle(bubblePanel, index, style)))
                .ToList();
            items.Add(styles);
            items.Add(new Separator());
            items.Add(Item("Add tail", () => vm.AddBubbleTail(bubblePanel, index)));
            if (hit.Kind == HitKind.TailTarget || hit.Kind == HitKind.TailBase)
            {
                var tail = hit.TailIndex;
                items.Add(Item("Remove this tail", () => vm.RemoveBubbleTail(bubblePanel, index, tail)));
            }
            items.Add(new Separator());
            items.Add(Item("Bring to front", () => vm.BringBubbleToFront(bubblePanel, index)));
            items.Add(Item("Send to back", () => vm.SendBubbleToBack(bubblePanel, index)));
            items.Add(new Separator());
            items.Add(Item("Delete bubble", () => vm.DeleteBubble(bubblePanel, index), "Del"));
        }
        else if (hit.Kind is HitKind.CharacterBody or HitKind.CharacterHandle && hit.PanelId is { } characterPanel)
        {
            var index = hit.CharacterIndex;
            vm.SelectCharacter(characterPanel, index);
            if (vm.EditCharacterCommand.CanExecute(null))
                items.Add(Item("Edit character…", () => vm.EditCharacterCommand.Execute(null)));
            var side = vm.SelectedCharacterView == ProjectModel.Geometry.ViewAngle.Profile;
            items.Add(Item(side ? "Front view" : "Side view",
                () => vm.SetCharacterView(characterPanel, index, side ? ProjectModel.Geometry.ViewAngle.Front : ProjectModel.Geometry.ViewAngle.Profile), side ? "F" : "S"));
            items.Add(Item(side ? "Face the other way" : "Flip", () => vm.FlipCharacter(characterPanel, index)));
            items.Add(Item("Bigger", () => vm.ScaleCharacter(characterPanel, index, 1.1)));
            items.Add(Item("Smaller", () => vm.ScaleCharacter(characterPanel, index, 1 / 1.1)));
            if (vm.SelectedCharacterHasOddScale)
                items.Add(Item("Match size to panel", () => vm.MatchCharacterSize(characterPanel, index)));
            var poses = new MenuItem { Header = "Pose" };
            poses.ItemsSource = PosePresets.All.Select(preset => Item(preset.Name, () => vm.ApplyPosePreset(characterPanel, index, preset))).ToList();
            items.Add(poses);
            if (vm.SelectedCharacterIsPosed)
            {
                items.Add(Item("Mirror pose", () => vm.MirrorCharacterPose(characterPanel, index)));
                items.Add(Item("Reset pose", () => vm.ResetCharacterPose(characterPanel, index)));
            }
            items.Add(new Separator());
            items.Add(Item("Bring to front", () => vm.ReorderCharacter(characterPanel, index, toFront: true)));
            items.Add(Item("Send to back", () => vm.ReorderCharacter(characterPanel, index, toFront: false)));
            items.Add(new Separator());
            items.Add(Item("Remove from panel", () => vm.DeleteCharacter(characterPanel, index), "Del"));
        }
        else if (PanelAt(page) is { } panelId)
        {
            vm.Select(panelId);
            var at = page;
            items.Add(Item("Add bubble here", () =>
            {
                var index = vm.CreateBubble(panelId, at);
                if (index >= 0)
                    _viewModel!.RequestTextEdit(panelId, index);
            }));
            items.Add(new Separator());
            items.Add(Item("Split side by side", () => vm.SplitPanel(panelId, BoundaryOrientation.Vertical, 0.5)));
            items.Add(Item("Split top and bottom", () => vm.SplitPanel(panelId, BoundaryOrientation.Horizontal, 0.5)));
            items.Add(new Separator());
            items.Add(Item("Delete panel", () => vm.DeletePanel(panelId), "Del"));
        }
        else
        {
            vm.ClearSelection();
            var layouts = new MenuItem { Header = "Page layout" };
            layouts.ItemsSource = PanelLayoutPresets.All.Select(preset => Item(preset.Name, () => vm.ApplyLayoutPreset(preset))).ToList();
            items.Add(layouts);
            items.Add(Item("Fit page", FitPage, "Ctrl+0"));
            items.Add(Item("Actual size", ActualSize, "Ctrl+1"));
        }

        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(this);
    }

    private static MenuItem Item(string header, Action action, string? gesture = null)
    {
        var item = new MenuItem { Header = header };
        if (gesture != null)
            item.InputGesture = KeyGesture.Parse(gesture == "Del" ? "Delete" : gesture);
        item.Click += (_, _) => action();
        return item;
    }

    // ---------------------------------------------------------------- cursors

    private void UpdateCursor(Hit? hit)
    {
        var vm = _viewModel;
        if (vm == null)
            return;

        StandardCursorType type;
        if (_drag == DragKind.Pan || _spaceHeld || vm.Tool == PageEditorTool.Pan)
            type = StandardCursorType.Hand;
        else if (vm.Tool is PageEditorTool.Panel or PageEditorTool.Bubble)
            type = StandardCursorType.Cross;
        else if (hit == null)
            type = _drag is DragKind.MoveBubble or DragKind.MovePanel or DragKind.MoveCharacter ? StandardCursorType.SizeAll : StandardCursorType.Arrow;
        else
            type = hit.Kind switch
            {
                HitKind.TailTarget or HitKind.TailBase or HitKind.LimbHandle or HitKind.BendHandle or HitKind.TrunkHandle => StandardCursorType.Hand,
                HitKind.BubbleHandle or HitKind.PanelCorner or HitKind.PanelEdge or HitKind.CharacterHandle => EdgeCursor(hit.Edges),
                HitKind.Gutter => hit.Gutter!.Drag.Orientation == BoundaryOrientation.Vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth,
                HitKind.BubbleBody or HitKind.PanelBody or HitKind.CharacterBody => StandardCursorType.SizeAll,
                _ => StandardCursorType.Arrow
            };

        if (!_cursors.TryGetValue(type, out var cursor))
        {
            try
            {
                cursor = new Cursor(type);
            }
            catch (Exception)
            {
                return; // no cursor support on this platform (e.g. headless) - purely cosmetic
            }
            _cursors[type] = cursor;
        }
        Cursor = cursor;
    }

    private static StandardCursorType EdgeCursor(RectEdges edges) => edges switch
    {
        RectEdges.Left | RectEdges.Top => StandardCursorType.TopLeftCorner,
        RectEdges.Right | RectEdges.Top => StandardCursorType.TopRightCorner,
        RectEdges.Right | RectEdges.Bottom => StandardCursorType.BottomRightCorner,
        RectEdges.Left | RectEdges.Bottom => StandardCursorType.BottomLeftCorner,
        RectEdges.Left or RectEdges.Right => StandardCursorType.SizeWestEast,
        RectEdges.Top or RectEdges.Bottom => StandardCursorType.SizeNorthSouth,
        _ => StandardCursorType.SizeAll
    };

    // ---------------------------------------------------------------- geometry helpers

    /// <summary>Moves only the grabbed edges by the drag delta - the grabbed corner follows the pointer without jumping to it.</summary>
    private static Rect2D MoveEdges(Rect2D start, RectEdges edges, double dx, double dy)
    {
        var left = start.Left + (edges.HasFlag(RectEdges.Left) ? dx : 0);
        var right = start.Right + (edges.HasFlag(RectEdges.Right) ? dx : 0);
        var top = start.Top + (edges.HasFlag(RectEdges.Top) ? dy : 0);
        var bottom = start.Bottom + (edges.HasFlag(RectEdges.Bottom) ? dy : 0);
        if (right - left < 1)
        {
            if (edges.HasFlag(RectEdges.Left)) left = right - 1;
            else right = left + 1;
        }
        if (bottom - top < 1)
        {
            if (edges.HasFlag(RectEdges.Top)) top = bottom - 1;
            else bottom = top + 1;
        }
        return Rect2D.FromEdges(left, top, right, bottom);
    }

    private static Rect2D RectFromPoints(Point2D a, Point2D b) =>
        Rect2D.FromEdges(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private static bool Contains(Rect2D r, Point2D p) => p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;

    private static double Dist(Point2D a, Point2D b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private enum DragKind
    {
        None,
        Pan,
        PendingMovePanel,
        MovePanel,
        PendingMoveBubble,
        MoveBubble,
        ResizePanel,
        ResizeBubble,
        MoveTailTarget,
        SlideTailAttachment,
        DragGutter,
        CreatePanel,
        CreateBubble,
        PendingMoveCharacter,
        MoveCharacter,
        ResizeCharacter,
        PoseLimb,
        PoseBend,
        PoseTrunk
    }
}
