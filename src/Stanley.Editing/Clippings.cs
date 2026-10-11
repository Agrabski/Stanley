using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// One thing taken off a page to be put back as a copy - by Copy and Paste, Duplicate or an
/// Alt+drag: a bubble, a placed character, an element (shape, text, picture, speed lines), several
/// of those selected together (<see cref="SelectionClipping"/>) or a whole panel with everything in
/// it, exactly as it was, with the panel it came from - where it sat in that panel is where it
/// goes in the next.
/// </summary>
public abstract record Clipping(PanelId FromPanel, Rect2D FromPanelBounds);

public sealed record BubbleClipping(PanelId FromPanel, Rect2D FromPanelBounds, Bubble Bubble) : Clipping(FromPanel, FromPanelBounds);

public sealed record CharacterClipping(PanelId FromPanel, Rect2D FromPanelBounds, CharacterInstance Character) : Clipping(FromPanel, FromPanelBounds);

public sealed record ElementClipping(PanelId FromPanel, Rect2D FromPanelBounds, PanelElement Element) : Clipping(FromPanel, FromPanelBounds);

public sealed record PanelClipping(PanelId FromPanel, Rect2D FromPanelBounds, Panel Panel) : Clipping(FromPanel, FromPanelBounds);

/// <summary>
/// Everything that was selected together in one panel - a bubble and the character it speaks for,
/// say - so Copy takes all of it, not just the last thing clicked. Each list is in the panel's own
/// order (the end is the front), so what was in front of what among them stays so.
/// </summary>
public sealed record SelectionClipping(
    PanelId FromPanel,
    Rect2D FromPanelBounds,
    IReadOnlyList<Bubble> Bubbles,
    IReadOnlyList<CharacterInstance> Characters,
    IReadOnlyList<PanelElement> Elements) : Clipping(FromPanel, FromPanelBounds)
{
    public int Count => Bubbles.Count + Characters.Count + Elements.Count;
}

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

    // A lone copy joins no group: whatever a bubble, character or element was grouped with stays
    // with the original (a whole panel's copy keeps its groups - they're all inside it).
    public static Bubble Copy(Bubble bubble) => bubble with { Id = BubbleId.New(), Link = null };

    // Nor does a copy keep the id its original is stacked under - two characters in a panel never share one.
    public static CharacterInstance Copy(CharacterInstance character) => character with { Link = null, Id = null };

    public static PanelElement Copy(PanelElement element) => element switch
    {
        GroupElement group => group with { Id = ElementId.New(), Link = null, Children = group.Children.Select(Copy).ToList() },
        _ => element with { Id = ElementId.New(), Link = null }
    };

    /// <summary>A panel under a new id, its bubbles and elements under new ones too; what was grouped in it stays grouped, and what was in front of what stays so.</summary>
    public static Panel Copy(Panel panel)
    {
        var bubbles = panel.Bubbles.Select(b => Copy(b) with { Link = b.Link }).ToList();
        var elements = panel.Elements.Select(e => Copy(e) with { Link = e.Link }).ToList();
        // The stacking names things by id, and the copies have new ones: say the same thing in them. (The characters keep theirs - ids only mean something inside one panel.)
        var renamed = new Dictionary<string, string>();
        for (var i = 0; i < bubbles.Count; i++)
            renamed[PanelStack.Token(panel.Bubbles[i])] = PanelStack.Token(bubbles[i]);
        for (var i = 0; i < elements.Count; i++)
            renamed[PanelStack.Token(panel.Elements[i])] = PanelStack.Token(elements[i]);
        return panel with
        {
            Id = PanelId.New(),
            Bubbles = bubbles,
            Elements = elements,
            Stack = panel.Stack?.Select(token => renamed.GetValueOrDefault(token, token)).ToList()
        };
    }

    /// <summary>
    /// Copies of everything in <paramref name="clipping"/>, each under a new id. What was grouped
    /// together (<see cref="PanelElement.Link"/>) and is copied together is a group of its own in the
    /// copies, never joined to the original's; a member copied without the rest of its group is
    /// left ungrouped, since a group of one is no group.
    /// </summary>
    public static SelectionClipping Copy(SelectionClipping clipping)
    {
        var links = clipping.Bubbles.Select(b => b.Link)
            .Concat(clipping.Characters.Select(c => c.Link))
            .Concat(clipping.Elements.Select(e => e.Link))
            .OfType<GroupLinkId>()
            .GroupBy(link => link)
            .ToDictionary(group => group.Key, group => group.Count());
        var relinker = new Relinker();
        GroupLinkId? Fresh(GroupLinkId? original) => original is { } link && links[link] > 1 ? relinker.For(link) : null;

        return clipping with
        {
            Bubbles = clipping.Bubbles.Select(b => Copy(b) with { Link = Fresh(b.Link) }).ToList(),
            Characters = clipping.Characters.Select(c => Copy(c) with { Link = Fresh(c.Link) }).ToList(),
            Elements = clipping.Elements.Select(e => Copy(e) with { Link = Fresh(e.Link) }).ToList()
        };
    }

    /// <summary>
    /// Hands out new group links for a set of copies, one per link they came from, so copying a
    /// whole group gives a whole new group rather than joining the original (or splitting into
    /// singles): <c>Copy(x) with { Link = relinker.For(x.Link) }</c>.
    /// </summary>
    public sealed class Relinker
    {
        private readonly Dictionary<GroupLinkId, GroupLinkId> _links = [];

        public GroupLinkId? For(GroupLinkId? original)
        {
            if (original is not { } link)
                return null;
            if (!_links.TryGetValue(link, out var fresh))
                _links[link] = fresh = GroupLinkId.New();
            return fresh;
        }
    }

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
        var (dx, dy) = FreeOffset([bounds], taken, container, stepX, stepY);
        return bounds with { X = bounds.X + dx, Y = bounds.Y + dy };
    }

    /// <summary>
    /// <see cref="FreeSpot"/> for several things that are pasted together and must keep their
    /// places relative to each other: the one offset (dx, dy) to move all of <paramref name="boxes"/>
    /// by, stepped until none of their corners is on one of <paramref name="taken"/>, and until
    /// they stand out of the way together - forwards, else backwards once forwards would take their
    /// combined centre out of <paramref name="container"/>. (0, 0) when they're clear already or
    /// there's no room.
    /// </summary>
    public static (double Dx, double Dy) FreeOffset(IReadOnlyCollection<Rect2D> boxes, IReadOnlyCollection<Rect2D> taken, Rect2D container, double stepX = CascadeStepMm, double stepY = CascadeStepMm)
    {
        var tolerance = Math.Max(Math.Abs(stepX), Math.Abs(stepY)) / 2;
        bool Clear(double dx, double dy) => boxes.All(b => taken.All(t => Math.Abs(t.Left - (b.Left + dx)) >= tolerance || Math.Abs(t.Top - (b.Top + dy)) >= tolerance));

        if (boxes.Count == 0 || tolerance <= 0 || Clear(0, 0))
            return (0, 0);
        var all = boxes.Count == 1 ? boxes.Single() : Rect2D.Union(boxes);
        bool Inside(double dx, double dy) =>
            all.MidX + dx >= container.Left && all.MidX + dx <= container.Right && all.MidY + dy >= container.Top && all.MidY + dy <= container.Bottom;

        foreach (var direction in new[] { 1, -1 })
        {
            for (var k = 1; k <= 50; k++)
            {
                var (dx, dy) = (direction * k * stepX, direction * k * stepY);
                if (!Inside(dx, dy))
                    break;
                if (Clear(dx, dy))
                    return (dx, dy);
            }
        }
        return (0, 0);
    }
}
