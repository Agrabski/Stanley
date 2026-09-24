using Stanley.Editing.Abstractions;
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
/// </summary>
public static class PanelLayoutEditing
{
    public const double MinPanelSizeMm = 20;

    public static EditResult<Panel> Resize(Panel panel, Rect2D newBounds, Rect2D pageBounds)
    {
        if (newBounds.Width < MinPanelSizeMm || newBounds.Height < MinPanelSizeMm)
            return EditResult<Panel>.Failure($"A panel must be at least {MinPanelSizeMm}x{MinPanelSizeMm}mm.");

        const double epsilon = 0.01;
        if (newBounds.Left < pageBounds.Left - epsilon || newBounds.Top < pageBounds.Top - epsilon ||
            newBounds.Right > pageBounds.Right + epsilon || newBounds.Bottom > pageBounds.Bottom + epsilon)
            return EditResult<Panel>.Failure("A panel can't extend past the page.");

        return EditResult<Panel>.Success(panel with { Shape = PanelShapes.Rectangle(newBounds) });
    }

    /// <summary>Divides one panel into two rectangles along <paramref name="orientation"/>, at <paramref name="fraction"/> of its current bounds. The second panel starts empty (no bubbles/characters/background) - those stay with whichever half the caller decides keeps the original id.</summary>
    public static EditResult<(Panel First, Panel Second)> Split(Panel panel, BoundaryOrientation orientation, double fraction)
    {
        if (fraction <= 0 || fraction >= 1)
            return EditResult<(Panel, Panel)>.Failure("Split fraction must be strictly between 0 and 1.");

        var bounds = AnchorRing.BoundingBox(panel.Shape.Anchors);
        Rect2D firstBounds, secondBounds;
        if (orientation == BoundaryOrientation.Vertical)
        {
            var splitX = bounds.Left + bounds.Width * fraction;
            firstBounds = Rect2D.FromEdges(bounds.Left, bounds.Top, splitX, bounds.Bottom);
            secondBounds = Rect2D.FromEdges(splitX, bounds.Top, bounds.Right, bounds.Bottom);
        }
        else
        {
            var splitY = bounds.Top + bounds.Height * fraction;
            firstBounds = Rect2D.FromEdges(bounds.Left, bounds.Top, bounds.Right, splitY);
            secondBounds = Rect2D.FromEdges(bounds.Left, splitY, bounds.Right, bounds.Bottom);
        }

        if (firstBounds.Width < MinPanelSizeMm || firstBounds.Height < MinPanelSizeMm ||
            secondBounds.Width < MinPanelSizeMm || secondBounds.Height < MinPanelSizeMm)
            return EditResult<(Panel, Panel)>.Failure($"Split would leave a panel under {MinPanelSizeMm}x{MinPanelSizeMm}mm.");

        var first = panel with { Shape = PanelShapes.Rectangle(firstBounds) };
        var second = new Panel(PanelId.New(), PanelShapes.Rectangle(secondBounds), Background: null, CharacterInstances: [], Bubbles: []);
        return EditResult<(Panel, Panel)>.Success((first, second));
    }

    /// <summary>Moves a shared boundary, resizing every panel on both sides to meet it at <paramref name="newPosition"/> (an X for a vertical boundary, a Y for a horizontal one).</summary>
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

        foreach (var id in boundary.PanelsAfter)
        {
            var bounds = AnchorRing.BoundingBox(panels[id].Shape.Anchors);
            var newBounds = boundary.Orientation == BoundaryOrientation.Vertical
                ? Rect2D.FromEdges(newPosition, bounds.Top, bounds.Right, bounds.Bottom)
                : Rect2D.FromEdges(bounds.Left, newPosition, bounds.Right, bounds.Bottom);
            var result = Resize(panels[id], newBounds, pageBounds);
            if (!result.IsValid)
                return EditResult<IReadOnlyDictionary<PanelId, Panel>>.Failure(result.Error!);
            updated[id] = result.Value;
        }

        return EditResult<IReadOnlyDictionary<PanelId, Panel>>.Success(updated);
    }
}
