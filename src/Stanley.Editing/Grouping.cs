using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// Welding several loose <see cref="PanelElement"/>s into one persistent <see cref="GroupElement"/>,
/// and taking one apart again (issue #86). Both are cheap: a group's children keep their own
/// absolute geometry throughout, so grouping just wraps what's selected and ungrouping just
/// unwraps it - no coordinates move either way. Pure functions, like <see cref="ElementEditing"/>.
/// <para>
/// A <see cref="GroupElement"/> can only hold elements of one layer, so anything else - a character,
/// a bubble, shapes on both sides of the characters (issue #125) - is grouped by <see cref="Link"/>
/// instead: each member keeps its own place in the drawing order and just carries a shared
/// <see cref="PanelElement.Link"/>, which the editor turns into one selection.
/// </para>
/// </summary>
public static class Grouping
{
    /// <summary>
    /// Wraps <paramref name="selected"/> into a new <see cref="GroupElement"/> on their shared
    /// layer, refusing fewer than two elements (nothing to group) or a mix of
    /// <see cref="ElementLayer.Background"/> and <see cref="ElementLayer.Foreground"/> (a group
    /// can't split across both drawing passes).
    /// </summary>
    public static EditResult<GroupElement> Group(IReadOnlyList<PanelElement> selected)
    {
        if (selected.Count < 2)
            return EditResult<GroupElement>.Failure("Select at least two things to group.");

        var layer = selected[0].Layer;
        if (selected.Any(e => e.Layer != layer))
            return EditResult<GroupElement>.Failure("Can't group elements from the background and the foreground.");

        // The children are the group's now: whatever link tied them to a panel's characters ends here.
        return EditResult<GroupElement>.Success(new GroupElement(ElementId.New(), layer, selected.Select(e => e.Link is null ? e : e with { Link = null }).ToList()));
    }

    /// <summary>The bubbles, characters and elements of a panel, by index into <see cref="Panel.Bubbles"/>, <see cref="Panel.CharacterInstances"/> and <see cref="Panel.Elements"/>.</summary>
    public sealed record Members(IReadOnlyList<int> Bubbles, IReadOnlyList<int> Characters, IReadOnlyList<int> Elements)
    {
        public int Count => Bubbles.Count + Characters.Count + Elements.Count;
    }

    /// <summary>Every bubble, character and element of <paramref name="panel"/> tied together by <paramref name="link"/>.</summary>
    public static Members MembersOf(Panel panel, GroupLinkId link) => new(
        Indices(panel.Bubbles, b => b.Link, link),
        Indices(panel.CharacterInstances, c => c.Link, link),
        Indices(panel.Elements, e => e.Link, link));

    private static List<int> Indices<T>(IReadOnlyList<T> items, Func<T, GroupLinkId?> linkOf, GroupLinkId link)
    {
        var found = new List<int>();
        for (var i = 0; i < items.Count; i++)
        {
            if (linkOf(items[i]) == link)
                found.Add(i);
        }
        return found;
    }

    /// <summary>
    /// Ties the given bubbles, characters and elements of <paramref name="panel"/> together under one
    /// new <see cref="GroupLinkId"/>, replacing any link they had - grouping two groups makes one
    /// bigger group. Nothing moves and nothing changes its place in the drawing order. Refuses fewer
    /// than two things (nothing to group) and an index that isn't in the panel.
    /// </summary>
    public static EditResult<Panel> Link(Panel panel, IReadOnlyCollection<int> bubbles, IReadOnlyCollection<int> characters, IReadOnlyCollection<int> elements)
    {
        if (bubbles.Count + characters.Count + elements.Count < 2)
            return EditResult<Panel>.Failure("Select at least two things to group.");
        if (bubbles.Any(i => i < 0 || i >= panel.Bubbles.Count)
            || characters.Any(i => i < 0 || i >= panel.CharacterInstances.Count)
            || elements.Any(i => i < 0 || i >= panel.Elements.Count))
            return EditResult<Panel>.Failure("Part of the selection is no longer there.");

        GroupLinkId? link = GroupLinkId.New();
        return EditResult<Panel>.Success(panel with
        {
            Bubbles = Relinked(panel.Bubbles, bubbles, (b, l) => b with { Link = l }, link),
            CharacterInstances = Relinked(panel.CharacterInstances, characters, (c, l) => c with { Link = l }, link),
            Elements = Relinked(panel.Elements, elements, (e, l) => e with { Link = l }, link)
        });
    }

    /// <summary>Takes apart every group <paramref name="links"/> names: their members stay exactly where and as they are, just no longer tied together.</summary>
    public static Panel Unlink(Panel panel, IReadOnlyCollection<GroupLinkId> links)
    {
        bool Named(GroupLinkId? link) => link is { } value && links.Contains(value);
        return panel with
        {
            Bubbles = panel.Bubbles.Select(b => Named(b.Link) ? b with { Link = null } : b).ToList(),
            CharacterInstances = panel.CharacterInstances.Select(c => Named(c.Link) ? c with { Link = null } : c).ToList(),
            Elements = panel.Elements.Select(e => Named(e.Link) ? e with { Link = null } : e).ToList()
        };
    }

    private static List<T> Relinked<T>(IReadOnlyList<T> items, IReadOnlyCollection<int> indices, Func<T, GroupLinkId?, T> withLink, GroupLinkId? link)
    {
        var result = items.ToList();
        foreach (var index in indices)
            result[index] = withLink(result[index], link);
        return result;
    }

    /// <summary>Unwraps a group back into its loose children, exactly as they were - lossless, since they were never anything but themselves.</summary>
    public static IReadOnlyList<PanelElement> Ungroup(GroupElement group) => group.Children;
}
