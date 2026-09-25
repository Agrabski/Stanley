using Stanley.Editing;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>Named looks: edited in the character editor, picked per panel and per issue on the page.</summary>
public sealed class LooksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-looks-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static (EditorSession Session, CharacterEditorViewModel Editor, PageEditorViewModel Page) NewSession()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var created = session.Characters.CreateCharacter();
        var editor = session.Characters.Items.Single(i => i.Id == created.Id).Editor;
        return (session, editor, session.Navigator.CurrentPage.Editor);
    }

    private static StickerChoice Choice(CharacterEditorViewModel editor, string slot, string name) =>
        editor.Gallery(slot).Choices.Single(c => c.Label == name);

    [Fact]
    public void The_look_tab_edits_the_look_being_shown_and_the_default_stays_as_it_was()
    {
        var (session, editor, _) = NewSession();
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Top, "T-shirt"));

        editor.NewLookCommand.Execute(null);
        var winter = editor.CurrentLook!.Value;
        editor.CurrentLookName = "Winter";
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Outer, "Coat"));
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Headwear, "Beanie"));

        Assert.Equal("Winter", editor.CurrentLookName);
        Assert.Equal("Coat", editor.Gallery(StickerSlots.Outer).Current);
        Assert.Equal([StickerSlots.Headwear, StickerSlots.Outer], editor.Working.Revisions[winter].ActiveStickers.Keys);
        Assert.False(editor.Working.Stickers.ContainsKey(StickerSlots.Outer));
        Assert.Contains(editor.LookWorking.Stickers[StickerSlots.Outer], id => editor.Working.Wardrobe.Find(id)?.Sticker.Name == "Coat");

        editor.ShowLookCommand.Execute(editor.Looks.Single(l => l.Id is null));
        Assert.Equal("None", editor.Gallery(StickerSlots.Outer).Current);
        Assert.Equal("T-shirt", editor.Gallery(StickerSlots.Top).Current);
        Assert.Equal(["Default", "Winter"], editor.Looks.Select(l => l.Name));

        session.Workspace.History.Undo(); // the beanie
        Assert.False(editor.Working.Revisions[winter].ActiveStickers.ContainsKey(StickerSlots.Headwear));
    }

    [Fact]
    public void Panels_pick_a_look_or_follow_the_issue_and_a_look_in_use_stays()
    {
        var (session, editor, page) = NewSession();
        editor.NewLookCommand.Execute(null);
        var winter = editor.CurrentLook!.Value;
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Headwear, "Beanie"));
        var panel = page.Working.PanelOrder[0];
        page.InsertCharacter(editor.CharacterId, panel);

        Assert.True(page.SelectedCharacterHasLooks);
        Assert.Equal("Default", page.SelectedLookName);
        page.SetIssueLookCommand.Execute(page.IssueLookChoices.Single(c => c.Look == winter));
        Assert.Equal(winter, session.Navigator.IssueLooks[editor.CharacterId]);
        Assert.Equal(winter, page.LookOf(page.Working.Panels[panel].CharacterInstances[0]));
        Assert.False(editor.CanDeleteCurrentLook); // the issue uses it

        page.SetPanelLookCommand.Execute(page.PanelLookChoices.Single(c => c.Name == "Default"));
        Assert.Equal(CharacterLooks.DefaultLook, page.Working.Panels[panel].CharacterInstances[0].RevisionOverride);
        Assert.Equal("Default (panel)", page.SelectedLookName);
        Assert.Null(page.LookOf(page.Working.Panels[panel].CharacterInstances[0]) is { } l && editor.Working.Revisions.ContainsKey(l) ? l : null);

        var folder = ComicProject.CreateNew().SaveAs(_root, session.Navigator.Snapshot(), null, session.Characters.Snapshot(), session.Navigator.IssueLooks);
        var reopened = ComicProject.Open(folder);
        Assert.Equal(winter, reopened.IssueLooks[editor.CharacterId]);
        Assert.Equal(CharacterLooks.DefaultLook, reopened.Pages[0].Document.Panels.Values.SelectMany(p => p.CharacterInstances).Single().RevisionOverride);
        Assert.Equal("Beanie", reopened.Characters.Single().Wardrobe.Stickers.Values.Single(a => a.Sticker.Slot == StickerSlots.Headwear).Sticker.Name); // worn only in the look: kept

        session.Workspace.History.Undo();
        session.Workspace.History.Undo();
        Assert.False(session.Navigator.IssueLooks.ContainsKey(editor.CharacterId));
        Assert.True(editor.CanDeleteCurrentLook);
        editor.DeleteLookCommand.Execute(null);
        Assert.Empty(editor.Working.Revisions);
        Assert.Null(editor.CurrentLook);
    }

    [Fact]
    public void This_panel_only_takes_something_off_without_touching_the_look()
    {
        var (session, editor, page) = NewSession();
        editor.WearCommand.Execute(Choice(editor, StickerSlots.Headwear, "Beanie"));
        var panel = page.Working.PanelOrder[0];
        page.InsertCharacter(editor.CharacterId, panel);
        var beanie = editor.Working.Stickers[StickerSlots.Headwear].Single();

        page.EditPanelLook(panel, 0, c => LookEditing.TakeOff(c, beanie));
        page.EditPanelLook(panel, 0, c => LookEditing.SetColor(c, "skin", ColorValue.FromHex("#8d5a3b")));

        var instance = page.Working.Panels[panel].CharacterInstances[0];
        Assert.Empty(instance.Overrides!.ActiveStickerOverrides![StickerSlots.Headwear]);
        Assert.Equal(ColorValue.FromHex("#8d5a3b"), instance.Overrides.ColorSlotOverrides!["skin"]);
        Assert.DoesNotContain(CharacterLooks.Resolve(page.PanelView(panel, 0)!).Stickers, w => w.Asset.Id == beanie);
        Assert.Contains(beanie, editor.Working.Stickers[StickerSlots.Headwear]);

        page.ClearPanelLook(panel, 0);
        Assert.Null(page.Working.Panels[panel].CharacterInstances[0].Overrides);
        session.Workspace.History.Undo();
        Assert.NotNull(page.Working.Panels[panel].CharacterInstances[0].Overrides);
    }
}
