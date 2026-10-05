using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// What every panel element - shape, text or picture - can do the same way: move, resize, change
/// layer and stacking, and stay with its panel. Elements belong to their panel like
/// characters do: clipped to it and carried along when it moves, allowed to hang out of it
/// (a hillside drawn past the frame), but never dragged so far out they can't be clicked
/// again (<see cref="KeepReachable"/>).
/// </summary>
public static class ElementEditing
{
    /// <summary>How much of an element's box (in mm, each way) must stay inside its panel.</summary>
    public const double MinVisibleMm = 4;

    public static PanelElement Move(PanelElement element, double dx, double dy) => element switch
    {
        ShapeElement shape => ShapeEditing.Move(shape, dx, dy),
        TextElement text => TextEditing.Move(text, dx, dy),
        PictureElement picture => PictureEditing.Move(picture, dx, dy),
        SpeedLinesElement speedLines => SpeedLinesEditing.Move(speedLines, dx, dy),
        GroupElement group => group with { Children = group.Children.Select(c => Move(c, dx, dy)).ToList() },
        _ => element
    };

    public static EditResult<PanelElement> Resize(PanelElement element, Rect2D bounds) => element switch
    {
        ShapeElement shape => Widen(ShapeEditing.Resize(shape, bounds)),
        TextElement text => Widen(TextEditing.Resize(text, bounds)),
        PictureElement picture => Widen(PictureEditing.Resize(picture, bounds)),
        SpeedLinesElement speedLines => Widen(SpeedLinesEditing.Resize(speedLines, bounds)),
        GroupElement group => ResizeGroup(group, bounds),
        _ => EditResult<PanelElement>.Failure("This can't be resized.")
    };

    /// <summary>
    /// Rescales every child proportionally from the group's current bounding box into
    /// <paramref name="newBounds"/> - the same linear remap <see cref="ShapeEditing.Resize"/>
    /// applies to a shape's anchors, applied here to each child's own bounds (which maps a
    /// child's axis-aligned box to another axis-aligned box), then recursing into
    /// <see cref="Resize"/> so nested groups and every leaf type resize correctly for free.
    /// </summary>
    private static EditResult<PanelElement> ResizeGroup(GroupElement group, Rect2D newBounds)
    {
        var from = PanelElements.Bounds(group);
        if (from.Width <= 1e-9 || from.Height <= 1e-9)
            return EditResult<PanelElement>.Failure("This group has no size to resize from.");

        Point2D Map(Point2D p) => new(
            newBounds.Left + (p.X - from.Left) * newBounds.Width / from.Width,
            newBounds.Top + (p.Y - from.Top) * newBounds.Height / from.Height);

        var resizedChildren = new List<PanelElement>(group.Children.Count);
        foreach (var child in group.Children)
        {
            var childBounds = PanelElements.Bounds(child);
            var topLeft = Map(new Point2D(childBounds.Left, childBounds.Top));
            var bottomRight = Map(new Point2D(childBounds.Right, childBounds.Bottom));

            var childResult = Resize(child, Rect2D.FromEdges(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y));
            if (!childResult.IsValid)
                return EditResult<PanelElement>.Failure(childResult.Error!);
            resizedChildren.Add(childResult.Value);
        }

        return EditResult<PanelElement>.Success(group with { Children = resizedChildren });
    }

    /// <summary>Changes an element's layer - and, for a group, every child's too, since all of a group's children must share its layer.</summary>
    public static PanelElement SetLayer(PanelElement element, ElementLayer layer)
    {
        if (element.Layer == layer) return element;
        return element is GroupElement group
            ? group with { Layer = layer, Children = group.Children.Select(c => SetLayer(c, layer)).ToList() }
            : element with { Layer = layer };
    }

    /// <summary>Slides the element back until at least <see cref="MinVisibleMm"/> of its box overlaps the panel each way (less for something smaller than that).</summary>
    public static PanelElement KeepReachable(PanelElement element, Rect2D panel)
    {
        var box = PanelElements.Bounds(element);
        var dx = Shift(box.Left, box.Right, panel.Left, panel.Right);
        var dy = Shift(box.Top, box.Bottom, panel.Top, panel.Bottom);
        return dx == 0 && dy == 0 ? element : Move(element, dx, dy);
    }

    /// <summary>
    /// Carries an element along when its panel is resized (<see cref="PanelContentScale"/>):
    /// wherever the scaled picture puts it, scaled with it - a group's children each the same
    /// way. Line weights (a shape's outline, speed lines' thickness) stay as they were, as ink
    /// does at any size.
    /// </summary>
    public static PanelElement Scale(PanelElement element, PanelContentScale scale) => element switch
    {
        ShapeElement shape => shape with { Anchors = scale.Map(shape.Anchors) },
        TextElement text => TextEditing.Scale(text, scale),
        PictureElement picture => picture with { Bounds = scale.Map(picture.Bounds) },
        SpeedLinesElement speedLines => speedLines with { Focus = scale.Map(speedLines.Focus) },
        GroupElement group => group with { Children = group.Children.Select(c => Scale(c, scale)).ToList() },
        _ => element
    };

    /// <summary>Brings an element into another panel (pasting it there): its centre keeps the same relative position in the panel, its size is kept. A panel's own resize goes through <see cref="Scale"/> instead.</summary>
    public static PanelElement Refit(PanelElement element, Rect2D oldPanel, Rect2D newPanel)
    {
        if (oldPanel.Width <= 0 || oldPanel.Height <= 0)
            return KeepReachable(element, newPanel);
        var box = PanelElements.Bounds(element);
        var x = newPanel.Left + (box.MidX - oldPanel.Left) / oldPanel.Width * newPanel.Width;
        var y = newPanel.Top + (box.MidY - oldPanel.Top) / oldPanel.Height * newPanel.Height;
        return KeepReachable(Move(element, x - box.MidX, y - box.MidY), newPanel);
    }

    /// <summary>
    /// Moves element <paramref name="index"/> as far forward or back as an element goes: to the
    /// front brings it into the <see cref="ElementLayer.Foreground"/> (in front of the
    /// characters), at the end of the list; to the back sends it into the
    /// <see cref="ElementLayer.Background"/>, at the start. Returns its new index - and
    /// <paramref name="elements"/> itself when it's already there, so a caller can skip the edit.
    /// </summary>
    public static (IReadOnlyList<PanelElement> Elements, int NewIndex) Reorder(IReadOnlyList<PanelElement> elements, int index, bool toFront)
    {
        if (index < 0 || index >= elements.Count)
            return (elements, index);
        var item = elements[index];
        var layer = toFront ? ElementLayer.Foreground : ElementLayer.Background;
        var others = toFront ? elements.Skip(index + 1) : elements.Take(index);
        if (item.Layer == layer && !others.Any(e => e.Layer == layer))
            return (elements, index);

        var list = elements.ToList();
        list.RemoveAt(index);
        var newIndex = toFront ? list.Count : 0;
        list.Insert(newIndex, SetLayer(item, layer));
        return (list, newIndex);
    }

    private static double Shift(double low, double high, double min, double max)
    {
        var need = Math.Min(MinVisibleMm, Math.Min(Math.Max(high - low, 0), max - min));
        if (high < min + need)
            return min + need - high;
        if (low > max - need)
            return max - need - low;
        return 0;
    }

    private static EditResult<PanelElement> Widen<T>(EditResult<T> result) where T : PanelElement =>
        result.IsValid ? EditResult<PanelElement>.Success(result.Value) : EditResult<PanelElement>.Failure(result.Error!);
}
