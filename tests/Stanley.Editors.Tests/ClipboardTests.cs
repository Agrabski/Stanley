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
}
