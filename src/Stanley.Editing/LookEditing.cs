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

    /// <summary>The tiles some fabric draws from: the character's, a named look's, a panel's (<paramref name="instances"/>) or a sticker's default.</summary>
    public static IReadOnlySet<string> TilesInUse(CharacterDefinition character, IEnumerable<CharacterInstance> instances)
    {
        var fabrics = (character.Fabrics?.Values ?? Enumerable.Empty<Fabric>())
            .Concat(character.Revisions.Values.SelectMany(r => r.FabricValues?.Values ?? Enumerable.Empty<Fabric>()))
            .Concat(instances.Where(i => i.CharacterId == character.Id).SelectMany(i => i.Overrides?.FabricOverrides?.Values ?? Enumerable.Empty<Fabric>()))
            .Concat(character.Wardrobe.Stickers.Values.SelectMany(a => a.Sticker.Fabrics?.Values ?? Enumerable.Empty<Fabric>()));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fabric in fabrics)
        {
            if (fabric.Pattern?.Tile is { } pattern)
                names.Add(pattern);
            if (fabric.Texture?.Tile is { } texture)
                names.Add(texture);
        }
        return names;
    }

    /// <summary>The character's tiles without unmodified library copies (<paramref name="isLibraryCopy"/>) that no fabric uses (<paramref name="inUse"/>) - picking a library tile and changing your mind leaves no file behind.</summary>
    public static CharacterDefinition TidyTiles(CharacterDefinition character, IReadOnlySet<string> inUse, Func<string, ArtFile, bool> isLibraryCopy)
    {
        var unused = character.Wardrobe.Tiles.Where(t => !inUse.Contains(t.Key) && isLibraryCopy(t.Key, t.Value)).Select(t => t.Key).ToList();
        if (unused.Count == 0)
            return character;
        var wardrobe = character.Wardrobe;
        foreach (var name in unused)
            wardrobe = wardrobe.WithoutTile(name);
        return character with { Wardrobe = wardrobe };
    }

    // ---------------------------------------------------------------- named looks and one panel

    /// <summary>
    /// The character as <paramref name="revision"/> (a named look) and then
    /// <paramref name="overrides"/> (one panel) dress it, flattened into a plain
    /// definition: what's worn per slot, colours and fabrics. Every edit above works on
    /// it; <see cref="StoreLook"/> and <see cref="StorePanel"/> write the result back as
    /// the sparse changes a look or a panel keeps.
    /// </summary>
    public static CharacterDefinition Project(CharacterDefinition character, CharacterRevision? revision = null, CharacterInstanceOverrides? overrides = null)
    {
        if (revision is null && (overrides is null || overrides.IsEmpty))
            return character;
        var stickers = new SortedDictionary<string, IReadOnlyList<StickerId>>(character.Stickers, StringComparer.Ordinal);
        var colors = new SortedDictionary<string, ColorValue>(character.ColorSlots, StringComparer.Ordinal);
        var fabrics = new SortedDictionary<string, Fabric>(character.Fabrics ?? new SortedDictionary<string, Fabric>(), StringComparer.Ordinal);
        Overlay(stickers, revision?.ActiveStickers);
        Overlay(colors, revision?.ColorSlotValues);
        Overlay(fabrics, revision?.FabricValues);
        Overlay(stickers, overrides?.ActiveStickerOverrides);
        Overlay(colors, overrides?.ColorSlotOverrides);
        Overlay(fabrics, overrides?.FabricOverrides);
        return character with { Stickers = stickers, ColorSlots = colors, Fabrics = fabrics.Count == 0 ? null : fabrics };
    }

    /// <summary>
    /// Writes an edited projection of the named look <paramref name="look"/> back: the look
    /// keeps only where it differs from the default look, the wardrobe takes any sticker
    /// the edit added, and everything else stays the character's own.
    /// </summary>
    public static CharacterDefinition StoreLook(CharacterDefinition character, CharacterRevisionId look, CharacterDefinition edited)
    {
        if (!character.Revisions.TryGetValue(look, out var revision))
            return character with { Wardrobe = edited.Wardrobe };
        var (stickers, colors, fabrics) = Differences(character, edited);
        var updated = revision with { ActiveStickers = stickers, ColorSlotValues = colors, FabricValues = fabrics.Count == 0 ? null : fabrics };
        var revisions = new Dictionary<CharacterRevisionId, CharacterRevision>(character.Revisions) { [look] = updated };
        return character with { Revisions = revisions, Wardrobe = edited.Wardrobe };
    }

    /// <summary>
    /// <paramref name="instance"/> dressed as an edited projection of its look says - kept
    /// as its panel overrides, only where it differs from the look (<paramref name="revision"/>,
    /// or the default look). No differences, no overrides.
    /// </summary>
    public static CharacterInstance StorePanel(CharacterDefinition character, CharacterRevision? revision, CharacterInstance instance, CharacterDefinition edited)
    {
        var (stickers, colors, fabrics) = Differences(Project(character, revision), edited);
        var overrides = new CharacterInstanceOverrides(stickers.Count == 0 ? null : stickers, colors.Count == 0 ? null : colors, fabrics.Count == 0 ? null : fabrics);
        return instance with { Overrides = overrides.IsEmpty ? null : overrides };
    }

    /// <summary>A new named look, called <paramref name="name"/>, starting as a copy of <paramref name="from"/> (or of the default look).</summary>
    public static (CharacterDefinition Character, CharacterRevisionId Id) NewLook(CharacterDefinition character, string name, CharacterRevisionId? from = null)
    {
        var id = CharacterRevisionId.New();
        var source = from is { } f && character.Revisions.TryGetValue(f, out var existing) ? existing : null;
        var look = new CharacterRevision(id, character.Id, name,
            new SortedDictionary<string, IReadOnlyList<StickerId>>(source?.ActiveStickers ?? new SortedDictionary<string, IReadOnlyList<StickerId>>(), StringComparer.Ordinal),
            new SortedDictionary<string, ColorValue>(source?.ColorSlotValues ?? new SortedDictionary<string, ColorValue>(), StringComparer.Ordinal),
            null, null,
            source?.FabricValues is { } fabrics ? new SortedDictionary<string, Fabric>(fabrics, StringComparer.Ordinal) : null);
        var revisions = new Dictionary<CharacterRevisionId, CharacterRevision>(character.Revisions) { [id] = look };
        return (character with { Revisions = revisions }, id);
    }

    public static CharacterDefinition RenameLook(CharacterDefinition character, CharacterRevisionId look, string name) =>
        character.Revisions.TryGetValue(look, out var revision) && revision.Name != name && name.Trim().Length > 0
            ? character with { Revisions = new Dictionary<CharacterRevisionId, CharacterRevision>(character.Revisions) { [look] = revision with { Name = name.Trim() } } }
            : character;

    public static CharacterDefinition DeleteLook(CharacterDefinition character, CharacterRevisionId look)
    {
        if (!character.Revisions.ContainsKey(look))
            return character;
        var revisions = new Dictionary<CharacterRevisionId, CharacterRevision>(character.Revisions);
        revisions.Remove(look);
        return character with { Revisions = revisions };
    }

    /// <summary>Where <paramref name="edited"/> dresses differently from <paramref name="baseline"/>: slots, colours, fabrics (a fabric taken off is a plain one, so it wins over what's underneath).</summary>
    private static (SortedDictionary<string, IReadOnlyList<StickerId>> Stickers, SortedDictionary<string, ColorValue> Colors, SortedDictionary<string, Fabric> Fabrics) Differences(
        CharacterDefinition baseline, CharacterDefinition edited)
    {
        var stickers = new SortedDictionary<string, IReadOnlyList<StickerId>>(StringComparer.Ordinal);
        foreach (var slot in baseline.Stickers.Keys.Union(edited.Stickers.Keys))
        {
            var before = baseline.Stickers.TryGetValue(slot, out var b) ? b : [];
            var after = edited.Stickers.TryGetValue(slot, out var a) ? a : [];
            if (!before.SequenceEqual(after))
                stickers[slot] = after;
        }
        var colors = new SortedDictionary<string, ColorValue>(StringComparer.Ordinal);
        foreach (var (slot, color) in edited.ColorSlots)
        {
            if (!baseline.ColorSlots.TryGetValue(slot, out var before) || before != color)
                colors[slot] = color;
        }
        var fabrics = new SortedDictionary<string, Fabric>(StringComparer.Ordinal);
        var baseFabrics = baseline.Fabrics ?? new SortedDictionary<string, Fabric>();
        var editedFabrics = edited.Fabrics ?? new SortedDictionary<string, Fabric>();
        foreach (var slot in baseFabrics.Keys.Union(editedFabrics.Keys))
        {
            var before = baseFabrics.GetValueOrDefault(slot);
            var after = editedFabrics.TryGetValue(slot, out var f) ? f : new Fabric();
            if (!Equals(before, after))
                fabrics[slot] = after;
        }
        return (stickers, colors, fabrics);
    }

    private static void Overlay<T>(SortedDictionary<string, T> into, IReadOnlyDictionary<string, T>? values)
    {
        foreach (var (key, value) in values ?? new Dictionary<string, T>())
            into[key] = value;
    }

    private static SortedDictionary<string, IReadOnlyList<StickerId>> WithSlot(SortedDictionary<string, IReadOnlyList<StickerId>> stickers, string slot, IReadOnlyList<StickerId> ids) =>
        new(stickers, StringComparer.Ordinal) { [slot] = ids };
}

/// <summary>
/// Text prints (docs/sticker-system.md): typed text worn on the clothes instead of a drawn
/// symbol. Pure functions, like <see cref="LookEditing"/>; the character editor turns each
/// into one undo step.
/// </summary>
public static class TextPrints
{
    /// <summary>The colour slot every text print uses by default - the character's own "print" swatch.</summary>
    public const string ColorSlot = "print";

    /// <summary>Off-white, like the library's skull: it reads on most shirts, and the "print" colour slot changes it.</summary>
    public static ColorValue DefaultColor { get; } = ColorValue.FromHex("#f4f4f4");

    /// <summary>Where a text print goes when the shirt already has a print on the chest: under it, like the words under a logo.</summary>
    public static Point2D BelowAnotherPrint { get; } = new(0, 115);

    /// <summary>What the Prints gallery's "Text" button puts on first - there to type over.</summary>
    public const string DefaultText = "HELLO";

    /// <summary>
    /// A new print with <paramref name="text"/> typed on it instead of drawn art: one text
    /// part, pinned on the chest, named after the text. It needs no art files - font
    /// fallback at draw time handles emoji and symbols the chosen font lacks.
    /// </summary>
    /// <param name="offset">Where it sits from the chest, in template units - e.g. <see cref="BelowAnotherPrint"/>.</param>
    public static StickerAsset New(string text, Point2D? offset = null)
    {
        var part = new StickerPart("print", BodyRegion.Torso, Art: new PartArt(ArtMapping.Pin, Offset: offset, KeepReadable: true), Clip: PartClip.Clothes, Text: new PartText(text));
        var sticker = new Sticker(StickerId.New(), text.Trim().Length == 0 ? "Text" : text.Trim(), StickerSlots.Print,
            [part], new SortedDictionary<string, ColorValue> { [ColorSlot] = DefaultColor }, [Sticker.DefaultVariant]);
        return new StickerAsset(sticker, new Dictionary<string, ArtFile>());
    }

    /// <summary>A text print's part (its only one) - for the Sticker tab's text box and Bold toggle.</summary>
    public static StickerPart? Part(Sticker sticker) => sticker.Parts.FirstOrDefault(p => p.Text is not null);

    /// <summary>The text sticker with its typed text changed - and renamed to match, like a new one is named after its text.</summary>
    public static Sticker WithText(Sticker sticker, string text) =>
        Part(sticker) is not { Text: { } current } part
            ? sticker
            : sticker with { Name = text.Trim().Length == 0 ? "Text" : text.Trim(), Parts = sticker.Parts.Select(p => ReferenceEquals(p, part) ? p with { Text = current with { Text = text } } : p).ToList() };

    /// <summary>The text sticker with its Bold toggle changed.</summary>
    public static Sticker WithBold(Sticker sticker, bool bold) =>
        Part(sticker) is not { Text: { } current } part
            ? sticker
            : sticker with { Parts = sticker.Parts.Select(p => ReferenceEquals(p, part) ? p with { Text = current with { Bold = bold } } : p).ToList() };
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
