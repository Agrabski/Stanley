using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Stanley.Bubbles;

namespace Stanley.App;

/// <summary>
/// Hosts a single <see cref="SpeechBubble"/>: renders it via a raw Skia draw
/// operation and handles pointer drags for resizing (corner handles), moving a
/// tail's tip (orange handle) and sliding a tail's attachment point along the
/// outline (green handle). Right-click a tail's tip to remove that tail.
/// </summary>
public sealed class BubbleCanvasControl : Control
{
    private const float HandleSize = 10f;
    private const float HandleHitRadius = 12f;
    private const float MinBubbleSize = 60f;

    private readonly SpeechBubble _bubble = new(
        new SKRect(300, 220, 620, 400),
        BubbleStylePreset.Speech);

    private DragState _drag = DragState.None;
    private int _dragTailIndex = -1;

    /// <summary>Exposed for headless UI tests to assert on model state after simulated input.</summary>
    public SpeechBubble Bubble => _bubble;

    public BubbleCanvasControl()
    {
        _bubble.AddTail(new SKPoint(220, 480));
        ClipToBounds = true;
        Focusable = true;
    }

    public void SetStyle(BubbleStylePreset style)
    {
        _bubble.SetStyle(style);
        InvalidateVisual();
    }

    public void AddTail()
    {
        var b = _bubble.Bounds;
        _bubble.AddTail(new SKPoint(b.MidX, b.Bottom + 120));
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.Custom(new BubbleDrawOperation(new Rect(Bounds.Size), _bubble));
        base.Render(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = ToSk(e.GetPosition(this));
        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsRightButtonPressed)
        {
            for (var i = _bubble.Tails.Count - 1; i >= 0; i--)
            {
                if (Dist(_bubble.Tails[i].Target, p) > HandleHitRadius) continue;
                _bubble.RemoveTail(_bubble.Tails[i]);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            return;
        }

        // Tail-tip handles first (drawn on top, most recently added checked first).
        for (var i = _bubble.Tails.Count - 1; i >= 0; i--)
        {
            if (Dist(_bubble.Tails[i].Target, p) > HandleHitRadius) continue;
            _drag = DragState.MoveTailTarget;
            _dragTailIndex = i;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Tail-attachment handles.
        for (var i = _bubble.Tails.Count - 1; i >= 0; i--)
        {
            var attachPoint = _bubble.Outline.PointAt(_bubble.Tails[i].AttachmentT);
            if (Dist(attachPoint, p) > HandleHitRadius) continue;
            _drag = DragState.MoveTailAttachment;
            _dragTailIndex = i;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Corner resize handles.
        var corner = HitTestCorner(p);
        if (corner == DragState.None) return;
        _drag = corner;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag == DragState.None) return;
        var p = ToSk(e.GetPosition(this));

        switch (_drag)
        {
            case DragState.MoveTailTarget:
                _bubble.Tails[_dragTailIndex].Target = p;
                break;
            case DragState.MoveTailAttachment:
                _bubble.Tails[_dragTailIndex].AttachmentT = _bubble.Outline.NearestT(p);
                break;
            default:
                ResizeFromCorner(_drag, p);
                break;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == DragState.None) return;
        _drag = DragState.None;
        _dragTailIndex = -1;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ResizeFromCorner(DragState corner, SKPoint p)
    {
        var b = _bubble.Bounds;
        var newBounds = corner switch
        {
            DragState.ResizeTopLeft => SKRect.Create(p.X, p.Y, b.Right - p.X, b.Bottom - p.Y),
            DragState.ResizeTopRight => SKRect.Create(b.Left, p.Y, p.X - b.Left, b.Bottom - p.Y),
            DragState.ResizeBottomLeft => SKRect.Create(p.X, b.Top, b.Right - p.X, p.Y - b.Top),
            DragState.ResizeBottomRight => SKRect.Create(b.Left, b.Top, p.X - b.Left, p.Y - b.Top),
            _ => b
        };

        if (newBounds.Width < MinBubbleSize || newBounds.Height < MinBubbleSize) return;
        _bubble.Resize(newBounds);
    }

    private DragState HitTestCorner(SKPoint p)
    {
        var b = _bubble.Bounds;
        if (Dist(new SKPoint(b.Left, b.Top), p) <= HandleHitRadius) return DragState.ResizeTopLeft;
        if (Dist(new SKPoint(b.Right, b.Top), p) <= HandleHitRadius) return DragState.ResizeTopRight;
        if (Dist(new SKPoint(b.Left, b.Bottom), p) <= HandleHitRadius) return DragState.ResizeBottomLeft;
        if (Dist(new SKPoint(b.Right, b.Bottom), p) <= HandleHitRadius) return DragState.ResizeBottomRight;
        return DragState.None;
    }

    private static float Dist(SKPoint a, SKPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static SKPoint ToSk(Point p) => new((float)p.X, (float)p.Y);

    private enum DragState
    {
        None,
        ResizeTopLeft,
        ResizeTopRight,
        ResizeBottomLeft,
        ResizeBottomRight,
        MoveTailTarget,
        MoveTailAttachment
    }

    private sealed class BubbleDrawOperation : ICustomDrawOperation
    {
        private readonly SpeechBubble _bubble;

        public BubbleDrawOperation(Rect bounds, SpeechBubble bubble)
        {
            Bounds = bounds;
            _bubble = bubble;
        }

        public Rect Bounds { get; }

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature leaseFeature)
                return;

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;

            canvas.Clear(new SKColor(0xF2, 0xF2, 0xF2));

            using var fillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var strokePaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3,
                IsAntialias = true
            };
            if (BubbleStylePresets.UsesDashedStroke(_bubble.Style))
                strokePaint.PathEffect = SKPathEffect.CreateDash(new float[] { 10, 8 }, 0);

            using var renderPath = _bubble.BuildRenderPath();
            canvas.DrawPath(renderPath, fillPaint);
            canvas.DrawPath(renderPath, strokePaint);

            DrawHandles(canvas);
        }

        private void DrawHandles(SKCanvas canvas)
        {
            using var handleStroke = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };

            using var cornerFill = new SKPaint { Color = SKColors.DodgerBlue, Style = SKPaintStyle.Fill, IsAntialias = true };
            var b = _bubble.Bounds;
            foreach (var corner in new[] { new SKPoint(b.Left, b.Top), new SKPoint(b.Right, b.Top), new SKPoint(b.Left, b.Bottom), new SKPoint(b.Right, b.Bottom) })
            {
                var rect = SKRect.Create(corner.X - HandleSize / 2, corner.Y - HandleSize / 2, HandleSize, HandleSize);
                canvas.DrawRect(rect, cornerFill);
                canvas.DrawRect(rect, handleStroke);
            }

            using var tailHandleFill = new SKPaint { Color = SKColors.OrangeRed, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var attachHandleFill = new SKPaint { Color = SKColors.MediumSeaGreen, Style = SKPaintStyle.Fill, IsAntialias = true };
            foreach (var tail in _bubble.Tails)
            {
                canvas.DrawCircle(tail.Target, 7, tailHandleFill);
                canvas.DrawCircle(tail.Target, 7, handleStroke);

                var attach = _bubble.Outline.PointAt(tail.AttachmentT);
                canvas.DrawCircle(attach, 6, attachHandleFill);
                canvas.DrawCircle(attach, 6, handleStroke);
            }
        }
    }
}
