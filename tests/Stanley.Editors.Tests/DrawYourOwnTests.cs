using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.Rendering;

namespace Stanley.Editors.Tests;

/// <summary>Plays the user's SVG editor: remembers what was opened, and "saves" on request.</summary>
internal sealed class FakeArtEditing : IArtEditing
{
    public List<(string FileName, string Text, Action<string> Saved)> Opened { get; } = [];
    public string? EditorPath { get; set; } = "/usr/bin/inkscape";

    public ArtEditSession? Edit(string fileName, string text, Action<string> saved, out string? error)
    {
        Opened.Add((fileName, text, saved));
        error = null;
        return new ArtEditSession("/drawing/" + fileName, null);
    }
}

public sealed class DrawYourOwnTests
{
    private static (EditorSession Session, CharacterEditorViewModel Editor, FakeArtEditing Art) NewCharacter()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var art = new FakeArtEditing();
        session.Characters.ArtEditing = art;
        var created = session.Characters.CreateCharacter();
        return (session, session.Characters.Items.Single(i => i.Id == created.Id).Editor, art);
    }

    [Fact]
    public void Draw_your_own_asks_for_an_SVG_editor_first_and_wears_nothing_until_one_is_set()
    {
        var (_, editor, art) = NewCharacter();
        art.EditorPath = null;
        var stickersBefore = editor.Working.Wardrobe.Stickers.Count;
        SvgEditorConfigurationRequest? request = null;
        editor.SvgEditorConfigurationRequested += (_, r) => request = r;

        editor.Gallery(StickerSlots.Hair).Draw!.Execute(StickerSlots.Hair);

        Assert.Equal(StickerSlots.Hair, request?.Slot);
        Assert.Empty(art.Opened);
        Assert.Equal(stickersBefore, editor.Working.Wardrobe.Stickers.Count);
        Assert.Null(editor.SelectedSticker);

        editor.SetSvgEditorPath("/usr/bin/inkscape");
        editor.DrawYourOwn(StickerSlots.Hair);

        Assert.Equal("/usr/bin/inkscape", art.EditorPath);
        Assert.Single(art.Opened);
        Assert.NotNull(editor.SelectedSticker);
    }

    [Fact]
    public void Draw_your_own_wears_a_new_sticker_opens_its_template_and_each_save_is_one_undo_step()
    {
        var (session, editor, art) = NewCharacter();

        editor.Gallery(StickerSlots.Hair).Draw!.Execute(StickerSlots.Hair);

        var drawn = Assert.IsType<StickerAsset>(editor.SelectedSticker);
        Assert.Equal("My hair", drawn.Sticker.Name);
        Assert.Equal("My hair", editor.Gallery(StickerSlots.Hair).Current);
        var (_, template, saved) = Assert.Single(art.Opened);
        Assert.Contains("data-stanley-slot=\"hair\"", template);
        Assert.Contains("/drawing/", editor.Hint);

        var cap = template.Replace("""<g id="front" inkscape:groupmode="layer" inkscape:label="front"/>""",
            """<g id="front" inkscape:groupmode="layer" inkscape:label="front"><rect class="slot-hair" x="-60" y="-1010" width="120" height="30" fill="#3355aa"/></g>""");
        saved(cap);

        Assert.Equal(cap, editor.Working.Wardrobe.Find(drawn.Id)!.Files["variants/default/front.svg"].Text);
        Assert.Contains("hair", editor.Working.Wardrobe.Find(drawn.Id)!.Sticker.Colors.Keys);
        session.Workspace.History.Undo();
        Assert.Equal(template, editor.Working.Wardrobe.Find(drawn.Id)!.Files["variants/default/front.svg"].Text);
        session.Workspace.History.Redo();

        // The side view: the same sticker, a side template to draw on.
        editor.PreviewAngle = ViewAngle.Profile;
        editor.EditSelectedArtCommand.Execute(null);
        Assert.Equal(2, art.Opened.Count);
        Assert.Contains("data-stanley-view=\"profile\"", art.Opened[1].Text);
        Assert.Single(editor.Working.Wardrobe.Stickers.Values, a => a.Sticker.Slot == StickerSlots.Hair);
    }

    [Fact]
    public void Draw_your_own_and_Import_put_theirs_on_over_what_the_slot_holds()
    {
        var (_, editor, _) = NewCharacter();
        editor.WearCommand.Execute(editor.Gallery(StickerSlots.Top).Choices.Single(c => c.Label == "T-shirt"));
        var tee = editor.Working.Stickers[StickerSlots.Top].Single();

        editor.Gallery(StickerSlots.Top).Draw!.Execute(StickerSlots.Top);
        var drawn = editor.SelectedStickerId!.Value;
        const string logo = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><circle cx="50" cy="50" r="40" fill="#ff9900"/></svg>""";
        Assert.True(editor.ImportArt(StickerSlots.Top, "Logo.svg", ArtFile.Svg(logo)));

        Assert.Equal([tee, drawn, editor.SelectedStickerId!.Value], editor.Working.Stickers[StickerSlots.Top]);
    }

    [Fact]
    public void An_imported_picture_is_worn_selected_and_placed_by_dragging_and_the_sticker_tab()
    {
        var (session, editor, _) = NewCharacter();
        const string logo = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><circle cx="50" cy="50" r="40" fill="#ff9900"/></svg>""";

        Assert.False(editor.ImportArt(StickerSlots.Accessory, "broken.svg", ArtFile.Svg("<svg")));
        Assert.Contains("broken.svg", editor.Hint);
        Assert.True(editor.ImportArt(StickerSlots.Accessory, "Star logo.svg", ArtFile.Svg(logo)));

        var id = editor.SelectedStickerId!.Value;
        Assert.Equal("Star logo", editor.SelectedSticker!.Sticker.Name);
        Assert.True(editor.HasArtParts);
        Assert.False(editor.SelectedHugsShape);
        var renderer = (FigureRenderer)CharacterRenderers.Default;
        double CentreX() { using var o = renderer.Drawing(editor.Working).OutlineOf(id); return o.TightBounds.MidX; }
        var before = CentreX();

        Assert.True(editor.BeginArtDrag());
        editor.UpdateArtDrag(new Point2D(0.02, 0));
        editor.UpdateArtDrag(new Point2D(0.05, 0));
        editor.EndArtDrag();
        Assert.Equal(before + 0.05, CentreX(), 3);

        editor.SelectedHugsShape = true;
        Assert.True(editor.SelectedHugsShape);
        editor.BeginSliderDrag();
        editor.SelectedArtScale = 150;
        editor.SelectedArtScale = 200;
        editor.EndSliderDrag();
        Assert.Equal(200, editor.SelectedArtScale);

        session.Workspace.History.Undo(); // the size drag
        session.Workspace.History.Undo(); // hug
        Assert.False(editor.SelectedHugsShape);
        session.Workspace.History.Undo(); // the move
        Assert.Equal(before, CentreX(), 3);
    }
}
