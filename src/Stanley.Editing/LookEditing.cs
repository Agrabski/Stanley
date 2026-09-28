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
    /// Wears <paramref name="asset"/> in its slot, on top of whatever the slot already
    /// holds (a cap over a hood, a shirt over a T-shirt), adding it to the wardrobe if it's
    /// new. Nothing is ever replaced; wearing what's already worn changes nothing.
    /// </summary>
    public static CharacterDefinition Wear(CharacterDefinition character, StickerAsset asset)
    {
        var slot = asset.Sticker.Slot;
        var wardrobe = character.Wardrobe.Find(asset.Id) is null ? character.Wardrobe.With(asset) : character.Wardrobe;
        var current = character.Stickers.TryGetValue(slot, out var worn) ? worn : [];
        if (current.Contains(asset.Id))
            return wardrobe == character.Wardrobe ? character : character with { Wardrobe = wardrobe };
        return character with { Stickers = WithSlot(character.Stickers, slot, current.Append(asset.Id).ToList()), Wardrobe = wardrobe };
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

    /// <summary>Removes a sticker from the wardrobe altogether - and so from everything that wore it here, named looks included, and the styles it was worn in.</summary>
    public static CharacterDefinition RemoveFromWardrobe(CharacterDefinition character, StickerId id)
    {
        var result = TakeOff(character, id) with
        {
            Wardrobe = character.Wardrobe.Without(id),
            StickerVariants = WithoutStyles(character.StickerVariants, i => i == id)
        };
        var revisions = character.Revisions.ToDictionary(r => r.Key, r => r.Value with
        {
            ActiveStickers = new SortedDictionary<string, IReadOnlyList<StickerId>>(
                r.Value.ActiveStickers.ToDictionary(s => s.Key, s => (IReadOnlyList<StickerId>)s.Value.Where(i => i != id).ToList()), StringComparer.Ordinal),
            StickerVariantValues = WithoutStyles(r.Value.StickerVariantValues, i => i == id)
        });
        return result with { Revisions = revisions };
    }

    // ---------------------------------------------------------------- styles (docs/sticker-system.md §20)

    /// <summary>
    /// Wears sticker <paramref name="id"/> in the style <paramref name="variant"/> - one of its
    /// variants: a hood up or down, a cap's brim forward or back. Its default style (what it
    /// shows with none chosen, <see cref="DefaultStyle"/>) leaves no entry, so files stay
    /// sparse. A sticker that isn't in the wardrobe, or a variant it doesn't have, changes nothing.
    /// </summary>
    public static CharacterDefinition SetVariant(CharacterDefinition character, StickerId id, string variant)
    {
        if (character.Wardrobe.Find(id) is not { } asset || !asset.Sticker.Variants.Contains(variant))
            return character;
        var styles = new SortedDictionary<StickerId, string>(character.StickerVariants ?? new SortedDictionary<StickerId, string>());
        if (variant == DefaultStyle(asset.Sticker))
        {
            if (!styles.Remove(id))
                return character;
        }
        else
        {
            if (styles.TryGetValue(id, out var current) && current == variant)
                return character;
            styles[id] = variant;
        }
        return character with { StickerVariants = styles.Count == 0 ? null : styles };
    }

    /// <summary>The style a sticker shows when none is chosen: its first variant ("neutral" instead, if it has one).</summary>
    public static string DefaultStyle(Sticker sticker) => sticker.VariantFor(sticker.Slot, null);

    /// <summary>
    /// Whether a sticker worn in <paramref name="slot"/> has styles to choose from: more than
    /// one variant, outside the face - a face's variants are its expressions, set per panel.
    /// </summary>
    public static bool HasStyles(Sticker sticker, string slot) => !StickerSlots.Get(slot).IsFace && sticker.Variants.Count > 1;

    /// <summary>A style's name for people: "up" is "Up", "brim-back" (or "brimBack") "Brim back".</summary>
    public static string StyleName(string variant)
    {
        var words = new System.Text.StringBuilder();
        for (var i = 0; i < variant.Length; i++)
        {
            var c = variant[i];
            if (c is '-' or '_' or ' ')
            {
                if (words.Length > 0 && words[words.Length - 1] != ' ')
                    words.Append(' ');
                continue;
            }
            if (i > 0 && char.IsUpper(c) && char.IsLower(variant[i - 1]))
                words.Append(' ');
            words.Append(words.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        var name = words.ToString().TrimEnd();
        return name.Length == 0 ? variant : name;
    }

    /// <summary><paramref name="styles"/> without the entries of stickers that are <paramref name="gone"/>: the same map if none is, null if nothing's left (files stay sparse).</summary>
    private static SortedDictionary<StickerId, string>? WithoutStyles(SortedDictionary<StickerId, string>? styles, Func<StickerId, bool> gone)
    {
        if (styles is null || !styles.Keys.Any(gone))
            return styles;
        var kept = new SortedDictionary<StickerId, string>(styles.Where(s => !gone(s.Key)).ToDictionary(s => s.Key, s => s.Value));
        return kept.Count == 0 ? null : kept;
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
    /// things on leaves no files behind; anything edited or imported stays. The styles of
    /// stickers no longer in the wardrobe go too, the named looks' included.
    /// </summary>
    public static CharacterDefinition TidyWardrobe(CharacterDefinition character, IReadOnlySet<StickerId> wornElsewhere)
    {
        var worn = character.Stickers.Values.SelectMany(v => v).ToHashSet();
        var unused = character.Wardrobe.Stickers.Values
            .Where(a => a.Sticker.IsFromLibrary && !worn.Contains(a.Id) && !wornElsewhere.Contains(a.Id))
            .Select(a => a.Id)
            .ToList();
        var wardrobe = character.Wardrobe;
        foreach (var id in unused)
            wardrobe = wardrobe.Without(id);
        bool Gone(StickerId id) => wardrobe.Find(id) is null;
        var styles = WithoutStyles(character.StickerVariants, Gone);
        var lookStylesGone = character.Revisions.Values.Any(r => r.StickerVariantValues?.Keys.Any(Gone) == true);
        if (unused.Count == 0 && ReferenceEquals(styles, character.StickerVariants) && !lookStylesGone)
            return character;
        var revisions = !lookStylesGone ? character.Revisions : character.Revisions.ToDictionary(r => r.Key,
            r => WithoutStyles(r.Value.StickerVariantValues, Gone) is var kept && ReferenceEquals(kept, r.Value.StickerVariantValues) ? r.Value : r.Value with { StickerVariantValues = kept });
        return character with { Wardrobe = wardrobe, StickerVariants = styles, Revisions = revisions };
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
    /// definition: what's worn per slot, colours, fabrics and styles. Every edit above works on
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
        var styles = new SortedDictionary<StickerId, string>(character.StickerVariants ?? new SortedDictionary<StickerId, string>());
        Overlay(stickers, revision?.ActiveStickers);
        Overlay(colors, revision?.ColorSlotValues);
        Overlay(fabrics, revision?.FabricValues);
        Overlay(styles, revision?.StickerVariantValues);
        Overlay(stickers, overrides?.ActiveStickerOverrides);
        Overlay(colors, overrides?.ColorSlotOverrides);
        Overlay(fabrics, overrides?.FabricOverrides);
        Overlay(styles, overrides?.StickerVariantOverrides);
        return character with
        {
            Stickers = stickers, ColorSlots = colors, Fabrics = fabrics.Count == 0 ? null : fabrics, StickerVariants = styles.Count == 0 ? null : styles
        };
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
        var (stickers, colors, fabrics, styles) = Differences(character, edited);
        var updated = revision with
        {
            ActiveStickers = stickers, ColorSlotValues = colors, FabricValues = fabrics.Count == 0 ? null : fabrics,
            StickerVariantValues = styles.Count == 0 ? null : styles
        };
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
        var (stickers, colors, fabrics, styles) = Differences(Project(character, revision), edited);
        var overrides = new CharacterInstanceOverrides(stickers.Count == 0 ? null : stickers, colors.Count == 0 ? null : colors, fabrics.Count == 0 ? null : fabrics,
            styles.Count == 0 ? null : styles);
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
            source?.FabricValues is { } fabrics ? new SortedDictionary<string, Fabric>(fabrics, StringComparer.Ordinal) : null,
            source?.StickerVariantValues is { } styles ? new SortedDictionary<StickerId, string>(styles) : null);
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

    /// <summary>
    /// Where <paramref name="edited"/> dresses differently from <paramref name="baseline"/>: slots,
    /// colours, fabrics (a fabric taken off is a plain one, so it wins over what's underneath)
    /// and styles (a sticker back to its default style says so, for the same reason).
    /// </summary>
    private static (SortedDictionary<string, IReadOnlyList<StickerId>> Stickers, SortedDictionary<string, ColorValue> Colors, SortedDictionary<string, Fabric> Fabrics,
        SortedDictionary<StickerId, string> Styles) Differences(CharacterDefinition baseline, CharacterDefinition edited)
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
        var styles = new SortedDictionary<StickerId, string>();
        var baseStyles = baseline.StickerVariants ?? new SortedDictionary<StickerId, string>();
        var editedStyles = edited.StickerVariants ?? new SortedDictionary<StickerId, string>();
        foreach (var id in baseStyles.Keys.Union(editedStyles.Keys))
        {
            var before = baseStyles.GetValueOrDefault(id);
            var after = editedStyles.GetValueOrDefault(id);
            if (after is null && (edited.Wardrobe.Find(id) ?? baseline.Wardrobe.Find(id)) is { } asset)
                after = DefaultStyle(asset.Sticker);
            if (after is not null && after != before)
                styles[id] = after;
        }
        return (stickers, colors, fabrics, styles);
    }

    private static void Overlay<TKey, T>(SortedDictionary<TKey, T> into, IReadOnlyDictionary<TKey, T>? values) where TKey : notnull
    {
        foreach (var (key, value) in values ?? new Dictionary<TKey, T>())
            into[key] = value;
    }

    private static SortedDictionary<string, IReadOnlyList<StickerId>> WithSlot(SortedDictionary<string, IReadOnlyList<StickerId>> stickers, string slot, IReadOnlyList<StickerId> ids) =>
        new(stickers, StringComparer.Ordinal) { [slot] = ids };
}

/// <summary>
/// Wearing the same design more than once (docs/sticker-system.md §19): in a slot that
/// stamps copies (<see cref="StickerSlotInfo.StampsCopies"/>), each click on a placeable
/// design - drawn art or text, a print or a badge - puts on another copy, with its own id
/// and placement (<see cref="Spot"/>), so ten skulls are ten stickers to move one by one.
/// Pure functions, like <see cref="LookEditing"/>.
/// </summary>
public static class StickerCopies
{
    /// <summary>Where the Sticker tab's Duplicate puts the copy, from the one it copies, in template units: a small step down and to the right.</summary>
    public static Point2D DuplicateStep { get; } = new(35, 35);

    /// <summary>The spacing of <see cref="Spot"/>'s grid, in template units - a little more than a library print is wide.</summary>
    public const double SpotSpacing = 70;

    /// <summary>Whether a sticker is placed rather than generated from the body: it has drawn or typed parts. Only these are worth wearing twice - two identical pairs of gloves would sit exactly on top of each other.</summary>
    public static bool IsPlaceable(Sticker sticker) => sticker.Parts.Any(p => p.Art is not null);

    /// <summary>
    /// Where the <paramref name="copy"/>th extra copy of a design goes, from where the design
    /// itself sits: across the chest and down - centre, right, left, then the next row - so a
    /// dozen stay on the shirt before any lands on another (the next dozen sit a little aside).
    /// </summary>
    public static Point2D Spot(int copy)
    {
        if (copy <= 0)
            return default;
        var (index, round) = (copy % 12, copy / 12);
        var column = (index % 3) switch { 0 => 0, 1 => SpotSpacing, _ => -SpotSpacing };
        return new Point2D(column + round * 10, index / 3 * SpotSpacing + round * 10);
    }

    /// <summary>
    /// A copy of <paramref name="asset"/> under a fresh id, its placed parts moved by
    /// <paramref name="shift"/> (template units). The art files come along unchanged. A
    /// moved copy is the user's own (no longer an unmodified library copy).
    /// </summary>
    public static StickerAsset Copy(StickerAsset asset, Point2D shift)
    {
        var sticker = asset.Sticker;
        var moved = shift != default;
        var parts = !moved ? sticker.Parts : sticker.Parts.Select(p => p.Art is { } art
            ? p with { Art = art with { Offset = new Point2D((art.Offset?.X ?? 0) + shift.X, (art.Offset?.Y ?? 0) + shift.Y) } }
            : p).ToList();
        return asset with { Sticker = sticker with { Id = StickerId.New(), Parts = parts, Source = moved ? null : sticker.Source } };
    }

    /// <summary>How many of <paramref name="slot"/>'s worn stickers are the design named <paramref name="name"/> - how many nudges the next copy needs.</summary>
    public static int WornCopies(CharacterDefinition character, string slot, string name) =>
        character.Stickers.TryGetValue(slot, out var worn)
            ? worn.Count(id => character.Wardrobe.Find(id) is { } a && string.Equals(a.Sticker.Name, name, StringComparison.CurrentCultureIgnoreCase))
            : 0;
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
