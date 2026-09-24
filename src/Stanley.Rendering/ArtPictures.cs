using System.Collections.Concurrent;
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
    private static readonly ConditionalWeakTable<ArtFile, ConcurrentDictionary<string, SKPicture>> Recoloured = new();

    /// <summary>
    /// A pattern tile in a slot's colours (docs/sticker-system.md §9.2): in an SVG tile,
    /// what's tagged <c>slot-ground</c> takes <paramref name="ground"/>, <c>slot-1</c> and
    /// <c>slot-2</c> the pattern's own colours (while it has them), each keeping its shade
    /// offset; everything else keeps its colours. A PNG tile is fixed-colour.
    /// </summary>
    public static SKPicture? PatternTile(ArtFile file, SKColor ground, IReadOnlyList<SKColor> colors)
    {
        if (StickerSvg.Parse(file) is not { } art)
            return Tile(file);
        var slots = new Dictionary<string, SKColor> { ["ground"] = ground };
        if (colors.Count > 0)
            slots["1"] = colors[0];
        if (colors.Count > 1)
            slots["2"] = colors[1];
        var key = string.Join(";", slots.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key}={s.Value}"));
        return Recoloured.GetValue(file, _ => new()).GetOrAdd(key, _ => SvgTile(art, Recolor(art, slots)));
    }

    /// <summary>Maps each tagged colour to its slot's new colour, relative to the first colour drawn in that slot (its default).</summary>
    private static Func<string?, SKColor, SKColor> Recolor(ParsedArt art, IReadOnlyDictionary<string, SKColor> slots)
    {
        var defaults = new Dictionary<string, SKColor>(StringComparer.Ordinal);
        foreach (var e in art.Layers.Values.SelectMany(l => l))
        {
            if (e.Slot is { } slot && (e.Fill ?? e.Stroke) is { } paint)
                defaults.TryAdd(slot, paint.Color);
        }
        return (slot, color) => slot is not null && slots.TryGetValue(slot, out var now) && defaults.TryGetValue(slot, out var original)
            ? ColorMath.Recolor(color, original, now)
            : color;
    }

    /// <summary>A pattern tile as one repeat in the unit square: a PNG stretched to fill it, or an SVG's view box scaled to it.</summary>
    public static SKPicture? Tile(ArtFile file)
    {
        if (Tiles.TryGetValue(file, out var cached))
            return cached;
        var picture = file.Bytes is { } bytes ? PngTile(bytes) : StickerSvg.Parse(file) is { } art ? SvgTile(art, (_, c) => c) : null;
        if (picture != null)
            Tiles.AddOrUpdate(file, picture);
        return picture;
    }

    /// <summary>Every layer of the art, as drawn, its view box filling the unit square.</summary>
    private static SKPicture SvgTile(ParsedArt art, Func<string?, SKColor, SKColor> recolor)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, 1, 1));
        canvas.Scale(1 / art.ViewBox.Width, 1 / art.ViewBox.Height);
        canvas.Translate(-art.ViewBox.Left, -art.ViewBox.Top);
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var e in art.Layers.Values.SelectMany(l => l))
        {
            canvas.Save();
            if (e.Clip is { } clip)
                canvas.ClipPath(clip, antialias: true);
            foreach (var (brush, stroke) in new[] { (e.Fill, false), (e.Stroke, true) })
            {
                if (brush is null)
                    continue;
                paint.Reset();
                paint.IsAntialias = true;
                paint.Style = stroke ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
                paint.StrokeWidth = e.StrokeWidth;
                paint.StrokeCap = e.Cap;
                paint.StrokeJoin = e.Join;
                paint.Color = recolor(e.Slot, brush.Color);
                using var shader = brush.Gradient is { } g ? Gradient(g with { Stops = g.Stops.Select(s => (recolor(e.Slot, s.Color), s.Offset)).ToList() }) : null;
                paint.Shader = shader;
                if (stroke && e.Dash is { } dash)
                    paint.PathEffect = SKPathEffect.CreateDash(dash, 0);
                canvas.DrawPath(e.Path, paint);
            }
            canvas.Restore();
        }
        return recorder.EndRecording();
    }

    private static SKShader Gradient(ArtGradient g)
    {
        var colors = g.Stops.Select(s => s.Color).ToArray();
        var offsets = g.Stops.Select(s => s.Offset).ToArray();
        return g.Radius is { } r
            ? SKShader.CreateRadialGradient(g.Start, r, colors, offsets, SKShaderTileMode.Clamp)
            : SKShader.CreateLinearGradient(g.Start, g.End, colors, offsets, SKShaderTileMode.Clamp);
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

/// <summary>Checks on art files the editors need before taking one in.</summary>
public static class ArtFiles
{
    /// <summary>Whether <paramref name="file"/> holds a raster image Skia can decode (a PNG).</summary>
    public static bool IsImage(ArtFile file)
    {
        if (file.Bytes is not { Length: > 0 } bytes)
            return false;
        using var image = SKImage.FromEncodedData(bytes);
        return image is not null;
    }
}
