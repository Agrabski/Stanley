using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors.Tests;

/// <summary>
/// Styles (docs/sticker-system.md §20): the Sticker tab's "Worn" gallery in the character
/// editor, and right-click › This panel only › Style on the page.
/// </summary>
public sealed class StylesTests
{
    private const string CapFront = "<svg xmlns=\"http://www.w3.org/2000/svg\"><!-- brim forward --></svg>";
    private const string CapBack = "<svg xmlns=\"http://www.w3.org/2000/svg\"><!-- brim back --></svg>";

    private static StickerAsset Asset(string name, string slot, IReadOnlyDictionary<string, ArtFile>? files, params string[] variants) =>
        new(new Sticker(StickerId.New(), name, slot, [new StickerPart("front", BodyRegion.Head, Art: new PartArt(ArtMapping.Warp))],
            new SortedDictionary<string, ColorValue>(), variants), files ?? new Dictionary<string, ArtFile>());

    private static (EditorSession Session, CharacterEditorViewModel Editor, FakeArtEditing Art, StickerAsset Cap, StickerAsset Hood, StickerAsset Beanie) NewCharacter()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var art = new FakeArtEditing();
        session.Characters.ArtEditing = art;
        var created = session.Characters.CreateCharacter();
        var editor = session.Characters.Items.Single(i => i.Id == created.Id).Editor;

        // A cap and a hood worn together in the headwear slot, and a one-style beanie in the wardrobe.
        var cap = Asset("Cap", StickerSlots.Headwear, new Dictionary<string, ArtFile>
        {
            ["variants/brim-front/front.svg"] = ArtFile.Svg(CapFront),
            ["variants/brim-back/front.svg"] = ArtFile.Svg(CapBack),
        }, "brim-front", "brim-back", "brim-left", "brim-right");
        var hood = Asset("Hood", StickerSlots.Headwear, null, "down", "up");
        var beanie = Asset("Beanie", StickerSlots.Accessory, null, "default");
        var character = editor.Committed;
        editor.Apply(EditResult<CharacterDefinition>.Success(character with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>>(character.Stickers, StringComparer.Ordinal)
            {
                [StickerSlots.Headwear] = [hood.Id, cap.Id],
                [StickerSlots.Accessory] = [beanie.Id],
            },
            Wardrobe = character.Wardrobe.With(cap).With(hood).With(beanie),
        }));
        return (session, editor, art, cap, hood, beanie);
    }

    private static string Shown(CharacterDefinition character, StickerId id)
    {
        var worn = CharacterLooks.Resolve(character).Stickers.Single(w => w.Asset.Id == id);
        return worn.Asset.Sticker.VariantFor(worn.Slot, null, worn.Variant);
    }

    [Fact]
    public void The_Worn_group_offers_each_style_previewed_on_the_character_and_a_click_is_one_undo_step()
    {
        var (session, editor, _, cap, hood, _) = NewCharacter();
        editor.SelectSticker(cap.Id);

        Assert.True(editor.HasSelectedStickerStyles);
        var styles = editor.SelectedStickerStyles;
        Assert.Equal(["Brim front", "Brim back", "Brim left", "Brim right"], styles.Select(s => s.Label));
        Assert.Equal("brim-front", Assert.Single(styles, s => s.IsCurrent).Variant);
        Assert.All(styles, s => Assert.Equal(s.Variant, Shown(s.Preview, cap.Id)));
        Assert.True(styles[0].Closeup);
        Assert.Equal("Brim front", editor.SelectedStickerStyleName);

        var raised = new List<string?>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        editor.SetStyleCommand.Execute(styles.Single(s => s.Variant == "brim-back"));

        Assert.Equal("brim-back", editor.Working.StickerVariants![cap.Id]);
        Assert.Equal("brim-back", Assert.Single(editor.SelectedStickerStyles, s => s.IsCurrent).Variant);
        Assert.Equal("Brim back", editor.SelectedStickerStyleName);
        Assert.Contains(nameof(CharacterEditorViewModel.SelectedStickerStyles), raised);
        Assert.Equal("down", Shown(editor.Working, hood.Id)); // the hood keeps its own

        // Picking the one it's already worn in changes nothing: no undo step.
        editor.SetStyleCommand.Execute(editor.SelectedStickerStyles.Single(s => s.IsCurrent));
        session.Workspace.History.Undo();
        Assert.Null(editor.Working.StickerVariants);
        Assert.Equal("brim-front", Assert.Single(editor.SelectedStickerStyles, s => s.IsCurrent).Variant);
        session.Workspace.History.Redo();

        // Back to its default way: the entry goes, so the file stays sparse.
        editor.SetStyleCommand.Execute(editor.SelectedStickerStyles.Single(s => s.Variant == "brim-front"));
        Assert.Null(editor.Working.StickerVariants);
    }

    [Fact]
    public void The_Worn_group_is_hidden_for_a_face_a_one_style_sticker_and_one_not_worn()
    {
        var (_, editor, _, cap, _, beanie) = NewCharacter();
        var eyes = editor.Working.Stickers[StickerSlots.Eyes].Single();
        Assert.True(editor.Working.Wardrobe.Find(eyes)!.Sticker.Variants.Count > 1); // a face has variants: its expressions

        editor.SelectSticker(eyes);
        Assert.False(editor.HasSelectedStickerStyles);
        Assert.Empty(editor.SelectedStickerStyles);

        editor.SelectSticker(beanie.Id);
        Assert.False(editor.HasSelectedStickerStyles);

        editor.SelectSticker(cap.Id);
        Assert.True(editor.HasSelectedStickerStyles);
        editor.TakeOffSelectedCommand.Execute(null);
        Assert.False(editor.HasSelectedStickerStyles);
        Assert.Equal("", editor.SelectedStickerStyleName);
    }

    [Fact]
    public void In_a_named_look_a_style_lands_in_the_look_and_the_default_look_is_untouched()
    {
        var (session, editor, _, cap, hood, _) = NewCharacter();
        editor.NewLookCommand.Execute(null);
        var winter = editor.CurrentLook!.Value;
        editor.SelectSticker(hood.Id);

        editor.SetStyleCommand.Execute(editor.SelectedStickerStyles.Single(s => s.Variant == "up"));

        Assert.Null(editor.Working.StickerVariants);
        Assert.Equal(new Dictionary<StickerId, string> { [hood.Id] = "up" }, editor.Working.Revisions[winter].StickerVariantValues);
        Assert.Equal("up", Assert.Single(editor.SelectedStickerStyles, s => s.IsCurrent).Variant);
        editor.ShowLookCommand.Execute(editor.Looks.Single(l => l.Id is null));
        Assert.Equal("down", Assert.Single(editor.SelectedStickerStyles, s => s.IsCurrent).Variant);

        session.Workspace.History.Undo();
        Assert.Null(editor.Working.Revisions[winter].StickerVariantValues);
        Assert.Equal("brim-front", Shown(editor.Working, cap.Id));
    }

    [Fact]
    public void Draw_your_own_on_a_styled_sticker_edits_the_style_it_is_shown_in()
    {
        var (_, editor, art, cap, _, _) = NewCharacter();
        editor.SelectSticker(cap.Id);
        editor.SetStyleCommand.Execute(editor.SelectedStickerStyles.Single(s => s.Variant == "brim-back"));

        editor.EditSelectedArtCommand.Execute(null);

        var (fileName, text, _) = Assert.Single(art.Opened);
        Assert.Equal(CapBack, text);
        Assert.Contains("brim-back", fileName);
        Assert.Contains("Cap (brim back)", editor.Hint);
    }

    [Fact]
    public void This_panel_only_Style_changes_one_panel_as_one_undo_step_and_Back_to_the_look_clears_it()
    {
        var (session, editor, _, cap, hood, _) = NewCharacter();
        editor.SelectSticker(cap.Id);
        editor.SetStyleCommand.Execute(editor.SelectedStickerStyles.Single(s => s.Variant == "brim-back"));
        var page = session.Navigator.CurrentPage.Editor;
        var panel = page.Working.PanelOrder[0];
        var index = page.InsertCharacter(editor.CharacterId, panel);

        var styled = page.PanelStyles(panel, index);
        Assert.Equal(["Hood", "Cap"], styled.Select(s => s.Name));
        var capStyles = styled.Single(s => s.Sticker == cap.Id).Styles;
        Assert.Equal("brim-back", Assert.Single(capStyles, s => s.IsCurrent).Variant);
        Assert.Equal("Brim back", capStyles.Single(s => s.IsCurrent).Label);

        page.SetPanelStyle(panel, index, hood.Id, "up");
        page.SetPanelStyle(panel, index, cap.Id, "brim-back"); // as the look has it already: nothing to keep

        var overrides = page.Working.Panels[panel].CharacterInstances[index].Overrides!;
        Assert.Equal(new Dictionary<StickerId, string> { [hood.Id] = "up" }, overrides.StickerVariantOverrides);
        Assert.Equal("up", Assert.Single(page.PanelStyles(panel, index).Single(s => s.Sticker == hood.Id).Styles, s => s.IsCurrent).Variant);
        Assert.Null(editor.Working.StickerVariants!.GetValueOrDefault(hood.Id)); // the character itself is unchanged

        session.Workspace.History.Undo();
        Assert.Null(page.Working.Panels[panel].CharacterInstances[index].Overrides);
        session.Workspace.History.Redo();

        page.ClearPanelLook(panel, index);
        Assert.Null(page.Working.Panels[panel].CharacterInstances[index].Overrides);
        Assert.Equal("down", Assert.Single(page.PanelStyles(panel, index).Single(s => s.Sticker == hood.Id).Styles, s => s.IsCurrent).Variant);
    }
}
