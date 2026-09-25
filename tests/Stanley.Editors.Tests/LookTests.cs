using Stanley.EditorFramework;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.StickerLibrary;

namespace Stanley.Editors.Tests;

public sealed class LookTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-look-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static (EditorSession Session, CharacterEditorViewModel Editor) NewCharacter()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var created = session.Characters.CreateCharacter();
        return (session, session.Characters.Items.Single(i => i.Id == created.Id).Editor);
    }

    private static StickerChoice Choice(CharacterEditorViewModel editor, string slot, string name) =>
        editor.Gallery(slot).Choices.Single(c => c.Label == name);

    [Fact]
    public void The_starter_library_is_all_valid_stickers_in_known_slots()
    {
        Assert.NotEmpty(StickerLibrary.StickerLibrary.All);
        foreach (var item in StickerLibrary.StickerLibrary.All)
        {
            Assert.Contains(StickerSlots.All, s => s.Name == item.Slot);
            Assert.NotEmpty(item.Asset.Sticker.Parts);
            Assert.All(item.Asset.Sticker.Parts, p => Assert.True(p.Cover is not null ^ p.Art is not null, $"{item.Key}/{p.Name}: exactly one of cover or art"));
            Assert.All(item.Asset.Sticker.Parts.Where(p => p.Cover is not null), p => Assert.True(item.Asset.Sticker.Colors.ContainsKey(p.Cover!.Color), $"{item.Key}: a default for {p.Cover!.Color}"));
        }
        Assert.Contains(StickerLibrary.StickerLibrary.ForSlot(StickerSlots.Top), s => s.Key == "top/t-shirt");
    }

    [Fact]
    public void Every_drawn_library_sticker_has_clean_front_and_side_art_for_every_variant_and_part()
    {
        var drawn = StickerLibrary.StickerLibrary.All.Where(s => s.Asset.HasArt).ToList();
        Assert.Contains(drawn, s => s.Slot == StickerSlots.Hair);
        Assert.Contains(drawn, s => s.Slot == StickerSlots.Eyes);
        foreach (var item in drawn)
        {
            foreach (var variant in item.Asset.Sticker.Variants)
            {
                foreach (var view in new[] { ViewAngle.Front, ViewAngle.Profile })
                {
                    var file = item.Asset.ArtFor(variant, view);
                    Assert.True(file is not null, $"{item.Key}: {variant} {view}");
                    var art = Stanley.Rendering.StickerSvg.Parse(file!);
                    Assert.True(art is not null, $"{item.Key}: {variant} {view} parses");
                    Assert.Empty(art!.Report);
                    Assert.Equal(StickerSlots.Get(item.Slot).Name, art.Slot);
                    foreach (var part in item.Asset.Sticker.Parts.Where(p => p.Art is not null))
                        Assert.True(art.Part(part.Name).Count > 0 || part.Depth == PartDepth.Back && view == ViewAngle.Front, $"{item.Key}: {variant} {view} draws {part.Name}");
                }
            }
        }
    }

    [Fact]
    public void A_library_sticker_is_worn_as_a_fresh_copy_marked_as_coming_from_the_library()
    {
        var item = StickerLibrary.StickerLibrary.Find("top/t-shirt")!;
        var (a, b) = (item.Instantiate(), item.Instantiate());

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal("library:top/t-shirt", a.Sticker.Source);
        Assert.Same(item, StickerLibrary.StickerLibrary.SourceOf(a.Sticker));
    }

    [Fact]
    public void Picking_from_a_gallery_wears_it_in_one_undo_step_and_the_gallery_then_offers_the_worn_copy()
    {
        var (session, editor) = NewCharacter();
        var tee = Choice(editor, StickerSlots.Top, "T-shirt");
        Assert.NotNull(tee.Library);
        Assert.Equal("None", editor.Gallery(StickerSlots.Top).Current);

        editor.WearCommand.Execute(tee);

        var gallery = editor.Gallery(StickerSlots.Top);
        Assert.Equal("T-shirt", gallery.Current);
        var worn = gallery.Choices.Single(c => c.Label == "T-shirt");
        Assert.True(worn.IsWorn);
        Assert.Null(worn.Library); // the wardrobe's copy, not the library's again
        Assert.Contains(editor.ColorEditors, e => e.Slot == "top");

        session.Workspace.History.Undo();
        Assert.Equal("None", editor.Gallery(StickerSlots.Top).Current);
        Assert.DoesNotContain(editor.Working.Wardrobe.Stickers.Values, a => a.Sticker.Slot == StickerSlots.Top);
    }

    [Fact]
    public void A_new_character_starts_with_the_default_face_on()
    {
        var (_, editor) = NewCharacter();

        var worn = CharacterLooks.Resolve(editor.Working).Stickers.Select(w => w.Asset.Sticker.Source).ToList();
        Assert.Equal(StickerLibrary.StickerLibrary.DefaultFace.Select(k => StickerLibrary.StickerLibrary.SourcePrefix + k).Order(), worn.Order());
        Assert.Equal("Dots", editor.Gallery(StickerSlots.Eyes).Current);
    }

    [Fact]
    public void Every_choice_is_previewed_on_the_character_wearing_it()
    {
        var (_, editor) = NewCharacter();
        editor.SetBody(BodyPresets.Shape(BodyPreset.Heavy));

        var jeans = Choice(editor, StickerSlots.Bottom, "Jeans");

        Assert.Equal(editor.Working.Body, jeans.Preview.Body);
        Assert.Contains(CharacterLooks.Resolve(jeans.Preview).Stickers, w => w.Asset.Sticker.Name == "Jeans");
        Assert.DoesNotContain(CharacterLooks.Resolve(Choice(editor, StickerSlots.Bottom, "None").Preview).Stickers, w => w.Slot == StickerSlots.Bottom);
    }

    [Fact]
    public void A_colour_swatch_recolours_the_slot_as_one_undo_step()
    {
        var (session, editor) = NewCharacter();
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Top, "T-shirt"));
        var top = editor.ColorEditors.Single(e => e.Slot == "top");
        var red = top.Swatches.Single(s => s.Name == "Red");

        top.SetColor.Execute(red);

        Assert.Equal(red.Color, editor.ColorEditors.Single(e => e.Slot == "top").Color);
        session.Workspace.History.Undo();
        Assert.NotEqual(red.Color, editor.ColorEditors.Single(e => e.Slot == "top").Color);
    }

    [Fact]
    public void The_sticker_tab_lengthens_a_selected_garment_in_one_undo_step_per_drag()
    {
        var (session, editor) = NewCharacter();
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Top, "T-shirt"));
        var id = editor.Working.Stickers[StickerSlots.Top].Single();
        editor.SelectSticker(id);
        Assert.True(editor.HasSelectedSticker && editor.HasLength && editor.HasSleeves);
        var sleeves = editor.SelectedSleeves;

        editor.BeginSliderDrag();
        editor.SelectedSleeves = 60;
        editor.SelectedSleeves = 100;
        editor.EndSliderDrag();

        Assert.Equal(100, editor.SelectedSleeves);
        Assert.Null(editor.Working.Wardrobe.Find(id)!.Sticker.Source); // now the user's own
        session.Workspace.History.Undo();
        Assert.Equal(sleeves, editor.SelectedSleeves);
        Assert.True(editor.TakeOffSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void Saving_keeps_what_is_worn_and_drops_library_things_only_tried_on()
    {
        var (session, editor) = NewCharacter();
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Top, "T-shirt"));
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Top, "Hoodie")); // the T-shirt is only tried on now
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Bottom, "Jeans"));

        var folder = ComicProject.CreateNew().SaveAs(_root, session.Navigator.Snapshot(), null, session.Characters.Snapshot());
        var saved = ComicProject.Open(folder).Characters.Single();

        Assert.Equal(["Dots", "Hoodie", "Jeans", "Simple", "Thin"], saved.Wardrobe.Stickers.Values.Select(a => a.Sticker.Name).Order());
        Assert.Equal(editor.Working.Stickers[StickerSlots.Top], saved.Stickers[StickerSlots.Top]);
        Assert.Equal(5, Directory.GetDirectories(Path.Combine(Directory.GetDirectories(Path.Combine(folder, "characters")).Single(), "stickers")).Length);
    }
}

public sealed class FabricEditingTests
{
    private static (EditorSession Session, CharacterEditorViewModel Editor) Dressed(params string[] items)
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var created = session.Characters.CreateCharacter();
        var editor = session.Characters.Items.Single(i => i.Id == created.Id).Editor;
        foreach (var name in items)
        {
            var slot = StickerLibrary.StickerLibrary.All.Single(s => s.Name == name).Slot;
            editor.WearCommand.Execute(editor.Gallery(slot).Choices.Single(c => c.Label == name));
        }
        return (session, editor);
    }

    [Fact]
    public void Jeans_come_in_denim_and_picking_a_pattern_is_one_undo_step()
    {
        var (session, editor) = Dressed("Jeans", "T-shirt");
        var bottom = editor.ColorEditors.Single(e => e.Slot == "bottom");
        Assert.Equal(TextureKind.Denim, bottom.Fabric?.Texture?.Kind);

        var top = editor.ColorEditors.Single(e => e.Slot == "top");
        top.SetPattern.Execute(top.PatternChoices.Single(c => c.Label == "Stripes"));

        Assert.Same(top, editor.ColorEditors.Single(e => e.Slot == "top")); // the same editor, so its dropdown stays open
        Assert.Equal(PatternKind.Stripes, top.Fabric?.Pattern?.Kind);
        Assert.True(top.PatternChoices.Single(c => c.Label == "Stripes").IsCurrent);
        session.Workspace.History.Undo();
        Assert.Null(top.Fabric);
    }

    [Fact]
    public void A_pattern_colour_and_a_size_drag_each_make_one_undo_step_and_the_pattern_survives_a_change_of_top()
    {
        var (session, editor) = Dressed("T-shirt");
        var top = editor.ColorEditors.Single(e => e.Slot == "top");
        top.SetPattern.Execute(top.PatternChoices.Single(c => c.Label == "Dots"));
        top.SetPatternColor.Execute(top.PatternSwatches.Single(s => s.Name == "Yellow"));

        top.BeginDrag();
        top.PatternSize = 8;
        top.PatternSize = 12;
        top.EndDrag();

        Assert.Equal(12, top.PatternSize);
        Assert.Equal(ColorValue.FromHex("#f1c40f"), top.Fabric!.Pattern!.Colors[0]);
        session.Workspace.History.Undo();
        Assert.Equal(PatternFill.DefaultSize * 100, top.PatternSize);

        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Top).Choices.Single(c => c.Label == "Hoodie"));
        Assert.Equal(PatternKind.Dots, editor.ColorEditors.Single(e => e.Slot == "top").Fabric?.Pattern?.Kind);
    }

    [Fact]
    public void Plain_denim_can_be_asked_for_and_skin_has_no_fabric()
    {
        var (_, editor) = Dressed("Jeans");
        var bottom = editor.ColorEditors.Single(e => e.Slot == "bottom");

        bottom.SetTexture.Execute(bottom.TextureChoices.Single(c => c.Label == "None"));

        Assert.Null(bottom.Fabric);
        Assert.False(editor.ColorEditors.Single(e => e.Slot == "skin").CanHaveFabric);
    }

    [Fact]
    public void A_library_tile_is_copied_in_when_picked_as_one_undo_step()
    {
        var (session, editor) = Dressed("T-shirt");
        var top = editor.ColorEditors.Single(e => e.Slot == "top");
        var floral = top.PatternChoices.Single(c => c.Label == "Floral");

        top.SetPattern.Execute(floral);

        Assert.Equal(PatternKind.Tile, top.Fabric?.Pattern?.Kind);
        Assert.Equal("floral.svg", top.Fabric?.Pattern?.Tile);
        Assert.True(editor.Working.Wardrobe.Tiles.ContainsKey("floral.svg"));
        Assert.True(top.PatternChoices.Single(c => c.Label == "Floral").IsCurrent);
        session.Workspace.History.Undo();
        Assert.Null(top.Fabric);
        Assert.Empty(editor.Working.Wardrobe.Tiles);
    }

    [Fact]
    public void Custom_asks_the_view_for_a_file_and_an_imported_tile_is_offered_from_then_on()
    {
        var (_, editor) = Dressed("T-shirt");
        var top = editor.ColorEditors.Single(e => e.Slot == "top");
        TileImportRequest? asked = null;
        editor.TileImportRequested += (_, request) => asked = request;

        top.SetTexture.Execute(top.TextureChoices.Single(c => c.IsCustom));
        Assert.Equal(new TileImportRequest("top", Texture: true), asked);

        const string tile = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="#ffffff"/><circle cx="5" cy="5" r="3" fill="#808080" filter="url(#x)"/></svg>""";
        Assert.True(editor.ImportTile("top", "My Burlap.svg", ArtFile.Svg(tile), texture: true));

        Assert.Equal("texture-my-burlap.svg", top.Fabric?.Texture?.Tile);
        Assert.Equal(TextureKind.Tile, top.Fabric?.Texture?.Kind);
        Assert.Contains(top.TextureChoices, c => c.Label == "My burlap" && c.IsCurrent);
        Assert.DoesNotContain(top.PatternChoices, c => c.Label == "My burlap");
        Assert.Contains("filter", editor.Hint, StringComparison.Ordinal); // what wasn't drawn is reported

        Assert.False(editor.ImportTile("top", "broken.png", ArtFile.Png([1, 2, 3]), texture: false));
        Assert.Contains("broken.png", editor.Hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_drops_library_tiles_nothing_uses_but_keeps_imported_ones()
    {
        var root = Path.Combine(Path.GetTempPath(), "stanley-tiles-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (session, editor) = Dressed("T-shirt");
            var top = editor.ColorEditors.Single(e => e.Slot == "top");
            top.SetPattern.Execute(top.PatternChoices.Single(c => c.Label == "Stars"));
            top.SetPattern.Execute(top.PatternChoices.Single(c => c.Label == "Hearts")); // stars only tried on
            editor.ImportTile("top", "mine.svg", ArtFile.Svg("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="5" height="5"/></svg>"""), texture: true);

            var folder = ComicProject.CreateNew().SaveAs(root, session.Navigator.Snapshot(), null, session.Characters.Snapshot());
            var saved = ComicProject.Open(folder).Characters.Single();

            Assert.Equal(["hearts.svg", "texture-mine.svg"], saved.Wardrobe.Tiles.Keys.Order());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_custom_colour_picked_for_skin_applies_it_in_one_undo_step()
    {
        var (session, editor) = Dressed();
        var customColor = ColorValue.FromHex("#ff00ff");
        var originalSkin = editor.Working.Skin;

        editor.SetSkin(customColor);

        Assert.Equal(customColor, editor.Working.Skin);
        Assert.NotEqual(originalSkin, editor.Working.Skin);
        Assert.True(session.Workspace.History.CanUndo);

        session.Workspace.History.Undo();
        Assert.Equal(originalSkin, editor.Working.Skin);
    }

    [Fact]
    public void A_custom_colour_picked_for_a_slot_applies_it_in_one_undo_step()
    {
        var (session, editor) = Dressed("T-shirt");
        var customColor = ColorValue.FromHex("#00ff00");
        var top = editor.ColorEditors.Single(e => e.Slot == "top");
        var originalColor = top.Color;

        var choice = new ColorSwatchChoice("top", "Custom", customColor);
        top.SetColor.Execute(choice);

        Assert.Equal(customColor, top.Color);
        Assert.NotEqual(originalColor, top.Color);
        Assert.True(session.Workspace.History.CanUndo);

        session.Workspace.History.Undo();
        Assert.Equal(originalColor, top.Color);
    }

    [Fact]
    public void A_custom_colour_picked_for_a_pattern_applies_it_in_one_undo_step()
    {
        var (session, editor) = Dressed("T-shirt");
        var customColor = ColorValue.FromHex("#0000ff");
        var top = editor.ColorEditors.Single(e => e.Slot == "top");
        top.SetPattern.Execute(top.PatternChoices.Single(c => c.Label == "Stripes"));
        var originalPatternColor = top.Fabric?.Pattern?.Colors[0] ?? ColorValue.FromHex("#000000");

        var choice = new ColorSwatchChoice("top", "Custom", customColor);
        top.SetPatternColor.Execute(choice);

        Assert.Equal(customColor, top.Fabric?.Pattern?.Colors[0]);
        Assert.NotEqual(originalPatternColor, top.Fabric?.Pattern?.Colors[0]);
        Assert.True(session.Workspace.History.CanUndo);

        session.Workspace.History.Undo();
        Assert.Equal(originalPatternColor, top.Fabric?.Pattern?.Colors[0]);
    }
}
