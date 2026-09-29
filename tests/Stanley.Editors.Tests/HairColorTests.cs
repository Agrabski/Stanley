using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors.Tests;

/// <summary>
/// Colour for hair built from pieces (GitHub issue #59): the Colours group showing a piece
/// once it has a colour of its own, the Sticker tab's colour, Same as hair and Over glasses,
/// the Dye section of a hair colour's dropdown and its Schemes row.
/// </summary>
public sealed class HairColorTests : IDisposable
{
    private static readonly ColorValue Red = ColorValue.FromHex("#c0392b");
    private static readonly ColorValue Green = ColorValue.FromHex("#4caf50");
    private static readonly ColorValue Teal = ColorValue.FromHex("#00897b");
    private static readonly ColorValue Purple = ColorValue.FromHex("#8e24aa");
    private static readonly ColorValue Blue = ColorValue.FromHex("#3a6fd8");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-hair-color-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static StickerAsset Piece(string slot, string name) =>
        new(new Sticker(StickerId.New(), name, slot, [new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp))],
            new SortedDictionary<string, ColorValue> { ["hair"] = ColorValue.FromHex("#222222") }, ["default"]), new Dictionary<string, ArtFile>());

    /// <summary>A character in a workspace, wearing a top, a fringe and a back piece over red hair - every piece following the hair.</summary>
    private static (EditorSession Session, CharacterEditorViewModel Editor, StickerAsset Top, StickerAsset Fringe, StickerAsset Back) NewHead()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var created = session.Characters.CreateCharacter();
        var editor = session.Characters.Items.Single(i => i.Id == created.Id).Editor;
        var (top, fringe, back) = (Piece(StickerSlots.HairTop, "Smooth"), Piece(StickerSlots.HairFringe, "Blunt"), Piece(StickerSlots.HairBack, "Bob"));
        var dressed = HairEditing.WearHairdo(editor.Committed, [(top, null), (fringe, null), (back, null)], replace: true);
        editor.Apply(EditResult<CharacterDefinition>.Success(LookEditing.SetColor(dressed, StickerSlots.Hair, Red)));
        return (session, editor, top, fringe, back);
    }

    private static ColorSwatchChoice Swatch(string slot, ColorValue color) => new(slot, "Colour", color);

    private static ColorSlotEditor HairEditor(CharacterEditorViewModel editor) => editor.ColorEditors.Single(e => e.Slot == StickerSlots.Hair);

    private static PatternFill? DyeOf(CharacterEditorViewModel editor, string key) =>
        editor.Working.Fabrics is { } fabrics && fabrics.TryGetValue(key, out var fabric) ? fabric.Pattern : null;

    // ---------------------------------------------------------------- the Colours group

    [Fact]
    public void A_piece_gets_a_swatch_in_the_Colours_group_only_once_it_has_a_colour_of_its_own()
    {
        var (session, editor, _, _, _) = NewHead();
        Assert.Contains(editor.ColorEditors, e => e.Slot == StickerSlots.Hair);
        Assert.DoesNotContain(editor.ColorEditors, e => e.Slot == StickerSlots.HairFringe); // every piece follows the Hair colour

        editor.SetColorCommand.Execute(Swatch(StickerSlots.HairFringe, Purple));

        var slots = editor.ColorEditors.Select(e => e.Slot).ToList();
        Assert.Equal(slots.IndexOf(StickerSlots.Hair) + 1, slots.IndexOf(StickerSlots.HairFringe)); // right after Hair
        Assert.Equal("Fringe", editor.ColorEditors.Single(e => e.Slot == StickerSlots.HairFringe).Label);
        Assert.DoesNotContain(editor.ColorEditors, e => e.Slot == StickerSlots.HairTop); // the others still follow
        session.Workspace.History.Undo();
        Assert.DoesNotContain(editor.ColorEditors, e => e.Slot == StickerSlots.HairFringe);
    }

    [Fact]
    public void A_dye_alone_gives_a_piece_a_swatch_and_a_streak_never_gets_one()
    {
        var (_, editor, top, _, _) = NewHead();
        editor.SelectSticker(top.Id);
        editor.SelectedColorEditor!.SetDye.Execute(editor.SelectedColorEditor.DyeChoices.Single(c => c.Label == "Tips"));
        var streak = Piece(StickerSlots.HairStreaks, "Thin");
        editor.Apply(EditResult<CharacterDefinition>.Success(HairEditing.WearStreak(editor.Committed, streak)));
        editor.SetColorCommand.Execute(Swatch(StickerSlots.StreakColorKey(streak.Id), Purple));

        Assert.Contains(editor.ColorEditors, e => e.Slot == StickerSlots.HairTop);
        Assert.DoesNotContain(editor.ColorEditors, e => e.Slot.StartsWith("streak", StringComparison.Ordinal));
    }

    [Fact]
    public void The_pieces_and_streaks_are_labelled_and_use_the_hair_palette_plus_vivid_dyes()
    {
        Assert.Equal("Top", CharacterEditorViewModel.ColorSlotLabel(StickerSlots.HairTop));
        Assert.Equal("Fringe", CharacterEditorViewModel.ColorSlotLabel(StickerSlots.HairFringe));
        Assert.Equal("Sides", CharacterEditorViewModel.ColorSlotLabel(StickerSlots.HairSides));
        Assert.Equal("Back", CharacterEditorViewModel.ColorSlotLabel(StickerSlots.HairBack));
        Assert.Equal("Extras", CharacterEditorViewModel.ColorSlotLabel(StickerSlots.HairExtras));
        Assert.Equal("Streak", CharacterEditorViewModel.ColorSlotLabel(StickerSlots.StreakColorKey(StickerId.New())));
        Assert.Equal("Hair", CharacterEditorViewModel.ColorSlotLabel(StickerSlots.Hair));

        var (_, editor, _, fringe, _) = NewHead();
        editor.SelectSticker(fringe.Id);
        var hairSwatches = HairEditor(editor).Swatches.Select(s => s.Color).ToList();
        Assert.Equal(hairSwatches, editor.SelectedColorEditor!.Swatches.Select(s => s.Color)); // a piece uses the hair's palette
        Assert.Contains(Purple, hairSwatches);
        Assert.Contains(hairSwatches, c => c == ColorValue.FromHex("#d6409f")); // magenta
    }

    // ---------------------------------------------------------------- the Sticker tab

    [Fact]
    public void Clicking_a_piece_offers_its_own_colour_and_Same_as_hair_undoes_it_in_one_step()
    {
        var (session, editor, _, fringe, _) = NewHead();
        editor.SelectSticker(fringe.Id);

        Assert.True(editor.HasSelectedHairColor);
        var dropdown = editor.SelectedColorEditor!;
        Assert.Equal(StickerSlots.HairFringe, dropdown.Slot);
        Assert.Equal("Fringe", dropdown.Label);
        Assert.Equal(Red, dropdown.Color); // following the hair
        Assert.False(editor.CanFollowHair); // nothing of its own to give up
        Assert.False(editor.SameAsHairCommand.CanExecute(null));

        dropdown.SetColor.Execute(Swatch(StickerSlots.HairFringe, Purple));

        Assert.Equal(Purple, editor.Working.ColorSlots[StickerSlots.HairFringe]);
        Assert.Same(dropdown, editor.SelectedColorEditor); // the same dropdown, so it stays open
        Assert.Equal(Purple, dropdown.Color);
        Assert.True(editor.CanFollowHair);
        Assert.True(editor.SameAsHairCommand.CanExecute(null));
        Assert.False(editor.HasSameAsHairNote); // the default look: it simply follows again

        editor.SameAsHairCommand.Execute(null);

        Assert.False(HairEditing.HasOwnColor(editor.Working, StickerSlots.HairFringe));
        Assert.Equal(Red, dropdown.Color);
        Assert.False(editor.CanFollowHair);
        session.Workspace.History.Undo(); // one step
        Assert.Equal(Purple, editor.Working.ColorSlots[StickerSlots.HairFringe]);
    }

    [Fact]
    public void Same_as_hair_takes_the_dye_too()
    {
        var (session, editor, _, fringe, _) = NewHead();
        editor.SelectSticker(fringe.Id);
        editor.SelectedColorEditor!.SetDye.Execute(editor.SelectedColorEditor.DyeChoices.Single(c => c.Label == "Roots"));
        Assert.Equal(PatternKind.Roots, DyeOf(editor, StickerSlots.HairFringe)!.Kind);
        Assert.True(editor.CanFollowHair);

        editor.SameAsHairCommand.Execute(null);

        Assert.Null(DyeOf(editor, StickerSlots.HairFringe));
        session.Workspace.History.Undo();
        Assert.Equal(PatternKind.Roots, DyeOf(editor, StickerSlots.HairFringe)!.Kind);
    }

    [Fact]
    public void Same_as_hair_is_for_pieces_only_and_the_hair_and_a_streak_have_none()
    {
        var (_, editor, top, _, _) = NewHead();
        var (legacy, streak) = (Piece(StickerSlots.Hair, "Bob"), Piece(StickerSlots.HairStreaks, "Thin"));
        editor.Apply(EditResult<CharacterDefinition>.Success(HairEditing.WearStreak(LookEditing.Wear(editor.Committed, legacy), streak)));
        editor.SetColorCommand.Execute(Swatch(StickerSlots.StreakColorKey(streak.Id), Purple));

        editor.SelectSticker(legacy.Id);
        Assert.Equal(StickerSlots.Hair, editor.SelectedColorEditor!.Slot);
        Assert.False(editor.CanFollowHair);
        editor.SelectSticker(streak.Id);
        Assert.Equal(StickerSlots.StreakColorKey(streak.Id), editor.SelectedColorEditor!.Slot);
        Assert.Equal("Streak", editor.SelectedColorEditor.Label);
        Assert.Equal(Purple, editor.SelectedColorEditor.Color);
        Assert.False(editor.CanFollowHair); // its own colour is the point of a streak
        editor.SelectSticker(top.Id);
        Assert.False(editor.CanFollowHair); // it has none of its own
    }

    [Fact]
    public void A_worn_sticker_has_a_colour_dropdown_on_the_Sticker_tab_and_an_unworn_one_does_not()
    {
        var (_, editor, _, fringe, _) = NewHead();
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Top).Choices.Single(c => c.Label == "T-shirt"));
        Assert.True(editor.HasSelectedSticker); // the T-shirt, put on and selected

        Assert.True(editor.HasSelectedHairColor); // any worn sticker can take a colour of its own (#127)
        Assert.True(StickerSlots.IsStickerColorKey(editor.SelectedColorEditor!.Slot));
        Assert.Equal("Colour", editor.SelectedColorGroupLabel);
        Assert.False(editor.CanFollowHair); // none of its own yet
        Assert.False(editor.CanSetOverGlasses);
        editor.SelectedColorEditor.SetColor.Execute(new ColorSwatchChoice(editor.SelectedColorEditor.Slot, "Custom", Purple));
        Assert.Equal(Purple, editor.SelectedColorEditor.Color);
        Assert.True(editor.CanFollowHair);
        Assert.Equal("Same as slot", editor.FollowLabel);
        editor.SameAsHairCommand.Execute(null);
        Assert.False(editor.CanFollowHair);

        editor.SelectSticker(fringe.Id);
        Assert.Equal("Hair", editor.SelectedColorGroupLabel);
        Assert.True(editor.HasSelectedHairColor);
        editor.TakeOffSelectedCommand.Execute(null); // still selected, but not worn any more
        Assert.False(editor.HasSelectedHairColor);
        Assert.Null(editor.SelectedColorEditor);
    }

    [Fact]
    public void Same_as_hair_in_a_named_look_copies_the_looks_hair_and_says_so_where_the_default_gives_the_piece_its_own()
    {
        var (_, editor, _, fringe, _) = NewHead();
        editor.SelectSticker(fringe.Id);
        editor.SelectedColorEditor!.SetColor.Execute(Swatch(StickerSlots.HairFringe, Purple)); // the default look: a purple fringe
        editor.NewLook();
        Assert.True(editor.IsNamedLook);
        HairEditor(editor).SetColor.Execute(Swatch(StickerSlots.Hair, Green)); // and here, green hair

        Assert.True(editor.CanFollowHair);
        Assert.True(editor.HasSameAsHairNote);
        Assert.Contains("copies", editor.SameAsHairNote);
        editor.SameAsHairCommand.Execute(null);

        Assert.Equal(Green, CharacterLooks.Resolve(editor.LookWorking).Colors[StickerSlots.HairFringe]);
        Assert.Equal(Purple, editor.Committed.ColorSlots[StickerSlots.HairFringe]); // the default look keeps its own
    }

    // ---------------------------------------------------------------- over glasses

    [Fact]
    public void Over_glasses_is_a_toggle_for_any_worn_hair_sticker_and_one_undo_step()
    {
        var (session, editor, _, fringe, _) = NewHead();
        editor.SelectSticker(fringe.Id);
        Assert.True(editor.CanSetOverGlasses);
        Assert.False(editor.SelectedOverGlasses);

        editor.SelectedOverGlasses = true;
        editor.SelectedOverGlasses = true; // nothing new

        Assert.True(editor.SelectedOverGlasses);
        Assert.True(HairEditing.IsOverGlasses(editor.Working, fringe.Id));
        session.Workspace.History.Undo(); // a single step
        Assert.False(editor.SelectedOverGlasses);
        Assert.False(HairEditing.IsOverGlasses(editor.Working, fringe.Id));
    }

    [Fact]
    public void Over_glasses_is_offered_for_a_whole_hairstyle_but_not_for_a_streak_or_clothes()
    {
        var (_, editor, _, _, _) = NewHead();
        var (legacy, streak) = (Piece(StickerSlots.Hair, "Bob"), Piece(StickerSlots.HairStreaks, "Thin"));
        editor.Apply(EditResult<CharacterDefinition>.Success(HairEditing.WearStreak(LookEditing.Wear(editor.Committed, legacy), streak)));

        editor.SelectSticker(legacy.Id);
        Assert.True(editor.CanSetOverGlasses);
        editor.SelectSticker(streak.Id);
        Assert.False(editor.CanSetOverGlasses);
    }

    // ---------------------------------------------------------------- dyes

    [Fact]
    public void A_hair_dropdown_says_Dye_and_lists_the_dyes_with_the_clothing_patterns_behind_More_patterns()
    {
        var (_, editor, _, _, _) = NewHead();
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Top).Choices.Single(c => c.Label == "T-shirt"));
        var hair = HairEditor(editor);

        Assert.True(hair.IsHairKey);
        Assert.Equal("Dye", hair.PatternTitle);
        Assert.Equal(["None", "Streaks", "Tips", "Roots", "Ombré", "Rainbow"], hair.DyeChoices.Select(c => c.Label));
        Assert.Single(hair.DyeChoices, c => c.IsCurrent && c.Label == "None");
        Assert.Contains(hair.MorePatternChoices, c => c.Label == "Stripes");
        Assert.DoesNotContain(hair.MorePatternChoices, c => c.Label == "None"); // that's the dye gallery's
        // A clothing colour keeps its dropdown as it was.
        var top = editor.ColorEditors.Single(e => e.Slot == "top");
        Assert.False(top.IsHairKey);
        Assert.Equal("Pattern", top.PatternTitle);
        Assert.Equal("None", top.PatternChoices[0].Label);
        Assert.Contains(top.PatternChoices, c => c.Label == "Stripes");
    }

    [Fact]
    public void A_dye_picked_on_a_purple_fringe_starts_in_a_colour_that_shows_on_it()
    {
        var (_, editor, _, _, _) = NewHead();
        editor.SetColorCommand.Execute(Swatch(StickerSlots.HairFringe, Purple));
        var fringe = editor.ColorEditors.Single(e => e.Slot == StickerSlots.HairFringe);

        // Purple tips on a purple fringe would show nothing - in the gallery or on the character.
        Assert.All(fringe.DyeChoices.Where(c => c.Pattern is PatternKind.Tips or PatternKind.Roots or PatternKind.Streaks or PatternKind.Ombre),
            c => Assert.NotEqual(Purple, c.Preview.Pattern!.Colors[0]));
        fringe.SetDye.Execute(fringe.DyeChoices.Single(c => c.Label == "Tips"));

        Assert.NotEqual(Purple, DyeOf(editor, StickerSlots.HairFringe)!.Colors[0]);
        // On hair it stands out from, the dye is the usual purple.
        Assert.Equal(ColorSlotEditor.DefaultDyeColor, ColorSlotEditor.DyeColorFor(Red));
    }

    [Fact]
    public void Tips_take_a_colour_and_a_Length_and_each_pick_or_drag_is_one_undo_step()
    {
        var (session, editor, _, _, _) = NewHead();
        var hair = HairEditor(editor);

        hair.SetDye.Execute(hair.DyeChoices.Single(c => c.Label == "Tips"));

        var tips = DyeOf(editor, StickerSlots.Hair)!;
        Assert.Equal(PatternKind.Tips, tips.Kind);
        Assert.Equal([ColorSlotEditor.DefaultDyeColor], tips.Colors);
        Assert.Equal(PatternFill.DefaultDyeWeight(PatternKind.Tips), tips.Weight);
        Assert.True(hair.HasDye);
        Assert.False(hair.HasClothingPattern);
        Assert.True(hair.HasDyeAmount);
        Assert.Equal("Length", hair.DyeAmountLabel);
        Assert.Single(hair.DyeChoices, c => c.IsCurrent && c.Label == "Tips");

        hair.SetPatternColor.Execute(Swatch(StickerSlots.Hair, Green));
        Assert.Equal([Green], DyeOf(editor, StickerSlots.Hair)!.Colors);

        hair.BeginDrag();
        hair.DyeAmount = 50;
        hair.DyeAmount = 60;
        hair.EndDrag();
        Assert.Equal(0.6, DyeOf(editor, StickerSlots.Hair)!.Weight);
        Assert.Equal(60, hair.DyeAmount);
        session.Workspace.History.Undo(); // the whole drag
        Assert.Equal(PatternFill.DefaultDyeWeight(PatternKind.Tips), DyeOf(editor, StickerSlots.Hair)!.Weight);
        Assert.Equal([Green], DyeOf(editor, StickerSlots.Hair)!.Colors);
    }

    [Fact]
    public void Each_dye_has_its_own_slider_and_None_takes_the_dye_off()
    {
        var (_, editor, _, _, _) = NewHead();
        var hair = HairEditor(editor);

        hair.SetDye.Execute(hair.DyeChoices.Single(c => c.Label == "Roots"));
        Assert.Equal("Length", hair.DyeAmountLabel);
        hair.SetDye.Execute(hair.DyeChoices.Single(c => c.Label == "Streaks"));
        Assert.Equal("Width", hair.DyeAmountLabel);
        Assert.Equal([ColorSlotEditor.DefaultDyeColor], DyeOf(editor, StickerSlots.Hair)!.Colors);
        hair.SetDye.Execute(hair.DyeChoices.Single(c => c.Label == "Ombré"));
        Assert.Equal("Blend", hair.DyeAmountLabel);

        // An ombré's weight is where the fade starts, so its Blend is the rest of the piece.
        var starts = DyeOf(editor, StickerSlots.Hair)!.Weight!.Value;
        Assert.Equal(Math.Round(100 * (1 - starts)), hair.DyeAmount);
        hair.DyeAmount = 80;
        Assert.Equal(0.2, DyeOf(editor, StickerSlots.Hair)!.Weight);

        hair.SetDye.Execute(hair.DyeChoices.Single(c => c.Label == "None"));
        Assert.Null(DyeOf(editor, StickerSlots.Hair));
        Assert.False(hair.HasDye);
        Assert.False(hair.HasDyeAmount);
    }

    [Fact]
    public void A_piece_is_dyed_on_its_own_and_the_hair_keeps_its_dye()
    {
        var (_, editor, top, fringe, _) = NewHead();
        var hair = HairEditor(editor);
        hair.SetDye.Execute(hair.DyeChoices.Single(c => c.Label == "Ombré"));
        editor.SelectSticker(fringe.Id);

        editor.SelectedColorEditor!.SetDye.Execute(editor.SelectedColorEditor.DyeChoices.Single(c => c.Label == "Streaks"));

        Assert.Equal(PatternKind.Streaks, DyeOf(editor, StickerSlots.HairFringe)!.Kind);
        Assert.Equal(PatternKind.Ombre, DyeOf(editor, StickerSlots.Hair)!.Kind);
        Assert.Equal(PatternKind.Ombre, CharacterLooks.Resolve(editor.LookWorking).FabricOf(StickerSlots.HairTop)!.Pattern!.Kind); // the top still follows
        Assert.Equal("Dye", editor.SelectedColorEditor.PatternTitle);
        editor.SelectSticker(top.Id);
        Assert.Equal("Dye", editor.SelectedColorEditor!.PatternTitle);
    }

    [Fact]
    public void A_rainbow_starts_with_its_six_colours_and_a_click_on_a_band_lets_you_recolour_it()
    {
        var (session, editor, _, _, _) = NewHead();
        var hair = HairEditor(editor);

        hair.SetDye.Execute(hair.DyeChoices.Single(c => c.Label == "Rainbow"));

        Assert.Equal(PatternFill.RainbowColors, DyeOf(editor, StickerSlots.Hair)!.Colors);
        Assert.True(hair.IsRainbow);
        Assert.False(hair.HasDyeAmount); // bands, not a length
        Assert.Equal(6, hair.RainbowBands.Count);
        Assert.Equal(PatternFill.RainbowColors, hair.RainbowBands.Select(b => b.Color));
        Assert.Equal(0, Assert.Single(hair.RainbowBands, b => b.IsSelected).Index);

        hair.SelectBand.Execute(hair.RainbowBands[3]);
        Assert.Equal(3, Assert.Single(hair.RainbowBands, b => b.IsSelected).Index);
        hair.SetPatternColor.Execute(Swatch(StickerSlots.Hair, Teal));

        var colors = DyeOf(editor, StickerSlots.Hair)!.Colors;
        Assert.Equal(6, colors.Count);
        Assert.Equal(Teal, colors[3]);
        Assert.All(new[] { 0, 1, 2, 4, 5 }, i => Assert.Equal(PatternFill.RainbowColors[i], colors[i]));
        Assert.Equal(Teal, hair.RainbowBands[3].Color);

        session.Workspace.History.Undo(); // one step
        Assert.Equal(PatternFill.RainbowColors, DyeOf(editor, StickerSlots.Hair)!.Colors);
    }

    // ---------------------------------------------------------------- schemes

    [Fact]
    public void Only_the_Hair_colour_starts_with_schemes_each_previewed_on_the_character()
    {
        var (_, editor, _, fringe, _) = NewHead();
        var hair = HairEditor(editor);

        Assert.True(hair.HasSchemes);
        Assert.Equal(["Natural", "Two-tone", "Peekaboo", "Fringe only", "Dip-dye", "Ombré", "Rainbow"], hair.SchemeChoices.Select(s => s.Label));
        var twoTone = hair.SchemeChoices.Single(s => s.Scheme == HairScheme.TwoTone);
        Assert.Equal(ColorSlotEditor.DefaultDyeColor, twoTone.Preview.ColorSlots[StickerSlots.HairFringe]); // the preview wears it...
        Assert.False(editor.Working.ColorSlots.ContainsKey(StickerSlots.HairFringe)); // ...the character doesn't
        Assert.Equal(PatternKind.Tips, hair.SchemeChoices.Single(s => s.Scheme == HairScheme.DipDye).Preview.Fabrics![StickerSlots.Hair].Pattern!.Kind);

        editor.SelectSticker(fringe.Id);
        Assert.False(editor.SelectedColorEditor!.HasSchemes);
        Assert.Empty(editor.SelectedColorEditor.SchemeChoices);
    }

    [Fact]
    public void A_scheme_click_is_one_undo_step_and_the_accent_is_the_second_colour()
    {
        var (session, editor, _, _, _) = NewHead();
        var hair = HairEditor(editor);
        Assert.Equal(ColorSlotEditor.DefaultDyeColor, Assert.Single(hair.AccentChoices, a => a.IsCurrent).Color); // a vivid purple to start

        hair.SetScheme.Execute(hair.SchemeChoices.Single(s => s.Scheme == HairScheme.TwoTone));

        Assert.Equal(Purple, editor.Working.ColorSlots[StickerSlots.HairTop]);
        Assert.Equal(Purple, editor.Working.ColorSlots[StickerSlots.HairFringe]);
        Assert.False(editor.Working.ColorSlots.ContainsKey(StickerSlots.HairBack));
        Assert.Contains(editor.ColorEditors, e => e.Slot == StickerSlots.HairFringe); // they have colours of their own now
        session.Workspace.History.Undo(); // the whole scheme
        Assert.False(editor.Working.ColorSlots.ContainsKey(StickerSlots.HairTop));
        Assert.False(editor.Working.ColorSlots.ContainsKey(StickerSlots.HairFringe));

        hair.SetAccent.Execute(hair.AccentChoices.Single(a => a.Name == "Blue"));
        Assert.Equal(Blue, Assert.Single(hair.AccentChoices, a => a.IsCurrent).Color);
        Assert.Equal(Blue, hair.SchemeChoices.Single(s => s.Scheme == HairScheme.Peekaboo).Preview.ColorSlots[StickerSlots.HairBack]);
        Assert.False(editor.Working.ColorSlots.ContainsKey(StickerSlots.HairBack)); // picking an accent changes nothing on the character
        hair.SetScheme.Execute(hair.SchemeChoices.Single(s => s.Scheme == HairScheme.Peekaboo));
        Assert.Equal(Blue, editor.Working.ColorSlots[StickerSlots.HairBack]);
    }

    [Fact]
    public void Picking_one_scheme_after_another_never_piles_up_and_Natural_resets()
    {
        var (_, editor, _, _, _) = NewHead();
        var hair = HairEditor(editor);
        void Pick(HairScheme scheme) => hair.SetScheme.Execute(hair.SchemeChoices.Single(s => s.Scheme == scheme));

        Pick(HairScheme.TwoTone);
        Pick(HairScheme.FringeOnly);
        Assert.False(editor.Working.ColorSlots.ContainsKey(StickerSlots.HairTop));
        Assert.True(editor.Working.ColorSlots.ContainsKey(StickerSlots.HairFringe));

        Pick(HairScheme.Rainbow);
        Assert.Equal(PatternKind.Rainbow, DyeOf(editor, StickerSlots.Hair)!.Kind);
        Assert.False(editor.Working.ColorSlots.ContainsKey(StickerSlots.HairFringe));
        Assert.DoesNotContain(editor.ColorEditors, e => e.Slot == StickerSlots.HairFringe);

        Pick(HairScheme.Ombre);
        Assert.Equal(PatternKind.Ombre, DyeOf(editor, StickerSlots.Hair)!.Kind);

        Pick(HairScheme.Natural);
        Assert.Null(DyeOf(editor, StickerSlots.Hair));
        Assert.All(StickerSlots.HairPieces, p => Assert.False(HairEditing.HasOwnColor(editor.Working, p)));
        Assert.Equal(Red, editor.Working.ColorSlots[StickerSlots.Hair]);
    }

    [Fact]
    public void A_scheme_in_a_named_look_lands_in_that_look_only()
    {
        var (_, editor, _, _, _) = NewHead();
        editor.NewLook();
        var hair = HairEditor(editor);

        hair.SetScheme.Execute(hair.SchemeChoices.Single(s => s.Scheme == HairScheme.Peekaboo));

        Assert.False(editor.Committed.ColorSlots.ContainsKey(StickerSlots.HairBack)); // the default look is as it was
        var look = editor.Committed.Revisions[editor.CurrentLook!.Value];
        Assert.Equal(Purple, look.ColorSlotValues[StickerSlots.HairBack]);
        Assert.Equal(Purple, CharacterLooks.Resolve(editor.LookWorking).Colors[StickerSlots.HairBack]);
    }

    // ---------------------------------------------------------------- streak colours are tidied

    [Fact]
    public void Removing_a_streak_from_the_wardrobe_takes_its_colour_with_it()
    {
        var (_, editor, _, _, _) = NewHead();
        var streak = Piece(StickerSlots.HairStreaks, "Thin");
        editor.Apply(EditResult<CharacterDefinition>.Success(HairEditing.WearStreak(editor.Committed, streak)));
        var key = StickerSlots.StreakColorKey(streak.Id);
        editor.SelectSticker(streak.Id);
        editor.SelectedColorEditor!.SetColor.Execute(Swatch(key, Purple));
        Assert.Contains(key, editor.Working.ColorSlots.Keys);

        editor.RemoveSelectedCommand.Execute(null);

        Assert.Null(editor.Working.Wardrobe.Find(streak.Id));
        Assert.DoesNotContain(key, editor.Working.ColorSlots.Keys);
        Assert.Equal(Red, editor.Working.ColorSlots[StickerSlots.Hair]); // the rest is untouched
    }

    [Fact]
    public void Saving_leaves_no_colour_behind_for_a_streak_that_is_gone()
    {
        var project = ComicProject.CreateNew();
        var orphan = StickerSlots.StreakColorKey(StickerId.New());
        var basis = CharacterDefinition.Create("A");
        var character = basis with { ColorSlots = new SortedDictionary<string, ColorValue>(basis.ColorSlots) { [StickerSlots.Hair] = Red, [orphan] = Purple } };
        var folder = Path.Combine(_root, "streaks");

        project.SaveAs(folder, new PageNavigatorViewModel(new EditorHistory(), project.Pages).Snapshot(), characters: [character]);

        var saved = ComicProject.Open(folder).Characters.Single();
        Assert.Equal(Red, saved.ColorSlots[StickerSlots.Hair]);
        Assert.DoesNotContain(orphan, saved.ColorSlots.Keys);
    }
}
