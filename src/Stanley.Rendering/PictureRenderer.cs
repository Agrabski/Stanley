using System.Runtime.CompilerServices;
using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>
/// Draws imported pictures - a panel's background picture, picture elements - in page
/// space. A PNG, JPEG, WebP, GIF or BMP is decoded once per file value and kept for as long
/// as the value lives (art files are immutable); an SVG is read through
/// <see cref="StickerSvg"/> and replayed as vectors, so it stays sharp at any zoom and in
/// the PDF.
/// </summary>
public static class PictureRenderer
{
    private sealed class Decoded(SKImage? image, SKPicture? unitPicture, SKSize size)
    {
        public SKImage? Image { get; } = image;

        /// <summary>An SVG's drawing, its view box scaled to the unit square.</summary>
        public SKPicture? UnitPicture { get; } = unitPicture;

        public SKSize Size { get; } = size;
    }

    private static readonly ConditionalWeakTable<ArtFile, Decoded> Cache = new();
    private static readonly Decoded Unreadable = new(null, null, SKSize.Empty);

    /// <summary>The picture's own size (pixels, or an SVG's view box units) - its shape, for placing it without squashing it; null if it can't be read.</summary>
    public static (double Width, double Height)? Size(ArtFile file) =>
        Decode(file) is { Size: { Width: > 0, Height: > 0 } size } ? (size.Width, size.Height) : null;

    public static bool CanRead(ArtFile file) => Size(file) is not null;

    /// <summary>
    /// Draws <paramref name="file"/> into <paramref name="dest"/>: stretched to fill it, or,
    /// with <paramref name="cover"/>, scaled to cover it without squashing, centred, and
    /// cropped to it. A missing or unreadable picture draws as a grey placeholder, so a
    /// panel never silently loses what should be there.
    /// </summary>
    public static void Draw(SKCanvas canvas, ArtFile? file, Rect2D dest, bool cover)
    {
        var rect = new SKRect((float)dest.Left, (float)dest.Top, (float)dest.Right, (float)dest.Bottom);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        if (file is null || Decode(file) is not { Size: { Width: > 0, Height: > 0 } size } decoded)
        {
            DrawMissing(canvas, rect);
            return;
        }

        var target = rect;
        if (cover)
        {
            var scale = Math.Max(rect.Width / size.Width, rect.Height / size.Height);
            target = SKRect.Create(rect.MidX - size.Width * scale / 2, rect.MidY - size.Height * scale / 2, size.Width * scale, size.Height * scale);
        }

        canvas.Save();
        canvas.ClipRect(rect, antialias: true);
        if (decoded.Image is { } image)
        {
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }
        else if (decoded.UnitPicture is { } picture)
        {
            canvas.Translate(target.Left, target.Top);
            canvas.Scale(target.Width, target.Height);
            canvas.DrawPicture(picture);
        }
        canvas.Restore();
    }

    /// <summary>A light grey box with a cross - "a picture belongs here, but its file is missing".</summary>
    private static void DrawMissing(SKCanvas canvas, SKRect rect)
    {
        using var fill = new SKPaint { Color = new SKColor(0xE5, 0xE7, 0xE9), IsAntialias = true };
        using var line = new SKPaint { Color = new SKColor(0x95, 0xA5, 0xA6), Style = SKPaintStyle.Stroke, StrokeWidth = Math.Min(rect.Width, rect.Height) / 80, IsAntialias = true };
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(rect, line);
        canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Bottom, line);
        canvas.DrawLine(rect.Right, rect.Top, rect.Left, rect.Bottom, line);
    }

    private static Decoded? Decode(ArtFile file)
    {
        var decoded = Cache.GetValue(file, static f =>
        {
            if (f.IsSvg)
            {
                return StickerSvg.Parse(f) is { } art && ArtPictures.Tile(f) is { } unit
                    ? new Decoded(null, unit, art.ViewBox.Size)
                    : Unreadable;
            }
            if (f.Bytes is not { Length: > 0 } bytes)
                return Unreadable;
            var encoded = SKImage.FromEncodedData(bytes);
            if (encoded is null)
                return Unreadable;
            // Decode now, once, rather than on every frame the page is drawn.
            var raster = encoded.ToRasterImage(ensurePixelData: true) ?? encoded;
            if (!ReferenceEquals(raster, encoded))
                encoded.Dispose();
            return new Decoded(raster, null, new SKSize(raster.Width, raster.Height));
        });
        return ReferenceEquals(decoded, Unreadable) ? null : decoded;
    }
}
