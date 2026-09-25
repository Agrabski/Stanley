using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Xunit;

namespace Stanley.Editing.Tests;

public class ShapeEditingTests
{
    private static readonly ShapeStyle Pen = ShapeEditing.DefaultStyle;

    /// <summary>A hand-drawn-looking trail: <paramref name="count"/> points around a circle, with a little wobble.</summary>
    private static List<Point2D> Circle(Point2D center, double radius, int count, double turns = 1)
    {
        var points = new List<Point2D>();
        for (var i = 0; i <= count; i++)
        {
            var a = Math.PI * 2 * turns * i / count;
            var wobble = 0.05 * Math.Sin(i * 1.7);
            points.Add(new Point2D(center.X + Math.Cos(a) * (radius + wobble), center.Y + Math.Sin(a) * (radius + wobble)));
        }
        return points;
    }

    [Fact]
    public void A_straight_drag_becomes_a_two_point_open_line()
    {
        var trail = Enumerable.Range(0, 50).Select(i => new Point2D(10 + i, 20 + i * 0.5)).ToList();

        var shape = ShapeEditing.Freehand(trail, Pen, ElementLayer.Background, toleranceMm: 0.2, closeDistanceMm: 2).Value;

        Assert.False(shape.Closed);
        Assert.Equal(2, shape.Anchors.Count);
        Assert.Equal(new Point2D(10, 20), shape.Anchors[0].Point);
        Assert.Equal(new Point2D(59, 44.5), shape.Anchors[1].Point);
    }

    [Fact]
    public void A_loop_that_ends_where_it_began_closes_into_a_smooth_ring()
    {
        var shape = ShapeEditing.Freehand(Circle(new Point2D(50, 50), 20, 200), Pen, ElementLayer.Background, toleranceMm: 0.3, closeDistanceMm: 2).Value;

        Assert.True(shape.Closed);
        Assert.InRange(shape.Anchors.Count, 4, 60); // simplified, not one anchor per pointer event
        Assert.Contains(shape.Anchors, a => a.HandleKind == AnchorHandleKind.Smooth);
        var box = AnchorRing.BoundingBox(shape.Anchors);
        Assert.InRange(box.Width, 38, 42);
        Assert.InRange(box.Height, 38, 42);
    }

    [Fact]
    public void The_smoothed_curve_stays_close_to_the_drawn_trail()
    {
        var trail = Circle(new Point2D(50, 50), 20, 200, turns: 0.75); // an open arc
        var shape = ShapeEditing.Freehand(trail, Pen, ElementLayer.Background, toleranceMm: 0.3, closeDistanceMm: 2).Value;

        Assert.False(shape.Closed);
        for (var s = 0; s + 1 < shape.Anchors.Count; s++)
        {
            for (var t = 0.0; t <= 1; t += 0.25)
            {
                var p = Cubic(shape.Anchors[s], shape.Anchors[s + 1], t);
                var r = Math.Sqrt((p.X - 50) * (p.X - 50) + (p.Y - 50) * (p.Y - 50));
                Assert.InRange(r, 19, 21);
            }
        }
    }

    [Fact]
    public void A_sharp_bend_stays_a_corner()
    {
        var trail = Enumerable.Range(0, 30).Select(i => new Point2D(10 + i, 10.0))
            .Concat(Enumerable.Range(1, 30).Select(i => new Point2D(39, 10.0 + i))).ToList();

        var shape = ShapeEditing.Freehand(trail, Pen, ElementLayer.Background, 0.2, 2).Value;

        Assert.Equal(3, shape.Anchors.Count);
        Assert.Equal(AnchorHandleKind.Corner, shape.Anchors[1].HandleKind);
        Assert.Equal(shape.Anchors[1].Point, shape.Anchors[1].InHandle);
    }

    [Fact]
    public void A_short_scribble_near_its_start_does_not_close()
    {
        var trail = new List<Point2D> { new(10, 10), new(11, 10.5), new(11.2, 11), new(10.4, 10.6) };

        var shape = ShapeEditing.Freehand(trail, Pen, ElementLayer.Background, 0.05, 2).Value;

        Assert.False(shape.Closed);
    }

    [Fact]
    public void A_click_without_a_drag_draws_nothing()
    {
        Assert.False(ShapeEditing.Freehand([new Point2D(5, 5), new Point2D(5.1, 5)], Pen, ElementLayer.Background, 0.2, 2).IsValid);
        Assert.False(ShapeEditing.Freehand([], Pen, ElementLayer.Background, 0.2, 2).IsValid);
    }

    [Fact]
    public void Rectangles_and_ellipses_fill_their_box()
    {
        var box = new Rect2D(10, 20, 30, 15);

        var rectangle = ShapeEditing.Rectangle(box, Pen, ElementLayer.Foreground).Value;
        var ellipse = ShapeEditing.Ellipse(box, Pen, ElementLayer.Foreground).Value;

        Assert.True(rectangle.Closed);
        Assert.Equal(box, AnchorRing.BoundingBox(rectangle.Anchors));
        Assert.Equal(box, AnchorRing.BoundingBox(ellipse.Anchors));
        Assert.Equal(ElementLayer.Foreground, ellipse.Layer);
        Assert.False(ShapeEditing.Rectangle(new Rect2D(0, 0, 0.2, 10), Pen, ElementLayer.Foreground).IsValid);
    }

    [Fact]
    public void Resize_stretches_the_anchors_into_the_new_box_and_a_flat_line_keeps_its_height()
    {
        var shape = ShapeEditing.Ellipse(new Rect2D(0, 0, 10, 10), Pen, ElementLayer.Background).Value;
        var resized = ShapeEditing.Resize(shape, new Rect2D(5, 5, 20, 40)).Value;
        Assert.Equal(new Rect2D(5, 5, 20, 40), AnchorRing.BoundingBox(resized.Anchors));

        var line = ShapeEditing.Line(new Point2D(0, 10), new Point2D(20, 10), Pen, ElementLayer.Background).Value;
        var longer = ShapeEditing.Resize(line, new Rect2D(0, 10, 40, 5)).Value;
        Assert.Equal(new Point2D(40, 10), longer.Anchors[1].Point);
        Assert.Equal(10, longer.Anchors[0].Point.Y);

        Assert.False(ShapeEditing.Resize(shape, new Rect2D(0, 0, 0.1, 10)).IsValid);
    }

    [Fact]
    public void Move_shifts_points_and_handles_alike()
    {
        var shape = ShapeEditing.Ellipse(new Rect2D(0, 0, 10, 10), Pen, ElementLayer.Background).Value;

        var moved = ShapeEditing.Move(shape, 3, -2);

        for (var i = 0; i < shape.Anchors.Count; i++)
        {
            Assert.Equal(shape.Anchors[i].Point.X + 3, moved.Anchors[i].Point.X, 9);
            Assert.Equal(shape.Anchors[i].OutHandle.Y - 2, moved.Anchors[i].OutHandle.Y, 9);
        }
    }

    [Fact]
    public void A_line_thicker_than_the_limit_is_refused()
    {
        var shape = ShapeEditing.Ellipse(new Rect2D(0, 0, 10, 10), Pen, ElementLayer.Background).Value;

        Assert.False(ShapeEditing.SetStyle(shape, Pen with { StrokeWidthMm = 100 }).IsValid);
        Assert.Equal(2, ShapeEditing.SetStyle(shape, Pen with { StrokeWidthMm = 2 }).Value.Style.StrokeWidthMm);
    }

    private static Point2D Cubic(ShapeAnchor from, ShapeAnchor to, double t)
    {
        var mt = 1 - t;
        double a = mt * mt * mt, b = 3 * mt * mt * t, c = 3 * mt * t * t, d = t * t * t;
        return new Point2D(
            a * from.Point.X + b * from.OutHandle.X + c * to.InHandle.X + d * to.Point.X,
            a * from.Point.Y + b * from.OutHandle.Y + c * to.InHandle.Y + d * to.Point.Y);
    }
}

public class TextEditingTests
{
    [Fact]
    public void Presets_give_a_boxed_caption_bare_text_and_an_outlined_sound_effect()
    {
        var caption = TextStylePresets.Style(TextStylePreset.Caption);
        var plain = TextStylePresets.Style(TextStylePreset.Plain);
        var effect = TextStylePresets.Style(TextStylePreset.SoundEffect);

        Assert.NotNull(caption.BoxFill);
        Assert.NotNull(caption.BoxStroke);
        Assert.Null(plain.BoxFill);
        Assert.NotNull(effect.Outline);
        Assert.True(effect.Bold);
        Assert.True(effect.FontSizeMm > plain.FontSizeMm);
    }

    [Fact]
    public void A_style_is_recognised_as_its_preset_at_any_size_but_not_once_changed()
    {
        var caption = TextStylePresets.Style(TextStylePreset.Caption);

        Assert.Equal(TextStylePreset.Caption, TextStylePresets.Of(caption));
        Assert.Equal(TextStylePreset.Caption, TextStylePresets.Of(caption with { FontSizeMm = 6 }));
        Assert.Equal(TextStylePreset.Caption, TextStylePresets.Of(caption with { FontFamily = "DejaVu Serif" }));
        Assert.Null(TextStylePresets.Of(caption with { Bold = true }));
    }

    [Fact]
    public void Font_names_are_trimmed_blank_means_the_default_and_nonsense_is_refused()
    {
        var text = TextEditing.Create(new Rect2D(0, 0, 30, 5), TextStylePresets.Style(TextStylePreset.Plain)).Value;

        Assert.Equal("Comic Neue", TextEditing.SetStyle(text, text.Style with { FontFamily = "  Comic Neue " }).Value.Style.FontFamily);
        Assert.Null(TextEditing.SetStyle(text, text.Style with { FontFamily = " " }).Value.Style.FontFamily);
        Assert.False(TextEditing.SetStyle(text, text.Style with { FontFamily = "Two\nLines" }).IsValid);
        Assert.False(TextEditing.CleanFontName(new string('x', TextEditing.MaxFontNameLength + 1)).IsValid);

        var bubble = BubbleEditing.Create(new Rect2D(0, 0, 40, 20), Stanley.ProjectModel.Bubbles.BubbleStylePreset.Speech).Value;
        Assert.Equal("Inter", BubbleEditing.SetFont(bubble, "Inter ").Value.FontFamily);
        Assert.Null(BubbleEditing.SetFont(bubble with { FontFamily = "Inter" }, null).Value.FontFamily);
        Assert.False(BubbleEditing.SetFont(bubble, "Tab\tName").IsValid);
    }

    [Fact]
    public void Bigger_and_smaller_step_through_the_size_ladder()
    {
        Assert.Equal(4, TextEditing.Bigger(3.5));
        Assert.Equal(3, TextEditing.Smaller(3.5));
        Assert.Equal(4, TextEditing.Bigger(3.7));
        Assert.Equal(TextEditing.MaxFontSizeMm, TextEditing.Bigger(TextEditing.MaxFontSizeMm));
        Assert.True(TextEditing.Smaller(TextEditing.SizeSteps[0]) < TextEditing.SizeSteps[0]);
    }

    [Fact]
    public void Text_boxes_have_a_minimum_size_and_text_a_maximum_length()
    {
        var style = TextStylePresets.Style(TextStylePreset.Plain);

        Assert.False(TextEditing.Create(new Rect2D(0, 0, 2, 10), style).IsValid);
        var text = TextEditing.Create(new Rect2D(0, 0, 30, 5), style).Value;
        Assert.False(TextEditing.SetText(text, new string('x', TextEditing.MaxTextLength + 1)).IsValid);
        Assert.False(TextEditing.SetStyle(text, style with { FontSizeMm = 0.2 }).IsValid);
        Assert.False(TextEditing.Resize(text, new Rect2D(0, 0, 30, 1)).IsValid);
    }

    [Fact]
    public void Growing_to_fit_only_ever_makes_the_box_taller()
    {
        var text = TextEditing.Create(new Rect2D(10, 10, 30, 5), TextStylePresets.Style(TextStylePreset.Plain)).Value;

        Assert.Equal(12, TextEditing.GrowToFit(text, 12).Bounds.Height);
        Assert.Equal(10, TextEditing.GrowToFit(text, 12).Bounds.Top);
        Assert.Same(text, TextEditing.GrowToFit(text, 3));
    }
}

public class ElementEditingTests
{
    private static readonly Rect2D Panel = new(0, 0, 100, 80);

    private static ShapeElement Blob(Rect2D box) => ShapeEditing.Ellipse(box, ShapeEditing.DefaultStyle, ElementLayer.Background).Value;

    [Fact]
    public void An_element_may_hang_out_of_its_panel_but_not_leave_it()
    {
        var hanging = Blob(new Rect2D(-20, 10, 30, 30));
        Assert.Same(hanging, ElementEditing.KeepReachable(hanging, Panel));

        var gone = Blob(new Rect2D(150, 10, 30, 30));
        var box = PanelElements.Bounds(ElementEditing.KeepReachable(gone, Panel));
        Assert.Equal(Panel.Right - ElementEditing.MinVisibleMm, box.Left, 6);
    }

    [Fact]
    public void Panel_resizes_carry_elements_along_at_their_own_size()
    {
        var shape = Blob(new Rect2D(10, 10, 20, 20)); // centre (20, 20): a fifth across, a quarter down

        var refit = PanelElements.Bounds(ElementEditing.Refit(shape, Panel, new Rect2D(100, 100, 200, 160)));

        Assert.Equal(20, refit.Width, 6);
        Assert.Equal(140, refit.MidX, 6);
        Assert.Equal(140, refit.MidY, 6);
    }

    [Fact]
    public void Reordering_moves_to_either_end_of_the_list()
    {
        var a = Blob(new Rect2D(0, 0, 5, 5));
        var b = Blob(new Rect2D(0, 0, 5, 5));
        var c = Blob(new Rect2D(0, 0, 5, 5));

        var (front, index) = ElementEditing.Reorder([a, b, c], 0, toFront: true);
        Assert.Equal([b, c, a], front);
        Assert.Equal(2, index);

        var (back, backIndex) = ElementEditing.Reorder([a, b, c], 2, toFront: false);
        Assert.Equal([c, a, b], back);
        Assert.Equal(0, backIndex);
    }

    [Fact]
    public void Resizing_text_sets_its_box_and_resizing_a_shape_stretches_it()
    {
        var text = TextEditing.Create(new Rect2D(0, 0, 30, 5), TextStylePresets.Style(TextStylePreset.Plain)).Value;

        Assert.Equal(new Rect2D(2, 2, 40, 10), PanelElements.Bounds(ElementEditing.Resize(text, new Rect2D(2, 2, 40, 10)).Value));
        Assert.Equal(new Rect2D(2, 2, 40, 10), PanelElements.Bounds(ElementEditing.Resize(Blob(new Rect2D(0, 0, 5, 5)), new Rect2D(2, 2, 40, 10)).Value));
    }

    [Fact]
    public void Splitting_a_panel_sends_each_element_to_the_half_its_centre_is_in_and_copies_a_fill()
    {
        var left = Blob(new Rect2D(10, 10, 20, 20));
        var right = Blob(new Rect2D(70, 10, 20, 20));
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(Panel), new ColorBackground(ColorValue.FromHex("#5dade2")), [], [], [left, right]);

        var (first, second) = PanelLayoutEditing.Split(panel, BoundaryOrientation.Vertical, 0.5, gutter: 4).Value;

        Assert.Equal([left], first.Elements);
        Assert.Equal([right], second.Elements);
        Assert.Equal(panel.Background, second.Background);
    }

    [Fact]
    public void Moving_a_panel_moves_its_elements()
    {
        var shape = Blob(new Rect2D(10, 10, 20, 20));
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(Panel), null, [], [], [shape]);

        var moved = PanelLayoutEditing.Move(panel, 15, 5, new Rect2D(0, 0, 210, 297)).Value;

        Assert.Equal(new Rect2D(25, 15, 20, 20), PanelElements.Bounds(moved.Elements[0]));
    }
}

public class PictureEditingTests
{
    private static PictureElement Picture(Rect2D box) => new(Stanley.ProjectModel.Ids.ElementId.New(), ElementLayer.Background, box, "p.png");

    [Fact]
    public void A_placed_picture_fits_in_the_middle_of_the_panel_at_its_own_shape()
    {
        var panel = new Rect2D(10, 10, 100, 50);

        var placed = PictureEditing.Place(panel, (400, 100), "p.png", ElementLayer.Foreground).Value;

        Assert.Equal(80, placed.Bounds.Width, 6); // 80% of the panel's width limits a wide picture
        Assert.Equal(20, placed.Bounds.Height, 6);
        Assert.Equal(panel.MidX, placed.Bounds.MidX, 6);
        Assert.Equal(panel.MidY, placed.Bounds.MidY, 6);
        Assert.False(PictureEditing.Place(panel, (0, 10), "p.png", ElementLayer.Foreground).IsValid);
    }

    [Fact]
    public void Dragging_a_corner_keeps_the_shape_and_the_opposite_corner_in_place()
    {
        var picture = Picture(new Rect2D(10, 10, 40, 20));

        var resized = PictureEditing.Resize(picture, Rect2D.FromEdges(10, 10, 90, 35)).Value; // bottom-right corner

        Assert.Equal(new Rect2D(10, 10, 80, 40), resized.Bounds);
    }

    [Fact]
    public void Dragging_an_edge_grows_the_picture_both_ways_across_it()
    {
        var picture = Picture(new Rect2D(10, 10, 40, 20));

        var resized = PictureEditing.Resize(picture, Rect2D.FromEdges(10, 10, 90, 30)).Value; // right edge only

        Assert.Equal(10, resized.Bounds.Left, 6);
        Assert.Equal(80, resized.Bounds.Width, 6);
        Assert.Equal(40, resized.Bounds.Height, 6);
        Assert.Equal(20, resized.Bounds.MidY, 6);
    }

    [Fact]
    public void A_picture_cant_be_shrunk_to_nothing()
    {
        Assert.False(PictureEditing.Resize(Picture(new Rect2D(10, 10, 40, 20)), Rect2D.FromEdges(10, 10, 12, 30)).IsValid);
    }
}
