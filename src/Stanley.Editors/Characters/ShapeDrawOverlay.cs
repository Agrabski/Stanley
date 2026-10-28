using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Stanley.Editing;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>
/// Draw shape on the character editor's stage (docs/shape-characters.md §9.3): laid over the
/// stage while <see cref="CharacterEditorViewModel.IsDrawingShape"/>, it takes the clicks, with
/// the page's Freeform gestures (docs/shape-editing.md §2) - each click places a corner, a drag
/// pulls out a smooth point (<see cref="ShapePointEditing.Placed"/>), Shift keeps the next point
/// at 45° steps; clicking the first point again, a double-click or Enter closes the shape (the
/// view model makes it the part's), Backspace takes the last point back, Esc stops. Draws the
/// points, the outline so far and the edge out to the pointer. Points are kept in figure units,
/// through the stage's placement, so the stage and the overlay can't disagree.
/// </summary>
public sealed class ShapeDrawOverlay : Control
{
    public static readonly StyledProperty<CharacterFigure?> StageProperty =
        AvaloniaProperty.Register<ShapeDrawOverlay, CharacterFigure?>(nameof(Stage));

    /// <summary>How near (pixels) a click on the first point has to be to close the shape.</summary>
    public const double CloseRadius = 9;

    /// <summary>How far (pixels) the pointer goes before a press becomes a drag that curves the point.</summary>
    private const double DragThreshold = 3;

    private const double PointHalf = 4;

    private static readonly Color Accent = Color.FromRgb(0x25, 0x7A, 0xE8);
    private static readonly IBrush AccentBrush = new SolidColorBrush(Accent);
    private static readonly IBrush DraftFill = new SolidColorBrush(Accent, 0.18);
    private static readonly IPen OutlinePen = new Pen(AccentBrush, 1.5);
    private static readonly IPen DashedPen = new Pen(AccentBrush, 1, new DashStyle([4, 3], 0));
    private static readonly IPen RingPen = new Pen(AccentBrush, 2);
    private static readonly IPen PointPen = new Pen(AccentBrush, 1.5);

    private CharacterEditorViewModel? _editor;
    private Point? _pointer;
    private (Point Pressed, Point2D Placed)? _press;
    private bool _pulling;

    public ShapeDrawOverlay()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        ClipToBounds = true;
    }

    /// <summary>The stage the shape is drawn on: its placement turns pointer positions into figure units.</summary>
    public CharacterFigure? Stage
    {
        get => GetValue(StageProperty);
        set => SetValue(StageProperty, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_editor != null)
            _editor.PropertyChanged -= OnEditorChanged;
        _editor = DataContext as CharacterEditorViewModel;
        if (_editor != null)
            _editor.PropertyChanged += OnEditorChanged;
        Sync();
    }

    private void OnEditorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CharacterEditorViewModel.IsDrawingShape))
            Sync();
        else if (e.PropertyName == nameof(CharacterEditorViewModel.DrawnShape))
            InvalidateVisual();
    }

    /// <summary>Shown (and given the keyboard, for Enter, Esc and Backspace) while drawing; out of the way otherwise.</summary>
    private void Sync()
    {
        var drawing = _editor?.IsDrawingShape == true;
        IsVisible = drawing;
        IsHitTestVisible = drawing;
        if (!drawing)
        {
            _press = null;
            _pulling = false;
            _pointer = null;
        }
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Focus());
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- input

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_editor is not { IsDrawingShape: true } editor || ToFigure(e.GetPosition(this)) is not { } at)
            return;
        e.Handled = true;
        Focus();
        var point = e.GetCurrentPoint(this);
        // A right-click closes it, like Enter (or does nothing with too few points to close).
        if (point.Properties.IsRightButtonPressed)
        {
            editor.CloseDrawnShape();
            return;
        }
        if (!point.Properties.IsLeftButtonPressed)
            return;
        var points = editor.DrawnShape;
        if (e.ClickCount >= 2 && points.Count > 0 && Placement() is { } placement && IsNear(ToScreen(placement, points[^1].Point), e.GetPosition(this)))
        {
            // A double-click closes it: the point its first click placed goes again.
            editor.TakeBackDrawnPoint();
            if (editor.CanCloseDrawnShape)
                editor.CloseDrawnShape();
            return;
        }
        if (editor.CanCloseDrawnShape && IsOnFirstPoint(e.GetPosition(this)))
        {
            editor.CloseDrawnShape();
            return;
        }
        if (points.Count > 0 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            at = ShapePointEditing.SnapAngle(points[^1].Point, at);
        editor.PlaceDrawnPoint(at);
        _press = (e.GetPosition(this), at);
        _pulling = false;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_editor is not { IsDrawingShape: true } editor)
            return;
        var p = e.GetPosition(this);
        if (_press is { } press)
        {
            if (!_pulling && Math.Abs(p.X - press.Pressed.X) < DragThreshold && Math.Abs(p.Y - press.Pressed.Y) < DragThreshold)
                return;
            _pulling = true;
            if (ToFigure(p) is { } pull)
                editor.PullDrawnPoint(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? ShapePointEditing.SnapAngle(press.Placed, pull) : pull);
            return;
        }
        _pointer = p;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _press = null;
        _pulling = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointer = null;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_editor is not { IsDrawingShape: true } editor)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                editor.CloseDrawnShape();
                e.Handled = true;
                break;
            case Key.Escape:
                editor.CancelDrawShape();
                e.Handled = true;
                break;
            case Key.Back or Key.Delete:
                editor.TakeBackDrawnPoint();
                e.Handled = true;
                break;
        }
    }

    // ---------------------------------------------------------------- drawing

    public override void Render(DrawingContext context)
    {
        // Transparent, but there: the whole stage takes the clicks while drawing.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_editor is not { IsDrawingShape: true } editor || editor.DrawnShape is not { Count: > 0 } anchors || Placement() is not { } placement)
            return;
        var screen = anchors.Select(a => new ShapeAnchor(ToScreen(placement, a.Point), ToScreen(placement, a.InHandle), ToScreen(placement, a.OutHandle), a.HandleKind)).ToList();
        var closing = editor.CanCloseDrawnShape && _pointer is { } hover && IsOnFirstPoint(hover);

        var outline = new StreamGeometry();
        using (var g = outline.Open())
        {
            g.BeginFigure(P(screen[0].Point), closing);
            for (var i = 1; i < screen.Count; i++)
                g.CubicBezierTo(P(screen[i - 1].OutHandle), P(screen[i].InHandle), P(screen[i].Point));
            if (closing)
                g.CubicBezierTo(P(screen[^1].OutHandle), P(screen[0].InHandle), P(screen[0].Point));
            g.EndFigure(closing);
        }
        context.DrawGeometry(closing ? DraftFill : null, OutlinePen, outline);

        // The edge the next click would make, out to the pointer.
        if (_pointer is { } pointer && _press is null && !closing)
        {
            var next = new StreamGeometry();
            using (var g = next.Open())
            {
                g.BeginFigure(P(screen[^1].Point), false);
                g.CubicBezierTo(P(screen[^1].OutHandle), pointer, pointer);
                g.EndFigure(false);
            }
            context.DrawGeometry(null, DashedPen, next);
        }

        // The last point's handles while it's being pulled out, then every point.
        var last = screen[^1];
        if (last.HandleKind == AnchorHandleKind.Smooth)
        {
            foreach (var handle in new[] { last.InHandle, last.OutHandle })
            {
                context.DrawLine(PointPen, P(last.Point), P(handle));
                context.DrawEllipse(Brushes.White, PointPen, P(handle), 3, 3);
            }
        }
        for (var i = 0; i < screen.Count; i++)
        {
            var at = P(screen[i].Point);
            var fill = i == screen.Count - 1 ? AccentBrush : Brushes.White;
            if (screen[i].HandleKind == AnchorHandleKind.Smooth)
                context.DrawEllipse(fill, PointPen, at, PointHalf + 0.5, PointHalf + 0.5);
            else
                context.DrawRectangle(fill, PointPen, new Rect(at.X - PointHalf, at.Y - PointHalf, 2 * PointHalf, 2 * PointHalf));
        }
        if (closing)
            context.DrawEllipse(null, RingPen, P(screen[0].Point), PointHalf + 4, PointHalf + 4);
    }

    // ---------------------------------------------------------------- coordinates

    private CharacterPlacement? Placement() => Stage?.MainPlacement;

    /// <summary>A point on the overlay in figure units, through the stage's placement; null with no character on the stage.</summary>
    private Point2D? ToFigure(Point p)
    {
        if (Stage is not { } stage || Placement() is not { } placement || this.TranslatePoint(p, stage) is not { } onStage)
            return null;
        return placement.ToFigure(new Point2D(onStage.X, onStage.Y));
    }

    private Point2D ToScreen(CharacterPlacement placement, Point2D figure)
    {
        var page = placement.ToPage(figure);
        var p = Stage?.TranslatePoint(new Point(page.X, page.Y), this) ?? new Point(page.X, page.Y);
        return new Point2D(p.X, p.Y);
    }

    private bool IsOnFirstPoint(Point p)
    {
        if (_editor?.DrawnShape is not { Count: > 0 } anchors || Placement() is not { } placement)
            return false;
        return IsNear(ToScreen(placement, anchors[0].Point), p);
    }

    private static bool IsNear(Point2D a, Point b) => Math.Abs(a.X - b.X) <= CloseRadius && Math.Abs(a.Y - b.Y) <= CloseRadius;

    private static Point P(Point2D p) => new(p.X, p.Y);
}
