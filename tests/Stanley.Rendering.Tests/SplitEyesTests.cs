using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;

namespace Stanley.Rendering.Tests;

/// <summary>
/// Split eyes (docs/sticker-system.md §21, GitHub issues #100-#102): a worn sticker
/// restricted to one side draws only its own side's elements (<c>class="side-left"</c>/
/// <c>"side-right"</c>), recolours from "eyesLeft"/"eyesRight" instead of the shared "eyes",
/// and shows that side's own expression.
/// </summary>
public class SplitEyesTests
{
    private static readonly SKColor DefaultBlue = new(0x4a, 0x7a, 0xb5);

    /// <summary>Both eyes in one layer, as the starter library draws them: the right eye at negative x, the left at positive x (docs/sticker-system.md §4.1 - character's own left is on the viewer's right).</summary>
    private const string BothEyesNeutral = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="-80 -1010 160 150">
          <ellipse class="slot-eyes side-right" cx="-21" cy="-930" rx="8" ry="8" fill="#4a7ab5"/>
          <ellipse class="slot-eyes side-left" cx="21" cy="-930" rx="8" ry="8" fill="#4a7ab5"/>
        </svg>
        """;

    private const string BothEyesHappy = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="-80 -1010 160 150">
          <path class="side-right" d="M-30,-927 Q-21,-941 -12,-927" fill="none" stroke="#1a1a1a" stroke-width="3"/>
          <path class="side-left" d="M12,-927 Q21,-941 30,-927" fill="none" stroke="#1a1a1a" stroke-width="3"/>
        </svg>
        """;

    private static Sticker EyeSticker(string name) =>
        new(StickerId.New(), name, StickerSlots.Eyes, [new StickerPart("eyes", BodyRegion.Head, Art: new PartArt(ArtMapping.Pin))],
            new SortedDictionary<string, ColorValue> { [StickerSlots.Eyes] = ColorValue.FromHex("#4a7ab5") }, ["neutral", "happy"]);

    private static Dictionary<string, ArtFile> Files() => new()
    {
        ["variants/neutral/front.svg"] = ArtFile.Svg(BothEyesNeutral),
        ["variants/happy/front.svg"] = ArtFile.Svg(BothEyesHappy),
    };

    [Fact]
    public void Unsplit_a_single_sticker_draws_both_sides()
    {
        var sticker = EyeSticker("Round");
        var character = CharacterDefinition.Create("A") with { Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Eyes] = [sticker.Id] } };
        character = character with { Wardrobe = character.Wardrobe.With(new StickerAsset(sticker, Files())) };
        var renderer = (FigureRenderer)CharacterRenderers.Default;

        using var outline = renderer.Drawing(character, ViewAngle.Front, new PoseData(ViewAngle.Front, [], [])).OutlineOf(sticker.Id);

        // The two ellipses are 42 template units apart, each 16 wide: a single-eye outline
        // would be about 0.016 wide in figure space, spanning both is far wider.
        Assert.True(outline.TightBounds.Width > 0.03, $"expected both eyes, width was {outline.TightBounds.Width}");
    }

    [Fact]
    public void Split_each_side_draws_only_its_own_elements()
    {
        var left = EyeSticker("Round");
        var right = EyeSticker("Round"); // a separate copy, as LookEditing.SplitEyes makes
        var files = Files();
        var character = CharacterDefinition.Create("A") with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Eyes] = [left.Id, right.Id] },
            StickerSides = new SortedDictionary<StickerId, LimbSide> { [left.Id] = LimbSide.Left, [right.Id] = LimbSide.Right },
        };
        character = character with { Wardrobe = character.Wardrobe.With(new StickerAsset(left, files)).With(new StickerAsset(right, files)) };
        var renderer = (FigureRenderer)CharacterRenderers.Default;
        var drawing = renderer.Drawing(character, ViewAngle.Front, new PoseData(ViewAngle.Front, [], []));

        using var leftOutline = drawing.OutlineOf(left.Id);
        using var rightOutline = drawing.OutlineOf(right.Id);

        Assert.True(leftOutline.TightBounds.Width < 0.02, $"left eye alone, width was {leftOutline.TightBounds.Width}");
        Assert.True(rightOutline.TightBounds.Width < 0.02, $"right eye alone, width was {rightOutline.TightBounds.Width}");
        Assert.NotEqual(leftOutline.TightBounds.MidX, rightOutline.TightBounds.MidX, 3);
    }

    [Fact]
    public void Split_recolours_from_eyesLeft_and_eyesRight_instead_of_the_shared_slot()
    {
        var left = EyeSticker("Round");
        var right = EyeSticker("Round");
        var files = Files();
        var character = CharacterDefinition.Create("A") with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Eyes] = [left.Id, right.Id] },
            StickerSides = new SortedDictionary<StickerId, LimbSide> { [left.Id] = LimbSide.Left, [right.Id] = LimbSide.Right },
            ColorSlots = new SortedDictionary<string, ColorValue> { [StickerSlots.EyesLeft] = ColorValue.FromHex("#00ff00") },
        };
        character = character with { Wardrobe = character.Wardrobe.With(new StickerAsset(left, files)).With(new StickerAsset(right, files)) };
        using var bitmap = new SKBitmap(400, 440);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            CharacterRenderers.Default.Draw(canvas, character, new CharacterPlacement(new Point2D(200, 420), 400, false), 2f, ViewAngle.Front, new PoseData(ViewAngle.Front, [], []));
        }
        var renderer = (FigureRenderer)CharacterRenderers.Default;
        var drawing = renderer.Drawing(character, ViewAngle.Front, new PoseData(ViewAngle.Front, [], []));
        using var leftOutline = drawing.OutlineOf(left.Id);
        using var rightOutline = drawing.OutlineOf(right.Id);
        var leftPixel = bitmap.GetPixel((int)Math.Round(200 + leftOutline.TightBounds.MidX * 400), (int)Math.Round(420 + leftOutline.TightBounds.MidY * 400));
        var rightPixel = bitmap.GetPixel((int)Math.Round(200 + rightOutline.TightBounds.MidX * 400), (int)Math.Round(420 + rightOutline.TightBounds.MidY * 400));

        Assert.Equal(new SKColor(0, 255, 0), leftPixel); // eyesLeft overridden
        Assert.Equal(DefaultBlue, rightPixel); // eyesRight falls back to the sticker's own default
    }

    [Fact]
    public void Split_each_side_shows_its_own_expression()
    {
        var left = EyeSticker("Round");
        var right = EyeSticker("Round");
        var files = Files();
        var character = CharacterDefinition.Create("A") with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Eyes] = [left.Id, right.Id] },
            StickerSides = new SortedDictionary<StickerId, LimbSide> { [left.Id] = LimbSide.Left, [right.Id] = LimbSide.Right },
        };
        character = character with { Wardrobe = character.Wardrobe.With(new StickerAsset(left, files)).With(new StickerAsset(right, files)) };
        var renderer = (FigureRenderer)CharacterRenderers.Default;
        var pose = new PoseData(ViewAngle.Front, [], new SortedDictionary<string, string> { [StickerSlots.EyesRight] = "happy" });

        var drawing = renderer.Drawing(character, ViewAngle.Front, pose);
        using var leftOutline = drawing.OutlineOf(left.Id);
        using var rightOutline = drawing.OutlineOf(right.Id);

        // The neutral eye is a round ellipse; the happy brow-curve is a wide, shallow stroke - different shapes.
        Assert.False(leftOutline.IsEmpty);
        Assert.False(rightOutline.IsEmpty);
        Assert.NotEqual(leftOutline.TightBounds, rightOutline.TightBounds);
    }
}
