using System.Runtime.CompilerServices;
using SkiaSharp;
using Stanley.ProjectModel.Characters;

namespace Stanley.Rendering;

/// <summary>
/// Art files turned into Skia pictures once and kept for as long as the file value lives
/// (art is immutable, so a changed file is a new value and a fresh picture).
/// </summary>
internal static class ArtPictures
{
    private static readonly ConditionalWeakTable<ArtFile, SKPicture> Tiles = new();

    /// <summary>A pattern tile as one repeat in the unit square: a PNG stretched to fill it (an SVG is drawn by the art reader).</summary>
    public static SKPicture? Tile(ArtFile file)
    {
        if (Tiles.TryGetValue(file, out var cached))
            return cached;
        var picture = file.Bytes is { } bytes ? PngTile(bytes) : null;
        if (picture != null)
            Tiles.AddOrUpdate(file, picture);
        return picture;
    }

    private static SKPicture? PngTile(byte[] bytes)
    {
        using var image = SKImage.FromEncodedData(bytes);
        if (image is null)
            return null;
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, 1, 1));
        canvas.DrawImage(image, new SKRect(0, 0, 1, 1), new SKSamplingOptions(SKFilterMode.Linear));
        return recorder.EndRecording();
    }
}
