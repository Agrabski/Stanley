using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Poses;
using Xunit;
using Library = Stanley.StickerLibrary.StickerLibrary;

namespace Stanley.Rendering.Tests;

/// <summary>The library's Zip jacket, worn closed, half open or open (#71): the opening cuts the jacket away, so what is under it shows.</summary>
public class ZipJacketTests
{
    private static readonly SKColor Jacket = FigureGeometry.ToSk(Library.Find("outer/zip-jacket")!.Asset.Sticker.Colors["outer"]);
    private static readonly CharacterPlacement Placement = new(new Point2D(300, 1320), 1200, false);

    private static (CharacterDefinition Character, PoseData Pose) Wearing(string? style, ViewAngle view)
    {
        var character = CharacterDefinition.Create("A");
        var asset = Library.Find("outer/zip-jacket")!.Instantiate();
        character = character with
        {
            Stickers = new SortedDictionary<string, IReadOnlyList<StickerId>> { [StickerSlots.Outer] = [asset.Id] },
            Wardrobe = character.Wardrobe.With(asset),
            StickerVariants = style is null ? null : new SortedDictionary<StickerId, string> { [asset.Id] = style },
        };
        return (character, new PoseData(view, [], new SortedDictionary<string, string>()));
    }

    private static SKColor Chest(string? style, ViewAngle view, double x, double y)
    {
        var (character, pose) = Wearing(style, view);
        using var bitmap = new SKBitmap(600, 600);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        CharacterRenderers.Default.Draw(canvas, character, Placement, 4f, view, pose);
        var figure = BodyRig.Build(character.Body, view, character.Skeleton, pose);
        var page = Placement.ToPage(RegionMapping.Warp(figure, BodyRegion.Torso, LimbSide.Left, null)(new Point2D(x, y)));
        if (Environment.GetEnvironmentVariable("ZIP_DUMP") is { } dir)
        {
            using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dir, $"{style}-{view}.png"), data.ToArray());
        }
        return bitmap.GetPixel((int)Math.Round(page.X), (int)Math.Round(page.Y));
    }

    [Fact]
    public void Is_closed_by_default_and_has_three_styles()
    {
        var sticker = Library.Find("outer/zip-jacket")!.Asset.Sticker;
        Assert.Equal(["closed", "half", "open"], sticker.Variants);
    }

    private static SKColor Skin => FigureGeometry.ToSk(CharacterDefinition.Create("A").Skin);

    [Fact]
    public void Closed_covers_the_chest_and_is_the_default()
    {
        Assert.Equal(Jacket, Chest(null, ViewAngle.Front, 12, -740));
        Assert.Equal(Jacket, Chest("closed", ViewAngle.Front, -12, -740));
    }

    [Fact]
    public void Half_open_shows_what_is_under_it_at_the_neck_only()
    {
        Assert.Equal(Skin, Chest("half", ViewAngle.Front, 0, -770));
        Assert.Equal(Jacket, Chest("half", ViewAngle.Front, 12, -580));
    }

    [Fact]
    public void Open_shows_what_is_under_it_all_the_way_down()
    {
        Assert.Equal(Skin, Chest("open", ViewAngle.Front, 0, -770));
        Assert.Equal(Skin, Chest("open", ViewAngle.Front, 0, -580));
    }
}
