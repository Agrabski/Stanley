using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// Welding several loose <see cref="PanelElement"/>s into one persistent <see cref="GroupElement"/>,
/// and taking one apart again (issue #86). Both are cheap: a group's children keep their own
/// absolute geometry throughout, so grouping just wraps what's selected and ungrouping just
/// unwraps it - no coordinates move either way. Pure functions, like <see cref="ElementEditing"/>.
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

        return EditResult<GroupElement>.Success(new GroupElement(ElementId.New(), layer, selected.ToList()));
    }

    /// <summary>Unwraps a group back into its loose children, exactly as they were - lossless, since they were never anything but themselves.</summary>
    public static IReadOnlyList<PanelElement> Ungroup(GroupElement group) => group.Children;
}
