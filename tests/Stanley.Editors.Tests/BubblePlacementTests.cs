using Stanley.EditorFramework;
using Stanley.Editing;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Editors.Tests;

/// <summary>Adding bubbles one after another, and moving one with or without its tail.</summary>
public sealed class BubblePlacementTests
{
    private static PageEditorViewModel NewEditor() =>
        new(new EditorHistory(), new Rect2D(0, 0, 210, 297), ComicProject.CreateNew().Pages[0].Document);

    private static Rect2D BoundsOf(PageEditorViewModel editor, int index) =>
        AnchorRing.BoundingBox(editor.Working.Panels[editor.Working.PanelOrder[0]].Bubbles[index].Shape.Anchors);

    [Fact]
    public void Adding_a_second_bubble_puts_it_beside_the_first_not_on_top_of_it()
    {
        var editor = NewEditor();

        editor.AddBubbleToSelectedPanel();
        editor.AddBubbleToSelectedPanel();
        editor.AddBubbleToSelectedPanel();

        var bounds = Enumerable.Range(0, 3).Select(i => BoundsOf(editor, i)).ToList();
        Assert.Equal(3, bounds.Select(b => (Math.Round(b.Left, 3), Math.Round(b.Top, 3))).Distinct().Count());
        Assert.Equal(BubbleEditing.CascadeStepMm, bounds[1].Left - bounds[0].Left, 6);
        Assert.Equal(BubbleEditing.CascadeStepMm, bounds[1].Top - bounds[0].Top, 6);
    }

    [Fact]
    public void Dragging_a_bubble_leaves_its_tail_on_the_speaker_but_Ctrl_takes_it_along()
    {
        var editor = NewEditor();
        var panel = editor.Working.PanelOrder[0];
        var index = editor.CreateBubble(panel, new Point2D(80, 80));
        var tip = editor.Working.Panels[panel].Bubbles[index].Tails[0].Target;

        editor.BeginMoveBubble(panel, index);
        editor.UpdateMoveBubble(panel, index, 20, 10);
        Assert.Equal(tip, editor.Working.Panels[panel].Bubbles[index].Tails[0].Target);

        editor.UpdateMoveBubble(panel, index, 20, 10, withTails: true); // Ctrl pressed mid-drag
        editor.EndGesture(commit: true);

        var moved = editor.Committed.Panels[panel].Bubbles[index].Tails[0].Target;
        Assert.Equal(tip.X + 20, moved.X, 6);
        Assert.Equal(tip.Y + 10, moved.Y, 6);
    }
}
