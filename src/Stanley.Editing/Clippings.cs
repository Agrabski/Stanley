using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// One thing taken off a page to be put back as a copy - by Copy and Paste, Duplicate or an
/// Alt+drag: a bubble, a placed character, an element (shape, text, picture, speed lines) or a
/// whole panel with everything in it, exactly as it was, with the panel it came from - where it
/// sat in that panel is where it goes in the next.
/// </summary>
public abstract record Clipping(PanelId FromPanel, Rect2D FromPanelBounds);

public sealed record BubbleClipping(PanelId FromPanel, Rect2D FromPanelBounds, Bubble Bubble) : Clipping(FromPanel, FromPanelBounds);

public sealed record CharacterClipping(PanelId FromPanel, Rect2D FromPanelBounds, CharacterInstance Character) : Clipping(FromPanel, FromPanelBounds);

public sealed record ElementClipping(PanelId FromPanel, Rect2D FromPanelBounds, PanelElement Element) : Clipping(FromPanel, FromPanelBounds);

public sealed record PanelClipping(PanelId FromPanel, Rect2D FromPanelBounds, Panel Panel) : Clipping(FromPanel, FromPanelBounds);

/// <summary>
/// Making the copies <see cref="Clipping"/>s put back: fresh ids, so a copy is never mistaken for
/// what it was copied from (a title page's words are found by theirs), and a free spot for it,
/// stepped aside the way Office cascades pasted shapes so it doesn't hide exactly on top of the
/// original. Pure functions, like <see cref="BubbleEditing"/>.
/// </summary>
public static class Clippings
{
    /// <summary>How far a copy steps aside from one already in its spot, in mm - the same step a new bubble takes (<see cref="BubbleEditing.CascadeStepMm"/>).</summary>
    public const double CascadeStepMm = BubbleEditing.CascadeStepMm;

    public static Bubble Copy(Bubble bubble) => bubble with { Id = BubbleId.New() };

    public static PanelElement Copy(PanelElement element) => element switch
    {
        GroupElement group => group with { Id = ElementId.New(), Children = group.Children.Select(Copy).ToList() },
        _ => element with { Id = ElementId.New() }
    };

    /// <summary>A panel under a new id, its bubbles and elements under new ones too.</summary>
    public static Panel Copy(Panel panel) => panel with
    {
        Id = PanelId.New(),
        Bubbles = panel.Bubbles.Select(Copy).ToList(),
        Elements = panel.Elements.Select(Copy).ToList()
    };

    /// <summary>
    /// Where a copy with box <paramref name="bounds"/> goes so it doesn't land exactly on
    /// something already there (<paramref name="taken"/>): stepped by
    /// (<paramref name="stepX"/>, <paramref name="stepY"/>) until its corner is clear of every one
    /// - forwards, else backwards once forwards would take its centre out of
    /// <paramref name="container"/>. Pasting the same thing again steps it once more, so a row
    /// of pastes fans out. Returns <paramref name="bounds"/> itself when it's clear already or
    /// there's no room.
    /// </summary>
    public static Rect2D FreeSpot(Rect2D bounds, IReadOnlyCollection<Rect2D> taken, Rect2D container, double stepX = CascadeStepMm, double stepY = CascadeStepMm)
    {
        var tolerance = Math.Max(Math.Abs(stepX), Math.Abs(stepY)) / 2;
        bool Clear(Rect2D r) => taken.All(t => Math.Abs(t.Left - r.Left) >= tolerance || Math.Abs(t.Top - r.Top) >= tolerance);
        bool Inside(Rect2D r) => r.MidX >= container.Left && r.MidX <= container.Right && r.MidY >= container.Top && r.MidY <= container.Bottom;

        if (tolerance <= 0 || Clear(bounds))
            return bounds;
        foreach (var direction in new[] { 1, -1 })
        {
            for (var k = 1; k <= 50; k++)
            {
                var candidate = bounds with { X = bounds.X + direction * k * stepX, Y = bounds.Y + direction * k * stepY };
                if (!Inside(candidate))
                    break;
                if (Clear(candidate))
                    return candidate;
            }
        }
        return bounds;
    }
}
