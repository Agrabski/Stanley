using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Rendering.Tests;

public class ElementRendererTests
{
    private static readonly ColorValue Red = ColorValue.FromHex("#ff0000");
    private static readonly ColorValue Blue = ColorValue.FromHex("#0000ff");
    private static readonly ColorValue Black = ColorValue.FromHex("#000000");
    private static readonly Rect2D PanelBounds = new(10, 10, 80, 60);

    private static ShapeElement Box(Rect2D bounds, ColorValue fill, ElementLayer layer) =>
        new(ElementId.New(), layer, PanelShapes.Rectangle(bounds).Anchors, Closed: true, new ShapeStyle(Stroke: null, fill, 0));

    private static SKBitmap Render(Panel panel, IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters = null)
    {
        var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        PageRenderer.Draw(canvas, new Rect2D(0, 0, 100, 100), [panel], characters: characters);
        return bitmap;
    }

    private static Panel PanelWith(PanelBackground? background, IReadOnlyList<CharacterInstance> characters, params PanelElement[] elements) =>
        new(PanelId.New(), PanelShapes.Rectangle(PanelBounds), background, characters, [], elements);

    [Fact]
    public void A_colour_background_fills_the_panel_and_nothing_outside_it()
    {
        using var bitmap = Render(PanelWith(new ColorBackground(Blue), []));

        Assert.Equal(new SKColor(0, 0, 255), bitmap.GetPixel(50, 40));
        Assert.Equal(SKColors.White, bitmap.GetPixel(50, 90));
    }

    [Fact]
    public void A_gradient_background_runs_from_its_top_colour_to_its_bottom_colour()
    {
        using var bitmap = Render(PanelWith(new GradientBackground(Blue, Red), []));

        var top = bitmap.GetPixel(50, 12);
        var bottom = bitmap.GetPixel(50, 68);
        Assert.True(top.Blue > 200 && top.Red < 60, $"top should be blue, was {top}");
        Assert.True(bottom.Red > 200 && bottom.Blue < 60, $"bottom should be red, was {bottom}");
    }

    [Fact]
    public void Characters_stand_in_front_of_background_elements_and_behind_foreground_ones()
    {
        var character = CharacterDefinition.Create("A") with
        {
            ColorSlots = new SortedDictionary<string, ColorValue> { [CharacterDefinition.SkinSlot] = ColorValue.FromHex("#00ff00") }
        };
        var characters = new Dictionary<CharacterId, CharacterDefinition> { [character.Id] = character };
        var instance = new CharacterInstance(character.Id, new CharacterPlacement(new Point2D(50, 90), 70, false), null, new PoseData(ViewAngle.Front, [], []), null);

        using (var behind = Render(PanelWith(null, [instance], Box(PanelBounds, Red, ElementLayer.Background)), characters))
            Assert.Equal(new SKColor(0, 255, 0), behind.GetPixel(50, 50)); // the chest, over the red scenery

        using (var inFront = Render(PanelWith(null, [instance], Box(PanelBounds, Red, ElementLayer.Foreground)), characters))
            Assert.Equal(new SKColor(255, 0, 0), inFront.GetPixel(50, 50)); // hidden behind the red bush
    }

    [Fact]
    public void Foreground_elements_draw_over_background_ones_whatever_their_list_order()
    {
        var front = Box(new Rect2D(30, 30, 20, 20), Red, ElementLayer.Foreground);
        var back = Box(new Rect2D(20, 20, 40, 40), Blue, ElementLayer.Background);

        using var bitmap = Render(PanelWith(null, [], front, back));

        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(40, 40));
        Assert.Equal(new SKColor(0, 0, 255), bitmap.GetPixel(25, 25));
    }

    [Fact]
    public void Within_a_layer_later_elements_draw_on_top_and_bubbles_stay_above_everything()
    {
        var first = Box(new Rect2D(20, 20, 40, 40), Blue, ElementLayer.Foreground);
        var second = Box(new Rect2D(30, 30, 20, 20), Red, ElementLayer.Foreground);
        var bubble = new Bubble(BubbleId.New(), BubbleStylePresets.GenerateShape(BubbleStylePreset.Speech, new Rect2D(32, 32, 16, 16)), BubbleStylePreset.Speech, [], "");
        var panel = PanelWith(null, [], first, second) with { Bubbles = [bubble] };

        using var bitmap = Render(panel);

        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(31, 48)); // second over first, outside the bubble
        Assert.Equal(SKColors.White, bitmap.GetPixel(40, 40)); // the bubble's white fill over both
    }

    [Fact]
    public void Elements_are_clipped_to_their_panel()
    {
        using var bitmap = Render(PanelWith(null, [], Box(new Rect2D(0, 0, 100, 100), Red, ElementLayer.Background)));

        Assert.Equal(SKColors.White, bitmap.GetPixel(5, 5));
        Assert.Equal(new SKColor(255, 0, 0), bitmap.GetPixel(15, 15));
    }

    [Fact]
    public void An_open_stroke_is_drawn_as_a_line_and_never_filled()
    {
        var anchors = new List<ShapeAnchor>
        {
            new(new Point2D(20, 50), new Point2D(20, 50), new Point2D(20, 50), AnchorHandleKind.Corner),
            new(new Point2D(50, 20), new Point2D(50, 20), new Point2D(50, 20), AnchorHandleKind.Corner),
            new(new Point2D(80, 50), new Point2D(80, 50), new Point2D(80, 50), AnchorHandleKind.Corner),
        };
        var stroke = new ShapeElement(ElementId.New(), ElementLayer.Background, anchors, Closed: false, new ShapeStyle(Black, Fill: Red, 4));

        using var bitmap = Render(PanelWith(null, [], stroke));

        Assert.Equal(new SKColor(0, 0, 0), bitmap.GetPixel(35, 35)); // on the first leg
        Assert.Equal(SKColors.White, bitmap.GetPixel(50, 45)); // inside the "V": no fill for an open line
    }

    [Fact]
    public void Hit_testing_finds_filled_shapes_inside_but_unfilled_ones_only_on_their_outline()
    {
        var filled = Box(new Rect2D(20, 20, 40, 40), Red, ElementLayer.Background);
        var outlined = new ShapeElement(ElementId.New(), ElementLayer.Background, PanelShapes.Rectangle(new Rect2D(20, 20, 40, 40)).Anchors, Closed: true,
            new ShapeStyle(Black, Fill: null, 0.5));

        Assert.True(ElementRenderer.Hits(filled, new Point2D(40, 40), 1));
        Assert.False(ElementRenderer.Hits(outlined, new Point2D(40, 40), 1));
        Assert.True(ElementRenderer.Hits(outlined, new Point2D(20.8, 40), 1));
        Assert.False(ElementRenderer.Hits(filled, new Point2D(70, 40), 1));
    }

    [Fact]
    public void Text_wraps_inside_its_box_and_a_caption_box_is_drawn_behind_it()
    {
        var caption = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(15, 15, 40, 20), "Meanwhile, back at the lab",
            new TextStyle(10, Black, Align: TextAlign.Left, BoxFill: ColorValue.FromHex("#ffff00"), BoxStroke: Black));

        using var bitmap = Render(PanelWith(null, [], caption));

        Assert.Equal(new SKColor(255, 255, 0), bitmap.GetPixel(53, 33)); // box corner, clear of the text
        var ink = 0;
        for (var y = 16; y < 35; y++)
        for (var x = 16; x < 55; x++)
            if (bitmap.GetPixel(x, y) is { Red: < 100, Green: < 100 })
                ink++;
        Assert.True(ink > 10, "some letters should be drawn");
        for (var y = 0; y < 100; y++)
            Assert.NotEqual(new SKColor(0, 0, 0), bitmap.GetPixel(70, y)); // nothing ran past the box's right edge
    }

    [Fact]
    public void Needed_height_grows_with_the_text_and_counts_the_box_padding()
    {
        var style = new TextStyle(10, Black);
        var one = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(0, 0, 40, 5), "Hi", style);
        var many = one with { Text = "Hi\nthere\nyou\nfour" };
        var boxed = one with { Style = style with { BoxFill = ColorValue.FromHex("#ffffff") } };

        Assert.True(ElementRenderer.NeededHeight(many) > 3.5 * ElementRenderer.NeededHeight(one));
        Assert.Equal(ElementRenderer.NeededHeight(one) + 2 * style.FontSizeMm * ElementRenderer.BoxPaddingFraction, ElementRenderer.NeededHeight(boxed), 3);
    }

    [Fact]
    public void Lettering_can_be_left_off_while_the_box_still_draws()
    {
        var caption = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(15, 15, 40, 20), "WHAM",
            new TextStyle(23, Black, BoxFill: ColorValue.FromHex("#ffff00")));
        var panel = PanelWith(null, [], caption);

        using var bitmap = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(bitmap))
            PageRenderer.DrawPanels(canvas, [panel], hideText: caption.Id);

        for (var y = 16; y < 35; y++)
        for (var x = 16; x < 55; x++)
            Assert.Equal(new SKColor(255, 255, 0), bitmap.GetPixel(x, y));
    }
}

public class LineStyleRenderingTests
{
    private static readonly ColorValue Black = ColorValue.FromHex("#000000");

    private static SKBitmap Render(params PanelElement[] elements)
    {
        var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        PageRenderer.Draw(canvas, new Rect2D(0, 0, 100, 100), [new Panel(PanelId.New(), PanelShapes.Rectangle(new Rect2D(0, 0, 100, 100)), null, [], [], elements)]);
        return bitmap;
    }

    private static int InkAlong(SKBitmap bitmap, int y, int from, int to)
    {
        var ink = 0;
        for (var x = from; x < to; x++)
            if (bitmap.GetPixel(x, y).Red < 128)
                ink++;
        return ink;
    }

    private static ShapeElement Line(LineDash dash) =>
        new(ElementId.New(), ElementLayer.Background,
            [AnchorRing.Corner(new Point2D(10, 50)), AnchorRing.Corner(new Point2D(90, 50))], Closed: false, new ShapeStyle(Black, null, 2, dash));

    [Fact]
    public void Dashed_and_dotted_lines_leave_gaps_a_solid_one_doesnt()
    {
        using var solid = Render(Line(LineDash.Solid));
        using var dashed = Render(Line(LineDash.Dash));
        using var dotted = Render(Line(LineDash.RoundDot));

        Assert.True(InkAlong(solid, 50, 12, 88) >= 75);
        Assert.InRange(InkAlong(dashed, 50, 12, 88), 30, 55); // 4 on, 3 off
        Assert.InRange(InkAlong(dotted, 50, 12, 88), 25, 55); // round dots, a dot's width apart
    }

    [Fact]
    public void Hollow_letters_draw_only_their_outline_and_a_heavier_box_border_is_thicker()
    {
        var text = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(10, 20, 80, 40), "O",
            new TextStyle(85, Color: null, Bold: true, Outline: Black, OutlineWidthMm: 1));
        using var hollow = Render(text);
        using var filled = Render(text with { Style = text.Style with { Color = Black } });
        int Ink(SKBitmap b) { var n = 0; for (var y = 20; y < 60; y++) n += InkAlong(b, y, 10, 90); return n; }
        Assert.True(Ink(hollow) > 0);
        Assert.True(Ink(hollow) < Ink(filled) * 0.7, "a hollow O should be mostly empty inside");

        var thin = new TextElement(ElementId.New(), ElementLayer.Foreground, new Rect2D(10, 20, 80, 40), "", new TextStyle(10, Black, BoxStroke: Black));
        using var thinBox = Render(thin);
        using var thickBox = Render(thin with { Style = thin.Style with { BoxStrokeWidthMm = 3 } });
        Assert.True(InkAlong(thickBox, 40, 5, 15) > InkAlong(thinBox, 40, 5, 15));
    }
}
