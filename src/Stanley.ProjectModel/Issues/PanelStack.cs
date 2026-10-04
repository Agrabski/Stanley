using Stanley.ProjectModel.Bubbles;

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
/// <remarks>
/// <para>
/// A panel stacks the usual way - drawings behind the characters, the characters, drawings in
/// front of them, then the bubbles - until its stacking is arranged by hand, which writes
/// <see cref="Panel.Stack"/>: every item named by a token, back to front. That list is read
/// forgivingly, so nothing that adds, copies, deletes or merges items has to keep it up to date:
/// tokens that name nothing are skipped, a token met twice counts once, and an item the list
/// doesn't name (a bubble added since, a character without an id) is put just in front of
/// whatever comes before it in the usual order - a new bubble lands in front of the panel's
/// frontmost bubble, wherever that has been put.
/// </para>
/// </remarks>
public static class PanelStack
{
    /// <summary>What <see cref="Panel.Stack"/> calls <paramref name="bubble"/>.</summary>
    public static string Token(Bubble bubble) => "b:" + bubble.Id.Value;

    /// <summary>What <see cref="Panel.Stack"/> calls <paramref name="element"/>.</summary>
    public static string Token(PanelElement element) => "e:" + element.Id.Value;

    /// <summary>What <see cref="Panel.Stack"/> calls <paramref name="character"/>; null for one with no id yet, which the stack can't name.</summary>
    public static string? Token(CharacterInstance character) => character.Id is { } id ? "c:" + id.Value : null;

    /// <summary>What <see cref="Panel.Stack"/> calls <paramref name="item"/> of <paramref name="panel"/>; null for a character with no id yet.</summary>
    public static string? Token(Panel panel, StackItem item) => item.Kind switch
    {
        StackKind.Bubble => Token(panel.Bubbles[item.Index]),
        StackKind.Character => Token(panel.CharacterInstances[item.Index]),
        _ => Token(panel.Elements[item.Index])
    };

    /// <summary>
    /// Everything in <paramref name="panel"/> that stacks, back to front. The usual order -
    /// the elements behind the characters, the characters, the elements in front of them, then
    /// the bubbles, each group in its list's order, the end of a list being its front - unless
    /// the panel's <see cref="Panel.Stack"/> says otherwise.
    /// </summary>
    public static IReadOnlyList<StackItem> Order(Panel panel)
    {
        var usual = UsualOrder(panel);
        if (panel.Stack is not { Count: > 0 } stack)
            return usual;

        // Who a token names. If two items claim one (a hand-edited file, a merge), the first in the usual order has it.
        var named = new Dictionary<string, StackItem>();
        foreach (var item in usual)
        {
            if (Token(panel, item) is { } token)
                named.TryAdd(token, item);
        }

        var order = new List<StackItem>(usual.Count);
        var placed = new HashSet<StackItem>();
        foreach (var token in stack)
        {
            if (named.TryGetValue(token, out var item) && placed.Add(item))
                order.Add(item);
        }
        if (order.Count == usual.Count)
            return order;

        // What the list doesn't name goes just in front of whatever precedes it in the usual
        // order - which is already placed by now, listed or put in its turn - or at the back.
        for (var i = 0; i < usual.Count; i++)
        {
            if (placed.Contains(usual[i]))
                continue;
            order.Insert(i == 0 ? 0 : order.IndexOf(usual[i - 1]) + 1, usual[i]);
            placed.Add(usual[i]);
        }
        return order;
    }

    private static List<StackItem> UsualOrder(Panel panel)
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
