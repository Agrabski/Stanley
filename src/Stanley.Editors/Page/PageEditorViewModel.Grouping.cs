using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>
/// Group and ungroup: welding the current multi-selection into one thing, and taking it apart again.
/// Elements alone on one side of the characters (issue #86) become one persistent
/// <see cref="GroupElement"/>; anything else - a character, a bubble, elements on both sides of the
/// characters (issue #125) - can't sit inside one, so its members are tied together by a shared
/// <see cref="PanelElement.Link"/> instead (<see cref="Grouping.Link"/>): each keeps its own place in
/// the drawing order, and selecting any one of them selects them all (<see cref="AddGroupedSiblings"/>).
/// Which of the two happens is the editor's business - the user just groups.
/// </summary>
public sealed partial class PageEditorViewModel
{
    /// <summary>The selected panel's element indices actually part of the current selection, in z-order (list order) rather than selection (click) order - what grouping preserves.</summary>
    private List<int> SelectedElementIndicesInOrder(Panel panel) =>
        AllSelected()
            .Where(item => item.Kind == SelectionKind.Element && item.Index >= 0 && item.Index < panel.Elements.Count)
            .Select(item => item.Index)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

    /// <summary>The selection's items that still exist, once each.</summary>
    private List<SelectedItem> SelectedItemsInPanel(Panel panel) =>
        AllSelected().Where(item => IsValidIndex(panel, item)).Distinct().ToList();

    /// <summary>
    /// The multi-selection is nothing but one whole group already - what Ungroup takes apart, and
    /// nothing Group has any use for. (A single group's members are always selected together.)
    /// </summary>
    private bool SelectionIsOneGroup =>
        HasMultiSelection && SelectedPanel is { } panel && SelectedItemsInPanel(panel) is { Count: > 1 } items
        && LinkOf(panel, items[0]) is { } link && items.All(item => LinkOf(panel, item) == link) && Grouping.MembersOf(panel, link).Count == items.Count;

    /// <summary>Group is offered for any multi-selection that isn't already exactly one group.</summary>
    private bool CanGroupSelection()
    {
        if (_selectedPanelId is not { } panelId || !HasMultiSelection || !Working.Panels.TryGetValue(panelId, out var panel))
            return false;
        return SelectedItemsInPanel(panel).Count >= 2 && !SelectionIsOneGroup;
    }

    /// <summary>Groups the selection, one undo step: welds elements alone on one side of the characters into a <see cref="GroupElement"/>, ties anything else together.</summary>
    private void GroupSelection()
    {
        if (_selectedPanelId is not { } panelId || !Working.Panels.TryGetValue(panelId, out var panel))
            return;

        var items = SelectedItemsInPanel(panel);
        if (items.Count < 2)
            return;

        if (items.All(item => item.Kind == SelectionKind.Element) && GroupSelectedElements(panelId, panel))
            return;

        LinkSelection(panelId, items);
    }

    /// <summary>Welds the selected elements into one <see cref="GroupElement"/>, taking the frontmost selected element's place in the z-order, and selects it. False if they can't be welded (they're on both sides of the characters).</summary>
    private bool GroupSelectedElements(PanelId panelId, Panel panel)
    {
        var indices = SelectedElementIndicesInOrder(panel);
        if (indices.Count < 2)
            return false;

        var grouped = Grouping.Group(indices.Select(i => panel.Elements[i]).ToList());
        if (!grouped.IsValid)
            return false;

        var elements = new List<PanelElement>(panel.Elements);
        var frontmost = indices[^1];
        for (var k = indices.Count - 1; k >= 0; k--)
            elements.RemoveAt(indices[k]);
        elements.Insert(frontmost - (indices.Count - 1), grouped.Value);

        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with { Elements = elements })));

        var newIndex = IndexOfElement(panelId, grouped.Value.Id);
        if (newIndex >= 0)
            SelectElement(panelId, newIndex);
        return true;
    }

    /// <summary>Ties the selected bubbles, characters and elements together (one undo step). Nothing moves, and the selection stays as it is - it's just one group now.</summary>
    private void LinkSelection(PanelId panelId, IReadOnlyList<SelectedItem> items)
    {
        List<int> Indices(SelectionKind kind) => items.Where(item => item.Kind == kind).Select(item => item.Index).ToList();

        var bubbles = Indices(SelectionKind.Bubble);
        var characters = Indices(SelectionKind.Character);
        var elements = Indices(SelectionKind.Element);
        Apply(EditPanel(Working, panelId, p => Grouping.Link(p, bubbles, characters, elements)));
        RaiseMultiSelectionChanged();
    }

    /// <summary>Whether anything selected is tied to other things in a group (<see cref="PanelElement.Link"/>).</summary>
    private bool SelectionHasGroup() =>
        SelectedPanel is { } panel && AllSelected().Any(item => LinkOf(panel, item) is not null);

    /// <summary>Ungroup is offered when a group is selected: a single <see cref="GroupElement"/>, or things tied together - ungrouping several <see cref="GroupElement"/>s at once isn't handled.</summary>
    private bool CanUngroupSelection() => SelectionHasGroup() || (HasSelectedElement && SelectedElement is GroupElement);

    /// <summary>Takes the selected group apart, one undo step: things tied together are just let go (they stay selected, no longer one); a <see cref="GroupElement"/> is unwrapped, its children spliced back into the panel at its old position and selected.</summary>
    private void UngroupSelection()
    {
        if (SelectionHasGroup())
        {
            UnlinkSelection();
            return;
        }
        UngroupElement();
    }

    private void UnlinkSelection()
    {
        if (_selectedPanelId is not { } panelId || SelectedPanel is not { } panel)
            return;

        var links = AllSelected().Select(item => LinkOf(panel, item)).OfType<GroupLinkId>().Distinct().ToList();
        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(Grouping.Unlink(p, links))));
        RaiseMultiSelectionChanged();
    }

    private void UngroupElement()
    {
        if (_selectedPanelId is not { } panelId || SelectedElement is not GroupElement group)
            return;

        var oldIndex = _selectedElementIndex;
        var children = Grouping.Ungroup(group);

        Apply(EditPanel(Working, panelId, p =>
        {
            if (oldIndex < 0 || oldIndex >= p.Elements.Count || p.Elements[oldIndex] is not GroupElement)
                return EditResult<Panel>.Failure("No such group.");
            var elements = new List<PanelElement>(p.Elements);
            elements.RemoveAt(oldIndex);
            elements.InsertRange(oldIndex, children);
            return EditResult<Panel>.Success(p with { Elements = elements });
        }));

        if (children.Count == 0 || !Working.Panels.ContainsKey(panelId))
        {
            Select(panelId);
            return;
        }

        var firstIndex = IndexOfElement(panelId, children[0].Id);
        if (firstIndex < 0)
        {
            Select(panelId);
            return;
        }
        SelectElement(panelId, firstIndex);
        for (var i = 1; i < children.Count; i++)
        {
            var index = IndexOfElement(panelId, children[i].Id);
            if (index >= 0)
                ToggleSelect(panelId, elementIndex: index);
        }
    }
}
