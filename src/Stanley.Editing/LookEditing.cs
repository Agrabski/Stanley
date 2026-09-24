using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// Dressing a character (docs/sticker-system.md §13.1): what it wears by default, in which
/// colours and fabrics, and what's in its wardrobe. Pure functions over the definition,
/// like <see cref="BubbleEditing"/>; the character editor turns each into one undo step.
/// </summary>
public static class LookEditing
{
    /// <summary>
    /// Wears <paramref name="asset"/> in its slot, adding it to the wardrobe if it's new:
    /// replacing what the slot held, or - <paramref name="stack"/>, or a slot that stacks -
    /// on top of it. Wearing what's already worn changes nothing.
    /// </summary>
    public static CharacterDefinition Wear(CharacterDefinition character, StickerAsset asset, bool stack = false)
    {
        var slot = asset.Sticker.Slot;
        var wardrobe = character.Wardrobe.Find(asset.Id) is null ? character.Wardrobe.With(asset) : character.Wardrobe;
        var current = character.Stickers.TryGetValue(slot, out var worn) ? worn : [];
        if (current.Contains(asset.Id))
            return wardrobe == character.Wardrobe ? character : character with { Wardrobe = wardrobe };
        var next = stack || StickerSlots.Get(slot).Stacks ? current.Append(asset.Id).ToList() : [asset.Id];
        return character with { Stickers = WithSlot(character.Stickers, slot, next), Wardrobe = wardrobe };
    }

    /// <summary>Nothing in <paramref name="slot"/> (the gallery's "None").</summary>
    public static CharacterDefinition ClearSlot(CharacterDefinition character, string slot) =>
        character.Stickers.TryGetValue(slot, out var worn) && worn.Count > 0
            ? character with { Stickers = WithSlot(character.Stickers, slot, []) }
            : character;

    /// <summary>Stops wearing a sticker; it stays in the wardrobe.</summary>
    public static CharacterDefinition TakeOff(CharacterDefinition character, StickerId id)
    {
        var slots = new SortedDictionary<string, IReadOnlyList<StickerId>>(character.Stickers, StringComparer.Ordinal);
        foreach (var (slot, ids) in character.Stickers)
        {
            if (ids.Contains(id))
                slots[slot] = ids.Where(i => i != id).ToList();
        }
        return character with { Stickers = slots };
    }

    /// <summary>Removes a sticker from the wardrobe altogether - and so from everything that wore it here, named looks included.</summary>
    public static CharacterDefinition RemoveFromWardrobe(CharacterDefinition character, StickerId id)
    {
        var result = TakeOff(character, id) with { Wardrobe = character.Wardrobe.Without(id) };
        var revisions = character.Revisions.ToDictionary(r => r.Key, r => r.Value with
        {
            ActiveStickers = new SortedDictionary<string, IReadOnlyList<StickerId>>(
                r.Value.ActiveStickers.ToDictionary(s => s.Key, s => (IReadOnlyList<StickerId>)s.Value.Where(i => i != id).ToList()), StringComparer.Ordinal)
        });
        return result with { Revisions = revisions };
    }

    /// <summary>Moves a worn sticker up (+1) or down (-1) its slot's stack.</summary>
    public static CharacterDefinition MoveInStack(CharacterDefinition character, StickerId id, int direction)
    {
        foreach (var (slot, ids) in character.Stickers)
        {
            var index = ids.ToList().IndexOf(id);
            if (index < 0)
                continue;
            var target = Math.Clamp(index + Math.Sign(direction), 0, ids.Count - 1);
            if (target == index)
                return character;
            var list = ids.ToList();
            (list[index], list[target]) = (list[target], list[index]);
            return character with { Stickers = WithSlot(character.Stickers, slot, list) };
        }
        return character;
    }

    /// <summary>The character's own colour for a colour slot - kept when outfits change.</summary>
    public static CharacterDefinition SetColor(CharacterDefinition character, string colorSlot, ColorValue color) =>
        character.ColorSlots.TryGetValue(colorSlot, out var current) && current == color
            ? character
            : character with { ColorSlots = new SortedDictionary<string, ColorValue>(character.ColorSlots, StringComparer.Ordinal) { [colorSlot] = color } };

    /// <summary>
    /// The character's own fabric (pattern/texture) for a colour slot. A plain fabric is
    /// stored too, not removed: it has to win over a sticker's default (plain jeans).
    /// </summary>
    public static CharacterDefinition SetFabric(CharacterDefinition character, string colorSlot, Fabric fabric) =>
        character.Fabrics is { } existing && existing.TryGetValue(colorSlot, out var current) && current == fabric
            ? character
            : character with { Fabrics = new SortedDictionary<string, Fabric>(character.Fabrics ?? new SortedDictionary<string, Fabric>(), StringComparer.Ordinal) { [colorSlot] = fabric } };

    /// <summary>
    /// Replaces a sticker in the wardrobe with an edited version of it. An edited library
    /// copy stops being one (its <see cref="Sticker.Source"/> goes), so tidying never removes it.
    /// </summary>
    public static CharacterDefinition UpdateSticker(CharacterDefinition character, Sticker sticker)
    {
        if (character.Wardrobe.Find(sticker.Id) is not { } asset || asset.Sticker == sticker)
            return character;
        var edited = sticker with { Source = null };
        return character with { Wardrobe = character.Wardrobe.With(asset with { Sticker = edited }) };
    }

    /// <summary>The colour slots the character's worn stickers use, skin first, then in the order the stickers are painted.</summary>
    public static IReadOnlyList<string> ColorSlotsInUse(CharacterDefinition character)
    {
        var look = CharacterLooks.Resolve(character);
        var slots = new List<string> { CharacterDefinition.SkinSlot };
        foreach (var worn in look.Stickers)
        {
            foreach (var part in worn.Asset.Sticker.Parts)
            {
                if (part.Cover is { } cover && !slots.Contains(cover.Color))
                    slots.Add(cover.Color);
            }
            foreach (var slot in worn.Asset.Sticker.Colors.Keys)
            {
                if (!slots.Contains(slot))
                    slots.Add(slot);
            }
        }
        return slots;
    }

    /// <summary>
    /// The sticker ids something outside the definition wears: the character's named
    /// looks and every panel override (<paramref name="instances"/>) that shows it.
    /// </summary>
    public static IReadOnlySet<StickerId> WornElsewhere(CharacterDefinition character, IEnumerable<CharacterInstance> instances)
    {
        var ids = new HashSet<StickerId>();
        foreach (var revision in character.Revisions.Values)
            ids.UnionWith(revision.ActiveStickers.Values.SelectMany(v => v));
        foreach (var instance in instances.Where(i => i.CharacterId == character.Id))
            ids.UnionWith(instance.Overrides?.ActiveStickerOverrides?.Values.SelectMany(v => v) ?? []);
        return ids;
    }

    /// <summary>
    /// The wardrobe without unmodified library copies that nothing wears - not the
    /// character, not a named look, not a panel (<paramref name="wornElsewhere"/>). Trying
    /// things on leaves no files behind; anything edited or imported stays.
    /// </summary>
    public static CharacterDefinition TidyWardrobe(CharacterDefinition character, IReadOnlySet<StickerId> wornElsewhere)
    {
        var worn = character.Stickers.Values.SelectMany(v => v).ToHashSet();
        var unused = character.Wardrobe.Stickers.Values
            .Where(a => a.Sticker.IsFromLibrary && !worn.Contains(a.Id) && !wornElsewhere.Contains(a.Id))
            .Select(a => a.Id)
            .ToList();
        if (unused.Count == 0)
            return character;
        var wardrobe = character.Wardrobe;
        foreach (var id in unused)
            wardrobe = wardrobe.Without(id);
        return character with { Wardrobe = wardrobe };
    }

    private static SortedDictionary<string, IReadOnlyList<StickerId>> WithSlot(SortedDictionary<string, IReadOnlyList<StickerId>> stickers, string slot, IReadOnlyList<StickerId> ids) =>
        new(stickers, StringComparer.Ordinal) { [slot] = ids };
}

/// <summary>
/// The Sticker tab's sliders for a worn garment: how long it is, how long its sleeves are,
/// how loose it fits - each one number over the sticker's cover parts.
/// </summary>
public static class StickerFitting
{
    /// <summary>The part "Length" moves: a skirt's hem if it has one, else the legs, else the torso's hem.</summary>
    public static StickerPart? LengthPart(Sticker sticker) =>
        new[] { BodyRegion.Skirt, BodyRegion.Leg, BodyRegion.Torso }
            .Select(region => sticker.Parts.FirstOrDefault(p => p.Region == region && p.Cover is not null && p.Blend is null))
            .FirstOrDefault(p => p is not null);

    public static StickerPart? SleevePart(Sticker sticker) =>
        sticker.Parts.FirstOrDefault(p => p.Region == BodyRegion.Arm && p.Cover is not null && p.Blend is null);

    public static bool HasCovers(Sticker sticker) => sticker.Parts.Any(p => p.Cover is not null);

    /// <summary>How far down its region the length part reaches (0-1).</summary>
    public static double Length(Sticker sticker) => LengthPart(sticker)?.Cover!.To ?? 0;

    public static double Sleeves(Sticker sticker) => SleevePart(sticker)?.Cover!.To ?? 0;

    /// <summary>The loosest part's ease.</summary>
    public static double Fit(Sticker sticker) =>
        sticker.Parts.Where(p => p.Cover is not null).Select(p => p.Cover!.EaseOrDefault).DefaultIfEmpty(PartCover.DefaultEase).Max();

    public static Sticker WithLength(Sticker sticker, double to) => WithPart(sticker, LengthPart(sticker), to);

    public static Sticker WithSleeves(Sticker sticker, double to) => WithPart(sticker, SleevePart(sticker), to);

    /// <summary>Every cover part eased so the loosest has <paramref name="ease"/>, keeping their differences.</summary>
    public static Sticker WithFit(Sticker sticker, double ease)
    {
        var delta = Math.Max(0, ease) - Fit(sticker);
        return sticker with
        {
            Parts = sticker.Parts.Select(p => p.Cover is { } c ? p with { Cover = c with { Ease = Math.Round(Math.Max(0, c.EaseOrDefault + delta), 4) } } : p).ToList()
        };
    }

    private static Sticker WithPart(Sticker sticker, StickerPart? part, double to)
    {
        if (part?.Cover is not { } cover)
            return sticker;
        var clamped = Math.Round(Math.Clamp(to, Math.Min(cover.From + 0.02, 1), 1), 3);
        return sticker with
        {
            Parts = sticker.Parts.Select(p => ReferenceEquals(p, part) ? p with { Cover = cover with { To = clamped } } : p).ToList()
        };
    }
}
