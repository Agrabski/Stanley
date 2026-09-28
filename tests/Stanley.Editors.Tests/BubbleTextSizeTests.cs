using Stanley.EditorFramework;
using Stanley.Editing;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>Issue #66: a bubble's font size is absolute - it never shrinks to fit, so a bubble too small for its text grows instead, in the same undo step as whatever asked for more room.</summary>
public class BubbleTextSizeTests
{
    private static readonly Rect2D PanelBounds = new(10, 10, 120, 100);

    private static (PageEditorViewModel Editor, EditorHistory History, PanelId Panel) NewEditor(Rect2D? panelBounds = null)
    {
        var history = new EditorHistory();
        var panelId = PanelId.New();
        var panel = new Panel(panelId, PanelShapes.Rectangle(panelBounds ?? PanelBounds), null, [], []);
        var document = new PageDocument([panelId], new Dictionary<PanelId, Panel> { [panelId] = panel });
        return (new PageEditorViewModel(history, new Rect2D(0, 0, 210, 297), document), history, panelId);
    }

    private static Rect2D BoundsOf(PageEditorViewModel editor, PanelId panel, int index) =>
        AnchorRing.BoundingBox(editor.Working.Panels[panel].Bubbles[index].Shape.Anchors);

    [Fact]
    public void Setting_long_text_grows_the_bubble_in_one_undo_step()
    {
        var (editor, history, panel) = NewEditor();
        var index = editor.CreateBubble(panel, new Point2D(60, 60));
        var before = BoundsOf(editor, panel, index);
        var longText = string.Join(" ", Enumerable.Repeat("a very long line of dialogue that keeps going", 6));

        editor.SetBubbleText(panel, index, longText);

        var after = BoundsOf(editor, panel, index);
        Assert.True(after.Width > before.Width + 1e-6 || after.Height > before.Height + 1e-6, "a bubble this full of text should have grown");
        Assert.Equal(longText, editor.Working.Panels[panel].Bubbles[index].Text);

        history.Undo(); // one step undoes both the text and the growth it caused
        Assert.Equal("", editor.Working.Panels[panel].Bubbles[index].Text);
        var reverted = BoundsOf(editor, panel, index);
        Assert.Equal(before.Width, reverted.Width, 6);
        Assert.Equal(before.Height, reverted.Height, 6);
    }

    [Fact]
    public void Setting_short_text_does_not_shrink_or_grow_the_bubble()
    {
        var (editor, _, panel) = NewEditor();
        var index = editor.CreateBubble(panel, new Point2D(60, 60));
        var before = BoundsOf(editor, panel, index);

        editor.SetBubbleText(panel, index, "Hi");

        var after = BoundsOf(editor, panel, index);
        Assert.Equal(before.Width, after.Width, 6);
        Assert.Equal(before.Height, after.Height, 6);
    }

    [Fact]
    public void Growing_never_shrinks_a_bubble_the_user_made_bigger_by_hand()
    {
        var (editor, _, panel) = NewEditor();
        var index = editor.CreateBubble(panel, new Point2D(60, 60));
        editor.BeginResizeBubble(panel, index);
        editor.UpdateResizeBubble(panel, index, new Rect2D(20, 20, 90, 70)); // deliberately bigger than the text needs
        editor.EndGesture(commit: true);
        var enlarged = BoundsOf(editor, panel, index);

        editor.SetBubbleText(panel, index, "Hi");

        var after = BoundsOf(editor, panel, index);
        Assert.Equal(enlarged.Width, after.Width, 6);
        Assert.Equal(enlarged.Height, after.Height, 6);
    }

    [Fact]
    public void Raising_the_font_size_with_the_size_box_grows_the_selected_bubble_in_one_undo_step()
    {
        var (editor, history, panel) = NewEditor();
        var index = editor.CreateBubble(panel, new Point2D(60, 60));
        editor.SetBubbleText(panel, index, string.Join(" ", Enumerable.Repeat("word", 15)));
        var before = BoundsOf(editor, panel, index);

        editor.SetTextSizeCommand.Execute("48"); // Word's largest listed size

        var after = BoundsOf(editor, panel, index);
        Assert.Equal(48, editor.Working.Panels[panel].Bubbles[index].FontSizePt);
        Assert.True(after.Width > before.Width + 1e-6 || after.Height > before.Height + 1e-6, "48pt text should need more room than the default size did");

        history.Undo();
        Assert.Null(editor.Working.Panels[panel].Bubbles[index].FontSizePt);
        var reverted = BoundsOf(editor, panel, index);
        Assert.Equal(before.Width, reverted.Width, 6);
        Assert.Equal(before.Height, reverted.Height, 6);
    }

    [Fact]
    public void Grow_Font_grows_the_selected_bubble_when_the_bigger_size_needs_more_room()
    {
        var (editor, _, panel) = NewEditor();
        var index = editor.CreateBubble(panel, new Point2D(60, 60));
        editor.SetBubbleText(panel, index, string.Join(" ", Enumerable.Repeat("word", 15)));
        var before = BoundsOf(editor, panel, index);

        for (var i = 0; i < 6; i++) // Grow Font, repeatedly, like clicking the ribbon button
            editor.BiggerTextCommand.Execute(null);

        var after = BoundsOf(editor, panel, index);
        Assert.True(editor.Working.Panels[panel].Bubbles[index].FontSizePt > Bubble.DefaultFontSizePt);
        Assert.True(after.Width > before.Width + 1e-6 || after.Height > before.Height + 1e-6);
    }

    [Fact]
    public void A_bubble_grown_bigger_than_its_panel_is_capped_to_fit_it()
    {
        var smallPanelBounds = new Rect2D(10, 10, 45, 30); // barely bigger than the default bubble
        var (editor, _, panel) = NewEditor(smallPanelBounds);
        var index = editor.CreateBubble(panel, new Point2D(smallPanelBounds.MidX, smallPanelBounds.MidY));
        var maxText = string.Join(" ", Enumerable.Repeat("word", 150))[..BubbleEditing.MaxTextLength];

        editor.SetBubbleText(panel, index, maxText);

        var after = BoundsOf(editor, panel, index);
        // It wanted to grow well past the panel - capped to exactly fill it, not left to spill outside.
        Assert.Equal(smallPanelBounds.Width, after.Width, 3);
        Assert.Equal(smallPanelBounds.Height, after.Height, 3);
        Assert.True(after.Left >= smallPanelBounds.Left - 1e-6 && after.Right <= smallPanelBounds.Right + 1e-6);
        Assert.True(after.Top >= smallPanelBounds.Top - 1e-6 && after.Bottom <= smallPanelBounds.Bottom + 1e-6);
    }
}
