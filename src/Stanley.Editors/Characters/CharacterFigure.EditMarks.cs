using Avalonia;
using SkiaSharp;
using Stanley.Editing;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>What of the edit marks over a part is under the pointer (<see cref="CharacterFigure.EditMarkAt"/>).</summary>
public enum ModuleEditHitKind
{
    /// <summary>One of the selected point's curve handles.</summary>
    Handle,

    /// <summary>A point of the outline.</summary>
    Point,

    /// <summary>The outline between points, where a press adds one.</summary>
    Outline,

    /// <summary>A socket's dot.</summary>
    Socket
}

/// <summary>A hit on the edit marks: a handle (<paramref name="Index"/>, <paramref name="Side"/>), a point, a spot on the outline (<paramref name="Segment"/>, <paramref name="T"/>) or a socket (<paramref name="Socket"/>).</summary>
public sealed record ModuleEditHit(ModuleEditHitKind Kind, int Index = -1, HandleSide Side = HandleSide.Out, int Segment = -1, double T = 0, string? Socket = null);

/// <summary>
/// The character editor's stage while a head's or torso's points or sockets are edited
/// (docs/shape-characters.md §9.2): the marks drawn over the part - its outline traced thin,
/// a square per corner and a circle per smooth point, the selected point filled in with its
/// curve handles on stalks, a hollow dot where a press would add a point; or each socket as
/// a labelled dot, filled once placed by hand - and what of them is under the pointer. They
/// look and hit-test like the page's Edit Points (docs/shape-editing.md), in pixels whatever
/// the stage's scale.
/// </summary>
public sealed partial class CharacterFigure
{
    public static readonly StyledProperty<ModuleEditMarks?> EditMarksProperty =
        AvaloniaProperty.Register<CharacterFigure, ModuleEditMarks?>(nameof(EditMarks));

    /// <summary>The picked part's outline and selected point, or its sockets, to draw and hit-test (figure space), or null.</summary>
    public ModuleEditMarks? EditMarks
    {
        get => GetValue(EditMarksProperty);
        set => SetValue(EditMarksProperty, value);
    }

    /// <summary>Where on the outline being edited a press would add a point (figure space), shown as a hollow dot; null for none.</summary>
    public Point2D? PointGhost
    {
        get;
        set
        {
            if (field == value)
                return;
            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>How near (pixels) a press has to be to a point, a handle or a socket.</summary>
    public const double PointHitRadius = 9;

    /// <summary>How near (pixels) a press has to be to the outline to add a point there.</summary>
    public const double OutlineHitRadius = 6;

    private const float PointHalf = 4f;
    private const float SocketRadius = 5f;
    private static readonly SKColor EditAccent = new(0x1E, 0x5A, 0xA8);

    /// <summary>
    /// What of the edit marks is under <paramref name="point"/> (control coordinates): the
    /// selected point's handles first (they sit over everything), then the nearest point, then
    /// the outline; or the nearest socket. Null when nothing is.
    /// </summary>
    public ModuleEditHit? EditMarkAt(Point point)
    {
        if (EditMarks is not { } marks || MainPlacement is not { } placement)
            return null;
        var p = new Point2D(point.X, point.Y);
        if (marks.Outline is { } outline)
        {
            var screen = OnScreen(outline, placement);
            if (marks.SelectedPoint >= 0 && marks.SelectedPoint < screen.Anchors.Count)
            {
                foreach (var (side, handle) in ShapePointEditing.VisibleHandles(screen, marks.SelectedPoint))
                {
                    if (Dist(handle, p) <= PointHitRadius)
                        return new ModuleEditHit(ModuleEditHitKind.Handle, marks.SelectedPoint, side);
                }
            }
            var nearest = -1;
            var nearestDistance = PointHitRadius;
            for (var i = 0; i < screen.Anchors.Count; i++)
            {
                var d = Dist(screen.Anchors[i].Point, p);
                if (d <= nearestDistance)
                    (nearest, nearestDistance) = (i, d);
            }
            if (nearest >= 0)
                return new ModuleEditHit(ModuleEditHitKind.Point, nearest);
            if (ShapePointEditing.Nearest(screen, p) is { } spot && spot.Distance <= OutlineHitRadius)
                return new ModuleEditHit(ModuleEditHitKind.Outline, Segment: spot.Segment, T: spot.T);
        }
        string? socket = null;
        var socketDistance = PointHitRadius;
        foreach (var spot in marks.Sockets)
        {
            var d = Dist(placement.ToPage(spot.At), p);
            if (d <= socketDistance)
                (socket, socketDistance) = (spot.Name, d);
        }
        return socket is null ? null : new ModuleEditHit(ModuleEditHitKind.Socket, Socket: socket);
    }

    /// <summary>Whether <paramref name="point"/> (control coordinates) is inside the outline being edited - a press there keeps on editing it.</summary>
    public bool IsInsideEditedOutline(Point point)
    {
        if (EditMarks?.Outline is not { } outline || MainPlacement is not { } placement)
            return false;
        using var path = AnchorRingPath.ToSkPath(OnScreen(outline, placement).Anchors);
        return path.Contains((float)point.X, (float)point.Y);
    }

    /// <summary>The outline in control coordinates.</summary>
    private static ShapeElement OnScreen(ShapeElement outline, ProjectModel.Issues.CharacterPlacement placement) =>
        outline with { Anchors = outline.Anchors.Select(a => a with { Point = placement.ToPage(a.Point), InHandle = placement.ToPage(a.InHandle), OutHandle = placement.ToPage(a.OutHandle) }).ToList() };

    /// <summary>The edit marks over the main character standing at <paramref name="placement"/>.</summary>
    private static void DrawEditMarks(SKCanvas canvas, ProjectModel.Issues.CharacterPlacement placement, ModuleEditMarks marks, Point2D? ghost, bool dark)
    {
        if (marks.Outline is { } outline)
        {
            var screen = OnScreen(outline, placement);
            using (var path = AnchorRingPath.ToSkPath(screen.Anchors))
            using (var line = Stroke(EditAccent, 1f))
                canvas.DrawPath(path, line);
            if (marks.SelectedPoint >= 0 && marks.SelectedPoint < screen.Anchors.Count)
                DrawHandles(canvas, screen.Anchors[marks.SelectedPoint], ShapePointEditing.VisibleHandles(screen, marks.SelectedPoint));
            for (var i = 0; i < screen.Anchors.Count; i++)
                DrawPoint(canvas, AnchorRingPath.ToSk(screen.Anchors[i].Point), screen.Anchors[i].HandleKind, i == marks.SelectedPoint);
            if (ghost is { } at)
            {
                var spot = AnchorRingPath.ToSk(placement.ToPage(at));
                using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true };
                using var ring = Stroke(EditAccent, 1.5f);
                canvas.DrawCircle(spot, 3.5f, fill);
                canvas.DrawCircle(spot, 3.5f, ring);
            }
        }
        if (marks.Sockets.Count == 0)
            return;
        using var font = new SKFont(SKTypeface.Default, 10);
        using var label = new SKPaint { Color = dark ? SKColors.White : new SKColor(0x10, 0x20, 0x40), IsAntialias = true };
        using var halo = new SKPaint { Color = (dark ? SKColors.Black : SKColors.White).WithAlpha(210), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3, StrokeJoin = SKStrokeJoin.Round };
        foreach (var socket in marks.Sockets)
        {
            var at = AnchorRingPath.ToSk(placement.ToPage(socket.At));
            using var fill = new SKPaint { Color = socket.HandSet ? EditAccent : SKColors.White, IsAntialias = true };
            using var ring = Stroke(socket.HandSet ? SKColors.White : EditAccent, 1.5f);
            canvas.DrawCircle(at, SocketRadius, fill);
            canvas.DrawCircle(at, SocketRadius, ring);
            // The label beside the dot - on the outer side for a shoulder, below and outward for a
            // hip (where the leg would hide it) - so a pair never run into each other.
            var text = CharacterEditorViewModel.SocketLabel(socket.Name);
            var (x, y, align) = socket.Name switch
            {
                ProjectModel.Characters.ModuleSockets.LeftShoulder => (at.X + SocketRadius + 3, at.Y + 3.5f, SKTextAlign.Left),
                ProjectModel.Characters.ModuleSockets.RightShoulder => (at.X - SocketRadius - 3, at.Y + 3.5f, SKTextAlign.Right),
                ProjectModel.Characters.ModuleSockets.LeftHip => (at.X + 2, at.Y + SocketRadius + 11, SKTextAlign.Left),
                ProjectModel.Characters.ModuleSockets.RightHip => (at.X - 2, at.Y + SocketRadius + 11, SKTextAlign.Right),
                _ => (at.X + SocketRadius + 3, at.Y + 3.5f, SKTextAlign.Left),
            };
            canvas.DrawText(text, x, y, align, font, halo);
            canvas.DrawText(text, x, y, align, font, label);
        }
    }

    private static void DrawHandles(SKCanvas canvas, ShapeAnchor anchor, IEnumerable<(HandleSide Side, Point2D Point)> handles)
    {
        using var stalk = Stroke(EditAccent, 1f);
        using var fill = new SKPaint { Color = EditAccent, IsAntialias = true };
        using var ring = Stroke(SKColors.White, 1.5f);
        var from = AnchorRingPath.ToSk(anchor.Point);
        foreach (var (_, handle) in handles)
        {
            var to = AnchorRingPath.ToSk(handle);
            canvas.DrawLine(from, to, stalk);
            canvas.DrawCircle(to, 4f, fill);
            canvas.DrawCircle(to, 4f, ring);
        }
    }

    private static void DrawPoint(SKCanvas canvas, SKPoint at, AnchorHandleKind kind, bool selected)
    {
        using var fill = new SKPaint { Color = selected ? EditAccent : SKColors.White, IsAntialias = true };
        using var stroke = Stroke(selected ? SKColors.White : EditAccent, 1.5f);
        if (kind == AnchorHandleKind.Smooth)
        {
            canvas.DrawCircle(at, PointHalf + 0.5f, fill);
            canvas.DrawCircle(at, PointHalf + 0.5f, stroke);
        }
        else
        {
            var rect = new SKRect(at.X - PointHalf, at.Y - PointHalf, at.X + PointHalf, at.Y + PointHalf);
            canvas.DrawRect(rect, fill);
            canvas.DrawRect(rect, stroke);
        }
    }

    private static double Dist(Point2D a, Point2D b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static SKPaint Stroke(SKColor color, float width) => new() { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };
}
