using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>How far to move a layer in its panel's stack.</summary>
public enum StackMove
{
    /// <summary>One place towards the front.</summary>
    Forward,

    /// <summary>One place towards the back.</summary>
    Backward,

    /// <summary>In front of everything else in the panel.</summary>
    ToFront,

    /// <summary>Behind everything else in the panel (but in front of its background).</summary>
    ToBack
}

/// <summary>
/// Arranging what's in front of what in a panel (the Layers pane): bubbles, characters and
/// drawings alike, in one stack (<see cref="PanelStack"/>). Pure functions, like
/// <see cref="ElementEditing"/>; an edit that changes nothing returns the very panel it was
/// given, so a caller can skip the undo step.
/// </summary>
public static class PanelStackEditing
{
    /// <summary>Moves <paramref name="item"/> within the panel's stack as it's drawn now, whatever kind of thing its neighbours are. Arranging a panel for the first time writes its <see cref="Panel.Stack"/>.</summary>
    public static EditResult<Panel> Move(Panel panel, StackItem item, StackMove move)
    {
        var order = PanelStack.Order(panel).ToList();
        var at = order.IndexOf(item);
        if (at < 0)
            return EditResult<Panel>.Failure("No such layer.");

        var to = move switch
        {
            StackMove.Forward => at + 1,
            StackMove.Backward => at - 1,
            StackMove.ToFront => order.Count - 1,
            _ => 0
        };
        return MoveTo(panel, order, at, to);
    }

    /// <summary>
    /// Puts <paramref name="item"/> at <paramref name="slot"/> of the panel's stack as it's drawn now
    /// (0 is the back, one less than the number of layers the front; anything past either end is
    /// that end) - where a layer dragged in the Layers pane is dropped. The others keep their order.
    /// </summary>
    public static EditResult<Panel> MoveTo(Panel panel, StackItem item, int slot)
    {
        var order = PanelStack.Order(panel).ToList();
        var at = order.IndexOf(item);
        return at < 0 ? EditResult<Panel>.Failure("No such layer.") : MoveTo(panel, order, at, slot);
    }

    private static EditResult<Panel> MoveTo(Panel panel, List<StackItem> order, int at, int slot)
    {
        var to = Math.Clamp(slot, 0, order.Count - 1);
        if (to == at)
            return EditResult<Panel>.Success(panel);

        var item = order[at];
        order.RemoveAt(at);
        order.Insert(to, item);
        return EditResult<Panel>.Success(Arranged(panel, order));
    }

    /// <summary>
    /// Puts <paramref name="item"/> just in front of the panel's frontmost character, or just behind
    /// its rearmost one - "In front of / Behind the characters" for a panel whose stacking was
    /// arranged by hand. The same panel when it's there already or the panel has no characters.
    /// </summary>
    public static EditResult<Panel> MoveBeyondCharacters(Panel panel, StackItem item, bool inFront)
    {
        var before = PanelStack.Order(panel);
        var order = before.ToList();
        var at = order.IndexOf(item);
        if (at < 0)
            return EditResult<Panel>.Failure("No such layer.");

        order.RemoveAt(at);
        var characterSlots = order.Select((other, slot) => (other, slot)).Where(x => x.other.Kind == StackKind.Character).Select(x => x.slot).ToList();
        if (characterSlots.Count == 0)
            return EditResult<Panel>.Success(panel);
        order.Insert(inFront ? characterSlots.Max() + 1 : characterSlots.Min(), item);
        return order.SequenceEqual(before) ? EditResult<Panel>.Success(panel) : EditResult<Panel>.Success(Arranged(panel, order));
    }

    /// <summary>
    /// <paramref name="panel"/> stacked as <paramref name="order"/> says (every one of its
    /// bubbles, characters and elements, back to front): the order written as its
    /// <see cref="Panel.Stack"/>, and each drawing's front-of-the-characters flag
    /// (<see cref="PanelElement.Layer"/>) brought in line with where it now sits - in front if
    /// it's above every character, behind if it's below every one, as it was if it's between two.
    /// </summary>
    public static Panel Arranged(Panel panel, IReadOnlyList<StackItem> order)
    {
        var named = NamedUniquely(panel);
        var tokens = order.Select(item => PanelStack.Token(named, item)!).ToList();

        var characterSlots = order.Select((item, slot) => (item, slot)).Where(x => x.item.Kind == StackKind.Character).Select(x => x.slot).ToList();
        var elements = named.Elements;
        if (characterSlots.Count > 0)
        {
            var behindAll = characterSlots.Min();
            var inFrontOfAll = characterSlots.Max();
            var layered = named.Elements.ToList();
            var changed = false;
            for (var slot = 0; slot < order.Count; slot++)
            {
                if (order[slot].Kind != StackKind.Element)
                    continue;
                var element = layered[order[slot].Index];
                var layer = slot > inFrontOfAll ? ElementLayer.Foreground : slot < behindAll ? ElementLayer.Background : element.Layer;
                if (layer == element.Layer)
                    continue;
                layered[order[slot].Index] = ElementEditing.SetLayer(element, layer);
                changed = true;
            }
            if (changed)
                elements = layered;
        }
        return named with { Elements = elements, Stack = tokens };
    }

    /// <summary>
    /// <paramref name="after"/> - what grouping the elements <paramref name="members"/> of
    /// <paramref name="before"/> into <paramref name="group"/> made of the panel - with the group
    /// stacked where its frontmost member was. A panel stacked the usual way needs nothing.
    /// </summary>
    public static Panel Grouped(Panel before, Panel after, IReadOnlyCollection<ElementId> members, PanelElement group)
    {
        if (before.Stack is null)
            return after;
        var named = WithCharacterIds(before);
        var tokens = TokensInOrder(named);
        var memberTokens = members.Select(id => "e:" + id.Value).ToHashSet(StringComparer.Ordinal);
        var frontmost = tokens.FindLastIndex(memberTokens.Contains);
        var stack = new List<string>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i == frontmost)
                stack.Add(PanelStack.Token(group));
            else if (!memberTokens.Contains(tokens[i]))
                stack.Add(tokens[i]);
        }
        return after with { CharacterInstances = named.CharacterInstances, Stack = stack };
    }

    /// <summary>
    /// <paramref name="after"/> - what taking the group <paramref name="group"/> apart into
    /// <paramref name="children"/> made of the panel - with them stacked where the group was, in
    /// the order they were in it. A panel stacked the usual way needs nothing.
    /// </summary>
    public static Panel Ungrouped(Panel before, Panel after, PanelElement group, IReadOnlyList<PanelElement> children)
    {
        if (before.Stack is null)
            return after;
        var named = WithCharacterIds(before);
        var groupToken = PanelStack.Token(group);
        var stack = new List<string>();
        foreach (var token in TokensInOrder(named))
        {
            if (token == groupToken)
                stack.AddRange(children.Select(PanelStack.Token));
            else
                stack.Add(token);
        }
        return after with { CharacterInstances = named.CharacterInstances, Stack = stack };
    }

    private static List<string> TokensInOrder(Panel panel) => PanelStack.Order(panel).Select(item => PanelStack.Token(panel, item)!).ToList();

    /// <summary>Gives every placed character without an id one: the stack names things by id, so a character has to have one to be arranged. The same panel when none lacks one.</summary>
    private static Panel WithCharacterIds(Panel panel) =>
        panel.CharacterInstances.All(c => c.Id is not null)
            ? panel
            : panel with { CharacterInstances = panel.CharacterInstances.Select(c => c.Id is null ? c with { Id = CharacterInstanceId.New() } : c).ToList() };

    /// <summary>
    /// <see cref="WithCharacterIds"/>, and every token unique: two things that share an id (only
    /// a hand-edited or merged file can) can't both be told apart in a stack, so the later one gets
    /// a new one - which is what lets the order just written be the order read back.
    /// </summary>
    private static Panel NamedUniquely(Panel panel)
    {
        var named = WithCharacterIds(panel);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var bubbles = named.Bubbles.Select(b => seen.Add(PanelStack.Token(b)) ? b : b with { Id = FreshBubbleId(seen) }).ToList();
        var characters = named.CharacterInstances.Select(c => seen.Add(PanelStack.Token(c)!) ? c : c with { Id = FreshCharacterId(seen) }).ToList();
        var elements = named.Elements.Select(e => seen.Add(PanelStack.Token(e)) ? e : e with { Id = FreshElementId(seen) }).ToList();
        return named with { Bubbles = bubbles, CharacterInstances = characters, Elements = elements };
    }

    private static BubbleId FreshBubbleId(ISet<string> seen)
    {
        var id = BubbleId.New();
        seen.Add("b:" + id.Value);
        return id;
    }

    private static CharacterInstanceId FreshCharacterId(ISet<string> seen)
    {
        var id = CharacterInstanceId.New();
        seen.Add("c:" + id.Value);
        return id;
    }

    private static ElementId FreshElementId(ISet<string> seen)
    {
        var id = ElementId.New();
        seen.Add("e:" + id.Value);
        return id;
    }
}
