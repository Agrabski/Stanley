using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Stanley.Editing;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;
using PanelModel = Stanley.ProjectModel.Issues.Panel;

namespace Stanley.Editors;

public partial class PageEditorView : UserControl
{
    public PageEditorView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        var viewModel = DataContext as PageEditorViewModel;
        if (this.FindControl<PageCanvasControl>("PageCanvas") is PageCanvasControl canvas)
        {
            canvas.ViewModel = viewModel;
        }
    }
}

public sealed class PageCanvasControl : Control
{
    private const float HandleSize = 8f;
    private const float HandleHitRadius = 12f;

    private PageEditorViewModel? _viewModel;
    private DragState _dragState = DragState.None;
    private PanelId? _dragPanelId;
    private int _dragBubbleIndex = -1;
    private int _dragTailIndex = -1;
    private PanelBoundaryDrag? _dragBoundary;
    private Rect2D _dragStartBounds;

    private PanelId? _selectedPanelId;
    private int _selectedBubbleIndex = -1;

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
            InvalidateVisual();
        }
    }

    public PageCanvasControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Working" or "Committed")
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_viewModel == null)
            return;

        context.Custom(new PageCanvasDrawOperation(
            new Rect(Bounds.Size),
            _viewModel.Working,
            _viewModel.PageBounds));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var pos = e.GetPosition(this);
        var skPoint = new SKPoint((float)pos.X, (float)pos.Y);
        var point2D = new Point2D(pos.X, pos.Y);

        if (_viewModel == null)
            return;

        // Try to hit-test: tail target handles, then tail attachment, then bubble corners,
        // then panel corners, then boundary line

        // 1. Tail targets
        foreach (var panelId in _viewModel.Working.PanelOrder)
        {
            if (!_viewModel.Working.Panels.TryGetValue(panelId, out var panel))
                continue;

            for (int bubbleIdx = 0; bubbleIdx < panel.Bubbles.Count; bubbleIdx++)
            {
                var bubble = panel.Bubbles[bubbleIdx];
                for (int tailIdx = bubble.Tails.Count - 1; tailIdx >= 0; tailIdx--)
                {
                    var tail = bubble.Tails[tailIdx];
                    if (DistPoint(tail.Target, point2D) <= HandleHitRadius)
                    {
                        _dragState = DragState.MoveTailTarget;
                        _dragPanelId = panelId;
                        _dragBubbleIndex = bubbleIdx;
                        _dragTailIndex = tailIdx;
                        _viewModel.BeginMoveBubbleTail(panelId, bubbleIdx, tailIdx);
                        e.Pointer.Capture(this);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        // 2. Tail attachments
        foreach (var panelId in _viewModel.Working.PanelOrder)
        {
            if (!_viewModel.Working.Panels.TryGetValue(panelId, out var panel))
                continue;

            for (int bubbleIdx = 0; bubbleIdx < panel.Bubbles.Count; bubbleIdx++)
            {
                var bubble = panel.Bubbles[bubbleIdx];
                for (int tailIdx = bubble.Tails.Count - 1; tailIdx >= 0; tailIdx--)
                {
                    var tail = bubble.Tails[tailIdx];
                    var attachPoint = AnchorRing.PointAt(bubble.Shape.Anchors, tail.AttachmentT);
                    if (DistPoint(attachPoint, point2D) <= HandleHitRadius)
                    {
                        _dragState = DragState.SlideTailAttachment;
                        _dragPanelId = panelId;
                        _dragBubbleIndex = bubbleIdx;
                        _dragTailIndex = tailIdx;
                        _viewModel.BeginSlideBubbleTailAttachment(panelId, bubbleIdx, tailIdx);
                        e.Pointer.Capture(this);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        // 3. Bubble resize corners
        foreach (var panelId in _viewModel.Working.PanelOrder)
        {
            if (!_viewModel.Working.Panels.TryGetValue(panelId, out var panel))
                continue;

            for (int bubbleIdx = 0; bubbleIdx < panel.Bubbles.Count; bubbleIdx++)
            {
                var bubble = panel.Bubbles[bubbleIdx];
                var bounds = AnchorRing.BoundingBox(bubble.Shape.Anchors);

                // Check if we hit a bubble body first (for selection)
                using (var path = BubbleRenderer.BuildRenderPath(bubble))
                {
                    if (path.Contains((float)point2D.X, (float)point2D.Y))
                    {
                        _selectedPanelId = panelId;
                        _selectedBubbleIndex = bubbleIdx;
                    }
                }

                // Check corners
                var corners = new[]
                {
                    new Point2D(bounds.Left, bounds.Top),
                    new Point2D(bounds.Right, bounds.Top),
                    new Point2D(bounds.Right, bounds.Bottom),
                    new Point2D(bounds.Left, bounds.Bottom)
                };

                for (int cornerIdx = 0; cornerIdx < corners.Length; cornerIdx++)
                {
                    if (DistPoint(corners[cornerIdx], point2D) <= HandleHitRadius)
                    {
                        _dragState = DragState.ResizeBubble;
                        _dragPanelId = panelId;
                        _dragBubbleIndex = bubbleIdx;
                        _dragStartBounds = bounds;
                        _viewModel.BeginResizeBubble(panelId, bubbleIdx);
                        e.Pointer.Capture(this);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        // 4. Panel resize corners
        foreach (var panelId in _viewModel.Working.PanelOrder)
        {
            if (!_viewModel.Working.Panels.TryGetValue(panelId, out var panel))
                continue;

            var bounds = AnchorRing.BoundingBox(panel.Shape.Anchors);
            var corners = new[]
            {
                new Point2D(bounds.Left, bounds.Top),
                new Point2D(bounds.Right, bounds.Top),
                new Point2D(bounds.Right, bounds.Bottom),
                new Point2D(bounds.Left, bounds.Bottom)
            };

            for (int cornerIdx = 0; cornerIdx < corners.Length; cornerIdx++)
            {
                if (DistPoint(corners[cornerIdx], point2D) <= HandleHitRadius)
                {
                    _dragState = DragState.ResizePanel;
                    _dragPanelId = panelId;
                    _dragStartBounds = bounds;
                    _viewModel.BeginResizePanel(panelId);
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }
            }
        }

        // 5. Panel boundary (only if exactly 2 panels)
        if (_viewModel.Working.PanelOrder.Count == 2)
        {
            var panelId1 = _viewModel.Working.PanelOrder[0];
            var panelId2 = _viewModel.Working.PanelOrder[1];
            if (_viewModel.Working.Panels.TryGetValue(panelId1, out var panel1) &&
                _viewModel.Working.Panels.TryGetValue(panelId2, out var panel2))
            {
                var bounds1 = AnchorRing.BoundingBox(panel1.Shape.Anchors);
                var bounds2 = AnchorRing.BoundingBox(panel2.Shape.Anchors);

                // Check if panels are side-by-side (vertical boundary) or stacked (horizontal)
                double boundaryPos = -1;
                BoundaryOrientation orientation = BoundaryOrientation.Vertical;

                if (Math.Abs(bounds1.Right - bounds2.Left) < 1)
                {
                    // Vertical boundary between them
                    boundaryPos = bounds1.Right;
                    orientation = BoundaryOrientation.Vertical;
                    if (Math.Abs(point2D.X - boundaryPos) <= HandleHitRadius &&
                        point2D.Y >= Math.Min(bounds1.Top, bounds2.Top) - HandleHitRadius &&
                        point2D.Y <= Math.Max(bounds1.Bottom, bounds2.Bottom) + HandleHitRadius)
                    {
                        _dragBoundary = new PanelBoundaryDrag(orientation, [panelId1], [panelId2]);
                        _dragState = DragState.DragBoundary;
                        _viewModel.BeginDragBoundary(_dragBoundary);
                        e.Pointer.Capture(this);
                        e.Handled = true;
                        return;
                    }
                }
                else if (Math.Abs(bounds1.Bottom - bounds2.Top) < 1)
                {
                    // Horizontal boundary between them
                    boundaryPos = bounds1.Bottom;
                    orientation = BoundaryOrientation.Horizontal;
                    if (Math.Abs(point2D.Y - boundaryPos) <= HandleHitRadius &&
                        point2D.X >= Math.Min(bounds1.Left, bounds2.Left) - HandleHitRadius &&
                        point2D.X <= Math.Max(bounds1.Right, bounds2.Right) + HandleHitRadius)
                    {
                        _dragBoundary = new PanelBoundaryDrag(orientation, [panelId1], [panelId2]);
                        _dragState = DragState.DragBoundary;
                        _viewModel.BeginDragBoundary(_dragBoundary);
                        e.Pointer.Capture(this);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_viewModel == null)
            return;

        var pos = e.GetPosition(this);
        var point2D = new Point2D(pos.X, pos.Y);

        switch (_dragState)
        {
            case DragState.ResizePanel:
                if (_dragPanelId.HasValue)
                {
                    var newBounds = ComputeResizeBounds(_dragStartBounds, point2D);
                    _viewModel.UpdateResizePanel(_dragPanelId.Value, newBounds);
                    InvalidateVisual();
                }
                break;

            case DragState.ResizeBubble:
                if (_dragPanelId.HasValue)
                {
                    var newBounds = ComputeResizeBounds(_dragStartBounds, point2D);
                    _viewModel.UpdateResizeBubble(_dragPanelId.Value, _dragBubbleIndex, newBounds);
                    InvalidateVisual();
                }
                break;

            case DragState.MoveTailTarget:
                if (_dragPanelId.HasValue && _dragTailIndex >= 0)
                {
                    _viewModel.UpdateMoveBubbleTail(_dragPanelId.Value, _dragBubbleIndex, _dragTailIndex, point2D);
                    InvalidateVisual();
                }
                break;

            case DragState.SlideTailAttachment:
                if (_dragPanelId.HasValue && _dragTailIndex >= 0)
                {
                    _viewModel.UpdateSlideBubbleTailAttachment(_dragPanelId.Value, _dragBubbleIndex, _dragTailIndex, point2D);
                    InvalidateVisual();
                }
                break;

            case DragState.DragBoundary:
                if (_dragBoundary != null)
                {
                    var newPosition = _dragBoundary.Orientation == BoundaryOrientation.Vertical ? point2D.X : point2D.Y;
                    _viewModel.UpdateDragBoundary(_dragBoundary, newPosition);
                    InvalidateVisual();
                }
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragState != DragState.None)
        {
            _viewModel?.CommitGesture();
            _dragState = DragState.None;
            _dragPanelId = null;
            _dragBubbleIndex = -1;
            _dragTailIndex = -1;
            _dragBoundary = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _dragState != DragState.None)
        {
            _viewModel?.CancelGesture();
            _dragState = DragState.None;
            _dragPanelId = null;
            _dragBubbleIndex = -1;
            _dragTailIndex = -1;
            _dragBoundary = null;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private Rect2D ComputeResizeBounds(Rect2D original, Point2D currentMouse)
    {
        var minX = original.Left;
        var minY = original.Top;
        var maxX = Math.Max(currentMouse.X, original.Left + 20);
        var maxY = Math.Max(currentMouse.Y, original.Top + 20);
        return Rect2D.FromEdges(minX, minY, maxX, maxY);
    }

    private double DistPoint(Point2D a, Point2D b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private enum DragState
    {
        None,
        ResizePanel,
        ResizeBubble,
        MoveTailTarget,
        SlideTailAttachment,
        DragBoundary
    }
}

public sealed class PageCanvasDrawOperation : ICustomDrawOperation
{
    private readonly Rect _bounds;
    private readonly PageDocument _document;
    private readonly Rect2D _pageBounds;

    public PageCanvasDrawOperation(Rect bounds, PageDocument document, Rect2D pageBounds)
    {
        _bounds = bounds;
        _document = document;
        _pageBounds = pageBounds;
    }

    public Rect Bounds => _bounds;

    public void Dispose() { }

    public bool Equals(ICustomDrawOperation? other) => false;

    public bool HitTest(Point p) => _bounds.Contains(p);

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature == null)
            return;

        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;

        // Draw page background
        using (var paint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(new SKRect(
                (float)_pageBounds.Left, (float)_pageBounds.Top,
                (float)_pageBounds.Right, (float)_pageBounds.Bottom), paint);
        }

        // Draw each panel
        foreach (var panelId in _document.PanelOrder)
        {
            if (!_document.Panels.TryGetValue(panelId, out var panel))
                continue;

            // Draw panel outline
            using (var path = PanelRenderer.ToSkPath(panel.Shape))
            using (var paint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                IsAntialias = true
            })
            {
                canvas.DrawPath(path, paint);
            }

            // Draw bubbles
            foreach (var bubble in panel.Bubbles)
            {
                BubbleRenderer.Draw(canvas, bubble, SKColors.White, SKColors.Black, 3f);
            }

            // Draw panel corners (resize handles)
            DrawPanelHandles(canvas, panel);
        }

        // Draw boundary handles (only if exactly 2 panels)
        if (_document.PanelOrder.Count == 2)
        {
            DrawBoundaryHandles(canvas);
        }
    }

    private void DrawPanelHandles(SKCanvas canvas, PanelModel panel)
    {
        var bounds = AnchorRing.BoundingBox(panel.Shape.Anchors);
        var corners = new[]
        {
            new Point2D(bounds.Left, bounds.Top),
            new Point2D(bounds.Right, bounds.Top),
            new Point2D(bounds.Right, bounds.Bottom),
            new Point2D(bounds.Left, bounds.Bottom)
        };

        using (var paint = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
        using (var strokePaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = true
        })
        {
            foreach (var corner in corners)
            {
                canvas.DrawCircle((float)corner.X, (float)corner.Y, 5f, paint);
                canvas.DrawCircle((float)corner.X, (float)corner.Y, 5f, strokePaint);
            }
        }
    }

    private void DrawBoundaryHandles(SKCanvas canvas)
    {
        if (_document.PanelOrder.Count != 2)
            return;

        var panelId1 = _document.PanelOrder[0];
        var panelId2 = _document.PanelOrder[1];

        if (!_document.Panels.TryGetValue(panelId1, out var panel1) ||
            !_document.Panels.TryGetValue(panelId2, out var panel2))
            return;

        var bounds1 = AnchorRing.BoundingBox(panel1.Shape.Anchors);
        var bounds2 = AnchorRing.BoundingBox(panel2.Shape.Anchors);

        using (var paint = new SKPaint
        {
            Color = SKColors.OrangeRed,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4f,
            IsAntialias = true
        })
        {
            // Vertical boundary
            if (Math.Abs(bounds1.Right - bounds2.Left) < 1)
            {
                var x = (float)bounds1.Right;
                var y1 = (float)Math.Min(bounds1.Top, bounds2.Top);
                var y2 = (float)Math.Max(bounds1.Bottom, bounds2.Bottom);
                canvas.DrawLine(x, y1, x, y2, paint);
            }
            // Horizontal boundary
            else if (Math.Abs(bounds1.Bottom - bounds2.Top) < 1)
            {
                var y = (float)bounds1.Bottom;
                var x1 = (float)Math.Min(bounds1.Left, bounds2.Left);
                var x2 = (float)Math.Max(bounds1.Right, bounds2.Right);
                canvas.DrawLine(x1, y, x2, y, paint);
            }
        }
    }
}
