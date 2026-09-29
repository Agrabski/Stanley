using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editing.Tests;

/// <summary>Modular hair (GitHub issue #59): wearing a hairdo, a piece's own colour, over glasses, streaks, schemes and switching an old hairstyle for its pieces.</summary>
public class HairEditingTests
{
    private static readonly ColorValue Red = ColorValue.FromHex("#c0392b");
    private static readonly ColorValue Blue = ColorValue.FromHex("#3a6fd8");
    private static readonly ColorValue Green = ColorValue.FromHex("#4caf50");
    private static readonly ColorValue Purple = ColorValue.FromHex("#8e24aa");

    /// <summary>A sticker on the head in <paramref name="slot"/>; hair art declares the shared "hair" colour, like the library's pieces.</summary>
    private static StickerAsset Asset(string slot, string name, string? source = null, params string[] variants) =>
        new(new Sticker(StickerId.New(), name, slot, [new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp))],
            slot == StickerSlots.Glasses ? new SortedDictionary<string, ColorValue>() : new SortedDictionary<string, ColorValue> { ["hair"] = ColorValue.FromHex("#222222") },
            variants.Length > 0 ? variants : ["default"], source), new Dictionary<string, ArtFile>());

    private static StickerAsset Top(string name = "Top") => Asset(StickerSlots.HairTop, name);
    private static StickerAsset Fringe(string name = "Fringe") => Asset(StickerSlots.HairFringe, name);
    private static StickerAsset Sides(string name = "Sides") => Asset(StickerSlots.HairSides, name);
    private static StickerAsset Back(string name = "Back") => Asset(StickerSlots.HairBack, name);
    private static StickerAsset Streak(string name = "Streak") => Asset(StickerSlots.HairStreaks, name);

    private static IReadOnlyList<StickerId> Worn(CharacterDefinition c, string slot) => c.Stickers.TryGetValue(slot, out var ids) ? ids : [];

    private static CharacterDefinition Wearing(params StickerAsset[] assets) =>
        assets.Aggregate(CharacterDefinition.Create("A"), LookEditing.Wear);

    /// <summary>The character with the hair colour <paramref name="hair"/> (the character's own, as the Colours group sets it).</summary>
    private static CharacterDefinition WithHair(CharacterDefinition c, ColorValue hair) => LookEditing.SetColor(c, StickerSlots.Hair, hair);

    private static ColorValue ColorOf(CharacterDefinition c, string key, CharacterRevision? look = null) => CharacterLooks.Resolve(c, look).Colors[key];

    private static Fabric? FabricOf(CharacterDefinition c, string key, CharacterRevision? look = null) => CharacterLooks.Resolve(c, look).FabricOf(key);

    private static PatternFill Dye(PatternKind kind, params ColorValue[] colors) => new(kind, colors);

    // ---------------------------------------------------------------- wearing a hairdo

    [Fact]
    public void Replacing_takes_the_old_hairdo_off_but_keeps_streaks_and_colours()
    {
        var (legacy, oldFringe, streak) = (Asset(StickerSlots.Hair, "Bob"), Fringe("Old"), Streak());
        var (top, fringe) = (Top(), Fringe());
        var character = WithHair(Wearing(legacy, oldFringe, streak), Blue);
        character = LookEditing.SetColor(character, StickerSlots.HairFringe, Purple);

        var dressed = HairEditing.WearHairdo(character, [(top, null), (fringe, null)], replace: true);

        Assert.Empty(Worn(dressed, StickerSlots.Hair));
        Assert.Equal([top.Id], Worn(dressed, StickerSlots.HairTop));
        Assert.Equal([fringe.Id], Worn(dressed, StickerSlots.HairFringe));
        Assert.Equal([streak.Id], Worn(dressed, StickerSlots.HairStreaks)); // streaks stay on
        Assert.NotNull(dressed.Wardrobe.Find(legacy.Id)); // taken off, not thrown away
        Assert.Equal(Blue, dressed.ColorSlots[StickerSlots.Hair]);
        Assert.Equal(Purple, dressed.ColorSlots[StickerSlots.HairFringe]); // the fringe keeps its own colour through a new hairstyle
    }

    [Fact]
    public void Adding_puts_pieces_on_over_what_is_worn_and_skips_the_ones_already_on()
    {
        var (top, fringe, otherFringe) = (Top(), Fringe(), Fringe("Other"));
        var character = Wearing(top, fringe);

        var added = HairEditing.WearHairdo(character, [(top, null), (otherFringe, null)], replace: false);

        Assert.Equal([top.Id], Worn(added, StickerSlots.HairTop)); // not stacked on itself
        Assert.Equal([fringe.Id, otherFringe.Id], Worn(added, StickerSlots.HairFringe)); // another design goes over
        Assert.Same(character, HairEditing.WearHairdo(character, [(top, null), (fringe, null)], replace: false)); // nothing new to put on
    }

    [Fact]
    public void A_piece_is_worn_in_its_style_even_when_it_was_on_already()
    {
        var top = Asset(StickerSlots.HairTop, "Smooth", null, "default", "left", "right");
        var character = Wearing(top);

        var styled = HairEditing.WearHairdo(character, [(top, "left")], replace: false);
        var replaced = HairEditing.WearHairdo(styled, [(top, "right")], replace: true);

        Assert.Equal("left", styled.StickerVariants![top.Id]);
        Assert.Equal("right", replaced.StickerVariants![top.Id]);
    }

    [Fact]
    public void A_piece_needs_a_top_only_on_a_head_with_nothing_on_top()
    {
        var bald = CharacterDefinition.Create("A");

        Assert.True(HairEditing.NeedsTop(bald, StickerSlots.HairFringe));
        Assert.True(HairEditing.NeedsTop(bald, StickerSlots.HairSides));
        Assert.True(HairEditing.NeedsTop(bald, StickerSlots.HairBack));
        Assert.True(HairEditing.NeedsTop(bald, StickerSlots.HairExtras));
        Assert.False(HairEditing.NeedsTop(bald, StickerSlots.HairTop)); // it is the top
        Assert.False(HairEditing.NeedsTop(bald, StickerSlots.Hair)); // a whole hairstyle brings its own
        Assert.False(HairEditing.NeedsTop(bald, StickerSlots.Top)); // a T-shirt has nothing to do with it
        Assert.False(HairEditing.NeedsTop(Wearing(Top()), StickerSlots.HairFringe));
        Assert.False(HairEditing.NeedsTop(Wearing(Asset(StickerSlots.Hair, "Bob")), StickerSlots.HairFringe));
    }

    // ---------------------------------------------------------------- a piece's colour key

    [Fact]
    public void A_worn_sticker_is_coloured_under_its_pieces_key_its_streaks_key_or_hair()
    {
        var (fringe, streak, legacy, tee) = (Fringe(), Streak(), Asset(StickerSlots.Hair, "Bob"), Asset(StickerSlots.Top, "Tee"));
        var notWorn = Sides();
        var character = Wearing(fringe, streak, legacy, tee);
        character = character with { Wardrobe = character.Wardrobe.With(notWorn) };

        Assert.Equal(StickerSlots.HairFringe, HairEditing.ColorKeyOf(character, fringe.Id));
        Assert.Equal(StickerSlots.StreakColorKey(streak.Id), HairEditing.ColorKeyOf(character, streak.Id));
        Assert.Equal(StickerSlots.Hair, HairEditing.ColorKeyOf(character, legacy.Id));
        Assert.Equal(StickerSlots.StickerColorKey(StickerSlots.Top, tee.Id), HairEditing.ColorKeyOf(character, tee.Id)); // a T-shirt has its own key too (#127)
        Assert.Null(HairEditing.ColorKeyOf(character, notWorn.Id)); // in the wardrobe, not worn
        Assert.Null(HairEditing.ColorKeyOf(character, StickerId.New()));
    }

    [Fact]
    public void A_piece_has_its_own_colour_once_it_has_a_colour_or_a_dye()
    {
        var character = Wearing(Top(), Fringe());

        Assert.False(HairEditing.HasOwnColor(character, StickerSlots.HairFringe));
        Assert.True(HairEditing.HasOwnColor(LookEditing.SetColor(character, StickerSlots.HairFringe, Purple), StickerSlots.HairFringe));
        Assert.True(HairEditing.HasOwnColor(LookEditing.SetFabric(character, StickerSlots.HairFringe, new Fabric(Dye(PatternKind.Tips, Purple))), StickerSlots.HairFringe));
        Assert.False(HairEditing.HasOwnColor(LookEditing.SetColor(character, StickerSlots.HairFringe, Purple), StickerSlots.HairTop));
    }

    // ---------------------------------------------------------------- Same as hair

    [Fact]
    public void Same_as_hair_gives_a_piece_back_to_the_hair_colour_and_dye()
    {
        var character = WithHair(Wearing(Top(), Fringe()), Red);
        character = LookEditing.SetColor(character, StickerSlots.HairFringe, Purple);
        character = LookEditing.SetFabric(character, StickerSlots.HairFringe, new Fabric(Dye(PatternKind.Tips, Blue)));
        character = LookEditing.SetColor(character, StickerSlots.HairTop, Green);
        Assert.Equal(Purple, ColorOf(character, StickerSlots.HairFringe));

        var followed = HairEditing.FollowHair(character, StickerSlots.HairFringe);

        Assert.False(HairEditing.HasOwnColor(followed, StickerSlots.HairFringe));
        Assert.Equal(Red, ColorOf(followed, StickerSlots.HairFringe));
        Assert.Null(FabricOf(followed, StickerSlots.HairFringe));
        Assert.Null(followed.Fabrics); // nothing left: absent, so the file stays as it was
        Assert.Equal(Green, followed.ColorSlots[StickerSlots.HairTop]); // the other pieces are left alone
    }

    [Fact]
    public void Same_as_hair_in_a_named_look_copies_the_looks_hair_where_the_default_gives_the_piece_its_own()
    {
        var character = WithHair(Wearing(Top(), Fringe()), Red);
        character = LookEditing.SetColor(character, StickerSlots.HairFringe, Purple);
        character = LookEditing.SetFabric(character, StickerSlots.HairFringe, new Fabric(Dye(PatternKind.Tips, Blue)));
        var (withLook, winter) = LookEditing.NewLook(character, "Winter");
        // In the look the hair is green and rainbow-dyed.
        var edited = LookEditing.Project(withLook, withLook.Revisions[winter]);
        edited = LookEditing.SetFabric(LookEditing.SetColor(edited, StickerSlots.Hair, Green), StickerSlots.Hair, new Fabric(new PatternFill(PatternKind.Rainbow, PatternFill.RainbowColors)));
        withLook = LookEditing.StoreLook(withLook, winter, edited);

        var projected = LookEditing.Project(withLook, withLook.Revisions[winter]);
        var followed = HairEditing.FollowHair(projected, StickerSlots.HairFringe, baseline: withLook);
        var stored = LookEditing.StoreLook(withLook, winter, followed);
        var look = stored.Revisions[winter];

        // The look can only override, so the piece gets a copy of the look's hair - not "no colour", which would show the default's purple.
        Assert.Equal(Green, ColorOf(stored, StickerSlots.HairFringe, look));
        Assert.Equal(PatternKind.Rainbow, FabricOf(stored, StickerSlots.HairFringe, look)!.Pattern!.Kind);
        Assert.Equal(Purple, ColorOf(stored, StickerSlots.HairFringe)); // the default look is untouched
        // ...and it no longer follows: the look's hair changing later leaves the copy behind.
        var later = LookEditing.StoreLook(stored, winter, LookEditing.SetColor(LookEditing.Project(stored, look), StickerSlots.Hair, Blue));
        Assert.Equal(Blue, ColorOf(later, StickerSlots.Hair, later.Revisions[winter]));
        Assert.Equal(Green, ColorOf(later, StickerSlots.HairFringe, later.Revisions[winter]));
    }

    [Fact]
    public void Same_as_hair_in_a_named_look_just_follows_when_the_look_alone_gave_the_piece_its_colour()
    {
        var character = WithHair(Wearing(Top(), Fringe()), Red);
        var (withLook, winter) = LookEditing.NewLook(character, "Winter");
        var own = LookEditing.SetColor(LookEditing.Project(withLook, withLook.Revisions[winter]), StickerSlots.HairFringe, Purple);
        withLook = LookEditing.StoreLook(withLook, winter, own);
        Assert.Equal(Purple, ColorOf(withLook, StickerSlots.HairFringe, withLook.Revisions[winter]));

        var followed = HairEditing.FollowHair(LookEditing.Project(withLook, withLook.Revisions[winter]), StickerSlots.HairFringe, baseline: withLook);
        var stored = LookEditing.StoreLook(withLook, winter, followed);

        Assert.Equal(Red, ColorOf(stored, StickerSlots.HairFringe, stored.Revisions[winter]));
        Assert.DoesNotContain(StickerSlots.HairFringe, stored.Revisions[winter].ColorSlotValues.Keys); // nothing copied: it follows again
    }

    // ---------------------------------------------------------------- the Colours group

    [Fact]
    public void The_Colours_group_lists_a_piece_right_after_Hair_once_it_has_its_own_colour()
    {
        var (top, fringe, back) = (Top(), Fringe(), Back());
        var character = Wearing(top, fringe, back);
        IReadOnlyList<string> inUse = ["skin", "hair", "top", "eyes"];

        Assert.Equal(inUse, HairEditing.ColorGroupSlots(character, inUse)); // every piece follows the hair: one Hair swatch, as ever

        character = LookEditing.SetColor(character, StickerSlots.HairBack, Purple);
        character = LookEditing.SetFabric(character, StickerSlots.HairTop, new Fabric(Dye(PatternKind.Ombre, Blue))); // a dye alone counts
        Assert.Equal(["skin", "hair", "hairTop", "hairBack", "top", "eyes"], HairEditing.ColorGroupSlots(character, inUse));
    }

    [Fact]
    public void The_Colours_group_leaves_out_pieces_that_are_not_worn_and_streaks_and_lists_a_piece_once()
    {
        var character = Wearing(Top());
        character = LookEditing.SetColor(character, StickerSlots.HairFringe, Purple); // a colour kept for a fringe that is not worn
        character = LookEditing.SetColor(character, StickerSlots.HairTop, Blue);

        var slots = HairEditing.ColorGroupSlots(character, ["skin", "hair", "hairTop", "streak"]);

        Assert.Equal(["skin", "hair", "hairTop"], slots); // no fringe (not worn), no streak swatch, the top once
        Assert.Equal(["skin", "hairTop"], HairEditing.ColorGroupSlots(character, ["skin"])); // no Hair in use: pieces go last
    }

    // ---------------------------------------------------------------- over glasses

    [Fact]
    public void Over_glasses_raises_a_sticker_over_the_glasses_and_back_again()
    {
        var (fringe, glasses) = (Fringe(), Asset(StickerSlots.Glasses, "Round"));
        var character = Wearing(fringe, glasses);
        static IEnumerable<string> Order(CharacterDefinition c) => CharacterLooks.Resolve(c).Stickers.Select(w => w.Asset.Sticker.Name);
        Assert.Equal(["Fringe", "Round"], Order(character)); // hair under glasses

        var over = HairEditing.SetOverGlasses(character, fringe.Id, true);
        Assert.True(HairEditing.IsOverGlasses(over, fringe.Id));
        Assert.Equal(["Round", "Fringe"], Order(over));

        var back = HairEditing.SetOverGlasses(over, fringe.Id, false);
        Assert.False(HairEditing.IsOverGlasses(back, fringe.Id));
        Assert.Null(back.Wardrobe.Find(fringe.Id)!.Sticker.OverGlasses); // absent, not false
        Assert.Equal(["Fringe", "Round"], Order(back));
    }

    [Fact]
    public void Over_glasses_changes_nothing_when_it_already_is_so_or_the_sticker_is_unknown()
    {
        var fringe = Fringe();
        var character = Wearing(fringe);

        Assert.Same(character, HairEditing.SetOverGlasses(character, fringe.Id, false));
        Assert.Same(character, HairEditing.SetOverGlasses(character, StickerId.New(), true));
        var over = HairEditing.SetOverGlasses(character, fringe.Id, true);
        Assert.Same(over, HairEditing.SetOverGlasses(over, fringe.Id, true));
    }

    [Fact]
    public void Flipping_a_library_copy_over_glasses_makes_it_the_characters_own()
    {
        var fringe = Asset(StickerSlots.HairFringe, "Blunt", "library:hairFringe/blunt");
        var character = Wearing(fringe);
        Assert.True(character.Wardrobe.Find(fringe.Id)!.Sticker.IsFromLibrary);

        var over = HairEditing.SetOverGlasses(character, fringe.Id, true);

        Assert.False(over.Wardrobe.Find(fringe.Id)!.Sticker.IsFromLibrary); // tidying never takes it away
    }

    // ---------------------------------------------------------------- streaks

    [Fact]
    public void A_new_streak_takes_the_colour_of_the_last_one_put_on()
    {
        var (first, second, third) = (Streak(), Streak(), Streak());
        Assert.Null(HairEditing.LastStreakColor(CharacterDefinition.Create("A")));

        var character = HairEditing.WearStreak(CharacterDefinition.Create("A"), first);
        Assert.Null(HairEditing.LastStreakColor(character));
        Assert.DoesNotContain(StickerSlots.StreakColorKey(first.Id), character.ColorSlots.Keys); // nothing to copy: it follows the streaks' default

        character = LookEditing.SetColor(character, StickerSlots.StreakColorKey(first.Id), Purple);
        character = HairEditing.WearStreak(character, second);
        Assert.Equal(Purple, character.ColorSlots[StickerSlots.StreakColorKey(second.Id)]);

        character = LookEditing.SetColor(character, StickerSlots.StreakColorKey(second.Id), Blue);
        character = HairEditing.WearStreak(character, third);
        Assert.Equal(Blue, character.ColorSlots[StickerSlots.StreakColorKey(third.Id)]); // the top of the stack, not the first
        Assert.Equal(Purple, character.ColorSlots[StickerSlots.StreakColorKey(first.Id)]); // each streak keeps its own
        Assert.Equal([first.Id, second.Id, third.Id], Worn(character, StickerSlots.HairStreaks));
    }

    [Fact]
    public void Extra_streaks_go_across_the_fringe_alternately_right_and_left()
    {
        Assert.Equal(default(Point2D), HairEditing.StreakSpot(0));
        Assert.Equal(default(Point2D), HairEditing.StreakSpot(-3));
        Assert.Equal(new Point2D(HairEditing.StreakSpacing, 0), HairEditing.StreakSpot(1));
        Assert.Equal(new Point2D(-HairEditing.StreakSpacing, 0), HairEditing.StreakSpot(2));
        Assert.Equal(new Point2D(2 * HairEditing.StreakSpacing, 0), HairEditing.StreakSpot(3));
        Assert.Equal(new Point2D(-2 * HairEditing.StreakSpacing, 0), HairEditing.StreakSpot(4));
        Assert.Equal(8, Enumerable.Range(0, 8).Select(HairEditing.StreakSpot).Distinct().Count()); // no two on the same spot
    }

    [Fact]
    public void A_removed_streak_leaves_no_colour_behind_in_the_character_or_its_looks()
    {
        var (kept, gone) = (Streak("Kept"), Streak("Gone"));
        var (keptKey, goneKey) = (StickerSlots.StreakColorKey(kept.Id), StickerSlots.StreakColorKey(gone.Id));
        var character = Wearing(kept, gone);
        character = LookEditing.SetColor(LookEditing.SetColor(character, keptKey, Purple), goneKey, Blue);
        character = LookEditing.SetFabric(character, goneKey, new Fabric(Dye(PatternKind.Streaks, Green)));
        character = LookEditing.SetColor(character, StickerSlots.StreakColor, Red); // the streaks' default isn't any one streak's
        var (withLook, winter) = LookEditing.NewLook(character, "Winter");
        var inLook = LookEditing.Project(withLook, withLook.Revisions[winter]);
        inLook = LookEditing.SetFabric(LookEditing.SetColor(inLook, goneKey, Green), goneKey, new Fabric(Dye(PatternKind.Tips, Red)));
        inLook = LookEditing.SetColor(inLook, keptKey, Blue);
        withLook = LookEditing.StoreLook(withLook, winter, inLook);
        Assert.Contains(goneKey, withLook.Revisions[winter].ColorSlotValues.Keys);

        var removed = LookEditing.RemoveFromWardrobe(withLook, gone.Id);
        var tidied = HairEditing.DropOrphanStreakColors(removed);

        Assert.DoesNotContain(goneKey, tidied.ColorSlots.Keys);
        Assert.Null(tidied.Fabrics); // the only fabric was the removed streak's
        Assert.Equal(Purple, tidied.ColorSlots[keptKey]);
        Assert.Equal(Red, tidied.ColorSlots[StickerSlots.StreakColor]);
        var look = tidied.Revisions[winter];
        Assert.DoesNotContain(goneKey, look.ColorSlotValues.Keys);
        Assert.Null(look.FabricValues);
        Assert.Equal(Blue, look.ColorSlotValues[keptKey]);
        Assert.Same(tidied, HairEditing.DropOrphanStreakColors(tidied)); // nothing more to drop
    }

    [Fact]
    public void Nothing_is_dropped_while_the_streaks_are_still_in_the_wardrobe_even_taken_off()
    {
        var streak = Streak();
        var character = LookEditing.SetColor(Wearing(streak), StickerSlots.StreakColorKey(streak.Id), Purple);
        var takenOff = LookEditing.TakeOff(character, streak.Id);

        Assert.Same(takenOff, HairEditing.DropOrphanStreakColors(takenOff));
    }

    // ---------------------------------------------------------------- schemes

    private static CharacterDefinition Headed() => WithHair(Wearing(Top(), Fringe(), Sides(), Back()), Red);

    private static IEnumerable<string> OwnPieces(CharacterDefinition c) => StickerSlots.HairPieces.Where(p => HairEditing.HasOwnColor(c, p));

    [Fact]
    public void Two_tone_puts_the_top_and_the_fringe_in_the_accent()
    {
        var scheme = HairEditing.ApplyScheme(Headed(), HairScheme.TwoTone, Purple);

        Assert.Equal([StickerSlots.HairTop, StickerSlots.HairFringe], OwnPieces(scheme));
        Assert.Equal(Purple, ColorOf(scheme, StickerSlots.HairTop));
        Assert.Equal(Purple, ColorOf(scheme, StickerSlots.HairFringe));
        Assert.Equal(Red, ColorOf(scheme, StickerSlots.HairBack));
        Assert.Equal(Red, ColorOf(scheme, StickerSlots.Hair));
    }

    [Fact]
    public void Peekaboo_puts_the_back_in_the_accent_and_fringe_only_just_the_fringe()
    {
        var peekaboo = HairEditing.ApplyScheme(Headed(), HairScheme.Peekaboo, Purple);
        var fringeOnly = HairEditing.ApplyScheme(Headed(), HairScheme.FringeOnly, Purple);

        Assert.Equal([StickerSlots.HairBack], OwnPieces(peekaboo));
        Assert.Equal(Purple, ColorOf(peekaboo, StickerSlots.HairBack));
        Assert.Equal([StickerSlots.HairFringe], OwnPieces(fringeOnly));
        Assert.Equal(Purple, ColorOf(fringeOnly, StickerSlots.HairFringe));
    }

    [Fact]
    public void Dip_dye_and_ombre_dye_all_the_hair_and_every_piece_follows_the_dye()
    {
        var dip = HairEditing.ApplyScheme(Headed(), HairScheme.DipDye, Purple);
        var ombre = HairEditing.ApplyScheme(Headed(), HairScheme.Ombre, Purple);

        var tips = FabricOf(dip, StickerSlots.Hair)!.Pattern!;
        Assert.Equal(PatternKind.Tips, tips.Kind);
        Assert.Equal([Purple], tips.Colors);
        Assert.Equal(HairEditing.DipDyeReach, tips.Weight);
        Assert.Equal(PatternKind.Ombre, FabricOf(ombre, StickerSlots.Hair)!.Pattern!.Kind);
        Assert.Equal([Purple], FabricOf(ombre, StickerSlots.Hair)!.Pattern!.Colors);
        Assert.Empty(OwnPieces(dip)); // a dye on the hair, so no piece has its own
        Assert.Equal(PatternKind.Tips, FabricOf(dip, StickerSlots.HairFringe)!.Pattern!.Kind); // and they all carry it
        Assert.Equal(Red, ColorOf(dip, StickerSlots.Hair)); // the hair colour is the ground
    }

    [Fact]
    public void Rainbow_dyes_the_hair_in_its_own_six_colours_whatever_the_accent()
    {
        var rainbow = HairEditing.ApplyScheme(Headed(), HairScheme.Rainbow, Purple);

        var pattern = FabricOf(rainbow, StickerSlots.Hair)!.Pattern!;
        Assert.Equal(PatternKind.Rainbow, pattern.Kind);
        Assert.Equal(PatternFill.RainbowColors, pattern.Colors);
        Assert.Equal(6, pattern.Colors.Count);
    }

    [Fact]
    public void Natural_puts_every_piece_back_on_the_hair_and_takes_the_dye_off_but_keeps_the_rest()
    {
        var messy = HairEditing.ApplyScheme(Headed(), HairScheme.TwoTone, Purple);
        messy = LookEditing.SetFabric(messy, StickerSlots.HairBack, new Fabric(Dye(PatternKind.Roots, Blue)));
        messy = LookEditing.SetFabric(messy, StickerSlots.Hair, new Fabric(new PatternFill(PatternKind.Rainbow, PatternFill.RainbowColors), new TextureFill(TextureKind.Felt)));

        var natural = HairEditing.ApplyScheme(messy, HairScheme.Natural, Blue);

        Assert.Empty(OwnPieces(natural));
        Assert.DoesNotContain(StickerSlots.HairBack, natural.Fabrics!.Keys); // its own dye is gone; it carries the hair's texture like the others
        Assert.Null(FabricOf(natural, StickerSlots.Hair)!.Pattern); // no dye...
        Assert.Equal(TextureKind.Felt, FabricOf(natural, StickerSlots.Hair)!.Texture!.Kind); // ...but the texture is the user's
        Assert.Equal(Red, ColorOf(natural, StickerSlots.Hair));
        Assert.Equal(Red, ColorOf(natural, StickerSlots.HairFringe));
    }

    [Fact]
    public void Natural_leaves_a_clothing_pattern_on_the_hair_alone()
    {
        var striped = LookEditing.SetFabric(Headed(), StickerSlots.Hair, new Fabric(new PatternFill(PatternKind.Stripes, [Blue])));

        var natural = HairEditing.ApplyScheme(striped, HairScheme.Natural, Purple);

        Assert.Equal(PatternKind.Stripes, FabricOf(natural, StickerSlots.Hair)!.Pattern!.Kind);
    }

    [Fact]
    public void Picking_one_scheme_after_another_never_piles_up()
    {
        var character = Headed();

        character = HairEditing.ApplyScheme(character, HairScheme.TwoTone, Purple);
        character = HairEditing.ApplyScheme(character, HairScheme.Peekaboo, Blue);
        Assert.Equal([StickerSlots.HairBack], OwnPieces(character)); // the top and fringe went back to the hair
        Assert.Equal(Blue, ColorOf(character, StickerSlots.HairBack));
        Assert.Equal(Red, ColorOf(character, StickerSlots.HairTop));

        character = HairEditing.ApplyScheme(character, HairScheme.Rainbow, Purple);
        Assert.Empty(OwnPieces(character));
        Assert.Equal(PatternKind.Rainbow, FabricOf(character, StickerSlots.Hair)!.Pattern!.Kind);

        character = HairEditing.ApplyScheme(character, HairScheme.Ombre, Purple);
        Assert.Equal(PatternKind.Ombre, FabricOf(character, StickerSlots.Hair)!.Pattern!.Kind); // replaced, not laid over

        character = HairEditing.ApplyScheme(character, HairScheme.FringeOnly, Green);
        Assert.Null(FabricOf(character, StickerSlots.Hair)); // the ombre went with it
        Assert.Equal([StickerSlots.HairFringe], OwnPieces(character));

        character = HairEditing.ApplyScheme(character, HairScheme.Natural, Purple);
        Assert.Empty(OwnPieces(character));
        Assert.Null(FabricOf(character, StickerSlots.Hair));
        Assert.Equal(Red, ColorOf(character, StickerSlots.HairFringe));
    }

    [Fact]
    public void A_scheme_in_a_named_look_can_only_copy_the_hair_where_the_default_gives_a_piece_its_own_colour()
    {
        var character = LookEditing.SetColor(Headed(), StickerSlots.HairFringe, Purple);
        var (withLook, winter) = LookEditing.NewLook(character, "Winter");
        var inLook = LookEditing.SetColor(LookEditing.Project(withLook, withLook.Revisions[winter]), StickerSlots.Hair, Green);
        withLook = LookEditing.StoreLook(withLook, winter, inLook);

        var natural = HairEditing.ApplyScheme(LookEditing.Project(withLook, withLook.Revisions[winter]), HairScheme.Natural, Purple, baseline: withLook);
        var stored = LookEditing.StoreLook(withLook, winter, natural);

        Assert.Equal(Green, ColorOf(stored, StickerSlots.HairFringe, stored.Revisions[winter])); // not the default's purple
        Assert.Equal(Purple, ColorOf(stored, StickerSlots.HairFringe)); // the default look keeps its own
    }

    // ---------------------------------------------------------------- old whole hairstyles

    [Fact]
    public void Replacing_an_old_hairstyle_swaps_it_for_its_pieces_and_keeps_the_colours()
    {
        var legacy = Asset(StickerSlots.Hair, "Bob", "library:hair/bob");
        var (top, fringe) = (Asset(StickerSlots.HairTop, "Smooth", null, "default", "middle"), Fringe());
        var character = WithHair(Wearing(legacy), Blue);

        var swapped = HairEditing.ReplaceLegacy(character, legacy.Id, [(top, "middle"), (fringe, null)]);

        Assert.Empty(Worn(swapped, StickerSlots.Hair));
        Assert.Equal([top.Id], Worn(swapped, StickerSlots.HairTop));
        Assert.Equal([fringe.Id], Worn(swapped, StickerSlots.HairFringe));
        Assert.NotNull(swapped.Wardrobe.Find(top.Id));
        Assert.NotNull(swapped.Wardrobe.Find(legacy.Id)); // stays until nothing wears it
        Assert.Equal("middle", swapped.StickerVariants![top.Id]);
        Assert.Equal(Blue, swapped.ColorSlots[StickerSlots.Hair]);
    }

    [Fact]
    public void Replacing_an_old_hairstyle_that_is_not_worn_by_the_default_look_changes_only_the_looks_that_wear_it()
    {
        var legacy = Asset(StickerSlots.Hair, "Bob", "library:hair/bob");
        var (oldFringe, top, fringe) = (Fringe("Old"), Top(), Fringe());
        // The default look wears an old fringe; "Winter" wears the old hairstyle; "Summer" wears nothing different.
        var character = Wearing(oldFringe);
        var (a, winter) = LookEditing.NewLook(character, "Winter");
        var (b, summer) = LookEditing.NewLook(a, "Summer");
        b = LookEditing.StoreLook(b, winter, LookEditing.Wear(LookEditing.Project(b, b.Revisions[winter]), legacy));
        Assert.Equal([legacy.Id], b.Revisions[winter].ActiveStickers[StickerSlots.Hair]);

        var swapped = HairEditing.ReplaceLegacy(b, legacy.Id, [(top, null), (fringe, null)]);

        Assert.Equal(b.Stickers, swapped.Stickers); // the default look never wore it
        var look = swapped.Revisions[winter];
        Assert.Empty(look.ActiveStickers[StickerSlots.Hair]);
        Assert.Equal([top.Id], look.ActiveStickers[StickerSlots.HairTop]);
        Assert.Equal([oldFringe.Id, fringe.Id], look.ActiveStickers[StickerSlots.HairFringe]); // on top of what the look wears there
        Assert.Same(b.Revisions[summer], swapped.Revisions[summer]);
        Assert.NotNull(swapped.Wardrobe.Find(top.Id));
    }
}
