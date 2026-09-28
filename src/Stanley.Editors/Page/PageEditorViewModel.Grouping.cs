using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>Group and ungroup (issue #86): welding the current multi-selection of panel elements into one persistent <see cref="GroupElement"/>, and taking one apart again.</summary>
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

    /// <summary>Group is offered only for a multi-selection made up entirely of panel elements (no bubbles, no characters - out of scope for v1) that share a layer, per <see cref="Grouping.Group"/>.</summary>
    private bool CanGroupSelection()
    {
        if (_selectedPanelId is not { } panelId || !HasMultiSelection || !Working.Panels.TryGetValue(panelId, out var panel))
            return false;
        if (AllSelected().Any(item => item.Kind != SelectionKind.Element))
            return false;

        var indices = SelectedElementIndicesInOrder(panel);
        return indices.Count >= 2 && Grouping.Group(indices.Select(i => panel.Elements[i]).ToList()).IsValid;
    }

    /// <summary>Welds the selected elements into one <see cref="GroupElement"/>, one undo step, taking the frontmost selected element's place in the z-order. Selects the new group.</summary>
    private void GroupSelection()
    {
        if (_selectedPanelId is not { } panelId || !Working.Panels.TryGetValue(panelId, out var panel))
            return;

        var indices = SelectedElementIndicesInOrder(panel);
        if (indices.Count < 2)
            return;

        var grouped = Grouping.Group(indices.Select(i => panel.Elements[i]).ToList());
        if (!grouped.IsValid)
            return;

        var elements = new List<PanelElement>(panel.Elements);
        var frontmost = indices[^1];
        for (var k = indices.Count - 1; k >= 0; k--)
            elements.RemoveAt(indices[k]);
        elements.Insert(frontmost - (indices.Count - 1), grouped.Value);

        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with { Elements = elements })));

        var newIndex = IndexOfElement(panelId, grouped.Value.Id);
        if (newIndex >= 0)
            SelectElement(panelId, newIndex);
    }

    /// <summary>Ungroup is offered for a single selected <see cref="GroupElement"/> - ungrouping several groups at once isn't handled in v1.</summary>
    private bool CanUngroupSelection() => HasSelectedElement && SelectedElement is GroupElement;

    /// <summary>Takes the selected group apart, splicing its children back into the panel at the group's old position, one undo step. Selects every child as the new multi-selection.</summary>
    private void UngroupSelection()
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
