using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Poses;
using Xunit;
using Library = Stanley.StickerLibrary.StickerLibrary;

namespace Stanley.Rendering.Tests;

/// <summary>
/// The villain's grin (#152): each starter mouth's "sinister" variant draws, front and side, as
/// a toothy grin - white teeth show, which the neutral mouth never does.
/// </summary>
public class SinisterGrinTests
{
    // Big enough for the teeth to be a few hundred pixels: 2400 px to the character's height.
    private static readonly CharacterPlacement Placement = new(new Point2D(600, 2640), 2400, false);

    private static int TeethShown(string mouthKey, ViewAngle view, string variant)
    {
        var asset = Library.Find(mouthKey)!.Instantiate();
        var character = CharacterDefinition.Create("A");
        character = character with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Mouth] = [asset.Id] },
            Wardrobe = character.Wardrobe.With(asset),
        };
        var pose = new PoseData(view, [], new SortedDictionary<string, string> { [StickerSlots.Mouth] = variant });
        using var bitmap = new SKBitmap(1200, 1200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0x80, 0x80, 0x80));
        CharacterRenderers.Default.Draw(canvas, character, Placement, 4f, view, pose);
        var teeth = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y) is { Red: >= 240, Green: >= 240, Blue: >= 240 })
                    teeth++;
        return teeth;
    }

    [Theory]
    [InlineData("mouth/simple", ViewAngle.Front)]
    [InlineData("mouth/simple", ViewAngle.Profile)]
    [InlineData("mouth/wide", ViewAngle.Front)]
    [InlineData("mouth/wide", ViewAngle.Profile)]
    [InlineData("mouth/lips", ViewAngle.Front)]
    [InlineData("mouth/lips", ViewAngle.Profile)]
    public void The_sinister_variant_shows_teeth_where_the_neutral_mouth_shows_none(string mouthKey, ViewAngle view)
    {
        Assert.Equal(0, TeethShown(mouthKey, view, "neutral"));
        var shown = TeethShown(mouthKey, view, "sinister");
        Assert.True(shown > 100, $"{mouthKey} {view} should show a row of teeth, showed {shown}");
    }
}
