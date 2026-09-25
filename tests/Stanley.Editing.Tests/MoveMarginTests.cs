using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing.Tests;

/// <summary>A new page margin applied to a layout: edges on the old margin line follow it, nothing else moves.</summary>
public class MoveMarginTests
{
    private static readonly Rect2D Page = new(0, 0, 210, 297);

    private static Dictionary<PanelId, Panel> Tiled(params int[] columnsPerRow) =>
        PanelLayoutEditing.GridLayout(Page, PanelGrid.Default, columnsPerRow).Value
            .Select(r => new Panel(PanelId.New(), PanelShapes.Rectangle(r), null, [], []))
            .ToDictionary(p => p.Id);

    private static Rect2D Bounds(Panel panel) => AnchorRing.BoundingBox(panel.Shape.Anchors);

    [Fact]
    public void Outer_edges_follow_the_margin_and_gutters_stay_where_they_are()
    {
        var panels = Tiled(2, 2);
        var before = panels.Values.Select(Bounds).ToList();

        var moved = PanelLayoutEditing.MoveMargin(panels, Page, 10, 20).Values.Select(Bounds).ToList();

        Assert.Equal(20, moved.Min(b => b.Left), 6);
        Assert.Equal(190, moved.Max(b => b.Right), 6);
        Assert.Equal(20, moved.Min(b => b.Top), 6);
        Assert.Equal(277, moved.Max(b => b.Bottom), 6);
        // The gutters between the panels haven't moved.
        Assert.Equal(before.Where(b => b.Left > 50).Select(b => b.Left).Distinct(), moved.Where(b => b.Left > 50).Select(b => b.Left).Distinct());
        Assert.Equal(before.Where(b => b.Top > 50).Select(b => b.Top).Distinct(), moved.Where(b => b.Top > 50).Select(b => b.Top).Distinct());
    }

    [Fact]
    public void A_panel_carries_its_bubbles_along()
    {
        var panel = Tiled(1).Values.Single();
        var bubble = BubbleEditing.Create(new Rect2D(12, 12, 40, 20), BubbleStylePreset.Speech).Value;
        var panels = new Dictionary<PanelId, Panel> { [panel.Id] = panel with { Bubbles = [bubble] } };

        var moved = PanelLayoutEditing.MoveMargin(panels, Page, 10, 20)[panel.Id];

        var bubbleBounds = AnchorRing.BoundingBox(moved.Bubbles[0].Shape.Anchors);
        Assert.True(bubbleBounds.Left >= 20 - 1e-6 && bubbleBounds.Top >= 20 - 1e-6, "the bubble stays inside its panel");
    }

    [Fact]
    public void A_panel_off_the_margin_or_one_that_would_get_too_small_is_left_alone()
    {
        var bleed = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(0, 0, 210, 100)), null, [], []);
        var thin = new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(10, 150, 25, 60)), null, [], []);
        var panels = new Dictionary<PanelId, Panel> { [bleed.Id] = bleed, [thin.Id] = thin };

        var moved = PanelLayoutEditing.MoveMargin(panels, Page, 10, 20);

        Assert.Same(panels, moved); // nothing could move: a bleed and a 25mm panel that would shrink to 15
    }

    [Fact]
    public void The_same_margin_changes_nothing()
    {
        var panels = Tiled(3, 3);

        Assert.Same(panels, PanelLayoutEditing.MoveMargin(panels, Page, 10, 10));
    }
}
