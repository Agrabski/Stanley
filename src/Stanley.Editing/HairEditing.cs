using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editing;

/// <summary>A dye scheme for the whole head (docs: modular hair) - one click to a multi-colour look, then editable piece by piece.</summary>
public enum HairScheme
{
    /// <summary>Every piece back to the hair colour, and no dye.</summary>
    Natural,

    /// <summary>Top and fringe in the accent.</summary>
    TwoTone,

    /// <summary>The back - the hair underneath - in the accent.</summary>
    Peekaboo,

    /// <summary>Just the fringe in the accent.</summary>
    FringeOnly,

    /// <summary>The ends of all the hair in the accent (<see cref="PatternKind.Tips"/>).</summary>
    DipDye,

    /// <summary>All the hair fading into the accent (<see cref="PatternKind.Ombre"/>).</summary>
    Ombre,

    /// <summary>All the hair in rainbow bands (<see cref="PatternKind.Rainbow"/>).</summary>
    Rainbow
}

/// <summary>
/// Hair built from pieces (docs: modular hair): wearing a whole hairdo at once, a piece's
/// own colour, fringes over glasses, streaks each in their own colour, colour schemes, and
/// switching an old whole-hairstyle sticker for the pieces that replace it. Pure functions
/// on a (flattened) character, like <see cref="LookEditing"/>; the character editor stores
/// the result in the look being edited.
/// </summary>
public static class HairEditing
{
    /// <summary>How far up the ends reach in the Dip-dye scheme.</summary>
    public const double DipDyeReach = 0.35;

    /// <summary>The stickers worn in the hairdo's slots (<see cref="StickerSlots.Hairdo"/>): what a hairstyle replaces.</summary>
    public static IReadOnlyList<StickerId> WornHairdo(CharacterDefinition character) =>
        StickerSlots.Hairdo.SelectMany(slot => character.Stickers.TryGetValue(slot, out var ids) ? ids : []).ToList();

    /// <summary>
    /// Puts on a hairdo's <paramref name="pieces"/>, each in its style (null for its default -
    /// a piece reused from the wardrobe drops the style another hairstyle gave it), as one edit. <paramref name="replace"/> takes everything in the hairdo's slots off first
    /// (a hairstyle preset); otherwise the pieces go on over what's there, skipping any already
    /// worn. Colours are the character's, so they're kept either way.
    /// </summary>
    public static CharacterDefinition WearHairdo(CharacterDefinition character, IReadOnlyList<(StickerAsset Asset, string? Style)> pieces, bool replace)
    {
        if (replace)
        {
            foreach (var slot in StickerSlots.Hairdo)
            {
                if (character.Stickers.TryGetValue(slot, out var ids) && ids.Count > 0)
                    character = LookEditing.ClearSlot(character, slot);
            }
        }
        var worn = WornHairdo(character).ToHashSet();
        foreach (var (asset, style) in pieces)
        {
            var already = worn.Contains(asset.Id);
            if (!already)
                character = LookEditing.Wear(character, asset);
            // A piece already in the mix keeps its style unless the hairstyle names one.
            if (style is not null || !already)
                character = LookEditing.SetVariant(character, asset.Id, style ?? LookEditing.DefaultStyle(asset.Sticker));
        }
        return character;
    }

    /// <summary>
    /// Whether putting a piece on in <paramref name="slot"/> should bring a top with it: a
    /// fringe, sides, back or extra on a head with nothing on top (no top piece, no whole
    /// hairstyle) would float on a bald crown.
    /// </summary>
    public static bool NeedsTop(CharacterDefinition character, string slot) =>
        StickerSlots.HairPieces.Contains(slot) && slot != StickerSlots.HairTop
        && !Worn(character, StickerSlots.HairTop) && !Worn(character, StickerSlots.Hair);

    private static bool Worn(CharacterDefinition character, string slot) => character.Stickers.TryGetValue(slot, out var ids) && ids.Count > 0;

    // ---------------------------------------------------------------- a piece's own colour

    /// <summary>
    /// The colour key the Sticker tab recolours for worn sticker <paramref name="id"/>: a hair
    /// piece's own (its slot, "hairFringe"), a streak's own ("streak-&lt;id&gt;"), "hair" for a whole
    /// hairstyle - or null for anything that isn't hair.
    /// </summary>
    public static string? ColorKeyOf(CharacterDefinition character, StickerId id)
    {
        foreach (var (slot, ids) in character.Stickers)
        {
            if (!ids.Contains(id))
                continue;
            if (slot == StickerSlots.HairStreaks)
                return StickerSlots.StreakColorKey(id);
            if (StickerSlots.Get(slot).SharesColor is not null)
                return slot;
            if (slot == StickerSlots.Hair)
                return StickerSlots.Hair;
            return StickerSlots.HasOwnColorKey(slot) ? StickerSlots.StickerColorKey(slot, id) : null;
        }
        return null;
    }

    /// <summary>Whether <paramref name="key"/> (a piece's colour key) has a colour or dye of its own on <paramref name="character"/>, rather than following the hair's.</summary>
    public static bool HasOwnColor(CharacterDefinition character, string key) =>
        character.ColorSlots.ContainsKey(key) || character.Fabrics?.ContainsKey(key) == true;

    /// <summary>
    /// "Same as hair": the piece <paramref name="key"/> follows the hair's colour and dye
    /// again. <paramref name="baseline"/> is the default look when a named look is being
    /// edited (else null): a named look can only override, so where the default look gives
    /// the piece its own colour, the look gets a copy of its current hair colour (and dye)
    /// instead of following it.
    /// </summary>
    public static CharacterDefinition FollowHair(CharacterDefinition character, string key, CharacterDefinition? baseline = null)
    {
        var look = CharacterLooks.Resolve(character);
        var colors = new SortedDictionary<string, ColorValue>(character.ColorSlots, StringComparer.Ordinal);
        var fabrics = new SortedDictionary<string, Fabric>(character.Fabrics ?? new SortedDictionary<string, Fabric>(), StringComparer.Ordinal);
        colors.Remove(key);
        fabrics.Remove(key);
        // What the key follows: the hair for a piece, the slot's colour for a sticker's own key ("top@id" -> "top").
        var follows = StickerSlots.IsStickerColorKey(key) ? StickerSlots.SharedColorOf(key) : StickerSlots.Hair;
        if (baseline is not null && baseline.ColorSlots.ContainsKey(key) && look.Colors.TryGetValue(follows, out var hair))
            colors[key] = hair;
        if (baseline?.Fabrics?.ContainsKey(key) == true)
            fabrics[key] = look.Fabrics.TryGetValue(follows, out var hairFabric) ? hairFabric : new Fabric();
        return character with { ColorSlots = colors, Fabrics = fabrics.Count == 0 ? null : fabrics };
    }

    /// <summary>
    /// The colour slots the Look tab's Colours group shows, from <paramref name="inUse"/>
    /// (<see cref="LookEditing.ColorSlotsInUse"/>): each worn hair piece with a colour of its
    /// own right after Hair, and no swatch for streaks - each streak has its own colour, set
    /// by clicking it.
    /// </summary>
    public static IReadOnlyList<string> ColorGroupSlots(CharacterDefinition character, IReadOnlyList<string> inUse)
    {
        var own = StickerSlots.HairPieces.Where(slot => Worn(character, slot) && HasOwnColor(character, slot)).ToList();
        var result = inUse.Where(s => s != StickerSlots.StreakColor && !own.Contains(s)).ToList();
        var at = result.IndexOf(StickerSlots.Hair);
        result.InsertRange(at < 0 ? result.Count : at + 1, own);
        return result;
    }

    // ---------------------------------------------------------------- over glasses

    /// <summary>Whether worn sticker <paramref name="id"/> paints over glasses (<see cref="Sticker.OverGlasses"/>).</summary>
    public static bool IsOverGlasses(CharacterDefinition character, StickerId id) => character.Wardrobe.Find(id)?.Sticker.OverGlasses == true;

    /// <summary>Paints sticker <paramref name="id"/> over glasses, or back at its slot's place. Flipping a library copy makes it the character's own, as any edit does.</summary>
    public static CharacterDefinition SetOverGlasses(CharacterDefinition character, StickerId id, bool over)
    {
        if (character.Wardrobe.Find(id) is not { } asset || (asset.Sticker.OverGlasses == true) == over)
            return character;
        return LookEditing.UpdateSticker(character, asset.Sticker with { OverGlasses = over ? true : null });
    }

    // ---------------------------------------------------------------- streaks

    /// <summary>
    /// Puts on a streak (already a fresh copy, placed where it goes) in the colour of the last
    /// streak put on - the top of the streaks' stack - if that one has a colour of its own.
    /// </summary>
    public static CharacterDefinition WearStreak(CharacterDefinition character, StickerAsset streak)
    {
        var last = LastStreakColor(character);
        character = LookEditing.Wear(character, streak);
        return last is { } color ? LookEditing.SetColor(character, StickerSlots.StreakColorKey(streak.Id), color) : character;
    }

    /// <summary>
    /// Where the <paramref name="copy"/>th extra streak goes, from where the design itself sits
    /// (template units): across the fringe - centre, then alternately right and left, a lock's
    /// width apart - so a few clicks give a few separate streaks to drag into place.
    /// </summary>
    public static Point2D StreakSpot(int copy)
    {
        if (copy <= 0)
            return default;
        var step = (copy + 1) / 2 * StreakSpacing;
        return new Point2D(copy % 2 == 1 ? step : -step, 0);
    }

    /// <summary>The spacing of <see cref="StreakSpot"/>, in template units - about a chunky lock's width.</summary>
    public const double StreakSpacing = 16;

    /// <summary>The own colour of the streak put on last, or null if there's none (or it follows the streaks' default).</summary>
    public static ColorValue? LastStreakColor(CharacterDefinition character)
    {
        if (!character.Stickers.TryGetValue(StickerSlots.HairStreaks, out var ids) || ids.Count == 0)
            return null;
        return character.ColorSlots.TryGetValue(StickerSlots.StreakColorKey(ids[^1]), out var color) ? color : null;
    }

    /// <summary>
    /// Drops the colours of streaks that are no longer in the wardrobe - from the character
    /// and every named look - so a removed streak leaves nothing behind in the files.
    /// </summary>
    public static CharacterDefinition DropOrphanStreakColors(CharacterDefinition character)
    {
        bool Orphan(string key) => StickerSlots.StickerOfKey(key) is { } id && character.Wardrobe.Find(id) is null;
        var colors = Without(character.ColorSlots, Orphan);
        var fabrics = Without(character.Fabrics, Orphan);
        var revisionsChanged = false;
        var revisions = character.Revisions.ToDictionary(r => r.Key, r =>
        {
            var lookColors = Without(r.Value.ColorSlotValues, Orphan)!;
            var lookFabrics = Without(r.Value.FabricValues, Orphan);
            if (ReferenceEquals(lookColors, r.Value.ColorSlotValues) && ReferenceEquals(lookFabrics, r.Value.FabricValues))
                return r.Value;
            revisionsChanged = true;
            return r.Value with { ColorSlotValues = lookColors, FabricValues = lookFabrics is { Count: 0 } ? null : lookFabrics };
        });
        if (ReferenceEquals(colors, character.ColorSlots) && ReferenceEquals(fabrics, character.Fabrics) && !revisionsChanged)
            return character;
        return character with
        {
            ColorSlots = colors!, Fabrics = fabrics is { Count: 0 } ? null : fabrics,
            Revisions = revisionsChanged ? revisions : character.Revisions
        };
    }

    private static SortedDictionary<string, T>? Without<T>(SortedDictionary<string, T>? values, Func<string, bool> drop)
    {
        if (values is null || !values.Keys.Any(drop))
            return values;
        var kept = new SortedDictionary<string, T>(StringComparer.Ordinal);
        foreach (var (key, value) in values.Where(v => !drop(v.Key)))
            kept[key] = value;
        return kept;
    }

    // ---------------------------------------------------------------- schemes

    /// <summary>
    /// Dresses the hair in <paramref name="scheme"/>, with <paramref name="accent"/> as its
    /// second colour (Rainbow uses its own six). Every scheme starts from Natural - every piece
    /// following the hair, the hair undyed - so picking one after another never piles up.
    /// <paramref name="baseline"/> as in <see cref="FollowHair"/>.
    /// </summary>
    public static CharacterDefinition ApplyScheme(CharacterDefinition character, HairScheme scheme, ColorValue accent, CharacterDefinition? baseline = null)
    {
        foreach (var piece in StickerSlots.HairPieces)
        {
            if (HasOwnColor(character, piece) || (baseline is not null && HasOwnColor(baseline, piece)))
                character = FollowHair(character, piece, baseline);
        }
        var hairFabric = CharacterLooks.Resolve(character).FabricOf(StickerSlots.Hair);
        if (hairFabric?.Pattern is { IsDye: true })
            character = LookEditing.SetFabric(character, StickerSlots.Hair, hairFabric with { Pattern = null });
        var undyed = hairFabric?.Pattern is { IsDye: true } ? hairFabric with { Pattern = null } : hairFabric ?? new Fabric();
        return scheme switch
        {
            HairScheme.TwoTone => LookEditing.SetColor(LookEditing.SetColor(character, StickerSlots.HairTop, accent), StickerSlots.HairFringe, accent),
            HairScheme.Peekaboo => LookEditing.SetColor(character, StickerSlots.HairBack, accent),
            HairScheme.FringeOnly => LookEditing.SetColor(character, StickerSlots.HairFringe, accent),
            HairScheme.DipDye => LookEditing.SetFabric(character, StickerSlots.Hair, undyed with { Pattern = new PatternFill(PatternKind.Tips, [accent], Weight: DipDyeReach) }),
            HairScheme.Ombre => LookEditing.SetFabric(character, StickerSlots.Hair, undyed with { Pattern = new PatternFill(PatternKind.Ombre, [accent]) }),
            HairScheme.Rainbow => LookEditing.SetFabric(character, StickerSlots.Hair, undyed with { Pattern = new PatternFill(PatternKind.Rainbow, PatternFill.RainbowColors) }),
            _ => character
        };
    }

    // ---------------------------------------------------------------- old whole hairstyles

    /// <summary>
    /// Switches the old whole-hairstyle sticker <paramref name="legacy"/> for the
    /// <paramref name="pieces"/> that replace it (each in its style), in the default look and
    /// in every named look that wears it - one edit, colours kept. Panels that wear it for
    /// one panel only keep it, so the old sticker stays in the wardrobe until nothing wears it.
    /// </summary>
    public static CharacterDefinition ReplaceLegacy(CharacterDefinition character, StickerId legacy, IReadOnlyList<(StickerAsset Asset, string? Style)> pieces)
    {
        var wardrobe = character.Wardrobe;
        foreach (var (asset, _) in pieces)
        {
            if (wardrobe.Find(asset.Id) is null)
                wardrobe = wardrobe.With(asset);
        }
        var stickers = Swap(character.Stickers, character.Stickers, legacy, pieces) ?? character.Stickers;
        var revisions = character.Revisions.ToDictionary(r => r.Key, r =>
            Swap(r.Value.ActiveStickers, stickers, legacy, pieces) is { } swapped ? r.Value with { ActiveStickers = swapped } : r.Value);
        character = character with { Stickers = stickers, Wardrobe = wardrobe, Revisions = revisions };
        foreach (var (asset, style) in pieces)
        {
            if (style is not null)
                character = LookEditing.SetVariant(character, asset.Id, style);
        }
        return character;
    }

    /// <summary>
    /// <paramref name="slots"/> (the default look's, or a named look's sparse ones) with
    /// <paramref name="legacy"/> taken out of its hair slot and the pieces added to theirs -
    /// on top of what the look wears there (<paramref name="inherited"/> where it doesn't say)
    /// - or null if it doesn't wear the legacy sticker.
    /// </summary>
    private static SortedDictionary<string, IReadOnlyList<StickerId>>? Swap(SortedDictionary<string, IReadOnlyList<StickerId>> slots,
        SortedDictionary<string, IReadOnlyList<StickerId>> inherited, StickerId legacy, IReadOnlyList<(StickerAsset Asset, string? Style)> pieces)
    {
        if (!slots.TryGetValue(StickerSlots.Hair, out var hair) || !hair.Contains(legacy))
            return null;
        var result = new SortedDictionary<string, IReadOnlyList<StickerId>>(slots, StringComparer.Ordinal)
        {
            [StickerSlots.Hair] = hair.Where(id => id != legacy).ToList()
        };
        foreach (var (asset, _) in pieces)
        {
            var slot = asset.Sticker.Slot;
            var current = result.TryGetValue(slot, out var own) ? own : inherited.TryGetValue(slot, out var from) ? from : [];
            if (!current.Contains(asset.Id))
                result[slot] = [.. current, asset.Id];
        }
        return result;
    }
}
