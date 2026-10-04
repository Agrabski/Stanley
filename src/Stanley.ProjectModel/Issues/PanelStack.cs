namespace Stanley.ProjectModel.Issues;

/// <summary>Which of a panel's three lists a <see cref="StackItem"/> is in.</summary>
public enum StackKind
{
    Bubble,
    Character,
    Element
}

/// <summary>One thing a panel stacks: item <paramref name="Index"/> of the panel's bubbles, characters or elements.</summary>
public readonly record struct StackItem(StackKind Kind, int Index);

/// <summary>
/// The one place that says what a panel draws in front of what: the order of everything
/// in it except its background, which is always at the very back. The renderer draws in
/// this order, the Layers pane lists it, and picking on the canvas walks it from the front.
/// </summary>
public static class PanelStack
{
    /// <summary>
    /// Everything in <paramref name="panel"/> that stacks, back to front: the elements behind
    /// the characters, the characters, the elements in front of them, then the bubbles - each
    /// group in its list's order, the end of a list being its front.
    /// </summary>
    public static IReadOnlyList<StackItem> Order(Panel panel)
    {
        var order = new List<StackItem>(panel.Elements.Count + panel.CharacterInstances.Count + panel.Bubbles.Count);
        for (var i = 0; i < panel.Elements.Count; i++)
        {
            if (panel.Elements[i].Layer == ElementLayer.Background)
                order.Add(new StackItem(StackKind.Element, i));
        }
        for (var i = 0; i < panel.CharacterInstances.Count; i++)
            order.Add(new StackItem(StackKind.Character, i));
        for (var i = 0; i < panel.Elements.Count; i++)
        {
            if (panel.Elements[i].Layer == ElementLayer.Foreground)
                order.Add(new StackItem(StackKind.Element, i));
        }
        for (var i = 0; i < panel.Bubbles.Count; i++)
            order.Add(new StackItem(StackKind.Bubble, i));
        return order;
    }
}
