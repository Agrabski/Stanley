using SkiaSharp;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Rendering.Tests;

/// <summary>A panel whose stacking was arranged by hand (<see cref="Panel.Stack"/>) draws in that order - bubbles behind characters, drawings in front of bubbles.</summary>
public class StackedRenderingTests
{
    private static readonly ColorValue Red = ColorValue.FromHex("#ff0000");
    private static readonly Rect2D PanelBounds = new(10, 10, 80, 60);
    private static readonly SKColor Green = new(0, 255, 0);

    private static SKBitmap Render(Panel panel, IReadOnlyDictionary<CharacterId, CharacterDefinition>? characters = null)
    {
        var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        PageRenderer.Draw(canvas, new Rect2D(0, 0, 100, 100), [panel], characters: characters);
        return bitmap;
    }

    /// <summary>One character (green skin, the chest at about (50, 50)) and a bubble (white) over the same spot.</summary>
    private static (Panel Panel, IReadOnlyDictionary<CharacterId, CharacterDefinition> Characters) CharacterAndBubble()
    {
        var definition = CharacterDefinition.Create("A") with
        {
            ColorSlots = new SortedDictionary<string, ColorValue> { [CharacterDefinition.SkinSlot] = ColorValue.FromHex("#00ff00") }
        };
        var instance = new CharacterInstance(definition.Id, new CharacterPlacement(new Point2D(50, 90), 70, false), null, new PoseData(ViewAngle.Front, [], []), null,
            Id: CharacterInstanceId.New());
        var bubble = new Bubble(BubbleId.New(), BubbleStylePresets.GenerateShape(BubbleStylePreset.Speech, new Rect2D(30, 38, 40, 24)), BubbleStylePreset.Speech, [], "");
        var panel = new Panel(PanelId.New(), PanelShapes.Rectangle(PanelBounds), null, [instance], [bubble]);
        return (panel, new Dictionary<CharacterId, CharacterDefinition> { [definition.Id] = definition });
    }

    [Fact]
    public void Without_a_stack_the_bubble_is_in_front_of_the_character()
    {
        var (panel, characters) = CharacterAndBubble();

        using var bitmap = Render(panel, characters);

        Assert.Equal(SKColors.White, bitmap.GetPixel(50, 50));
    }

    [Fact]
    public void A_stack_can_put_the_bubble_behind_the_character()
    {
        var (panel, characters) = CharacterAndBubble();
        var stacked = panel with { Stack = [PanelStack.Token(panel.Bubbles[0]), PanelStack.Token(panel.CharacterInstances[0])!] };

        using var bitmap = Render(stacked, characters);

        Assert.Equal(Green, bitmap.GetPixel(50, 50)); // the chest, over the bubble's white
        Assert.Contains(Enumerable.Range(31, 38), x => bitmap.GetPixel(x, 50) == SKColors.White); // the bubble still shows where the character isn't
    }

    [Fact]
    public void A_stack_can_put_a_drawing_in_front_of_a_bubble()
    {
        var (panel, characters) = CharacterAndBubble();
        var box = new ShapeElement(ElementId.New(), ElementLayer.Foreground, PanelShapes.Rectangle(new Rect2D(28, 36, 44, 28)).Anchors, Closed: true,
            new ShapeStyle(Stroke: null, Red, 0));
        var withBox = panel with { Elements = [box] };
        using (var usual = Render(withBox, characters))
            Assert.Equal(SKColors.White, usual.GetPixel(50, 50)); // a bubble covers drawings, as ever

        var stacked = withBox with { Stack = [PanelStack.Token(withBox.Bubbles[0]), PanelStack.Token(box), PanelStack.Token(withBox.CharacterInstances[0])!] };
        using var arranged = Render(stacked, characters);

        Assert.Equal(new SKColor(255, 0, 0), arranged.GetPixel(32, 50)); // the box now covers the bubble, though not the character standing in front of it
        Assert.Equal(Green, arranged.GetPixel(50, 50));
    }

    [Fact]
    public void A_stack_never_hides_or_duplicates_anything_a_stale_list_does_not_mention()
    {
        var (panel, characters) = CharacterAndBubble();
        // names nothing that exists: the panel draws the usual way
        var stale = panel with { Stack = ["b:gone", "c:gone", "e:gone"] };

        using var bitmap = Render(stale, characters);

        Assert.Equal(SKColors.White, bitmap.GetPixel(50, 50));
    }
}
