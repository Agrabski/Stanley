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
        _ => element
    };

    public static EditResult<PanelElement> Resize(PanelElement element, Rect2D bounds) => element switch
    {
        ShapeElement shape => Widen(ShapeEditing.Resize(shape, bounds)),
        TextElement text => Widen(TextEditing.Resize(text, bounds)),
        PictureElement picture => Widen(PictureEditing.Resize(picture, bounds)),
        _ => EditResult<PanelElement>.Failure("This can't be resized.")
    };

    public static PanelElement SetLayer(PanelElement element, ElementLayer layer) =>
        element.Layer == layer ? element : element with { Layer = layer };

    /// <summary>Slides the element back until at least <see cref="MinVisibleMm"/> of its box overlaps the panel each way (less for something smaller than that).</summary>
    public static PanelElement KeepReachable(PanelElement element, Rect2D panel)
    {
        var box = PanelElements.Bounds(element);
        var dx = Shift(box.Left, box.Right, panel.Left, panel.Right);
        var dy = Shift(box.Top, box.Bottom, panel.Top, panel.Bottom);
        return dx == 0 && dy == 0 ? element : Move(element, dx, dy);
    }

    /// <summary>Carries an element along when its panel moves or resizes: its centre keeps the same relative position in the panel, its size is kept.</summary>
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
    /// Moves element <paramref name="index"/> to the front (the end of the list) or the back
    /// (the start) - which, since each layer draws in list order, is the front or back of its
    /// own layer. Returns its new index.
    /// </summary>
    public static (IReadOnlyList<PanelElement> Elements, int NewIndex) Reorder(IReadOnlyList<PanelElement> elements, int index, bool toFront)
    {
        if (index < 0 || index >= elements.Count)
            return (elements, index);
        var list = elements.ToList();
        var item = list[index];
        list.RemoveAt(index);
        var newIndex = toFront ? list.Count : 0;
        list.Insert(newIndex, item);
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
