using SkiaSharp;
using Stanley.Editing;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>Copy, Cut, Paste, Duplicate and Alt+drag on a page, through the page editor's view model.</summary>
public sealed class ClipboardTests
{
    /// <summary>A comic with two panels side by side, the same wiring the app uses, with a clipboard of its own (not the app-wide one other tests could be using).</summary>
    private static (EditorSession Session, PageEditorViewModel Page, PanelId Left, PanelId Right) NewSession(PageClipboard? clipboard = null)
    {
        var session = PageEditorHost.CreateWorkspace(ComicProject.CreateNew());
        var page = session.Navigator.CurrentPage.Editor;
        page.Clipboard = clipboard ?? new PageClipboard();
        page.SplitPanel(page.Working.PanelOrder[0], BoundaryOrientation.Vertical, 0.5);
        return (session, page, page.Working.PanelOrder[0], page.Working.PanelOrder[1]);
    }

    private static Rect2D Box(Bubble bubble) => AnchorRing.BoundingBox(bubble.Shape.Anchors);

    private static ArtFile Png(SKColor color)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return ArtFile.Png(data.ToArray());
    }

    [Fact]
    public void A_copied_bubble_pastes_beside_itself_with_its_words_and_style_selected_in_one_undo_step()
    {
        var (session, page, left, _) = NewSession();
        var bounds = page.PanelBounds(left);
        page.CreateBubble(left, new Point2D(bounds.MidX, bounds.MidY));
        page.SetBubbleText(left, 0, "Hello!");
        page.SetBubbleStyle(left, 0, BubbleStylePreset.Shout);
        Assert.False(page.CanPaste);

        Assert.True(page.Copy());
        Assert.True(page.CanPaste);
        Assert.True(page.Paste());

        var bubbles = page.Working.Panels[left].Bubbles;
        Assert.Equal(2, bubbles.Count);
        Assert.Equal(("Hello!", BubbleStylePreset.Shout), (bubbles[1].Text, bubbles[1].Style));
        Assert.NotEqual(bubbles[0].Id, bubbles[1].Id);
        Assert.Equal(Box(bubbles[0]).Left + Clippings.CascadeStepMm, Box(bubbles[1]).Left, 6);
        Assert.Equal(Box(bubbles[0]).Top + Clippings.CascadeStepMm, Box(bubbles[1]).Top, 6);
        Assert.Equal(1, page.SelectedBubbleIndex);

        // Pasting again steps it once more, so a row of pastes fans out.
        page.Paste();
        Assert.Equal(Box(bubbles[0]).Left + 2 * Clippings.CascadeStepMm, Box(page.Working.Panels[left].Bubbles[2]).Left, 6);

        session.Workspace.History.Undo();
        session.Workspace.History.Undo();
        Assert.Single(page.Working.Panels[left].Bubbles);
    }

    [Fact]
    public void Paste_goes_into_the_selected_panel_where_it_sat_in_the_one_it_came_from()
    {
        var (_, page, left, right) = NewSession();
        var from = page.PanelBounds(left);
        page.CreateBubble(left, new Point2D(from.Left + 25, from.Top + 20));
        var original = Box(page.Working.Panels[left].Bubbles[0]);
        page.Copy();

        page.Select(right);
        Assert.True(page.Paste());

        var to = page.PanelBounds(right);
        var pasted = Box(Assert.Single(page.Working.Panels[right].Bubbles));
        Assert.Equal(original.Top, pasted.Top, 6); // same row: the panels are side by side
        Assert.Equal((original.MidX - from.Left) / from.Width, (pasted.MidX - to.Left) / to.Width, 6);
        Assert.Equal(right, page.SelectedPanelId);
        Assert.Equal(0, page.SelectedBubbleIndex);
    }

    [Fact]
    public void Cut_takes_it_off_the_page_and_paste_puts_it_back_where_it_was()
    {
        var (_, page, left, _) = NewSession();
        var bounds = page.PanelBounds(left);
        page.CreateText(left, new Point2D(bounds.MidX, bounds.MidY));
        page.SetElementText(left, 0, "Meanwhile...");
        var before = PanelElements.Bounds(page.Working.Panels[left].Elements[0]);

        Assert.True(page.Cut());
        Assert.Empty(page.Working.Panels[left].Elements);

        page.Select(left);
        Assert.True(page.Paste());
        var text = Assert.IsType<TextElement>(Assert.Single(page.Working.Panels[left].Elements));
        Assert.Equal("Meanwhile...", text.Text);
        Assert.Equal(before, text.Bounds); // nothing in the way, so no stepping aside
        Assert.True(page.HasSelectedText);
    }

    [Fact]
    public void Duplicate_puts_a_copy_beside_it_and_leaves_the_clipboard_alone()
    {
        var (_, page, left, _) = NewSession();
        var bounds = page.PanelBounds(left);
        page.CreateBubble(left, new Point2D(bounds.MidX, bounds.MidY));
        page.Copy();
        page.Tool = PageEditorTool.Rectangle;
        page.BeginDrawShape(left);
        page.UpdateDrawShape(new Point2D(bounds.Left + 5, bounds.Top + 5), new Point2D(bounds.Left + 25, bounds.Top + 20));
        page.CommitDrawShape();

        Assert.True(page.Duplicate());

        var shapes = page.Working.Panels[left].Elements;
        Assert.Equal(2, shapes.Count);
        Assert.Equal(PanelElements.Bounds(shapes[0]).Left + Clippings.CascadeStepMm, PanelElements.Bounds(shapes[1]).Left, 6);
        Assert.Equal(1, page.SelectedElementIndex);
        Assert.IsType<BubbleClipping>(page.Clipboard.Content);
    }

    [Fact]
    public void A_copied_character_keeps_its_pose_and_steps_along_the_same_floor()
    {
        var (session, page, left, _) = NewSession();
        var character = session.Characters.CreateCharacter();
        page.InsertCharacter(character.Id, left);
        page.ApplyPosePreset(left, 0, PosePresets.All[1]);
        page.FlipCharacter(left, 0);

        page.Copy();
        Assert.True(page.Paste());

        var instances = page.Working.Panels[left].CharacterInstances;
        Assert.Equal(2, instances.Count);
        Assert.Equal(instances[0].Pose, instances[1].Pose);
        Assert.True(instances[1].Placement.Mirrored);
        Assert.Equal(instances[0].Placement.Ground.Y, instances[1].Placement.Ground.Y, 6);
        Assert.NotEqual(instances[0].Placement.Ground.X, instances[1].Placement.Ground.X);
        Assert.Equal(1, page.SelectedCharacterIndex);
    }

    [Fact]
    public void A_character_from_another_comic_isnt_pasted()
    {
        var clipboard = new PageClipboard();
        var (session, page, left, _) = NewSession(clipboard);
        page.InsertCharacter(session.Characters.CreateCharacter().Id, left);
        page.Copy();

        var (_, other, otherLeft, _) = NewSession(clipboard);
        other.Select(otherLeft);

        Assert.False(other.Paste());
        Assert.Empty(other.Working.Panels[otherLeft].CharacterInstances);
        Assert.NotNull(other.LastError);
    }

    [Fact]
    public void A_whole_panel_pastes_onto_the_page_with_everything_in_it_under_new_ids()
    {
        var (session, page, left, _) = NewSession();
        var bounds = page.PanelBounds(left);
        page.CreateBubble(left, new Point2D(bounds.MidX, bounds.MidY));
        page.SetPanelBackground(left, DrawingPalette.Backgrounds.Single(b => b.Name == "Night").Background);
        page.Select(left);

        page.Copy();
        Assert.True(page.Paste());

        Assert.Equal(3, page.Working.PanelOrder.Count);
        var copyId = page.Working.PanelOrder[^1];
        var copy = page.Working.Panels[copyId];
        Assert.Equal(copyId, page.SelectedPanelId);
        Assert.True(page.IsPanelContext);
        Assert.Equal(page.Working.Panels[left].Background, copy.Background);
        Assert.NotEqual(page.Working.Panels[left].Bubbles[0].Id, Assert.Single(copy.Bubbles).Id);
        Assert.Equal(bounds.Left + Clippings.CascadeStepMm, page.PanelBounds(copyId).Left, 6);

        session.Workspace.History.Undo();
        Assert.Equal(2, page.Working.PanelOrder.Count);
    }

    [Fact]
    public void A_panel_doesnt_paste_onto_a_locked_layout_but_what_goes_in_one_does()
    {
        var (_, page, left, right) = NewSession();
        page.Select(left);
        page.Copy();
        page.IsLayoutLocked = true;

        Assert.False(page.CanPaste);
        Assert.False(page.Paste());
        Assert.Equal(2, page.Working.PanelOrder.Count);

        page.IsLayoutLocked = false;
        var bounds = page.PanelBounds(left);
        page.CreateBubble(left, new Point2D(bounds.MidX, bounds.MidY));
        page.Copy();
        page.IsLayoutLocked = true;
        Assert.True(page.PasteInto(right));
        Assert.Single(page.Working.Panels[right].Bubbles);
    }

    [Fact]
    public void A_copy_of_the_title_pages_title_is_just_text_not_the_title()
    {
        var (_, page, left, _) = NewSession();
        var title = new TextElement(TitlePages.TitleId, ElementLayer.Foreground, new Rect2D(20, 20, 60, 15), "{title}", TextStylePresets.Style(TextStylePreset.Plain));
        page.Apply(Stanley.Editing.Abstractions.EditResult<PageDocument>.Success(page.Working with
        {
            Panels = new Dictionary<PanelId, Panel>(page.Working.Panels) { [left] = page.Working.Panels[left] with { Elements = [title] } }
        }));
        page.SelectElement(left, 0);

        page.Duplicate();

        Assert.Single(page.Working.Panels[left].Elements, e => e.Id == TitlePages.TitleId);
    }

    [Fact]
    public void A_picture_copied_from_one_comic_comes_along_into_another()
    {
        var clipboard = new PageClipboard();
        var (session, page, left, _) = NewSession(clipboard);
        page.ImportPicture(new PictureImportRequest(left, AsBackground: false), "tree.png", Png(SKColors.Green));
        page.Copy();

        var (otherSession, other, otherLeft, _) = NewSession(clipboard);
        other.Select(otherLeft);
        Assert.True(other.Paste());

        var picture = Assert.IsType<PictureElement>(Assert.Single(other.Working.Panels[otherLeft].Elements));
        Assert.True(otherSession.Pictures.Files.ContainsKey(picture.ArtFileName));
        Assert.Equal(session.Pictures.Files[picture.ArtFileName].Bytes, otherSession.Pictures.Files[picture.ArtFileName].Bytes);
    }

    [Fact]
    public void Copy_and_paste_work_across_pages_through_the_shared_clipboard()
    {
        var clipboard = new PageClipboard();
        var (session, page, left, _) = NewSession(clipboard);
        var bounds = page.PanelBounds(left);
        page.CreateBubble(left, new Point2D(bounds.MidX, bounds.MidY));
        page.SetBubbleText(left, 0, "Again?");
        page.Copy();

        session.Navigator.AddPageCommand.Execute(null);
        var second = session.Navigator.CurrentPage.Editor;
        second.Clipboard = clipboard;
        Assert.True(second.Paste());

        var panel = second.Working.Panels[second.Working.PanelOrder[0]];
        Assert.Equal("Again?", Assert.Single(panel.Bubbles).Text);
    }

    [Fact]
    public void Alt_dragging_a_bubble_moves_a_copy_away_and_leaves_the_original_in_one_undo_step()
    {
        var (session, page, left, _) = NewSession();
        var bounds = page.PanelBounds(left);
        page.CreateBubble(left, new Point2D(bounds.MidX, bounds.MidY));
        var original = page.Working.Panels[left].Bubbles[0];

        var copy = page.BeginDuplicateBubble(left, 0);
        Assert.Equal(1, copy);
        Assert.Equal(1, page.SelectedBubbleIndex);
        page.UpdateMoveBubble(left, copy, 10, 15);
        page.UpdateMoveBubble(left, copy, 20, 25);
        page.EndGesture(commit: true);

        var bubbles = page.Working.Panels[left].Bubbles;
        Assert.Equal(2, bubbles.Count);
        Assert.Equal(original, bubbles[0]);
        Assert.Equal(Box(original).Left + 20, Box(bubbles[1]).Left, 6);
        Assert.Equal(Box(original).Top + 25, Box(bubbles[1]).Top, 6);

        session.Workspace.History.Undo();
        Assert.Single(page.Working.Panels[left].Bubbles);
    }

    [Fact]
    public void Cancelling_an_Alt_drag_takes_the_copy_away_and_selects_the_original_again()
    {
        var (session, page, left, _) = NewSession();
        page.InsertCharacter(session.Characters.CreateCharacter().Id, left);

        var copy = page.BeginDuplicateCharacter(left, 0);
        page.UpdateMoveCharacter(left, copy, 30, 0, 0);
        page.EndGesture(commit: false);

        Assert.Single(page.Working.Panels[left].CharacterInstances);
        Assert.Equal(0, page.SelectedCharacterIndex);
        session.Workspace.History.Undo(); // the insert: the cancelled drag left nothing to undo
        Assert.Empty(page.Working.Panels[left].CharacterInstances);
    }

    [Fact]
    public void Alt_dragging_a_panel_moves_a_copy_of_it_and_the_original_stays()
    {
        var (_, page, left, _) = NewSession();
        var before = page.PanelBounds(left);

        var copy = page.BeginDuplicatePanel(left);
        Assert.NotNull(copy);
        page.UpdateMovePanel(copy.Value, 5, 8, snapTolerance: 0);
        page.EndGesture(commit: true);

        Assert.Equal(3, page.Working.PanelOrder.Count);
        Assert.Equal(before, page.PanelBounds(left));
        Assert.Equal(before.Top + 8, page.PanelBounds(copy.Value).Top, 6);
        Assert.Equal(copy, page.SelectedPanelId);
    }

    // ---------------------------------------------------------------- several things at once (Shift+click)

    /// <summary>A character and, above them, a bubble with a tail, both in <paramref name="panel"/> and selected together (the character last clicked).</summary>
    private static void SelectBubbleAndCharacter(EditorSession session, PageEditorViewModel page, PanelId panel)
    {
        page.InsertCharacter(session.Characters.CreateCharacter().Id, panel);
        var bounds = page.PanelBounds(panel);
        page.CreateBubble(panel, new Point2D(bounds.MidX, bounds.Top + bounds.Height * 0.25));
        page.AddBubbleTail(panel, 0);
        page.SetBubbleText(panel, 0, "Hi!");
        page.ToggleSelect(panel, characterIndex: 0);
        Assert.Equal(2, page.SelectionCount);
    }

    /// <summary>A bubble and a line of text, both in <paramref name="panel"/> and selected together.</summary>
    private static void SelectBubbleAndText(PageEditorViewModel page, PanelId panel)
    {
        var bounds = page.PanelBounds(panel);
        var text = page.CreateText(panel, new Point2D(bounds.Left + bounds.Width * 0.3, bounds.Top + bounds.Height * 0.2));
        page.SetElementText(panel, text, "Meanwhile...");
        var bubble = page.CreateBubble(panel, new Point2D(bounds.Left + bounds.Width * 0.6, bounds.Top + bounds.Height * 0.5));
        page.ToggleSelect(panel, elementIndex: text);
        Assert.Equal(2, page.SelectionCount);
        Assert.Equal(0, bubble);
    }

    private static Rect2D CharacterBox(PageEditorViewModel page, PanelId panel, int index) =>
        page.CharacterBounds(page.Working.Panels[panel].CharacterInstances[index]);

    [Fact]
    public void A_bubble_and_a_character_copied_together_paste_together_keeping_their_places_in_one_undo_step()
    {
        var (session, page, left, _) = NewSession();
        SelectBubbleAndCharacter(session, page, left);
        var character = page.Working.Panels[left].CharacterInstances[0].CharacterId;

        Assert.True(page.Copy());
        var clipping = Assert.IsType<SelectionClipping>(page.Clipboard.Content);
        Assert.Equal((1, 1, 0), (clipping.Bubbles.Count, clipping.Characters.Count, clipping.Elements.Count));
        Assert.True(page.Paste());

        var panel = page.Working.Panels[left];
        Assert.Equal(2, panel.Bubbles.Count);
        Assert.Equal(2, panel.CharacterInstances.Count);
        Assert.Equal("Hi!", panel.Bubbles[1].Text);
        Assert.Equal(character, panel.CharacterInstances[1].CharacterId);
        Assert.NotEqual(panel.Bubbles[0].Id, panel.Bubbles[1].Id);

        // One shared offset - sideways, with a character among them, as for a lone character - and the bubble's tail went with it.
        var dx = Box(panel.Bubbles[1]).Left - Box(panel.Bubbles[0]).Left;
        Assert.True(dx > 0);
        Assert.Equal(dx, CharacterBox(page, left, 1).Left - CharacterBox(page, left, 0).Left, 6);
        Assert.Equal(Box(panel.Bubbles[0]).Top, Box(panel.Bubbles[1]).Top, 6);
        Assert.Equal(CharacterBox(page, left, 0).Top, CharacterBox(page, left, 1).Top, 6);
        Assert.Equal(panel.Bubbles[0].Tails[0].Target.X + dx, panel.Bubbles[1].Tails[0].Target.X, 6);
        Assert.Equal(panel.Bubbles[0].Tails[0].Target.Y, panel.Bubbles[1].Tails[0].Target.Y, 6);

        // The copies are the selection now, and only they are.
        Assert.True(page.HasMultiSelection);
        Assert.Equal(2, page.SelectionCount);
        Assert.True(page.IsPartOfSelection(left, bubbleIndex: 1));
        Assert.True(page.IsPartOfSelection(left, characterIndex: 1));
        Assert.False(page.IsPartOfSelection(left, bubbleIndex: 0));
        Assert.False(page.IsPartOfSelection(left, characterIndex: 0));

        session.Workspace.History.Undo();
        Assert.Single(page.Working.Panels[left].Bubbles);
        Assert.Single(page.Working.Panels[left].CharacterInstances);
    }

    [Fact]
    public void Pasting_the_same_selection_again_steps_it_once_more_so_a_row_of_pastes_fans_out()
    {
        var (session, page, left, _) = NewSession();
        SelectBubbleAndCharacter(session, page, left);
        page.Copy();

        page.Paste();
        page.Paste();

        var panel = page.Working.Panels[left];
        Assert.Equal(3, panel.Bubbles.Count);
        Assert.Equal(3, panel.CharacterInstances.Count);
        Assert.Equal(Box(panel.Bubbles[0]).Left + 2 * Clippings.CascadeStepMm, Box(panel.Bubbles[2]).Left, 6);
        Assert.Equal(CharacterBox(page, left, 0).Left + 2 * Clippings.CascadeStepMm, CharacterBox(page, left, 2).Left, 6);
        Assert.True(page.IsPartOfSelection(left, bubbleIndex: 2));
        Assert.True(page.IsPartOfSelection(left, characterIndex: 2));
        Assert.Equal(2, page.SelectionCount);
    }

    [Fact]
    public void Duplicate_copies_everything_selected_beside_it_and_leaves_the_clipboard_alone()
    {
        var (session, page, left, _) = NewSession();
        SelectBubbleAndText(page, left);
        var bubbleBefore = Box(page.Working.Panels[left].Bubbles[0]);
        var textBefore = PanelElements.Bounds(page.Working.Panels[left].Elements[0]);

        Assert.True(page.Duplicate());

        var panel = page.Working.Panels[left];
        Assert.Equal(2, panel.Bubbles.Count);
        Assert.Equal(2, panel.Elements.Count);
        // No character among them, so they step down and across.
        Assert.Equal(bubbleBefore.Left + Clippings.CascadeStepMm, Box(panel.Bubbles[1]).Left, 6);
        Assert.Equal(bubbleBefore.Top + Clippings.CascadeStepMm, Box(panel.Bubbles[1]).Top, 6);
        Assert.Equal(textBefore.Left + Clippings.CascadeStepMm, PanelElements.Bounds(panel.Elements[1]).Left, 6);
        Assert.Equal(textBefore.Top + Clippings.CascadeStepMm, PanelElements.Bounds(panel.Elements[1]).Top, 6);
        Assert.Equal("Meanwhile...", Assert.IsType<TextElement>(panel.Elements[1]).Text);
        Assert.True(page.IsPartOfSelection(left, bubbleIndex: 1));
        Assert.True(page.IsPartOfSelection(left, elementIndex: 1));
        Assert.Equal(2, page.SelectionCount);
        Assert.Null(page.Clipboard.Content);

        session.Workspace.History.Undo();
        Assert.Single(page.Working.Panels[left].Bubbles);
        Assert.Single(page.Working.Panels[left].Elements);
    }

    [Fact]
    public void Cut_takes_the_whole_selection_off_in_one_undo_step_and_paste_puts_it_back_where_it_was()
    {
        var (session, page, left, _) = NewSession();
        SelectBubbleAndText(page, left);
        var bubbleBefore = Box(page.Working.Panels[left].Bubbles[0]);
        var textBefore = PanelElements.Bounds(page.Working.Panels[left].Elements[0]);

        Assert.True(page.Cut());
        Assert.Empty(page.Working.Panels[left].Bubbles);
        Assert.Empty(page.Working.Panels[left].Elements);

        page.Select(left);
        Assert.True(page.Paste());
        var panel = page.Working.Panels[left];
        // Nothing in the way, so no stepping aside (to rounding: refitting maps through the panel's fractions).
        var bubbleAfter = Box(Assert.Single(panel.Bubbles));
        var textAfter = PanelElements.Bounds(Assert.Single(panel.Elements));
        Assert.Equal(bubbleBefore.Left, bubbleAfter.Left, 6);
        Assert.Equal(bubbleBefore.Top, bubbleAfter.Top, 6);
        Assert.Equal(textBefore.Left, textAfter.Left, 6);
        Assert.Equal(textBefore.Top, textAfter.Top, 6);
        Assert.Equal(2, page.SelectionCount);

        session.Workspace.History.Undo(); // the paste
        Assert.Empty(page.Working.Panels[left].Bubbles);
        session.Workspace.History.Undo(); // the cut, all of it in one step
        Assert.Single(page.Working.Panels[left].Bubbles);
        Assert.Single(page.Working.Panels[left].Elements);
    }

    [Fact]
    public void A_selection_pasted_into_another_panel_keeps_its_places_relative_to_each_other_and_to_the_panel()
    {
        var (_, page, left, right) = NewSession();
        SelectBubbleAndText(page, left);
        var from = page.PanelBounds(left);
        var bubble = Box(page.Working.Panels[left].Bubbles[0]);
        var text = PanelElements.Bounds(page.Working.Panels[left].Elements[0]);
        page.Copy();

        page.Select(right);
        Assert.True(page.Paste());

        var to = page.PanelBounds(right);
        var pasted = page.Working.Panels[right];
        var pastedBubble = Box(Assert.Single(pasted.Bubbles));
        var pastedText = PanelElements.Bounds(Assert.Single(pasted.Elements));
        Assert.Equal((bubble.MidX - from.Left) / from.Width, (pastedBubble.MidX - to.Left) / to.Width, 6);
        Assert.Equal((text.MidY - from.Top) / from.Height, (pastedText.MidY - to.Top) / to.Height, 6);
        Assert.Equal(text.MidX - bubble.MidX, pastedText.MidX - pastedBubble.MidX, 6); // the panels are the same size
        Assert.Equal(text.MidY - bubble.MidY, pastedText.MidY - pastedBubble.MidY, 6);
        Assert.Equal(right, page.SelectedPanelId);
        Assert.Equal(2, page.SelectionCount);
        Assert.Single(page.Working.Panels[left].Bubbles); // the originals stay
    }

    [Fact]
    public void A_character_this_comic_hasnt_got_is_left_out_of_a_pasted_selection_with_a_note()
    {
        var clipboard = new PageClipboard();
        var (session, page, left, _) = NewSession(clipboard);
        SelectBubbleAndCharacter(session, page, left);
        page.Copy();

        var (_, other, otherLeft, _) = NewSession(clipboard);
        other.Select(otherLeft);
        Assert.True(other.Paste());

        var panel = other.Working.Panels[otherLeft];
        Assert.Equal("Hi!", Assert.Single(panel.Bubbles).Text);
        Assert.Empty(panel.CharacterInstances);
        Assert.Equal(0, other.SelectedBubbleIndex);
        Assert.Contains("isn't in this comic", other.Hint);
    }

    [Fact]
    public void A_selection_of_only_characters_this_comic_hasnt_got_isnt_pasted()
    {
        var clipboard = new PageClipboard();
        var (session, page, left, _) = NewSession(clipboard);
        page.InsertCharacter(session.Characters.CreateCharacter().Id, left);
        page.InsertCharacter(session.Characters.CreateCharacter().Id, left);
        page.ToggleSelect(left, characterIndex: 0);
        page.Copy();

        var (_, other, otherLeft, _) = NewSession(clipboard);
        other.Select(otherLeft);

        Assert.False(other.Paste());
        Assert.Empty(other.Working.Panels[otherLeft].CharacterInstances);
        Assert.Contains("aren't in this comic", other.LastError);
    }

    [Fact]
    public void The_pictures_of_a_copied_selection_come_along_into_another_comic()
    {
        var clipboard = new PageClipboard();
        var (session, page, left, _) = NewSession(clipboard);
        page.ImportPicture(new PictureImportRequest(left, AsBackground: false), "tree.png", Png(SKColors.Green));
        var bounds = page.PanelBounds(left);
        page.CreateBubble(left, new Point2D(bounds.MidX, bounds.MidY));
        page.ToggleSelect(left, elementIndex: 0);
        page.Copy();

        var (otherSession, other, otherLeft, _) = NewSession(clipboard);
        other.Select(otherLeft);
        Assert.True(other.Paste());

        var panel = other.Working.Panels[otherLeft];
        Assert.Single(panel.Bubbles);
        var picture = Assert.IsType<PictureElement>(Assert.Single(panel.Elements));
        Assert.True(otherSession.Pictures.Files.ContainsKey(picture.ArtFileName));
        Assert.Equal(session.Pictures.Files[picture.ArtFileName].Bytes, otherSession.Pictures.Files[picture.ArtFileName].Bytes);
    }

    [Fact]
    public void A_copied_group_pastes_as_a_group_of_its_own_and_the_original_stays_one()
    {
        var (session, page, left, _) = NewSession();
        SelectBubbleAndCharacter(session, page, left);
        page.GroupSelectionCommand.Execute(null);
        var link = page.Working.Panels[left].Bubbles[0].Link;
        Assert.NotNull(link);

        page.Copy();
        page.Paste();

        var panel = page.Working.Panels[left];
        Assert.Equal(link, panel.Bubbles[0].Link);
        Assert.Equal(link, panel.CharacterInstances[0].Link);
        Assert.NotNull(panel.Bubbles[1].Link);
        Assert.NotEqual(link, panel.Bubbles[1].Link);
        Assert.Equal(panel.Bubbles[1].Link, panel.CharacterInstances[1].Link);

        // Clicking one of the copies picks all of the copies, and none of the originals.
        page.ClearSelection();
        Assert.True(page.SelectGroupOf(left, bubbleIndex: 1));
        Assert.Equal(2, page.SelectionCount);
        Assert.True(page.IsPartOfSelection(left, characterIndex: 1));
        Assert.False(page.IsPartOfSelection(left, characterIndex: 0));
    }

    [Fact]
    public void A_selection_pasted_into_a_panel_stacked_by_hand_leaves_the_stacking_it_has_alone()
    {
        var (session, page, left, _) = NewSession();
        SelectBubbleAndCharacter(session, page, left);
        page.MoveInStack(left, new StackItem(StackKind.Bubble, 0), StackMove.ToBack); // behind the character
        var before = page.Working.Panels[left];
        Assert.NotNull(before.Stack);
        page.Copy();

        Assert.True(page.Paste());

        var panel = page.Working.Panels[left];
        Assert.Equal(before.Stack, panel.Stack);
        Assert.Null(panel.CharacterInstances[1].Id); // no id of the original's to be stacked under
        var order = PanelStack.Order(panel).ToList();
        Assert.Equal(4, order.Count);
        Assert.True(order.IndexOf(new StackItem(StackKind.Bubble, 0)) < order.IndexOf(new StackItem(StackKind.Character, 0)));
    }

    [Fact]
    public void A_selection_pastes_into_a_panel_of_a_locked_layout()
    {
        var (session, page, left, right) = NewSession();
        SelectBubbleAndCharacter(session, page, left);
        page.Copy();
        page.IsLayoutLocked = true;

        Assert.True(page.CanPaste);
        Assert.True(page.PasteInto(right));

        var panel = page.Working.Panels[right];
        Assert.Single(panel.Bubbles);
        Assert.Single(panel.CharacterInstances);
    }
}
