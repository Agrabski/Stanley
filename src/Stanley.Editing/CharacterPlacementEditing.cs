using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// Placing characters in a panel. Characters, like bubbles, belong to their panel (they're
/// clipped to it and carried along when it moves), but unlike bubbles they may hang out
/// of it - a waist-up shot is a character whose feet are below the frame - so the only
/// rule is <see cref="KeepReachable"/>: enough of the figure stays inside to click on.
///
/// Scale: every character in a panel shares one <see cref="CharacterPlacement.UnitHeightMm"/>
/// by default, so relative heights are right without anyone thinking about it. New
/// characters take the panel's scale, and resizing one resizes all that share its scale
/// (<see cref="Resize"/> with <c>together</c>); resizing just one - further away, closer
/// to camera - is the deliberate exception.
/// </summary>
public static class CharacterPlacementEditing
{
    public const double MinUnitHeightMm = 5;
    public const double MaxUnitHeightMm = 5000;

    /// <summary>How much of the panel's height the first character in it fills.</summary>
    public const double DefaultPanelFill = 0.8;

    /// <summary>How much of a figure (in mm, each way) must stay inside its panel.</summary>
    public const double MinVisibleMm = 6;

    /// <summary>
    /// Where a new character goes: at the panel's scale (the most recently placed
    /// character's) or, in an empty panel, sized to fill most of its height; standing on
    /// the same floor as the others, just to the right of the rightmost one (or centred).
    /// </summary>
    /// <param name="figure">The new character's <see cref="BodyFigure.Extent"/>.</param>
    /// <param name="others">The panel's existing instances with their page-space bounding boxes.</param>
    public static CharacterPlacement DefaultPlacement(Rect2D panel, Rect2D figure, IReadOnlyList<(CharacterInstance Instance, Rect2D PageBounds)> others)
    {
        if (others.Count == 0)
        {
            var unit = Math.Clamp(panel.Height * DefaultPanelFill / Math.Max(figure.Height, 1e-6), MinUnitHeightMm, MaxUnitHeightMm);
            return new CharacterPlacement(new Point2D(panel.MidX, panel.Top + panel.Height * (1 + DefaultPanelFill) / 2), unit, Mirrored: false);
        }

        var last = others[^1].Instance.Placement;
        var halfWidth = Math.Max(-figure.Left, figure.Right) * last.UnitHeightMm;
        var rightmost = others.Max(o => o.PageBounds.Right);
        var x = rightmost + halfWidth * 1.1;
        if (x + halfWidth > panel.Right)
            x = Math.Max(panel.Left + halfWidth, panel.Right - halfWidth);
        return new CharacterPlacement(new Point2D(x, last.Ground.Y), last.UnitHeightMm, Mirrored: false);
    }

    public static CharacterInstance Move(CharacterInstance instance, double dx, double dy) =>
        instance with { Placement = instance.Placement with { Ground = new Point2D(instance.Placement.Ground.X + dx, instance.Placement.Ground.Y + dy) } };

    /// <summary>Shows the character from <paramref name="angle"/> (front or side), standing where it was.</summary>
    public static CharacterInstance Turn(CharacterInstance instance, ViewAngle angle) =>
        instance.Pose.ViewAngle == angle ? instance : instance with { Pose = instance.Pose with { ViewAngle = angle } };

    public static CharacterInstance Flip(CharacterInstance instance) =>
        instance with { Placement = instance.Placement with { Mirrored = !instance.Placement.Mirrored } };

    /// <summary>
    /// Slides the character back until at least <see cref="MinVisibleMm"/> of its bounding
    /// box overlaps the panel in each direction (less, for a panel or figure smaller than
    /// that) - otherwise it could be dragged out of sight and never clicked again.
    /// </summary>
    /// <param name="figure">The character's figure-space extent (<see cref="BodyFigure.Extent"/>).</param>
    public static CharacterInstance KeepReachable(CharacterInstance instance, Rect2D figure, Rect2D panel)
    {
        var box = instance.Placement.ToPage(figure);
        var dx = Shift(box.Left, box.Right, panel.Left, panel.Right);
        var dy = Shift(box.Top, box.Bottom, panel.Top, panel.Bottom);
        return dx == 0 && dy == 0 ? instance : Move(instance, dx, dy);
    }

    private static double Shift(double low, double high, double min, double max)
    {
        var need = Math.Min(MinVisibleMm, Math.Min(high - low, max - min));
        if (high < min + need)
            return min + need - high;
        if (low > max - need)
            return max - need - low;
        return 0;
    }

    /// <summary>
    /// Sets character <paramref name="index"/>'s scale to <paramref name="unitHeightMm"/>,
    /// each about its own feet. With <paramref name="together"/>, every character that
    /// shared its old scale gets the new one too, so the group keeps its relative heights.
    /// </summary>
    public static IReadOnlyList<CharacterInstance> Resize(IReadOnlyList<CharacterInstance> instances, int index, double unitHeightMm, bool together)
    {
        if (index < 0 || index >= instances.Count)
            return instances;

        var unit = Math.Clamp(unitHeightMm, MinUnitHeightMm, MaxUnitHeightMm);
        var old = instances[index].Placement.UnitHeightMm;
        return instances
            .Select((instance, i) => i == index || together && SameScale(instance.Placement.UnitHeightMm, old)
                ? instance with { Placement = instance.Placement with { UnitHeightMm = unit } }
                : instance)
            .ToList();
    }

    /// <summary>Whether two scales count as "the same camera distance" - equal up to rounding.</summary>
    public static bool SameScale(double a, double b) => Math.Abs(a - b) <= 1e-6 * Math.Max(1, Math.Max(a, b));

    /// <summary>The scale most of the panel's other characters share (the panel's scale), or null if it has no others.</summary>
    public static double? PanelScale(IReadOnlyList<CharacterInstance> instances, int except = -1)
    {
        var groups = instances
            .Where((_, i) => i != except)
            .Select(i => i.Placement.UnitHeightMm)
            .GroupBy(u => Math.Round(u, 6))
            .OrderByDescending(g => g.Count())
            .ToList();
        return groups.Count == 0 ? null : groups[0].First();
    }

    /// <summary>Carries a character along when its panel moves or resizes: its ground point keeps the same relative position in the panel; its size is kept (people don't squash).</summary>
    public static CharacterInstance Refit(CharacterInstance instance, Rect2D oldPanel, Rect2D newPanel)
    {
        if (oldPanel.Width <= 0 || oldPanel.Height <= 0)
            return instance;
        var g = instance.Placement.Ground;
        var mapped = new Point2D(
            newPanel.Left + (g.X - oldPanel.Left) / oldPanel.Width * newPanel.Width,
            newPanel.Top + (g.Y - oldPanel.Top) / oldPanel.Height * newPanel.Height);
        return instance with { Placement = instance.Placement with { Ground = mapped } };
    }

    /// <summary>Reorders for z-order (the end of the list draws in front). Returns the item's new index.</summary>
    public static (IReadOnlyList<CharacterInstance> Instances, int NewIndex) Reorder(IReadOnlyList<CharacterInstance> instances, int index, bool toFront)
    {
        if (index < 0 || index >= instances.Count)
            return (instances, index);
        var list = instances.ToList();
        var item = list[index];
        list.RemoveAt(index);
        var newIndex = toFront ? list.Count : 0;
        list.Insert(newIndex, item);
        return (list, newIndex);
    }
}
