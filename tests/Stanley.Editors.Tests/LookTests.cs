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
        Assert.Empty(editor.Working.Wardrobe.Stickers);
    }

    [Fact]
    public void Every_choice_is_previewed_on_the_character_wearing_it()
    {
        var (_, editor) = NewCharacter();
        editor.SetBody(BodyPresets.Shape(BodyPreset.Heavy));

        var jeans = Choice(editor, StickerSlots.Bottom, "Jeans");

        Assert.Equal(editor.Working.Body, jeans.Preview.Body);
        Assert.Contains(CharacterLooks.Resolve(jeans.Preview).Stickers, w => w.Asset.Sticker.Name == "Jeans");
        Assert.Empty(CharacterLooks.Resolve(Choice(editor, StickerSlots.Bottom, "None").Preview).Stickers);
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

        Assert.Equal(["Hoodie", "Jeans"], saved.Wardrobe.Stickers.Values.Select(a => a.Sticker.Name).Order());
        Assert.Equal(editor.Working.Stickers[StickerSlots.Top], saved.Stickers[StickerSlots.Top]);
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(Directory.GetDirectories(Path.Combine(folder, "characters")).Single(), "stickers")).Length);
    }
}
