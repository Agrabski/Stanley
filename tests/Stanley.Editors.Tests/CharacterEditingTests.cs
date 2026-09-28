using Dock.Model.Core;
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
    public void A_character_turns_to_a_side_view_in_place_undoably_and_the_view_is_saved()
    {
        var (session, page, left, _) = NewSession();
        var item = Add(session, "A", BodyPresets.Shape(BodyPreset.Heavy));
        page.InsertCharacter(item.Id, left);
        var ground = page.Working.Panels[left].CharacterInstances[0].Placement.Ground;
        var frontBounds = page.CharacterBounds(page.Working.Panels[left].CharacterInstances[0]);

        page.IsSelectedCharacterSide = true;

        var turned = page.Working.Panels[left].CharacterInstances[0];
        Assert.Equal(ViewAngle.Profile, turned.Pose.ViewAngle);
        Assert.Equal(ground, turned.Placement.Ground);
        Assert.True(page.IsSelectedCharacterSide);
        Assert.False(page.IsSelectedCharacterFront);
        Assert.NotEqual(frontBounds, page.CharacterBounds(turned)); // hit-testing and handles follow the new outline
        Assert.Equal(frontBounds.Height, page.CharacterBounds(turned).Height, 6);

        var saved = ComicProject.CreateNew().SaveAs(_root, session.Navigator.Snapshot(), null, session.Characters.Snapshot());
        var reopened = ComicProject.Open(saved).Pages[0].Document.Panels.Values.SelectMany(p => p.CharacterInstances).Single();
        Assert.Equal(ViewAngle.Profile, reopened.Pose.ViewAngle);

        session.Workspace.History.Undo();
        Assert.Equal(ViewAngle.Front, page.Working.Panels[left].CharacterInstances[0].Pose.ViewAngle);
        Assert.True(page.IsSelectedCharacterFront);
    }

    [Fact]
    public void Dragging_a_hand_poses_the_arm_in_one_undo_step_moves_its_bounds_and_is_saved()
    {
        var (session, page, left, _) = NewSession();
        var item = Add(session, "A");
        page.InsertCharacter(item.Id, left);
        var instance = page.Working.Panels[left].CharacterInstances[0];
        var handle = page.LimbHandles(instance).Single(h => h.Limb == Limb.RightArm).Point;
        var box = page.CharacterBounds(instance);
        var raised = new Point2D(handle.X - 5, box.Top - 2); // hand up over the head

        page.BeginPoseLimb(left, 0, Limb.RightArm);
        page.UpdatePoseLimb(left, 0, Limb.RightArm, new Point2D(handle.X - 10, handle.Y - 10));
        page.UpdatePoseLimb(left, 0, Limb.RightArm, raised);
        page.EndGesture(commit: true);

        var posed = page.Working.Panels[left].CharacterInstances[0];
        Assert.True(page.SelectedCharacterIsPosed);
        Assert.True(page.CharacterBounds(posed).Top < box.Top, "the raised hand is inside the new selection box");
        var hand = page.LimbHandles(posed).Single(h => h.Limb == Limb.RightArm).Point;
        Assert.True(Math.Abs(hand.X - raised.X) < 0.1 && Math.Abs(hand.Y - raised.Y) < 0.1);

        var saved = ComicProject.CreateNew().SaveAs(_root, session.Navigator.Snapshot(), null, session.Characters.Snapshot());
        var reopened = ComicProject.Open(saved).Pages[0].Document.Panels.Values.SelectMany(p => p.CharacterInstances).Single();
        Assert.Equal(posed.Pose.BoneRotations, reopened.Pose.BoneRotations);

        page.ResetPoseCommand.Execute(null);
        Assert.Empty(page.Working.Panels[left].CharacterInstances[0].Pose.BoneRotations);
        session.Workspace.History.Undo();
        session.Workspace.History.Undo();
        Assert.Empty(page.Working.Panels[left].CharacterInstances[0].Pose.BoneRotations); // the whole drag was one step
    }

    [Fact]
    public void Expressions_are_close_ups_of_the_selected_character_and_apply_in_one_undo_step()
    {
        var (session, page, left, _) = NewSession();
        var item = Add(session, "A");
        page.InsertCharacter(item.Id, left);

        var choices = page.ExpressionChoices;
        Assert.Equal(ExpressionPresets.All.Count, choices.Count);
        Assert.True(choices.Single(c => c.Preset.Preset == ExpressionPreset.Neutral).IsCurrent);
        Assert.Equal("Neutral", page.SelectedExpressionName);

        page.ApplyExpressionCommand.Execute(choices.Single(c => c.Preset.Preset == ExpressionPreset.Laughing));
        var laughing = page.Working.Panels[left].CharacterInstances[0].Pose;
        Assert.Equal("grin", laughing.Expression[StickerSlots.Mouth]);
        Assert.Empty(laughing.BoneRotations); // the face only
        Assert.Equal("Laughing", page.SelectedExpressionName);
        Assert.True(page.ExpressionChoices.Single(c => c.Preset.Preset == ExpressionPreset.Laughing).IsCurrent);

        var saved = ComicProject.CreateNew().SaveAs(_root, session.Navigator.Snapshot(), null, session.Characters.Snapshot());
        var reopened = ComicProject.Open(saved).Pages[0].Document.Panels.Values.SelectMany(p => p.CharacterInstances).Single();
        Assert.Equal(laughing.Expression, reopened.Pose.Expression);

        session.Workspace.History.Undo();
        Assert.Equal("Neutral", page.SelectedExpressionName);
    }

    [Fact]
    public void Mixing_your_own_expression_sets_one_face_slot_at_a_time_each_one_undo_step()
    {
        var (session, page, left, _) = NewSession();
        var item = Add(session, "A");
        page.InsertCharacter(item.Id, left);
        page.ApplyExpressionCommand.Execute(page.ExpressionChoices.Single(c => c.Preset.Preset == ExpressionPreset.Happy));

        var mixer = page.ExpressionMixer;
        Assert.Equal(["Eyes", "Brows", "Mouth"], mixer.Select(r => r.Label));
        Assert.Equal("happy", mixer[0].Choices.Single(c => c.IsCurrent).Variant);
        var open = mixer[2].Choices.Single(c => c.Variant == "open");
        Assert.Equal("happy", open.Pose.Expression[StickerSlots.Eyes]); // previewed with the rest of the face as it is
        page.SetExpressionVariantCommand.Execute(open);

        var face = page.Working.Panels[left].CharacterInstances[0].Pose.Expression;
        Assert.Equal("happy", face[StickerSlots.Eyes]);
        Assert.Equal("open", face[StickerSlots.Mouth]);
        Assert.Equal("Custom", page.SelectedExpressionName);
        Assert.True(page.ExpressionMixer[2].Choices.Single(c => c.Variant == "open").IsCurrent);
        Assert.DoesNotContain(page.ExpressionChoices, c => c.IsCurrent);

        session.Workspace.History.Undo();
        Assert.Equal("Happy", page.SelectedExpressionName);
    }

    [Fact]
    public void A_face_saved_on_the_page_is_the_characters_to_use_in_any_panel_and_undoes_as_a_page_step()
    {
        var (session, page, left, right) = NewSession();
        var item = Add(session, "Pip");
        page.InsertCharacter(item.Id, right);
        page.InsertCharacter(item.Id, left);
        page.ApplyExpressionCommand.Execute(page.ExpressionChoices.Single(c => c.Preset.Preset == ExpressionPreset.Happy));
        page.SetExpressionVariantCommand.Execute(page.ExpressionMixer.Single(r => r.Slot == StickerSlots.Mouth).Choices.Single(c => c.Variant == "open"));
        Assert.False(page.HasSavedFaces);
        Assert.False(page.SaveFaceCommand.CanExecute(null)); // nothing to call it yet

        page.NewFaceName = "Grinning";
        page.SaveFaceCommand.Execute(null);

        Assert.Equal("", page.NewFaceName);
        var face = Assert.Single(item.Editor.Committed.Expressions!);
        Assert.Equal(("happy", "open"), (face.Variants[StickerSlots.Eyes], face.Variants[StickerSlots.Mouth]));
        Assert.Equal("Grinning", page.SelectedExpressionName);
        Assert.Equal("Pip's faces", page.SavedFacesTitle);
        Assert.True(Assert.Single(page.SavedFaceChoices).IsCurrent);

        // Another panel: one click.
        page.SelectCharacter(right, 0);
        Assert.Equal("Neutral", page.SelectedExpressionName);
        page.SavedFaceChoices.Single().Apply.Execute(null);
        Assert.Equal("open", page.Working.Panels[right].CharacterInstances[0].Pose.Expression[StickerSlots.Mouth]);
        Assert.Equal("Grinning", page.SelectedExpressionName);

        var saved = ComicProject.CreateNew().SaveAs(_root, session.Navigator.Snapshot(), null, session.Characters.Snapshot());
        Assert.Equal("Grinning", Assert.Single(ComicProject.Open(saved).Characters.Single().Expressions!).Name);

        // Deleting it leaves the panels their faces.
        page.SavedFaceChoices.Single().Delete.Execute(null);
        Assert.Null(item.Editor.Committed.Expressions);
        Assert.Equal("Custom", page.SelectedExpressionName);

        session.Workspace.History.Undo(); // the delete
        session.Workspace.History.Undo(); // the face on the right
        session.Workspace.History.Undo(); // saving it
        Assert.Null(item.Editor.Committed.Expressions);
        Assert.Same(page, session.Workspace.ActiveEditor); // undone on the page, not in the character's editor
    }

    [Fact]
    public void A_new_expression_drawn_from_the_page_starts_as_the_one_shown_is_worn_and_each_save_comes_back()
    {
        var (session, page, left, _) = NewSession();
        var art = new FakeArtEditing();
        session.Characters.ArtEditing = art;
        var item = Add(session, "Pip");
        page.InsertCharacter(item.Id, left);
        page.ApplyExpressionCommand.Execute(page.ExpressionChoices.Single(c => c.Preset.Preset == ExpressionPreset.Happy));
        var mouthRow = page.ExpressionMixer.Single(r => r.Slot == StickerSlots.Mouth);
        Assert.True(mouthRow.CanDraw);

        mouthRow.DrawNew.Execute(null);

        var mouth = item.Editor.Committed.Wardrobe.Stickers.Values.Single(a => a.Sticker.Slot == StickerSlots.Mouth);
        Assert.Equal("myMouth", mouth.Sticker.Variants[^1]);
        Assert.Equal(mouth.Files["variants/smile/front.svg"].Text, mouth.Files["variants/myMouth/front.svg"].Text);
        Assert.Equal("myMouth", page.Working.Panels[left].CharacterInstances[0].Pose.Expression[StickerSlots.Mouth]);
        Assert.Equal("My mouth", page.ExpressionMixer.Single(r => r.Slot == StickerSlots.Mouth).Choices.Single(c => c.IsCurrent).Name);
        var (_, text, save) = Assert.Single(art.Opened);
        Assert.Equal(mouth.Files["variants/myMouth/front.svg"].Text, text);
        Assert.Contains("/drawing/", page.Hint);

        var redrawn = text.Replace("</svg>", "<!-- mine --></svg>");
        save(redrawn);
        Assert.Equal(redrawn, item.Editor.Committed.Wardrobe.Find(mouth.Id)!.Files["variants/myMouth/front.svg"].Text);
        Assert.Same(page, session.Workspace.ActiveEditor);

        session.Workspace.History.Undo(); // the save
        Assert.Equal(text, item.Editor.Committed.Wardrobe.Find(mouth.Id)!.Files["variants/myMouth/front.svg"].Text);
        session.Workspace.History.Undo(); // the new mouth and wearing it: one step
        Assert.DoesNotContain("myMouth", item.Editor.Committed.Wardrobe.Find(mouth.Id)!.Sticker.Variants);
        Assert.Equal("smile", page.Working.Panels[left].CharacterInstances[0].Pose.Expression[StickerSlots.Mouth]);
        Assert.Same(page, session.Workspace.ActiveEditor);

        // The pen redraws the one shown.
        page.ExpressionMixer.Single(r => r.Slot == StickerSlots.Mouth).EditDrawing.Execute(null);
        Assert.Equal(mouth.Files["variants/smile/front.svg"].Text, art.Opened[^1].Text);
    }

    [Fact]
    public void Placing_a_character_from_the_pane_puts_it_on_the_page_and_closes_the_tab_the_first_click_opened()
    {
        var (session, page, _, _) = NewSession();
        var item = Add(session, "A");
        session.Characters.Show(item); // a double-click's first click opens it
        Assert.Same(item.Editor, session.Workspace.ActiveEditor);

        session.Characters.PlaceOnPage(item);

        Assert.Same(page, session.Workspace.ActiveEditor);
        Assert.Null(session.Characters.Current);
        Assert.DoesNotContain(item.Editor, Dockables(session));
        var placed = Assert.Single(page.Working.Panels.Values.SelectMany(p => p.CharacterInstances));
        Assert.Equal(item.Id, placed.CharacterId);
        Assert.True(page.IsCharacterContext);

        // Another character's tab, open in the background, stays open.
        var other = Add(session, "B");
        session.Characters.Show(other);
        session.Characters.ReturnToPage();
        session.Characters.Show(item);
        session.Characters.Show(other);
        session.Characters.PlaceOnPage(other);
        Assert.Contains(item.Editor, Dockables(session));
        Assert.DoesNotContain(other.Editor, Dockables(session));
    }

    private static IEnumerable<IDockable> Dockables(EditorSession session)
    {
        var pending = new Stack<IDockable>([session.Workspace.Layout]);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            yield return next;
            if (next is IDock { VisibleDockables: { } children })
                foreach (var child in children)
                    pending.Push(child);
        }
    }

    [Fact]
    public void The_character_editor_previews_an_expression_without_an_undo_step_and_says_what_a_face_lacks()
    {
        var (session, _, _, _) = NewSession();
        var item = Add(session, "A");
        var editor = item.Editor;
        var before = editor.Working;

        editor.PreviewExpressionCommand.Execute(editor.PreviewExpressionChoices.Single(c => c.Preset.Preset == ExpressionPreset.Wink));

        Assert.Equal("wink", editor.StagePose?.Expression[StickerSlots.Eyes]);
        Assert.Same(before, editor.Working); // a preview, not an edit
        Assert.All(editor.FaceGalleries.SelectMany(g => g.Choices), c => Assert.Equal("wink", c.Pose?.Expression[StickerSlots.Eyes]));
        Assert.Null(editor.ExpressionWarning); // the default face draws every expression

        // A face with no wink says so, and shows its neutral instead.
        var sticker = new Sticker(StickerId.New(), "Plain", StickerSlots.Eyes, [new StickerPart("eyes", BodyRegion.Head, Art: new PartArt(ArtMapping.Pin))],
            new SortedDictionary<string, ColorValue>(), ["neutral"]);
        editor.WearCommand.Execute(new StickerChoice("Plain", StickerSlots.Eyes, editor.Working, new StickerAsset(sticker, new Dictionary<string, ArtFile>()), null, false));
        Assert.Contains("Plain", editor.ExpressionWarning);
        Assert.Contains("wink", editor.Hint);
        editor.SelectSticker(sticker.Id);
        Assert.Contains("No side view", editor.SelectedStickerWarning);
        Assert.Contains("expressions", editor.SelectedStickerWarning);
    }

    [Fact]
    public void Pose_presets_are_previewed_on_the_selected_character_and_apply_in_one_undo_step()
    {
        var (session, page, left, _) = NewSession();
        var item = Add(session, "A", BodyPresets.Shape(BodyPreset.Child));
        page.InsertCharacter(item.Id, left);

        var choices = page.PoseChoices;
        Assert.Equal(PosePresets.All.Count, choices.Count);
        Assert.All(choices, c => Assert.Same(page.CharacterSnapshot[item.Id], c.Character));

        page.ApplyPosePresetCommand.Execute(choices.Single(c => c.Preset.Preset == PosePreset.Walk));
        var walking = page.Working.Panels[left].CharacterInstances[0];
        Assert.Equal(ViewAngle.Profile, walking.Pose.ViewAngle); // walking reads side on
        Assert.True(page.IsSelectedCharacterSide);
        Assert.True(page.MirrorPoseCommand.CanExecute(null));

        page.MirrorPoseCommand.Execute(null);
        Assert.NotEqual(walking.Pose.BoneRotations, page.Working.Panels[left].CharacterInstances[0].Pose.BoneRotations);

        session.Workspace.History.Undo();
        session.Workspace.History.Undo();
        Assert.False(page.SelectedCharacterIsPosed);
        Assert.Equal(ViewAngle.Front, page.Working.Panels[left].CharacterInstances[0].Pose.ViewAngle);
    }

    [Fact]
    public void Dragging_the_hips_ring_crouches_with_the_feet_planted_in_one_undo_step()
    {
        var (session, page, left, _) = NewSession();
        var item = Add(session, "A");
        page.InsertCharacter(item.Id, left);
        var instance = page.Working.Panels[left].CharacterInstances[0];
        var hips = page.TrunkHandles(instance).Single(h => h.Part == TrunkPart.Hips).Point;
        var feet = page.LimbHandles(instance).Where(h => h.Limb is Limb.LeftLeg or Limb.RightLeg).Select(h => h.Point).ToList();

        page.BeginPoseTrunk(left, 0, TrunkPart.Hips);
        page.UpdatePoseTrunk(left, 0, TrunkPart.Hips, new Point2D(hips.X, hips.Y + 4), hips);
        page.UpdatePoseTrunk(left, 0, TrunkPart.Hips, new Point2D(hips.X, hips.Y + 9), hips);
        page.EndGesture(commit: true);

        var crouched = page.Working.Panels[left].CharacterInstances[0];
        Assert.Equal(hips.Y + 9, page.TrunkHandles(crouched).Single(h => h.Part == TrunkPart.Hips).Point.Y, 3);
        var after = page.LimbHandles(crouched).Where(h => h.Limb is Limb.LeftLeg or Limb.RightLeg).Select(h => h.Point).ToList();
        for (var i = 0; i < 2; i++)
            Assert.True(Math.Abs(after[i].X - feet[i].X) < 0.05 && Math.Abs(after[i].Y - feet[i].Y) < 0.05);

        session.Workspace.History.Undo();
        Assert.False(page.SelectedCharacterIsPosed);
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
