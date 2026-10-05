using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Editing.Tests;

public class PanelContentScaleTests
{
    private static readonly Rect2D PageBounds = new(0, 0, 210, 297);

    private static Panel NewPanel(Rect2D bounds) =>
        new(PanelId.New(), PanelShapes.Rectangle(bounds), Background: null, CharacterInstances: [], Bubbles: []);

    private static CharacterInstance StandingAt(double x, double y, double unit) =>
        new(CharacterId.New(), new CharacterPlacement(new Point2D(x, y), unit, Mirrored: false), null, new PoseData(ViewAngle.Front, [], []), null);

    private static Point2D Centre(Bubble bubble)
    {
        var box = AnchorRing.BoundingBox(bubble.Shape.Anchors);
        return new Point2D(box.MidX, box.MidY);
    }

    private static void Near(Point2D expected, Point2D actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }

    [Fact]
    public void A_corner_drag_scales_everything_about_the_corner_that_stayed_put()
    {
        var scale = PanelContentScale.Between(new Rect2D(10, 10, 100, 80), new Rect2D(10, 10, 50, 40));

        Assert.Equal(0.5, scale.Scale, 9);
        Near(new Point2D(10, 10), scale.Map(new Point2D(10, 10)));
        Near(new Point2D(60, 50), scale.Map(new Point2D(110, 90)));
    }

    [Fact]
    public void Widening_one_way_keeps_everything_its_size_and_where_it_was()
    {
        var right = PanelContentScale.Between(new Rect2D(10, 10, 100, 80), new Rect2D(10, 10, 160, 80));
        var left = PanelContentScale.Between(new Rect2D(60, 10, 100, 80), new Rect2D(10, 10, 150, 80));

        Assert.Equal(PanelContentScale.None, right);
        Assert.Equal(PanelContentScale.None, left);
    }

    [Fact]
    public void Narrowing_one_way_shrinks_the_picture_to_fit_against_the_edge_that_stayed_and_on_the_floor()
    {
        var scale = PanelContentScale.Between(new Rect2D(10, 10, 100, 80), new Rect2D(10, 10, 50, 80));

        Assert.Equal(0.5, scale.Scale, 9);
        Near(new Point2D(10, 90), scale.Map(new Point2D(10, 90))); // the old bottom-left corner: same wall, same floor
        Near(new Point2D(60, 50), scale.Map(new Point2D(110, 10)));
    }

    [Fact]
    public void Making_a_panel_shorter_centres_the_shrunk_picture_across_it()
    {
        var scale = PanelContentScale.Between(new Rect2D(0, 0, 100, 100), new Rect2D(0, 0, 100, 50));

        Assert.Equal(0.5, scale.Scale, 9);
        Near(new Point2D(25, 0), scale.Map(new Point2D(0, 0)));
        Near(new Point2D(75, 50), scale.Map(new Point2D(100, 100)));
    }

    [Fact]
    public void A_move_is_exactly_a_shift()
    {
        var scale = PanelContentScale.Between(new Rect2D(10.3, 20.7, 93.1, 100.9), new Rect2D(40.3, 5.7, 93.1, 100.9));

        Assert.Equal(1, scale.Scale);
        Near(new Point2D(30, -15), scale.Map(new Point2D(0, 0)));
    }

    [Fact]
    public void A_gutter_drag_that_only_widens_a_panel_leaves_its_lettering_alone()
    {
        var bubble = BubbleEditing.Create(new Rect2D(20.3, 20.9, 30, 20), BubbleStylePreset.Speech).Value;
        var left = NewPanel(new Rect2D(10.3, 10.9, 93.7, 100.3)) with { Bubbles = [bubble] };
        var right = NewPanel(new Rect2D(107.9, 10.9, 93.7, 100.3));
        var boundary = new PanelBoundaryDrag(BoundaryOrientation.Vertical, [left.Id], [right.Id], Gap: 4);

        var result = PanelLayoutEditing.DragBoundary(new Dictionary<PanelId, Panel> { [left.Id] = left, [right.Id] = right }, boundary, 120.1, PageBounds);

        Assert.True(result.IsValid);
        var kept = Assert.Single(result.Value[left.Id].Bubbles);
        Assert.Null(kept.FontSizePt);
        Near(Centre(bubble), Centre(kept));
    }

    [Fact]
    public void Resizing_a_panel_keeps_its_composition_together()
    {
        // A waist-up shot: the character's feet are below the frame, its speech bubble above
        // its head, a bush in front of it and a sign behind.
        var character = StandingAt(50, 130, 60);
        var bubble = BubbleEditing.AddTail(BubbleEditing.Create(new Rect2D(30, 15, 40, 20), BubbleStylePreset.Speech).Value, new Point2D(55, 45)).Value;
        var bush = ShapeEditing.Ellipse(new Rect2D(60, 70, 30, 20), ShapeEditing.DefaultStyle, ElementLayer.Foreground).Value;
        var sign = TextEditing.Create(new Rect2D(15, 40, 25, 10), TextStylePresets.Style(TextStylePreset.Plain), ElementLayer.Background, "EXIT").Value;
        var panel = NewPanel(new Rect2D(10, 10, 100, 80)) with { CharacterInstances = [character], Bubbles = [bubble], Elements = [bush, sign] };

        // Dragged shorter from the bottom edge: everything is drawn at 3/4 size.
        var resized = PanelLayoutEditing.Resize(panel, new Rect2D(10, 10, 100, 60), PageBounds).Value;

        var feet = resized.CharacterInstances[0].Placement.Ground;
        Point2D FromFeet(Point2D p) => new(p.X - feet.X, p.Y - feet.Y);
        Point2D Before(Point2D p) => new((p.X - 50) * 0.75, (p.Y - 130) * 0.75);

        Assert.Equal(45, resized.CharacterInstances[0].Placement.UnitHeightMm, 9);
        Near(Before(Centre(bubble)), FromFeet(Centre(resized.Bubbles[0])));
        Near(Before(bubble.Tails[0].Target), FromFeet(resized.Bubbles[0].Tails[0].Target));
        Assert.Equal(bubble.Tails[0].AttachmentT, resized.Bubbles[0].Tails[0].AttachmentT, 9);
        var bushBox = PanelElements.Bounds(resized.Elements[0]);
        Near(Before(new Point2D(75, 80)), FromFeet(new Point2D(bushBox.MidX, bushBox.MidY)));
        Assert.Equal(22.5, bushBox.Width, 6);
        var signBox = PanelElements.Bounds(resized.Elements[1]);
        Near(Before(new Point2D(27.5, 45)), FromFeet(new Point2D(signBox.MidX, signBox.MidY)));
    }

    [Fact]
    public void Lettering_scales_with_its_bubble_or_box_but_ink_keeps_its_weight()
    {
        var bubble = BubbleEditing.Create(new Rect2D(20, 20, 30, 20), BubbleStylePreset.Speech).Value with { FontSizePt = 12 };
        var effect = TextEditing.Create(new Rect2D(20, 50, 40, 15), TextStylePresets.Style(TextStylePreset.Caption) with { OutlineWidthMm = 0.6, BoxStrokeWidthMm = 0.5 }).Value;
        var line = ShapeEditing.Line(new Point2D(15, 80), new Point2D(90, 80), ShapeEditing.DefaultStyle with { StrokeWidthMm = 1.5 }, ElementLayer.Background).Value;
        var lines = SpeedLinesEditing.Place(new Rect2D(10, 10, 100, 80));
        var panel = NewPanel(new Rect2D(10, 10, 100, 80)) with { Bubbles = [bubble], Elements = [effect, line, lines] };

        var resized = PanelLayoutEditing.Resize(panel, new Rect2D(10, 10, 50, 40), PageBounds).Value;

        Assert.Equal(6, resized.Bubbles[0].FontSizePt);
        var text = Assert.IsType<TextElement>(resized.Elements[0]);
        Assert.Equal(5, text.Style.FontSizePt, 9);
        Assert.Equal(0.3, text.Style.OutlineWidthMm!.Value, 9);
        Assert.Equal(0.5, text.Style.BoxStrokeWidthMm, 9);
        var shape = Assert.IsType<ShapeElement>(resized.Elements[1]);
        Assert.Equal(1.5, shape.Style.StrokeWidthMm, 9);
        Assert.Equal(37.5, PanelElements.Bounds(shape).Width, 6); // a flat line still scales, and moves
        Assert.Equal(45, PanelElements.Bounds(shape).Top, 6);
        Assert.Equal(lines.Style, Assert.IsType<SpeedLinesElement>(resized.Elements[2]).Style);
    }

    [Fact]
    public void A_bubble_scaled_back_to_the_usual_size_says_nothing_about_its_size()
    {
        var bubble = BubbleEditing.Create(new Rect2D(20, 20, 30, 20), BubbleStylePreset.Speech).Value with { FontSizePt = 5 };

        Assert.Null(BubbleEditing.Scale(bubble, new PanelContentScale(2, 0, 0)).FontSizePt);
    }

    [Fact]
    public void A_groups_children_scale_together()
    {
        var a = ShapeEditing.Rectangle(new Rect2D(20, 20, 10, 10), ShapeEditing.DefaultStyle, ElementLayer.Background).Value;
        var b = ShapeEditing.Rectangle(new Rect2D(40, 30, 20, 10), ShapeEditing.DefaultStyle, ElementLayer.Background).Value;
        var group = Grouping.Group([a, b]).Value;

        var scaled = Assert.IsType<GroupElement>(ElementEditing.Scale(group, new PanelContentScale(0.5, 10, 10)));

        Assert.Equal(new Rect2D(20, 20, 5, 5), PanelElements.Bounds(scaled.Children[0]));
        Assert.Equal(new Rect2D(30, 25, 10, 5), PanelElements.Bounds(scaled.Children[1]));
    }

    [Fact]
    public void Shrinking_a_panel_and_growing_it_back_puts_everything_back()
    {
        var character = StandingAt(50, 130, 60);
        var bubble = BubbleEditing.AddTail(BubbleEditing.Create(new Rect2D(70, 15, 38, 20), BubbleStylePreset.Speech).Value, new Point2D(55, 45)).Value;
        var bush = ShapeEditing.Ellipse(new Rect2D(60, 70, 30, 20), ShapeEditing.DefaultStyle, ElementLayer.Foreground).Value;
        var panel = NewPanel(new Rect2D(10, 10, 100, 80)) with { CharacterInstances = [character], Bubbles = [bubble], Elements = [bush] };

        var shrunk = PanelLayoutEditing.Resize(panel, new Rect2D(10, 10, 60, 48), PageBounds).Value;
        var back = PanelLayoutEditing.Resize(shrunk, new Rect2D(10, 10, 100, 80), PageBounds).Value;

        Near(character.Placement.Ground, back.CharacterInstances[0].Placement.Ground);
        Assert.Equal(60, back.CharacterInstances[0].Placement.UnitHeightMm, 9);
        Near(Centre(bubble), Centre(back.Bubbles[0]));
        Near(bubble.Tails[0].Target, back.Bubbles[0].Tails[0].Target);
        Assert.Equal(Bubble.DefaultFontSizePt, back.Bubbles[0].FontSizePt ?? Bubble.DefaultFontSizePt, 9);
        Assert.Equal(PanelElements.Bounds(bush).X, PanelElements.Bounds(back.Elements[0]).X, 6);
        Assert.Equal(PanelElements.Bounds(bush).Width, PanelElements.Bounds(back.Elements[0]).Width, 6);
    }
}
