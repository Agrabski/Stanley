using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Storage;

namespace Stanley.Editors.Tests;

public sealed class CharacterEditingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stanley-character-editing-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>A comic with two panels side by side, and its session - the same wiring the app uses.</summary>
    private static (EditorSession Session, PageEditorViewModel Page, PanelId Left, PanelId Right) NewSession()
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var page = session.Navigator.CurrentPage.Editor;
        page.SplitPanel(page.Working.PanelOrder[0], BoundaryOrientation.Vertical, 0.5);
        return (session, page, page.Working.PanelOrder[0], page.Working.PanelOrder[1]);
    }

    private static CharacterItem Add(EditorSession session, string name, BodyShape? body = null)
    {
        var created = session.Characters.CreateCharacter();
        var item = session.Characters.Items.Single(i => i.Id == created.Id);
        item.Editor.Name = name;
        if (body != null)
            item.Editor.SetBody(body);
        return item;
    }

    [Fact]
    public void A_second_character_in_a_panel_takes_the_first_ones_scale_so_relative_heights_are_true()
    {
        var (session, page, left, _) = NewSession();
        var adult = Add(session, "Adult");
        var child = Add(session, "Child", BodyPresets.Shape(BodyPreset.Child));

        page.InsertCharacter(adult.Id, left);
        page.InsertCharacter(child.Id, left);

        var placed = page.Working.Panels[left].CharacterInstances;
        Assert.Equal(2, placed.Count);
        Assert.Equal(placed[0].Placement.UnitHeightMm, placed[1].Placement.UnitHeightMm);
        Assert.Equal(placed[0].Placement.Ground.Y, placed[1].Placement.Ground.Y);
        var adultHeight = page.CharacterBounds(placed[0]).Height;
        var childHeight = page.CharacterBounds(placed[1]).Height;
        Assert.Equal(0.72, childHeight / adultHeight, 2);
        Assert.Equal(1, page.SelectedCharacterIndex);
        Assert.True(page.IsCharacterContext);
    }

    [Fact]
    public void Dragging_a_resize_handle_resizes_the_whole_panel_together_unless_done_alone()
    {
        var (session, page, left, _) = NewSession();
        var a = Add(session, "A");
        var b = Add(session, "B");
        page.InsertCharacter(a.Id, left);
        page.InsertCharacter(b.Id, left);
        var start = page.Working.Panels[left].CharacterInstances[0].Placement.UnitHeightMm;

        page.BeginResizeCharacter(left, 0);
        page.UpdateResizeCharacter(left, 0, start * 0.5, together: true);
        page.EndGesture(commit: true);
        Assert.All(page.Working.Panels[left].CharacterInstances, c => Assert.Equal(start * 0.5, c.Placement.UnitHeightMm, 6));

        page.BeginResizeCharacter(left, 0);
        page.UpdateResizeCharacter(left, 0, start * 0.8, together: false);
        page.EndGesture(commit: true);
        Assert.Equal([start * 0.8, start * 0.5], page.Working.Panels[left].CharacterInstances.Select(c => c.Placement.UnitHeightMm));

        page.SelectCharacter(left, 0);
        Assert.True(page.MatchCharacterSizeCommand.CanExecute(null));
        page.MatchCharacterSizeCommand.Execute(null);
        Assert.All(page.Working.Panels[left].CharacterInstances, c => Assert.Equal(start * 0.5, c.Placement.UnitHeightMm, 6));
    }

    [Fact]
    public void Moving_a_character_snaps_its_feet_to_the_others_floor()
    {
        var (session, page, left, _) = NewSession();
        var a = Add(session, "A");
        page.InsertCharacter(a.Id, left);
        page.InsertCharacter(a.Id, left);
        var floor = page.Working.Panels[left].CharacterInstances[0].Placement.Ground.Y;

        page.BeginMoveCharacter(left, 1);
        page.UpdateMoveCharacter(left, 1, -5, 1.5, snapTolerance: 2);
        page.EndGesture(commit: true);

        Assert.Equal(floor, page.Working.Panels[left].CharacterInstances[1].Placement.Ground.Y);
    }

    [Fact]
    public void A_body_slider_drag_previews_on_the_page_live_and_is_one_undo_step()
    {
        var (session, page, left, _) = NewSession();
        var item = Add(session, "Alice");
        page.InsertCharacter(item.Id, left);
        var snapshots = 0;
        page.PropertyChanged += (_, e) => snapshots += e.PropertyName == nameof(PageEditorViewModel.CharacterSnapshot) ? 1 : 0;

        item.Editor.BeginSliderDrag();
        item.Editor.HeightPercent = 120;
        item.Editor.HeightPercent = 140;
        item.Editor.Weight = 90;
        Assert.Equal(1.4, page.CharacterSnapshot[item.Id].Body.Height, 6); // the page draws the drag as it happens
        item.Editor.EndSliderDrag();
        Assert.True(snapshots >= 3);

        Assert.Equal(1.4, item.Editor.Committed.Body.Height, 6);
        Assert.Equal(0.9, item.Editor.Committed.Body.Build, 6);
        session.Workspace.History.Undo();
        Assert.Equal(BodyShape.Default, item.Editor.Committed.Body);
    }

    [Fact]
    public void A_preset_sets_every_body_value_and_undoing_a_body_edit_brings_its_editor_back()
    {
        var (session, _, _, _) = NewSession();
        var item = Add(session, "Hero");
        CharacterItem? shown = null;
        session.Characters.CharacterShown += i => shown = i;

        item.Editor.ApplyPresetCommand.Execute(item.Editor.Presets.Single(p => p.Preset == BodyPreset.Heroic));
        Assert.Equal(BodyPresets.Shape(BodyPreset.Heroic), item.Editor.Committed.Body);

        session.Characters.ReturnToPage();
        session.Workspace.History.Undo();
        Assert.Same(item, shown);
        Assert.Same(item, session.Characters.Current);
    }

    [Fact]
    public void Opening_a_character_swaps_it_into_the_editor_area_and_Close_brings_the_page_back()
    {
        var (session, page, _, _) = NewSession();
        var item = Add(session, "A");

        session.Characters.OpenCharacter(item.Id);
        Assert.Same(item.Editor, session.Workspace.ActiveEditor);

        item.Editor.BackToPageCommand.Execute(null);
        Assert.Same(page, session.Workspace.ActiveEditor);
        Assert.Null(session.Characters.Current);
    }

    [Fact]
    public void A_placed_character_cant_be_deleted_until_its_removed_from_its_panels()
    {
        var (session, page, left, _) = NewSession();
        var item = Add(session, "A");
        page.InsertCharacter(item.Id, left);

        Assert.Equal(1, item.Usage);
        Assert.False(session.Characters.DeleteCharacterCommand.CanExecute(item));

        page.DeleteSelection();
        Assert.Equal(0, item.Usage);
        Assert.True(session.Characters.DeleteCharacterCommand.CanExecute(item));
        session.Characters.DeleteCharacterCommand.Execute(item);
        Assert.DoesNotContain(item, session.Characters.Items);

        session.Workspace.History.Undo();
        Assert.Contains(item, session.Characters.Items);
    }

    [Fact]
    public void New_character_from_the_page_ribbon_creates_and_places_one()
    {
        var (session, page, left, _) = NewSession();
        page.Select(left);

        page.NewCharacterCommand.Execute(null);

        var placed = Assert.Single(page.Working.Panels[left].CharacterInstances);
        Assert.Contains(session.Characters.Items, i => i.Id == placed.CharacterId);
    }

    [Fact]
    public void Save_then_open_round_trips_characters_and_their_placements_and_prunes_deleted_ones()
    {
        var (session, page, left, _) = NewSession();
        var kept = Add(session, "Kept", BodyPresets.Shape(BodyPreset.Strong));
        var dropped = Add(session, "Dropped");
        page.InsertCharacter(kept.Id, left);
        page.FlipCharacter(left, 0);

        var project = ComicProject.CreateNew();
        // Save the session's state through a project, as the window does.
        var saved = project.SaveAs(_root, session.Navigator.Snapshot(), session.Navigator.PageNumbering, session.Characters.Snapshot());
        Assert.Equal(2, new ProjectRepository(saved).ListCharacters().Count);

        session.Characters.DeleteCharacter(session.Characters.Items.Single(i => i.Id == dropped.Id));
        project.Save(session.Navigator.Snapshot(), session.Navigator.PageNumbering, session.Characters.Snapshot());

        var reopened = ComicProject.Open(saved);
        var character = Assert.Single(reopened.Characters);
        Assert.Equal("Kept", character.Name);
        Assert.Equal(BodyPresets.Shape(BodyPreset.Strong), character.Body);
        var instance = reopened.Pages[0].Document.Panels.Values.SelectMany(p => p.CharacterInstances).Single();
        Assert.Equal(kept.Id, instance.CharacterId);
        Assert.True(instance.Placement.Mirrored);
    }

    [Fact]
    public void Saving_without_characters_leaves_the_ones_on_disk_alone()
    {
        var repository = ProjectRepository.Initialize(_root, "Comic", new PageTrim(new PageSize(210, 297), 3));
        repository.SaveCharacter(CharacterDefinition.Create("Existing"));
        var project = ComicProject.Open(_root);

        project.Save(new PageNavigatorViewModel(new EditorHistory(), project.Pages).Snapshot());

        Assert.Single(new ProjectRepository(_root).ListCharacters());
    }
}
