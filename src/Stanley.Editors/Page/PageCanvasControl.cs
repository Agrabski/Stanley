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

    /// <summary>How far (screen px) a smoothed pen stroke may stray from the pointer's path, and how near its start (px) it must end to close into a shape.</summary>
    private const double FreehandTolerancePx = 0.8;
    private const double CloseDistancePx = 12;

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
    private int _dragElementIndex = -1;
    private readonly List<Point2D> _trail = [];
    private bool _dragMoved;
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
    private Hit? _hoverHit;
    private bool _altHeld;

    /// <summary>The drag is moving a copy (Alt+drag) rather than the thing itself.</summary>
    private bool _duplicating;

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

    /// <summary>The bubble the inline text editor is open over, if any: the canvas leaves its lettering and handles off so the editor sits on a clean bubble.</summary>
    public EditingBubble? EditingBubble
    {
        get => _editingBubble;
        set
        {
            if (_editingBubble == value)
                return;
            _editingBubble = value;
            InvalidateVisual();
        }
    }

    private EditingBubble? _editingBubble;

    /// <summary>The text element the inline text editor is open over, if any: the canvas leaves its lettering (not its box) and handles off.</summary>
    public ElementId? EditingText
    {
        get => _editingText;
        set
        {
            if (_editingText == value)
                return;
            _editingText = value;
            InvalidateVisual();
        }
    }

    private ElementId? _editingText;

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
            or nameof(PageEditorViewModel.SelectedCharacterIndex) or nameof(PageEditorViewModel.CharacterSnapshot)
            or nameof(PageEditorViewModel.IssueLooks) or nameof(PageEditorViewModel.SelectedElementIndex)
            or nameof(PageEditorViewModel.PictureSnapshot) or nameof(PageEditorViewModel.Fields)
            or nameof(PageEditorViewModel.SelectionCount))
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
            _viewModel.IssueLooks,
            _viewModel.SelectedCharacterIndex,
            _viewModel.SelectedCharacter is { } selectedCharacter ? _viewModel.CharacterBounds(selectedCharacter) : null,
            _viewModel.SelectedCharacter is { } posed ? _viewModel.LimbHandles(posed).Select(h => h.Point).ToList() : null,
            _viewModel.SelectedCharacter is { } bent ? _viewModel.BendHandles(bent).Select(h => h.Point).ToList() : null,
            _viewModel.SelectedCharacter is { } trunk ? _viewModel.TrunkHandles(trunk).Select(h => h.Point).ToList() : null,
            _editingBubble,
            _viewModel.SelectedElementIndex,
            _editingText,
            _viewModel.PictureSnapshot,
            _viewModel.Fields,
            _viewModel.ExtraSelectionBounds)));
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
        ElementHandle,
        PanelCorner,
        Gutter,
        BubbleBody,
        ElementBody,
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
        int ElementIndex = -1,
        Limb Limb = Limb.LeftArm,
        TrunkPart Trunk = TrunkPart.Hips,
        RectEdges Edges = RectEdges.None,
        GutterHit? Gutter = null);

    /// <summary>
    /// What's under a point, in priority order: the selected bubble's own handles first
    /// (they sit on top), then the selected character's pose handles (hand/foot, then
    /// elbow/knee, then trunk) and resize handles, the selected element's resize handles,
    /// panel corners and gutters, then bubble bodies (so a bubble flush against a panel edge
    /// is still grabbable), foreground elements, characters, panel edges, background
    /// elements (scenery covering a panel mustn't stop its edges being dragged), and last
    /// panel bodies - front to back, the way they're drawn.
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

        if (vm.SelectedElement is { } element && vm.SelectedPanelId is { } elementPanel && element.Id != _editingText)
        {
            var handleEdges = HitHandles(ProjectModel.Issues.PanelElements.Bounds(element), p, tol, includeMidpoints: true);
            if (handleEdges != RectEdges.None)
                return new Hit(HitKind.ElementHandle, elementPanel, ElementIndex: vm.SelectedElementIndex, Edges: handleEdges);
            // Selected speed lines keep their clear circle even where a character stands in it
            // (the usual place for one), so dragging there always moves where the lines radiate from.
            if (element is ProjectModel.Issues.SpeedLinesElement speedLines && InEllipse(speedLines.Focus, p))
                return new Hit(HitKind.ElementBody, elementPanel, ElementIndex: vm.SelectedElementIndex);
        }

        // A locked layout offers no panel handles, edges or gutters to grab.
        foreach (var id in doc.LayoutLocked ? [] : doc.PanelOrder)
        {
            if (!doc.Panels.TryGetValue(id, out var panel))
                continue;
            var edges = HitHandles(AnchorRing.BoundingBox(panel.Shape.Anchors), p, tol, includeMidpoints: false);
            if (edges != RectEdges.None)
                return new Hit(HitKind.PanelCorner, id, Edges: edges);
        }

        if (!doc.LayoutLocked && PanelGutters.FindAt(doc.Panels.Values, p, EdgeBandPx / _zoom) is { } gutter)
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

        if (ElementAt(p, ProjectModel.Issues.ElementLayer.Foreground) is { } front)
            return front;

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

            if (!doc.LayoutLocked)
            {
                var edges = RectEdges.None;
                if (p.X - bounds.Left <= band) edges |= RectEdges.Left;
                if (bounds.Right - p.X <= band) edges |= RectEdges.Right;
                if (p.Y - bounds.Top <= band) edges |= RectEdges.Top;
                if (bounds.Bottom - p.Y <= band) edges |= RectEdges.Bottom;
                if (edges != RectEdges.None)
                    return new Hit(HitKind.PanelEdge, id, Edges: edges);
            }
            return ElementAt(p, ProjectModel.Issues.ElementLayer.Background, id) ?? new Hit(HitKind.PanelBody, id);
        }

        return new Hit(HitKind.None);
    }

    /// <summary>The topmost element of <paramref name="layer"/> under <paramref name="p"/> - only where it shows, inside its panel.</summary>
    private Hit? ElementAt(Point2D p, ProjectModel.Issues.ElementLayer layer, PanelId? only = null)
    {
        var doc = _viewModel!.Working;
        var tol = HitRadiusPx / 2 / _zoom;
        for (var i = doc.PanelOrder.Count - 1; i >= 0; i--)
        {
            var id = doc.PanelOrder[i];
            if (only is { } wanted && !wanted.Equals(id))
                continue;
            if (!doc.Panels.TryGetValue(id, out var panel) || !Contains(AnchorRing.BoundingBox(panel.Shape.Anchors), p))
                continue;
            for (var e = panel.Elements.Count - 1; e >= 0; e--)
            {
                if (panel.Elements[e].Layer == layer && ElementRenderer.Hits(panel.Elements[e], p, tol))
                    return new Hit(HitKind.ElementBody, id, ElementIndex: e);
            }
        }
        return null;
    }

    /// <summary>
    /// Right-click › This panel only: take something off or put something on, recolour or
    /// re-pattern a colour slot - for this one panel (sunglasses for one shot) - or go
    /// back to the look.
    /// </summary>
    private MenuItem PanelOnlyMenu(PageEditorViewModel vm, ProjectModel.Ids.PanelId panelId, int index)
    {
        var menu = new MenuItem { Header = "This panel only" };
        var items = new List<object>();
        if (vm.PanelView(panelId, index) is { } shown)
        {
            var worn = ProjectModel.Characters.CharacterLooks.Resolve(shown).Stickers;
            if (worn.Count > 0)
                items.Add(new MenuItem
                {
                    Header = "Take off",
                    ItemsSource = worn.Select(w => Item(w.Asset.Sticker.Name, () => vm.EditPanelLook(panelId, index, c => LookEditing.TakeOff(c, w.Asset.Id)))).ToList()
                });
            var spare = shown.Wardrobe.Stickers.Values.Where(a => worn.All(w => w.Asset.Id != a.Id)).OrderBy(a => a.Sticker.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (spare.Count > 0)
                items.Add(new MenuItem
                {
                    Header = "Put on",
                    ItemsSource = spare.Select(a => Item(a.Sticker.Name, () => vm.EditPanelLook(panelId, index, c => LookEditing.Wear(c, a)))).ToList()
                });
            var slots = LookEditing.ColorSlotsInUse(shown);
            items.Add(new MenuItem
            {
                Header = "Colour",
                ItemsSource = slots.Select(slot => new MenuItem
                {
                    Header = CharacterEditorViewModel.ColorSlotLabel(slot),
                    ItemsSource = CharacterEditorViewModel.Palette(slot)
                        .Select(p => Item(p.Name, () => vm.EditPanelLook(panelId, index, c => LookEditing.SetColor(c, slot, p.Color)))).ToList()
                }).ToList()
            });
            var fabricSlots = slots.Where(slot => slot is not (ProjectModel.Characters.CharacterDefinition.SkinSlot or "eyes")).ToList();
            if (fabricSlots.Count > 0)
                items.Add(new MenuItem
                {
                    Header = "Pattern",
                    ItemsSource = fabricSlots.Select(slot => new MenuItem
                    {
                        Header = CharacterEditorViewModel.ColorSlotLabel(slot),
                        ItemsSource = new (string Name, ProjectModel.Characters.PatternKind? Kind)[]
                            {
                                ("Plain", null), ("Stripes", ProjectModel.Characters.PatternKind.Stripes), ("Checks", ProjectModel.Characters.PatternKind.Checks),
                                ("Plaid", ProjectModel.Characters.PatternKind.Plaid), ("Dots", ProjectModel.Characters.PatternKind.Dots),
                            }
                            .Select(p => Item(p.Name, () => vm.EditPanelLook(panelId, index, c => LookEditing.SetFabric(c, slot,
                                p.Kind is { } kind ? new ProjectModel.Characters.Fabric(new ProjectModel.Characters.PatternFill(kind, [])) : new ProjectModel.Characters.Fabric()))))
                            .ToList()
                    }).ToList()
                });
        }
        var clear = Item("Back to the look", () => vm.ClearPanelLook(panelId, index));
        clear.IsEnabled = vm.Working.Panels.TryGetValue(panelId, out var panel) && index < panel.CharacterInstances.Count && panel.CharacterInstances[index].Overrides is not null;
        items.Add(new Separator());
        items.Add(clear);
        menu.ItemsSource = items;
        return menu;
    }

    /// <summary>The figure's actual silhouette, not its box - characters stand close together and overlap.</summary>
    private bool HitsCharacter(ProjectModel.Issues.CharacterInstance instance, Point2D p)
    {
        var vm = _viewModel!;
        if (!Contains(vm.CharacterBounds(instance), p))
            return false;
        if (!vm.CharacterSnapshot.TryGetValue(instance.CharacterId, out var character))
            return true; // a missing character's placeholder box
        using var path = CharacterRenderers.Default.BuildSilhouette(character, instance.Placement, instance.Pose.ViewAngle, instance.Pose, instance.Overrides, vm.LookOf(instance));
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

            case PageEditorTool.Draw or PageEditorTool.Line or PageEditorTool.Rectangle or PageEditorTool.Ellipse:
                if (PanelAt(page) is { } drawPanel)
                {
                    _viewModel.ClearSelection();
                    _dragPanelId = drawPanel;
                    _trail.Clear();
                    _trail.Add(page);
                    _viewModel.BeginDrawShape(drawPanel);
                    StartDrag(e, DragKind.DrawShape);
                }
                return;

            case PageEditorTool.Text:
                if (PanelAt(page) is { } textPanel)
                {
                    _dragPanelId = textPanel;
                    StartDrag(e, DragKind.CreateText);
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
                case HitKind.ElementBody or HitKind.ElementHandle when hit.PanelId is { } textPanel
                                                                     && vm.Working.Panels[textPanel].Elements[hit.ElementIndex] is ProjectModel.Issues.TextElement:
                    vm.SelectElement(textPanel, hit.ElementIndex);
                    vm.RequestElementTextEdit(textPanel, hit.ElementIndex);
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

        // Shift+click on a bubble, character or element toggles it in or out of the selection,
        // instead of the usual press-to-select-and-drag: adding to a selection is the point,
        // and a drag straight from here would just move the one thing Shift was held over.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && hit.PanelId is { } shiftPanel
            && hit.Kind is HitKind.BubbleBody or HitKind.CharacterBody or HitKind.ElementBody)
        {
            vm.ToggleSelect(shiftPanel, hit.BubbleIndex, hit.CharacterIndex, hit.ElementIndex);
            return;
        }

        _dragPanelId = hit.PanelId;
        _dragBubbleIndex = hit.BubbleIndex;
        _dragTailIndex = hit.TailIndex;
        _dragCharacterIndex = hit.CharacterIndex;
        _dragElementIndex = hit.ElementIndex;
        _dragEdges = hit.Edges;

        // A plain press on something already part of a multi-selection drags the whole group
        // without collapsing it first; released without moving, it falls back to a plain click
        // (see the DragKind.PendingMoveGroup case in FinishDrag).
        if (hit.PanelId is { } groupPanel && hit.Kind is HitKind.BubbleBody or HitKind.CharacterBody or HitKind.ElementBody
            && vm.IsPartOfSelection(groupPanel, hit.BubbleIndex, hit.CharacterIndex, hit.ElementIndex) && vm.HasMultiSelection)
        {
            StartDrag(e, DragKind.PendingMoveGroup);
            return;
        }

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

            case HitKind.ElementHandle:
                _dragStartBounds = ProjectModel.Issues.PanelElements.Bounds(vm.Working.Panels[hit.PanelId!.Value].Elements[hit.ElementIndex]);
                vm.BeginResizeElement(hit.PanelId.Value, hit.ElementIndex);
                StartDrag(e, DragKind.ResizeElement);
                break;

            case HitKind.ElementBody:
                vm.SelectElement(hit.PanelId!.Value, hit.ElementIndex);
                StartDrag(e, DragKind.PendingMoveElement);
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

            case HitKind.PanelBody when vm.Working.LayoutLocked:
                // Its layout can't be changed, but it can still be selected (issue #67) - so a
                // double-click character or Insert > Character has somewhere to go. Dragging
                // from it does nothing but pan the view, like the pasteboard.
                vm.Select(hit.PanelId);
                StartDrag(e, DragKind.Pan);
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
        _dragMoved = false;
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

        _altHeld = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (_drag == DragKind.None)
        {
            UpdateHover(page);
            return;
        }

        var dx = page.X - _pressPage.X;
        var dy = page.Y - _pressPage.Y;
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        // Alt turns snapping off - except while Alt+dragging a copy, where it's busy duplicating.
        var snap = alt && !_duplicating ? 0 : SnapDistancePx / _zoom;
        var beyondThreshold = Math.Abs(pos.X - _pressScreen.X) > DragThresholdPx || Math.Abs(pos.Y - _pressScreen.Y) > DragThresholdPx;
        _dragMoved |= beyondThreshold;

        switch (_drag)
        {
            case DragKind.PendingMoveBubble when beyondThreshold:
                // Alt as the drag starts drags a copy away, leaving the original where it is.
                if (alt && _viewModel.BeginDuplicateBubble(_dragPanelId!.Value, _dragBubbleIndex) is >= 0 and var bubbleCopy)
                    (_dragBubbleIndex, _duplicating) = (bubbleCopy, true);
                else
                    _viewModel.BeginMoveBubble(_dragPanelId!.Value, _dragBubbleIndex);
                _drag = DragKind.MoveBubble;
                UpdateCursor(null);
                goto case DragKind.MoveBubble;

            case DragKind.MoveBubble:
                _viewModel.UpdateMoveBubble(_dragPanelId!.Value, _dragBubbleIndex, dx, dy,
                    withTails: e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));
                break;

            case DragKind.PendingMoveCharacter when beyondThreshold:
                if (alt && _viewModel.BeginDuplicateCharacter(_dragPanelId!.Value, _dragCharacterIndex) is >= 0 and var characterCopy)
                    (_dragCharacterIndex, _duplicating) = (characterCopy, true);
                else
                    _viewModel.BeginMoveCharacter(_dragPanelId!.Value, _dragCharacterIndex);
                _drag = DragKind.MoveCharacter;
                UpdateCursor(null);
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
                if (alt && _viewModel.BeginDuplicatePanel(_dragPanelId!.Value) is { } panelCopy)
                    (_dragPanelId, _duplicating) = (panelCopy, true);
                else
                    _viewModel.BeginMovePanel(_dragPanelId!.Value);
                _drag = DragKind.MovePanel;
                UpdateCursor(null);
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

            case DragKind.CreateText when _dragMoved:
                _rubberBand = RectFromPoints(_pressPage, page);
                break;

            case DragKind.PendingMoveElement when beyondThreshold:
                if (alt && _viewModel.BeginDuplicateElement(_dragPanelId!.Value, _dragElementIndex) is >= 0 and var elementCopy)
                    (_dragElementIndex, _duplicating) = (elementCopy, true);
                else
                    _viewModel.BeginMoveElement(_dragPanelId!.Value, _dragElementIndex);
                _drag = DragKind.MoveElement;
                UpdateCursor(null);
                goto case DragKind.MoveElement;

            case DragKind.MoveElement:
                _viewModel.UpdateMoveElement(_dragPanelId!.Value, _dragElementIndex, dx, dy);
                break;

            case DragKind.ResizeElement:
                _viewModel.UpdateResizeElement(_dragPanelId!.Value, _dragElementIndex, MoveEdges(_dragStartBounds, _dragEdges, dx, dy));
                break;

            case DragKind.PendingMoveGroup when beyondThreshold:
                if (alt && _viewModel.BeginDuplicateSelection(_dragPanelId!.Value))
                    _duplicating = true;
                else
                    _viewModel.BeginMoveSelection(_dragPanelId!.Value);
                _drag = DragKind.MoveGroup;
                UpdateCursor(null);
                goto case DragKind.MoveGroup;

            case DragKind.MoveGroup:
                _viewModel.UpdateMoveSelection(_dragPanelId!.Value, dx, dy);
                break;

            case DragKind.DrawShape when _viewModel.Tool == PageEditorTool.Draw:
                // Pointer events come faster than the pen needs; skip ones under half a pixel apart.
                if (Dist(_trail[^1], page) * _zoom >= 0.5)
                {
                    _trail.Add(page);
                    _viewModel.UpdateDrawFreehand(_trail, FreehandTolerancePx / _zoom, CloseDistancePx / _zoom);
                }
                break;

            case DragKind.DrawShape when _dragMoved:
                _viewModel.UpdateDrawShape(_pressPage, page, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
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
                or DragKind.MoveCharacter or DragKind.ResizeCharacter or DragKind.PoseLimb or DragKind.PoseBend or DragKind.PoseTrunk
                or DragKind.MoveElement or DragKind.ResizeElement or DragKind.MoveGroup:
                vm.EndGesture(commit);
                break;

            // Never dragged (no BeginMoveSelection call was ever made, so there's no gesture to
            // end): a plain click on a multi-selected item collapses the selection to just it.
            case DragKind.PendingMoveGroup when commit && _dragPanelId is { } collapsePanel:
                if (_dragBubbleIndex >= 0)
                    vm.Select(collapsePanel, _dragBubbleIndex);
                else if (_dragCharacterIndex >= 0)
                    vm.SelectCharacter(collapsePanel, _dragCharacterIndex);
                else if (_dragElementIndex >= 0)
                    vm.SelectElement(collapsePanel, _dragElementIndex);
                break;

            case DragKind.DrawShape when commit:
                // A click with the rectangle or ellipse tool places a standard-size one there.
                if (!_dragMoved && vm.Tool is PageEditorTool.Rectangle or PageEditorTool.Ellipse)
                    vm.UpdateDrawShape(
                        new Point2D(_pressPage.X - PageEditorViewModel.DefaultShapeWidthMm / 2, _pressPage.Y - PageEditorViewModel.DefaultShapeHeightMm / 2),
                        new Point2D(_pressPage.X + PageEditorViewModel.DefaultShapeWidthMm / 2, _pressPage.Y + PageEditorViewModel.DefaultShapeHeightMm / 2));
                vm.CommitDrawShape();
                break;

            case DragKind.DrawShape:
                vm.CancelDrawShape();
                break;

            case DragKind.CreateText when commit && _dragPanelId is { } textPanel:
                var textIndex = vm.CreateText(textPanel, _pressPage, _dragMoved ? _rubberBand : null);
                if (textIndex >= 0)
                {
                    vm.Tool = PageEditorTool.Select;
                    vm.SelectElement(textPanel, textIndex);
                    vm.RequestElementTextEdit(textPanel, textIndex);
                }
                break;
        }

        _dragPanelId = null;
        _duplicating = false;
        _dragBubbleIndex = -1;
        _dragTailIndex = -1;
        _dragCharacterIndex = -1;
        _dragElementIndex = -1;
        _trail.Clear();
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
        _hoverHit = null;
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
        // The "you could select/land this here" highlight shows for the Select tool even on a
        // locked layout, since a locked panel can still be selected (issue #67).
        var panel = vm.Tool is PageEditorTool.Bubble or PageEditorTool.Text or PageEditorTool.Select || vm.IsShapeTool ? PanelAt(page) : null;
        if (!Equals(gutter, _hoverGutter) || !Equals(panel, _hoverPanelId))
        {
            _hoverGutter = gutter;
            _hoverPanelId = panel;
            InvalidateVisual();
        }
        _hoverHit = hit;
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
            case Key.Enter or Key.F2 when vm.HasSelectedText:
                vm.RequestElementTextEdit(vm.SelectedPanelId!.Value, vm.SelectedElementIndex);
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
            case Key.LeftAlt or Key.RightAlt:
                // Alt over something that can be Alt+dragged shows the copy cursor straight away.
                _altHeld = true;
                UpdateCursor(_drag == DragKind.None ? _hoverHit : null);
                return;
            case Key.C when ctrl && _drag == DragKind.None:
                vm.Copy();
                break;
            case Key.X when ctrl && _drag == DragKind.None:
                vm.Cut();
                break;
            case Key.V when ctrl && _drag == DragKind.None:
                vm.Paste();
                break;
            case Key.D when ctrl && _drag == DragKind.None:
                vm.Duplicate();
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
            case Key.D when !ctrl:
                vm.Tool = PageEditorTool.Draw;
                break;
            case Key.L when !ctrl:
                vm.Tool = PageEditorTool.Line;
                break;
            case Key.R when !ctrl:
                vm.Tool = PageEditorTool.Rectangle;
                break;
            case Key.E when !ctrl:
                vm.Tool = PageEditorTool.Ellipse;
                break;
            case Key.T when !ctrl:
                vm.Tool = PageEditorTool.Text;
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
        else if (e.Key is Key.LeftAlt or Key.RightAlt)
        {
            _altHeld = false;
            UpdateCursor(_drag == DragKind.None ? _hoverHit : null);
        }
    }

    // ---------------------------------------------------------------- context menu

    private ContextMenu? _contextMenu;

    private void ShowContextMenu(Point2D page)
    {
        _contextMenu = new ContextMenu { ItemsSource = ContextMenuItems(page) };
        _contextMenu.Open(this);
    }

    /// <summary>The right-click menu for what's at <paramref name="page"/> (page mm) - selecting it first. Public for headless UI tests.</summary>
    public IReadOnlyList<Control> ContextMenuItems(Point2D page)
    {
        var vm = _viewModel!;
        var hit = HitTest(page);
        var items = new List<Control>();

        if (hit.Kind is HitKind.BubbleBody or HitKind.BubbleHandle or HitKind.TailBase or HitKind.TailTarget && hit.PanelId is { } bubblePanel)
        {
            var index = hit.BubbleIndex;
            vm.Select(bubblePanel, index);
            items.AddRange(ClipboardItems(vm));
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
        else if (hit.Kind is HitKind.ElementBody or HitKind.ElementHandle && hit.PanelId is { } elementPanel)
        {
            var index = hit.ElementIndex;
            vm.SelectElement(elementPanel, index);
            items.AddRange(ClipboardItems(vm));
            var element = vm.Working.Panels[elementPanel].Elements[index];
            if (element is ProjectModel.Issues.TextElement)
            {
                items.Add(Item("Edit text", () => vm.RequestElementTextEdit(elementPanel, index), "Enter"));
                items.Add(new MenuItem
                {
                    Header = "Kind",
                    ItemsSource = vm.TextPresetChoices.Select(choice => Item(choice.Name, () => vm.ApplyTextPresetCommand.Execute(choice.Preset))).ToList()
                });
                items.Add(Item("Bigger letters", () => vm.BiggerTextCommand.Execute(null)));
                items.Add(Item("Smaller letters", () => vm.SmallerTextCommand.Execute(null)));
                var style = vm.CurrentTextStyle;
                items.Add(ColorMenu("Text Fill", new ColorMenuOptions(vm.SetTextColorCommand, "No Fill", "More Fill Colors…", style.Color)));
                items.Add(ColorMenu("Text Outline", new ColorMenuOptions(vm.SetTextOutlineCommand, "No Outline", "More Outline Colors…", style.Outline,
                    vm.SetTextOutlineWeightCommand, style.OutlineWidthMm)));
                items.Add(ColorMenu("Shape Fill", new ColorMenuOptions(vm.SetBoxFillCommand, "No Fill", "More Fill Colors…", style.BoxFill)));
                items.Add(ColorMenu("Shape Outline", new ColorMenuOptions(vm.SetBoxOutlineCommand, "No Outline", "More Outline Colors…", style.BoxStroke,
                    vm.SetBoxWeightCommand, style.BoxStrokeWidthMm, vm.SetBoxDashCommand, style.BoxDash)));
            }
            else if (element is ProjectModel.Issues.ShapeElement)
            {
                var style = vm.CurrentShapeStyle;
                items.Add(ColorMenu("Fill", new ColorMenuOptions(vm.SetFillColorCommand, "No Fill", "More Fill Colors…", style.Fill)));
                items.Add(ColorMenu("Outline", new ColorMenuOptions(vm.SetStrokeColorCommand, "No Outline", "More Outline Colors…", style.Stroke,
                    vm.SetStrokeWeightCommand, style.StrokeWidthMm, vm.SetStrokeDashCommand, style.Dash)));
            }
            else if (element is ProjectModel.Issues.SpeedLinesElement)
            {
                var style = vm.CurrentSpeedLinesStyle;
                items.Add(ColorMenu("Color", new ColorMenuOptions(vm.SetSpeedLinesColorCommand, null, "More Colors…", style.Color)));
                items.Add(new MenuItem
                {
                    Header = "Lines",
                    ItemsSource = new (string Name, int Count)[] { ("Few", 20), ("Some", 80), ("Many", 200) }
                        .Select(p => Item(p.Name, () => vm.SpeedLinesCount = p.Count)).ToList()
                });
                items.Add(new MenuItem { Header = "Thickness", ItemsSource = vm.WeightChoices.Select(w => Item(w.Name, () => vm.SpeedLinesThickness = w.Mm)).ToList() });
                items.Add(Item("Shuffle", () => vm.ShuffleSpeedLinesCommand.Execute(null)));
            }
            items.Add(new Separator());
            var inFront = element.Layer == ProjectModel.Issues.ElementLayer.Foreground;
            items.Add(Item(inFront ? "Put behind the characters" : "Put in front of the characters",
                () => vm.SetElementLayer(elementPanel, index, inFront ? ProjectModel.Issues.ElementLayer.Background : ProjectModel.Issues.ElementLayer.Foreground)));
            items.Add(Item("Bring to front", () => vm.ReorderElement(elementPanel, index, toFront: true)));
            items.Add(Item("Send to back", () => vm.ReorderElement(elementPanel, index, toFront: false)));
            items.Add(new Separator());
            items.Add(Item(element switch
            {
                ProjectModel.Issues.TextElement => "Delete text",
                ProjectModel.Issues.PictureElement => "Delete picture",
                ProjectModel.Issues.SpeedLinesElement => "Delete speed lines",
                _ => "Delete shape"
            }, () => vm.DeleteElement(elementPanel, index), "Del"));
        }
        else if (hit.Kind is HitKind.CharacterBody or HitKind.CharacterHandle && hit.PanelId is { } characterPanel)
        {
            var index = hit.CharacterIndex;
            vm.SelectCharacter(characterPanel, index);
            items.AddRange(ClipboardItems(vm));
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
            var expressions = new MenuItem { Header = "Expression" };
            // The presets, the character's saved faces, then a mix of your own: Eyes ▸, Brows ▸, Mouth ▸ one at a time.
            var saved = vm.SavedFaceChoices;
            var mixer = vm.ExpressionMixer;
            expressions.ItemsSource = ExpressionPresets.All.Select(preset => (Control)Item(preset.Name, () => vm.ApplyExpression(characterPanel, index, preset)))
                .Concat(saved.Count > 0 ? [new Separator()] : Array.Empty<Control>())
                .Concat(saved.Select(face => (Control)Item((face.IsCurrent ? "✓ " : "") + face.Name, () => vm.ApplySavedFace(characterPanel, index, face.Face))))
                .Concat(mixer.Count > 0 ? [new Separator()] : Array.Empty<Control>())
                .Concat(mixer.Select(row => (Control)new MenuItem
                {
                    Header = row.Label,
                    ItemsSource = row.Choices
                        .Select(choice => Item((choice.IsCurrent ? "✓ " : "") + choice.Name, () => vm.SetExpressionVariant(characterPanel, index, row.Slot, choice.Variant)))
                        .ToList()
                }))
                .ToList();
            items.Add(expressions);
            if (vm.PanelLookChoices is { Count: > 2 } looks)
            {
                var look = new MenuItem { Header = "Look" };
                look.ItemsSource = looks.Select(choice => Item((choice.IsCurrent ? "✓ " : "") + choice.Name, () => vm.SetPanelLook(characterPanel, index, choice.Look))).ToList();
                items.Add(look);
            }
            items.Add(PanelOnlyMenu(vm, characterPanel, index));
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
        else if (PanelAt(page) is { } lockedPanelId && vm.Working.LayoutLocked)
        {
            vm.Select(lockedPanelId);
            if (vm.CanPaste)
            {
                // Paste explicitly targets the panel that was right-clicked, regardless of what's selected.
                items.Add(Item("Paste", () => vm.PasteInto(lockedPanelId), "Ctrl+V"));
                items.Add(new Separator());
            }
            var at = page;
            items.Add(Item("Add bubble here", () =>
            {
                var index = vm.CreateBubble(lockedPanelId, at);
                if (index >= 0)
                    _viewModel!.RequestTextEdit(lockedPanelId, index);
            }));
            items.Add(AddTextItem(vm, lockedPanelId, at));
            items.Add(BackgroundMenu(vm, lockedPanelId));
            items.Add(BorderItem(vm, lockedPanelId));
            items.Add(new Separator());
            items.Add(Item("Unlock layout", () => vm.IsLayoutLocked = false));
        }
        else if (PanelAt(page) is { } panelId)
        {
            vm.Select(panelId);
            items.AddRange(ClipboardItems(vm));
            var at = page;
            items.Add(Item("Add bubble here", () =>
            {
                var index = vm.CreateBubble(panelId, at);
                if (index >= 0)
                    _viewModel!.RequestTextEdit(panelId, index);
            }));
            items.Add(AddTextItem(vm, panelId, at));
            items.Add(BackgroundMenu(vm, panelId));
            items.Add(BorderItem(vm, panelId));
            // The panel as Shape Fill and Shape Outline see it: its background colour and its border.
            var style = vm.CurrentPanelStyle;
            items.Add(ColorMenu("Shape Fill", new ColorMenuOptions(vm.SetPanelFillCommand, "No Fill", "More Fill Colors…", style.Fill)));
            items.Add(ColorMenu("Shape Outline", new ColorMenuOptions(vm.SetPanelOutlineCommand, "No Outline", "More Outline Colors…", style.Stroke,
                vm.SetPanelOutlineWeightCommand, style.StrokeWidthMm, vm.SetPanelOutlineDashCommand, style.Dash)));
            items.Add(new Separator());
            items.Add(Item("Split side by side", () => vm.SplitPanel(panelId, BoundaryOrientation.Vertical, 0.5)));
            items.Add(Item("Split top and bottom", () => vm.SplitPanel(panelId, BoundaryOrientation.Horizontal, 0.5)));
            items.Add(new Separator());
            items.Add(Item("Delete panel", () => vm.DeletePanel(panelId), "Del"));
        }
        else
        {
            vm.ClearSelection();
            if (vm.CanPaste)
            {
                items.Add(Item("Paste", () => vm.Paste(), "Ctrl+V"));
                items.Add(new Separator());
            }
            var layouts = new MenuItem { Header = "Page layout" };
            layouts.ItemsSource = PanelLayoutPresets.All.Select(preset => Item(preset.Name, () => vm.ApplyLayoutPreset(preset))).ToList();
            items.Add(layouts);
            items.Add(Item("Fit page", FitPage, "Ctrl+0"));
            items.Add(Item("Actual size", ActualSize, "Ctrl+1"));
        }
        return items;
    }

    /// <summary>Word's Cut, Copy and Paste - and Duplicate - for what the menu was opened on (already selected), then a separator.</summary>
    private static IEnumerable<Control> ClipboardItems(PageEditorViewModel vm)
    {
        var paste = Item("Paste", () => vm.Paste(), "Ctrl+V");
        paste.IsEnabled = vm.CanPaste;
        return
        [
            Item("Cut", () => vm.Cut(), "Ctrl+X"),
            Item("Copy", () => vm.Copy(), "Ctrl+C"),
            paste,
            Item("Duplicate", () => vm.Duplicate(), "Ctrl+D"),
            new Separator()
        ];
    }

    private static MenuItem AddTextItem(PageEditorViewModel vm, PanelId panelId, Point2D at) => Item("Add text here", () =>
    {
        var index = vm.CreateText(panelId, at);
        if (index >= 0)
            vm.RequestElementTextEdit(panelId, index);
    }, "T");

    /// <summary>Background ▸ every choice, for this panel - offered on a locked layout too, since a background isn't layout.</summary>
    private static MenuItem BackgroundMenu(PageEditorViewModel vm, PanelId panelId)
    {
        var items = vm.BackgroundChoices.Select(choice => (object)Item(choice.Name, () => vm.SetPanelBackground(panelId, choice.Background))).ToList();
        if (vm.CanImportPictures)
        {
            items.Add(new Separator());
            items.Add(Item("Picture…", () => vm.RequestPictureImport(panelId, asBackground: true)));
        }
        return new MenuItem { Header = "Background", ItemsSource = items };
    }

    /// <summary>Border, ticked while the panel has one - offered on a locked layout too, since a border isn't layout.</summary>
    private static MenuItem BorderItem(PageEditorViewModel vm, PanelId panelId)
    {
        var hasBorder = vm.Working.Panels.TryGetValue(panelId, out var panel) && !panel.Borderless;
        var item = Item("Border", () => vm.SetPanelBorder(panelId, !hasBorder));
        item.ToggleType = MenuItemToggleType.CheckBox;
        item.IsChecked = hasBorder;
        return item;
    }

    /// <summary>A submenu holding Word's colour menu (<see cref="ColorMenus"/>) - the same palette, None, More Colors…, Weight and Dashes as the ribbon's buttons.</summary>
    private MenuItem ColorMenu(string header, ColorMenuOptions options) =>
        new() { Header = header, ItemsSource = ColorMenus.Items(options, () => _contextMenu?.Close(), this, atPointer: true) };

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
        else if (vm.Tool is PageEditorTool.Panel or PageEditorTool.Bubble || vm.IsShapeTool)
            type = StandardCursorType.Cross;
        else if (vm.Tool == PageEditorTool.Text)
            type = StandardCursorType.Ibeam;
        else if (_duplicating || _drag == DragKind.None && _altHeld && hit is not null && CanDuplicate(hit))
            type = StandardCursorType.DragCopy;
        else if (hit == null)
            type = _drag is DragKind.MoveBubble or DragKind.MovePanel or DragKind.MoveCharacter or DragKind.MoveElement ? StandardCursorType.SizeAll : StandardCursorType.Arrow;
        else
            type = hit.Kind switch
            {
                HitKind.TailTarget or HitKind.TailBase or HitKind.LimbHandle or HitKind.BendHandle or HitKind.TrunkHandle => StandardCursorType.Hand,
                HitKind.BubbleHandle or HitKind.PanelCorner or HitKind.PanelEdge or HitKind.CharacterHandle or HitKind.ElementHandle => EdgeCursor(hit.Edges),
                HitKind.Gutter => hit.Gutter!.Drag.Orientation == BoundaryOrientation.Vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth,
                HitKind.PanelBody when vm.Working.LayoutLocked => StandardCursorType.Arrow,
                HitKind.BubbleBody or HitKind.PanelBody or HitKind.CharacterBody or HitKind.ElementBody => StandardCursorType.SizeAll,
                _ => StandardCursorType.Arrow
            };

        CursorType = type;
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

    /// <summary>Whether an Alt+drag on <paramref name="hit"/> would drag a copy of it away (what the copy cursor promises).</summary>
    private bool CanDuplicate(Hit hit) => hit.Kind switch
    {
        HitKind.BubbleBody or HitKind.CharacterBody or HitKind.ElementBody => true,
        HitKind.PanelBody => !_viewModel!.Working.LayoutLocked,
        _ => false
    };

    /// <summary>The cursor showing now, for headless UI tests (which have no real cursor to look at).</summary>
    public StandardCursorType CursorType { get; private set; } = StandardCursorType.Arrow;

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

    private static bool InEllipse(Rect2D r, Point2D p)
    {
        if (r.Width <= 0 || r.Height <= 0)
            return false;
        var dx = (p.X - r.MidX) / (r.Width / 2);
        var dy = (p.Y - r.MidY) / (r.Height / 2);
        return dx * dx + dy * dy <= 1;
    }

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
        PoseTrunk,
        PendingMoveElement,
        MoveElement,
        ResizeElement,
        DrawShape,
        CreateText,
        PendingMoveGroup,
        MoveGroup
    }
}
