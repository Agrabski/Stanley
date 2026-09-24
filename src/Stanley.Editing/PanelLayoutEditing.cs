using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// Editing operations for a page's panel layout. Scoped to axis-aligned rectangular
/// panels - the default a panel-layout editor creates and resizes - so a resize always
/// replaces a panel's shape with a fresh <see cref="PanelShapes.Rectangle"/> at the new
/// bounds, whatever shape it had before. An arbitrary hand-edited <see cref="PanelShape"/>
/// is still the escape hatch for panel outlines beyond what this editor covers; it just
/// isn't draggable through these operations.
///
/// A panel's bubbles and characters belong to it: every operation here carries them along
/// (<see cref="BubbleEditing.Refit"/>, <see cref="CharacterPlacementEditing.Refit"/>) and
/// keeps bubbles inside the panel's new bounds.
/// </summary>
public static class PanelLayoutEditing
{
    public const double MinPanelSizeMm = 20;

    public static EditResult<Panel> Resize(Panel panel, Rect2D newBounds, Rect2D pageBounds)
    {
        if (newBounds.Width < MinPanelSizeMm - 1e-9 || newBounds.Height < MinPanelSizeMm - 1e-9)
            return EditResult<Panel>.Failure($"A panel must be at least {MinPanelSizeMm}x{MinPanelSizeMm}mm.");

        const double epsilon = 0.01;
        if (newBounds.Left < pageBounds.Left - epsilon || newBounds.Top < pageBounds.Top - epsilon ||
            newBounds.Right > pageBounds.Right + epsilon || newBounds.Bottom > pageBounds.Bottom + epsilon)
            return EditResult<Panel>.Failure("A panel can't extend past the page.");

        var oldBounds = AnchorRing.BoundingBox(panel.Shape.Anchors);
        return EditResult<Panel>.Success(panel with
        {
            Shape = PanelShapes.Rectangle(newBounds),
            CharacterInstances = panel.CharacterInstances.Select(c => CharacterPlacementEditing.Refit(c, oldBounds, newBounds)).ToList(),
            Bubbles = panel.Bubbles.Select(b => BubbleEditing.Refit(b, oldBounds, newBounds)).ToList()
        });
    }

    /// <summary>Translates a panel and everything in it, clamped so it never leaves the page (dragging past the edge just stops at the edge rather than failing).</summary>
    public static EditResult<Panel> Move(Panel panel, double dx, double dy, Rect2D pageBounds)
    {
        var bounds = AnchorRing.BoundingBox(panel.Shape.Anchors);
        var left = Math.Clamp(bounds.Left + dx, pageBounds.Left, Math.Max(pageBounds.Left, pageBounds.Right - bounds.Width));
        var top = Math.Clamp(bounds.Top + dy, pageBounds.Top, Math.Max(pageBounds.Top, pageBounds.Bottom - bounds.Height));
        return Resize(panel, bounds with { X = left, Y = top }, pageBounds);
    }

    /// <summary>
    /// Divides one panel into two rectangles along <paramref name="orientation"/>, at
    /// <paramref name="fraction"/> of its current bounds, leaving <paramref name="gutter"/>
    /// between them. Each bubble goes to whichever half its centre lies in (clamped inside
    /// it), each character to the half its feet are in (staying exactly where it was on the
    /// page); the background stays with the first half, which keeps the original id.
    /// </summary>
    public static EditResult<(Panel First, Panel Second)> Split(Panel panel, BoundaryOrientation orientation, double fraction, double gutter = 0)
    {
        if (fraction <= 0 || fraction >= 1)
            return EditResult<(Panel, Panel)>.Failure("Split fraction must be strictly between 0 and 1.");

        var bounds = AnchorRing.BoundingBox(panel.Shape.Anchors);
        var halfGutter = gutter / 2;
        Rect2D firstBounds, secondBounds;
        if (orientation == BoundaryOrientation.Vertical)
        {
            var splitX = bounds.Left + bounds.Width * fraction;
            firstBounds = Rect2D.FromEdges(bounds.Left, bounds.Top, splitX - halfGutter, bounds.Bottom);
            secondBounds = Rect2D.FromEdges(splitX + halfGutter, bounds.Top, bounds.Right, bounds.Bottom);
        }
        else
        {
            var splitY = bounds.Top + bounds.Height * fraction;
            firstBounds = Rect2D.FromEdges(bounds.Left, bounds.Top, bounds.Right, splitY - halfGutter);
            secondBounds = Rect2D.FromEdges(bounds.Left, splitY + halfGutter, bounds.Right, bounds.Bottom);
        }

        if (firstBounds.Width < MinPanelSizeMm || firstBounds.Height < MinPanelSizeMm ||
            secondBounds.Width < MinPanelSizeMm || secondBounds.Height < MinPanelSizeMm)
            return EditResult<(Panel, Panel)>.Failure($"Split would leave a panel under {MinPanelSizeMm}x{MinPanelSizeMm}mm.");

        bool InSecond(Bubble b)
        {
            var bb = AnchorRing.BoundingBox(b.Shape.Anchors);
            return orientation == BoundaryOrientation.Vertical ? bb.MidX >= secondBounds.Left - halfGutter : bb.MidY >= secondBounds.Top - halfGutter;
        }

        bool CharacterInSecond(CharacterInstance c) =>
            orientation == BoundaryOrientation.Vertical
                ? c.Placement.Ground.X >= secondBounds.Left - halfGutter
                : c.Placement.Ground.Y >= secondBounds.Top - halfGutter;

        var first = panel with
        {
            Shape = PanelShapes.Rectangle(firstBounds),
            CharacterInstances = panel.CharacterInstances.Where(c => !CharacterInSecond(c)).ToList(),
            Bubbles = panel.Bubbles.Where(b => !InSecond(b)).Select(b => BubbleEditing.KeepInside(b, firstBounds)).ToList()
        };
        var second = new Panel(
            PanelId.New(),
            PanelShapes.Rectangle(secondBounds),
            Background: null,
            CharacterInstances: panel.CharacterInstances.Where(CharacterInSecond).ToList(),
            Bubbles: panel.Bubbles.Where(InSecond).Select(b => BubbleEditing.KeepInside(b, secondBounds)).ToList());
        return EditResult<(Panel, Panel)>.Success((first, second));
    }

    /// <summary>Moves a shared boundary, resizing every panel on both sides to meet it: the "before" panels' edge lands on <paramref name="newPosition"/> (an X for a vertical boundary, a Y for a horizontal one), the "after" panels' edge one <see cref="PanelBoundaryDrag.Gap"/> further on.</summary>
    public static EditResult<IReadOnlyDictionary<PanelId, Panel>> DragBoundary(
        IReadOnlyDictionary<PanelId, Panel> panels,
        PanelBoundaryDrag boundary,
        double newPosition,
        Rect2D pageBounds)
    {
        var affected = boundary.PanelsBefore.Concat(boundary.PanelsAfter).ToList();
        foreach (var id in affected)
        {
            if (!panels.ContainsKey(id))
                return EditResult<IReadOnlyDictionary<PanelId, Panel>>.Failure($"Unknown panel '{id}'.");
        }

        var updated = new Dictionary<PanelId, Panel>(panels);

        foreach (var id in boundary.PanelsBefore)
        {
            var bounds = AnchorRing.BoundingBox(panels[id].Shape.Anchors);
            var newBounds = boundary.Orientation == BoundaryOrientation.Vertical
                ? Rect2D.FromEdges(bounds.Left, bounds.Top, newPosition, bounds.Bottom)
                : Rect2D.FromEdges(bounds.Left, bounds.Top, bounds.Right, newPosition);
            var result = Resize(panels[id], newBounds, pageBounds);
            if (!result.IsValid)
                return EditResult<IReadOnlyDictionary<PanelId, Panel>>.Failure(result.Error!);
            updated[id] = result.Value;
        }

        var afterPosition = newPosition + boundary.Gap;
        foreach (var id in boundary.PanelsAfter)
        {
            var bounds = AnchorRing.BoundingBox(panels[id].Shape.Anchors);
            var newBounds = boundary.Orientation == BoundaryOrientation.Vertical
                ? Rect2D.FromEdges(afterPosition, bounds.Top, bounds.Right, bounds.Bottom)
                : Rect2D.FromEdges(bounds.Left, afterPosition, bounds.Right, bounds.Bottom);
            var result = Resize(panels[id], newBounds, pageBounds);
            if (!result.IsValid)
                return EditResult<IReadOnlyDictionary<PanelId, Panel>>.Failure(result.Error!);
            updated[id] = result.Value;
        }

        return EditResult<IReadOnlyDictionary<PanelId, Panel>>.Success(updated);
    }

    /// <summary>
    /// Tiles the page's live area (page minus <see cref="PanelGrid.MarginMm"/>) into rows
    /// of equal height, row <c>i</c> holding <paramref name="columnsPerRow"/>[i] equal-width
    /// panels, with <see cref="PanelGrid.GutterMm"/> between every neighbour. Returned in
    /// reading order (left-to-right, top-to-bottom).
    /// </summary>
    public static EditResult<IReadOnlyList<Rect2D>> GridLayout(Rect2D pageBounds, PanelGrid grid, IReadOnlyList<int> columnsPerRow)
    {
        if (columnsPerRow.Count == 0 || columnsPerRow.Any(c => c < 1))
            return EditResult<IReadOnlyList<Rect2D>>.Failure("A layout needs at least one row, each with at least one panel.");

        var live = grid.LiveArea(pageBounds);
        var rows = columnsPerRow.Count;
        var rowHeight = (live.Height - grid.GutterMm * (rows - 1)) / rows;
        var rects = new List<Rect2D>();
        for (var row = 0; row < rows; row++)
        {
            var cols = columnsPerRow[row];
            var colWidth = (live.Width - grid.GutterMm * (cols - 1)) / cols;
            var top = live.Top + row * (rowHeight + grid.GutterMm);
            for (var col = 0; col < cols; col++)
                rects.Add(new Rect2D(live.Left + col * (colWidth + grid.GutterMm), top, colWidth, rowHeight));
        }

        if (rects.Any(r => r.Width < MinPanelSizeMm || r.Height < MinPanelSizeMm))
            return EditResult<IReadOnlyList<Rect2D>>.Failure($"That layout would leave panels under {MinPanelSizeMm}x{MinPanelSizeMm}mm.");

        return EditResult<IReadOnlyList<Rect2D>>.Success(rects);
    }
}
