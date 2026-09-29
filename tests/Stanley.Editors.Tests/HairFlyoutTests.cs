using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.StickerLibrary;

namespace Stanley.Editors.Tests;

/// <summary>
/// The Hair button and its flyout (docs: modular hair): hairstyle presets and Bald, the piece tabs,
/// the automatic top, streaks, the "Replaced your hair" bar and the old-hairstyle upgrade bar.
/// Logic that only needs a hairstyle's recipe runs on synthetic pieces (unmodified "library"
/// copies in the wardrobe, which is what <see cref="Hairstyles.PiecesFor"/> picks first);
/// what needs the real art in the library is skipped until the pieces are drawn.
/// </summary>
public sealed class HairFlyoutTests
{
    private static readonly ColorValue Red = ColorValue.FromHex("#c0392b");
    private static readonly ColorValue Purple = ColorValue.FromHex("#8e44ad");

    private static (EditorSession Session, CharacterEditorViewModel Editor) NewCharacter()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var created = session.Characters.CreateCharacter();
        return (session, session.Characters.Items.Single(i => i.Id == created.Id).Editor);
    }

    /// <summary>A placeable sticker for <paramref name="slot"/>; with a library <paramref name="key"/> it is an unmodified copy of that library piece.</summary>
    private static StickerAsset Piece(string slot, string name, string? key = null) =>
        new(new Sticker(StickerId.New(), name, slot, [new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp))],
            new SortedDictionary<string, ColorValue>(), ["default"], Source: key is null ? null : Stanley.StickerLibrary.StickerLibrary.SourcePrefix + key),
            new Dictionary<string, ArtFile>());

    private static void Keep(CharacterEditorViewModel editor, params StickerAsset[] assets) =>
        editor.Apply(EditResult<CharacterDefinition>.Success(editor.Committed with { Wardrobe = assets.Aggregate(editor.Committed.Wardrobe, (w, a) => w.With(a)) }));

    private static void WearOn(CharacterEditorViewModel editor, params StickerAsset[] assets) =>
        editor.Apply(EditResult<CharacterDefinition>.Success(assets.Aggregate(editor.Committed, LookEditing.Wear)));

    /// <summary>Bob's recipe (see <see cref="Hairstyles.All"/>) as unmodified copies, so a click finds them in the wardrobe instead of the library.</summary>
    private static IReadOnlyList<StickerAsset> BobPieces() =>
    [
        Piece(StickerSlots.HairTop, "Smooth", "hairTop/smooth"),
        Piece(StickerSlots.HairFringe, "Blunt", "hairFringe/blunt"),
        Piece(StickerSlots.HairSides, "Chin", "hairSides/chin"),
        Piece(StickerSlots.HairBack, "Bob back", "hairBack/bob"),
    ];

    private static HairstyleChoice BobChoice(CharacterEditorViewModel editor) =>
        new("Bob", Hairstyles.Get("Bob"), null, editor.Working, false);

    private static void SetColor(CharacterEditorViewModel editor, string slot, ColorValue color) =>
        editor.SetColorCommand.Execute(new ColorSwatchChoice(slot, "test", color));

    private static IReadOnlyList<StickerId> Hairdo(CharacterEditorViewModel editor) => HairEditing.WornHairdo(editor.Working);

    private static void RequireBob() =>
        Assert.SkipUnless(Hairstyles.Get("Bob") is { } bob && Hairstyles.IsAvailable(bob), "the hair pieces aren't in the library yet");

    // ---------------------------------------------------------------- the button and its tabs

    [Fact]
    public void The_button_says_none_a_lone_whole_hairstyle_by_name_and_an_own_mix()
    {
        var (_, editor) = NewCharacter();
        Assert.Equal("None", editor.HairCurrent);
        var raised = new List<string?>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        WearOn(editor, Piece(StickerSlots.Hair, "Curls"));
        Assert.Equal("Curls", editor.HairCurrent);
        Assert.Contains(nameof(CharacterEditorViewModel.HairCurrent), raised);

        WearOn(editor, Piece(StickerSlots.HairFringe, "Blunt"));
        Assert.Equal("Own mix", editor.HairCurrent);
        Assert.Equal("Hair: Own mix", editor.HairTip);
    }

    [Fact]
    public void The_button_names_the_hairstyle_worn_exactly()
    {
        RequireBob();
        var (_, editor) = NewCharacter();
        editor.WearHairstyleCommand.Execute(editor.HairstylesGallery().Choices.Single(c => c.Label == "Bob"));

        Assert.Equal("Bob", editor.HairCurrent);
        Assert.True(editor.HairstylesGallery().Choices.Single(c => c.Label == "Bob").IsWorn);

        // One more piece and it's no longer exactly Bob.
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.HairExtras).Choices.First(c => !c.IsNone));
        Assert.Equal("Own mix", editor.HairCurrent);
    }

    [Fact]
    public void The_flyout_has_seven_tabs_opens_on_Hairstyles_and_remembers_the_last_one()
    {
        var (_, editor) = NewCharacter();

        Assert.Equal(["Hairstyles", "Top", "Fringe", "Sides", "Back", "Extras", "Streaks"], editor.HairTabs.Select(t => t.Label));
        Assert.Equal("Hairstyles", Assert.Single(editor.HairTabs, t => t.IsCurrent).Label);
        Assert.IsType<HairstyleGallery>(editor.HairTabContent);

        editor.ShowHairTabCommand.Execute(editor.HairTabs.Single(t => t.Label == "Fringe"));

        Assert.Equal(StickerSlots.HairFringe, editor.HairTabKey);
        Assert.Equal("Fringe", Assert.Single(editor.HairTabs, t => t.IsCurrent).Label);
        var gallery = Assert.IsType<SlotGallery>(editor.HairTabContent);
        Assert.Equal(StickerSlots.HairFringe, gallery.Info.Name);
        Assert.True(gallery.Choices[0].IsNone);
        Assert.NotNull(gallery.Draw);
        Assert.NotNull(gallery.Import);

        // Dressing the character, or looking at another look, leaves it where it was.
        WearOn(editor, Piece(StickerSlots.HairTop, "Smooth"));
        Assert.Equal(StickerSlots.HairFringe, editor.HairTabKey);
        editor.HairTabKey = "nonsense";
        Assert.Equal(StickerSlots.HairFringe, editor.HairTabKey);

        editor.HairTabKey = StickerSlots.HairStreaks;
        Assert.Equal(StickerSlots.HairStreaks, Assert.IsType<SlotGallery>(editor.HairTabContent).Info.Name);
        editor.HairTabKey = CharacterEditorViewModel.HairstylesTabKey;
        Assert.IsType<HairstyleGallery>(editor.HairTabContent);
    }

    [Fact]
    public void The_Hairstyles_tab_offers_Bald_the_presets_then_the_characters_own_whole_hairstyles_as_close_ups()
    {
        var (_, editor) = NewCharacter();
        var mine = Piece(StickerSlots.Hair, "My curls");
        var old = Piece(StickerSlots.Hair, "Bob", "hair/bob");
        var fringe = Piece(StickerSlots.HairFringe, "Blunt"); // a piece isn't a whole hairstyle
        Keep(editor, mine, old, fringe);

        var gallery = editor.HairstylesGallery();

        var presets = Hairstyles.Available.Select(h => h.Name).ToList();
        var oldLabel = Hairstyles.ForLegacy(old.Sticker) is null ? "Bob" : "Bob (old)"; // told apart from the preset once there is one
        Assert.Equal(["Bald", .. presets, oldLabel, "My curls"], gallery.Choices.Select(c => c.Label));
        Assert.True(gallery.Choices[0].IsBald);
        Assert.True(gallery.Choices[0].IsWorn); // nothing on
        Assert.All(gallery.Choices, c => Assert.True(c.Closeup));
        // Previewed wearing it, in place of whatever is there now.
        Assert.Equal([mine.Id], HairEditing.WornHairdo(gallery.Choices[^1].Preview));
        Assert.Empty(HairEditing.WornHairdo(gallery.Choices[0].Preview));
        // The old library hairstyles are not offered from the library - only what the character already has.
        Assert.DoesNotContain(gallery.Choices, c => c.Label is "Short" or "Ponytail" or "Bun" && c.Style is null);
        Assert.NotNull(gallery.Draw);
        Assert.NotNull(gallery.Import);
    }

    // ---------------------------------------------------------------- putting a hairstyle on

    [Fact]
    public void A_hairstyle_click_replaces_a_whole_hairstyle_in_one_undo_step_and_leaves_the_colours_alone()
    {
        var (session, editor) = NewCharacter();
        var bob = BobPieces();
        var whole = Piece(StickerSlots.Hair, "Bob", "hair/bob"); // an old library hairstyle: replacing it loses nothing
        Keep(editor, [.. bob]);
        WearOn(editor, whole);
        SetColor(editor, "hair", Red);
        var before = Hairdo(editor);

        editor.WearHairstyleCommand.Execute(BobChoice(editor));

        Assert.Equal(bob.Select(p => p.Id).Order(), Hairdo(editor).Order());
        Assert.DoesNotContain(StickerSlots.Hair, editor.Working.Stickers.Where(s => s.Value.Count > 0).Select(s => s.Key));
        Assert.Equal(Red, editor.Working.ColorSlots["hair"]);
        Assert.Equal(bob.Select(p => p.Id), bob.Select(p => editor.Working.Stickers[p.Sticker.Slot].Single())); // one of each: no copies piled up

        session.Workspace.History.Undo();
        Assert.Equal(before, Hairdo(editor));
        Assert.Equal([whole.Id], editor.Working.Stickers[StickerSlots.Hair]);
    }

    [Fact]
    public void A_hairstyle_click_with_nothing_to_lose_shows_no_bar()
    {
        var (_, editor) = NewCharacter();
        Keep(editor, [.. BobPieces()]);

        editor.WearHairstyleCommand.Execute(BobChoice(editor));

        Assert.False(editor.HasHairReplaced);
        Assert.Null(editor.HairReplacedText);
        Assert.Equal(4, Hairdo(editor).Count);
    }

    [Fact]
    public void Replacing_the_hairstyle_in_a_named_look_lands_in_the_look()
    {
        var (session, editor) = NewCharacter();
        Keep(editor, [.. BobPieces()]);
        editor.NewLookCommand.Execute(null);
        var winter = editor.CurrentLook!.Value;

        editor.WearHairstyleCommand.Execute(BobChoice(editor));

        Assert.Equal(4, HairEditing.WornHairdo(editor.LookWorking).Count); // the look shown
        Assert.All(StickerSlots.HairPieces, slot => Assert.False(editor.Committed.Stickers.TryGetValue(slot, out var ids) && ids.Count > 0));
        Assert.Equal(4, StickerSlots.HairPieces.Count(slot => editor.Committed.Revisions[winter].ActiveStickers.ContainsKey(slot)));
        session.Workspace.History.Undo();
        Assert.Empty(Hairdo(editor));
    }

    [Fact]
    public void Bald_takes_off_every_hairdo_slot_but_not_the_streaks_in_one_undo_step()
    {
        var (session, editor) = NewCharacter();
        var streak = Piece(StickerSlots.HairStreaks, "Test streak");
        WearOn(editor, Piece(StickerSlots.Hair, "Curls"), Piece(StickerSlots.HairTop, "Smooth"), Piece(StickerSlots.HairFringe, "Blunt"),
            Piece(StickerSlots.HairExtras, "Bun"), streak);
        var before = Hairdo(editor);
        Assert.Equal(4, before.Count);

        var bald = editor.HairstylesGallery().Choices[0];
        Assert.False(bald.IsWorn);
        editor.WearHairstyleCommand.Execute(bald);

        Assert.Empty(Hairdo(editor));
        Assert.Equal("None", editor.HairCurrent);
        Assert.Equal([streak.Id], editor.Working.Stickers[StickerSlots.HairStreaks]);
        Assert.False(editor.HasHairReplaced); // Bald is what you asked for
        Assert.True(editor.HairstylesGallery().Choices[0].IsWorn);

        session.Workspace.History.Undo();
        Assert.Equal(before, Hairdo(editor));
    }

    [Fact]
    public void Clicking_the_whole_hairstyle_that_is_already_worn_changes_nothing()
    {
        var (session, editor) = NewCharacter();
        var mine = Piece(StickerSlots.Hair, "My curls");
        WearOn(editor, mine);
        var worn = editor.HairstylesGallery().Choices.Single(c => c.Own is not null);
        Assert.True(worn.IsWorn);
        var before = editor.Committed;

        editor.WearHairstyleCommand.Execute(worn);

        Assert.Same(before, editor.Committed);
        session.Workspace.History.Undo(); // undoes the wearing above, not a no-op click
        Assert.Empty(Hairdo(editor));
    }

    // ---------------------------------------------------------------- "Replaced your hair with Bob."

    [Fact]
    public void Replacing_an_own_mix_shows_a_bar_and_Add_to_my_mix_keeps_the_mix_plus_the_pieces_as_one_undo_step()
    {
        var (session, editor) = NewCharacter();
        var bob = BobPieces();
        var myTop = Piece(StickerSlots.HairTop, "My top");
        var ribbon = Piece(StickerSlots.HairExtras, "Ribbon");
        Keep(editor, [.. bob]);
        WearOn(editor, myTop, ribbon);
        var mix = Hairdo(editor);
        Assert.Equal([myTop.Id, ribbon.Id], mix);

        editor.WearHairstyleCommand.Execute(BobChoice(editor));

        Assert.True(editor.HasHairReplaced);
        Assert.Equal("Replaced your hair with Bob.", editor.HairReplacedText);
        Assert.Equal(bob.Select(p => p.Id).Order(), Hairdo(editor).Order());
        Assert.True(editor.AddToMixCommand.CanExecute(null));

        editor.AddToMixCommand.Execute(null);

        Assert.False(editor.HasHairReplaced);
        var mixed = Hairdo(editor);
        Assert.All(mix.Concat(bob.Select(p => p.Id)), id => Assert.Contains(id, mixed));
        Assert.Equal(2, editor.Working.Stickers[StickerSlots.HairTop].Count); // both tops, one over the other

        // Undo, then apply the add: one step, straight back to the mix as it was before the click.
        session.Workspace.History.Undo();
        Assert.Equal(mix, Hairdo(editor));
        session.Workspace.History.Redo();
        Assert.Equal(mixed, Hairdo(editor));
    }

    [Fact]
    public void An_own_drawing_in_the_hair_slot_counts_as_an_own_mix_too()
    {
        var (_, editor) = NewCharacter();
        Keep(editor, [.. BobPieces()]);
        WearOn(editor, Piece(StickerSlots.Hair, "My curls"));

        editor.WearHairstyleCommand.Execute(BobChoice(editor));

        Assert.True(editor.HasHairReplaced);
        Assert.False(editor.Working.Stickers.TryGetValue(StickerSlots.Hair, out var whole) && whole.Count > 0);
    }

    [Fact]
    public void The_bar_goes_away_on_any_other_edit_on_undo_on_close_and_when_dismissed()
    {
        var (session, editor) = NewCharacter();
        Keep(editor, [.. BobPieces()]);
        void Replace()
        {
            WearOn(editor, Piece(StickerSlots.HairExtras, "Ribbon"));
            editor.WearHairstyleCommand.Execute(BobChoice(editor));
            Assert.True(editor.HasHairReplaced);
        }
        var raised = new List<string?>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Replace(); // any other edit
        editor.SetSkin(Red);
        Assert.False(editor.HasHairReplaced);
        Assert.Contains(nameof(CharacterEditorViewModel.HasHairReplaced), raised);

        Replace(); // an edit in the middle of a slider drag
        editor.BeginSliderDrag();
        editor.HeightPercent += 5;
        Assert.False(editor.HasHairReplaced);
        editor.EndSliderDrag();

        Replace(); // undo
        session.Workspace.History.Undo();
        Assert.False(editor.HasHairReplaced);

        Replace(); // close: back to the page
        session.Characters.OpenCharacter(editor.CharacterId);
        session.Characters.ReturnToPage();
        Assert.False(editor.HasHairReplaced);

        Replace(); // the bar's own close button
        editor.DismissHairReplacedCommand.Execute(null);
        Assert.False(editor.HasHairReplaced);
        Assert.False(editor.AddToMixCommand.CanExecute(null));
    }

    [Fact]
    public void Looking_at_another_look_takes_the_bar_down_too()
    {
        var (_, editor) = NewCharacter();
        Keep(editor, [.. BobPieces()]);
        editor.NewLookCommand.Execute(null);
        WearOn(editor, Piece(StickerSlots.HairExtras, "Ribbon"));
        editor.WearHairstyleCommand.Execute(BobChoice(editor));
        Assert.True(editor.HasHairReplaced);

        editor.ShowLookCommand.Execute(editor.Looks.Single(l => l.Id is null));

        Assert.False(editor.HasHairReplaced);
    }

    [Fact]
    public void Add_to_my_mix_is_used_up_after_one_press()
    {
        var (session, editor) = NewCharacter();
        Keep(editor, [.. BobPieces()]);
        WearOn(editor, Piece(StickerSlots.HairExtras, "Ribbon"));
        editor.WearHairstyleCommand.Execute(BobChoice(editor));
        editor.AddToMixCommand.Execute(null);
        var after = editor.Committed;

        editor.AddToMixCommand.Execute(null);

        Assert.Same(after, editor.Committed);
        Assert.False(editor.HasHairReplaced);
        session.Workspace.History.Undo();
        Assert.Single(Hairdo(editor)); // only the ribbon: the two steps before are still separate
    }

    // ---------------------------------------------------------------- the automatic top

    [Fact]
    public void A_fringe_sides_back_or_extra_brings_the_default_top_when_nothing_is_on_top()
    {
        var (session, editor) = NewCharacter();
        var top = Piece(StickerSlots.HairTop, "Smooth", Hairstyles.DefaultTop);
        var fringe = Piece(StickerSlots.HairFringe, "Test fringe");
        Keep(editor, top, fringe);

        editor.WearCommand.Execute(editor.Gallery(StickerSlots.HairFringe).Choices.Single(c => c.Label == "Test fringe"));

        Assert.Equal([fringe.Id], editor.Working.Stickers[StickerSlots.HairFringe]);
        Assert.Equal([top.Id], editor.Working.Stickers[StickerSlots.HairTop]);
        Assert.Equal(fringe.Id, editor.SelectedStickerId); // the piece clicked is the one selected

        session.Workspace.History.Undo(); // one undo step for both
        Assert.Empty(Hairdo(editor));
    }

    [Theory]
    [InlineData(StickerSlots.HairSides)]
    [InlineData(StickerSlots.HairBack)]
    [InlineData(StickerSlots.HairExtras)]
    public void Every_piece_but_the_top_brings_one(string slot)
    {
        var (_, editor) = NewCharacter();
        var top = Piece(StickerSlots.HairTop, "Smooth", Hairstyles.DefaultTop);
        var piece = Piece(slot, "Test piece");
        Keep(editor, top, piece);

        editor.WearCommand.Execute(editor.Gallery(slot).Choices.Single(c => c.Label == "Test piece"));

        Assert.Equal([top.Id], editor.Working.Stickers[StickerSlots.HairTop]);
    }

    [Fact]
    public void No_top_comes_when_there_is_one_already_a_whole_hairstyle_or_the_click_is_the_top()
    {
        var (_, editor) = NewCharacter();
        var defaultTop = Piece(StickerSlots.HairTop, "Smooth", Hairstyles.DefaultTop);
        var myTop = Piece(StickerSlots.HairTop, "My top");
        var fringe = Piece(StickerSlots.HairFringe, "Test fringe");
        var sides = Piece(StickerSlots.HairSides, "Test sides");
        Keep(editor, defaultTop, myTop, fringe, sides);

        editor.WearCommand.Execute(editor.Gallery(StickerSlots.HairTop).Choices.Single(c => c.Label == "My top"));
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.HairFringe).Choices.Single(c => c.Label == "Test fringe"));
        Assert.Equal([myTop.Id], editor.Working.Stickers[StickerSlots.HairTop]); // its own top, no second one

        editor.WearCommand.Execute(editor.Gallery(StickerSlots.HairTop).Choices.Single(c => c.Label == "My top")); // takes it off
        WearOn(editor, Piece(StickerSlots.Hair, "Curls"));
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.HairSides).Choices.Single(c => c.Label == "Test sides"));
        Assert.False(editor.Working.Stickers.TryGetValue(StickerSlots.HairTop, out var tops) && tops.Count > 0); // a whole hairstyle covers the crown

        editor.WearHairstyleCommand.Execute(editor.HairstylesGallery().Choices[0]); // bald
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.HairTop).Choices.Single(c => c.Label == "Smooth"));
        Assert.Equal([defaultTop.Id], editor.Working.Stickers[StickerSlots.HairTop]); // a top asked for is not doubled
    }

    // ---------------------------------------------------------------- streaks

    [Fact]
    public void Each_click_on_a_streak_stamps_a_new_copy_across_the_fringe_in_the_last_streaks_colour_and_selects_it()
    {
        var (session, editor) = NewCharacter();
        var lock1 = Piece(StickerSlots.HairStreaks, "Test streak");
        Keep(editor, lock1);
        StickerChoice Choose() => editor.Gallery(StickerSlots.HairStreaks).Choices.Single(c => c.Label == "Test streak");
        Assert.True(Choose().StampsCopy);

        editor.WearCommand.Execute(Choose());
        var first = editor.SelectedSticker!;
        Assert.Equal(lock1.Id, first.Id); // the first goes on as it is
        Assert.Null(first.Sticker.Parts[0].Art!.Offset);

        // The first streak is dyed purple; the next ones take the colour of the last one put on.
        SetColor(editor, StickerSlots.StreakColorKey(first.Id), Purple);
        editor.WearCommand.Execute(Choose());
        var second = editor.SelectedSticker!;
        editor.WearCommand.Execute(Choose());
        var third = editor.SelectedSticker!;

        Assert.Equal(3, editor.Working.Stickers[StickerSlots.HairStreaks].Count);
        Assert.Equal(3, new[] { first.Id, second.Id, third.Id }.Distinct().Count());
        Assert.Equal(new Point2D(HairEditing.StreakSpacing, 0), second.Sticker.Parts[0].Art!.Offset!.Value);
        Assert.Equal(new Point2D(-HairEditing.StreakSpacing, 0), third.Sticker.Parts[0].Art!.Offset!.Value);
        Assert.Equal(Purple, editor.Working.ColorSlots[StickerSlots.StreakColorKey(second.Id)]);
        Assert.Equal(Purple, editor.Working.ColorSlots[StickerSlots.StreakColorKey(third.Id)]);
        Assert.Equal(second.Id, editor.Working.Stickers[StickerSlots.HairStreaks][1]);
        Assert.Equal("Test streak ×3", editor.Gallery(StickerSlots.HairStreaks).Current);
        Assert.Single(editor.Gallery(StickerSlots.HairStreaks).Choices, c => c.Label == "Test streak"); // offered once

        session.Workspace.History.Undo(); // one copy per step
        Assert.Equal(2, editor.Working.Stickers[StickerSlots.HairStreaks].Count);
    }

    [Fact]
    public void A_streak_is_not_spaced_like_a_print_across_the_chest()
    {
        var (_, editor) = NewCharacter();
        Keep(editor, Piece(StickerSlots.HairStreaks, "Test streak"));
        StickerChoice Choose() => editor.Gallery(StickerSlots.HairStreaks).Choices.Single(c => c.Label == "Test streak");

        editor.WearCommand.Execute(Choose());
        editor.WearCommand.Execute(Choose());

        var offset = editor.SelectedSticker!.Sticker.Parts[0].Art!.Offset!.Value;
        Assert.NotEqual(StickerCopies.Spot(1), offset);
        Assert.Equal(0, offset.Y);
    }

    // ---------------------------------------------------------------- the old-hairstyle upgrade bar

    private static StickerAsset OldBob() => Piece(StickerSlots.Hair, "Bob", "hair/bob");

    [Fact]
    public void A_character_wearing_an_old_library_hairstyle_gets_the_upgrade_bar_and_the_others_do_not()
    {
        RequireBob();
        var (_, editor) = NewCharacter();
        Assert.False(editor.HasHairUpgrade);
        Assert.Null(editor.HairUpgradeText);

        WearOn(editor, Piece(StickerSlots.Hair, "My curls")); // your own drawing isn't offered a switch
        Assert.False(editor.HasHairUpgrade);

        WearOn(editor, OldBob());
        Assert.True(editor.HasHairUpgrade);
        Assert.Equal("This is the old Bob - switch to the new Bob made of pieces?", editor.HairUpgradeText);
    }

    [Fact]
    public void Switch_swaps_the_old_hairstyle_for_its_pieces_in_the_default_look_and_every_named_look_in_one_undo_step()
    {
        RequireBob();
        var (session, editor) = NewCharacter();
        var old = OldBob();
        var extra = Piece(StickerSlots.Hair, "My curls");
        WearOn(editor, old);
        SetColor(editor, "hair", Red);
        editor.NewLookCommand.Execute(null);
        var winter = editor.CurrentLook!.Value;
        // The look's own hair slot: the old Bob and something more.
        editor.Apply(EditResult<CharacterDefinition>.Success(LookEditing.StoreLook(editor.Committed, winter,
            LookEditing.Wear(LookEditing.Project(editor.Committed, editor.Committed.Revisions[winter]), extra))));
        Assert.Contains(old.Id, editor.Committed.Revisions[winter].ActiveStickers[StickerSlots.Hair]);
        Assert.True(editor.HasHairUpgrade);

        editor.SwitchHairCommand.Execute(null);

        var bob = Hairstyles.Get("Bob")!;
        Assert.False(editor.HasHairUpgrade);
        Assert.DoesNotContain(old.Id, editor.Committed.Stickers[StickerSlots.Hair]);
        Assert.Equal([extra.Id], editor.Committed.Revisions[winter].ActiveStickers[StickerSlots.Hair]);
        Assert.Equal(bob.Pieces.Count, StickerSlots.HairPieces.Count(slot => editor.Committed.Stickers.TryGetValue(slot, out var ids) && ids.Count > 0));
        Assert.Equal(Red, editor.Committed.ColorSlots["hair"]); // colours kept
        editor.ShowLookCommand.Execute(editor.Looks.Single(l => l.Id is null));
        Assert.Equal("Bob", editor.HairCurrent);

        session.Workspace.History.Undo();
        Assert.Equal([old.Id], editor.Committed.Stickers[StickerSlots.Hair]);
        Assert.Contains(old.Id, editor.Committed.Revisions[winter].ActiveStickers[StickerSlots.Hair]);
        Assert.All(StickerSlots.HairPieces, slot => Assert.False(editor.Committed.Stickers.TryGetValue(slot, out var ids) && ids.Count > 0));
    }

    [Fact]
    public void Keep_hides_the_bar_for_that_character_for_good_and_is_remembered_outside_the_comic()
    {
        RequireBob();
        var (session, editor) = NewCharacter();
        var remembered = new List<string>();
        session.Characters.HairUpgrades = new HairUpgradeMemory(() => remembered, ids => remembered = [.. ids]);
        var other = session.Characters.CreateCharacter();
        var otherEditor = session.Characters.Items.Single(i => i.Id == other.Id).Editor;
        WearOn(editor, OldBob());
        WearOn(otherEditor, OldBob());
        var raised = new List<string?>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var committed = editor.Committed;

        editor.KeepHairCommand.Execute(null);

        Assert.False(editor.HasHairUpgrade);
        Assert.Contains(nameof(CharacterEditorViewModel.HasHairUpgrade), raised);
        Assert.Equal([editor.CharacterId.Value], remembered);
        Assert.True(otherEditor.HasHairUpgrade); // per character
        Assert.Same(committed, editor.Committed); // nothing in the comic changed
        WearOn(editor, Piece(StickerSlots.HairTop, "Smooth")); // and it stays away
        Assert.False(editor.HasHairUpgrade);
    }

    [Fact]
    public void Without_settings_plugged_in_the_choice_lives_in_memory()
    {
        var memory = new HairUpgradeMemory();
        var id = CharacterId.New();
        Assert.False(memory.IsDeclined(id));

        memory.Decline(id);
        memory.Decline(id);

        Assert.True(memory.IsDeclined(id));
        Assert.False(memory.IsDeclined(CharacterId.New()));

        var stored = new List<string>();
        var persisted = new HairUpgradeMemory(() => stored, ids => stored = [.. ids]);
        persisted.Decline(id);
        Assert.Equal([id.Value], stored);
        Assert.True(new HairUpgradeMemory(() => stored, _ => { }).IsDeclined(id));
    }
}
