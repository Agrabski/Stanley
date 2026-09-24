using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.Rendering;

/// <summary>
/// Patterns and textures (docs/sticker-system.md §9): each is a shader laid out in a body
/// region's frame, so stripes on a sleeve turn with the arm. Patterns are drawn as one
/// repeat - a unit tile, recorded as a picture - over the slot's colour; textures are
/// greyscale and multiplied on top, so they survive any recolour. All generated here,
/// no art files, except a drawn tile (<see cref="PatternKind.Tile"/>).
/// </summary>
public static class FabricShaders
{
    private static readonly SKRect Unit = new(0, 0, 1, 1);

    /// <summary>
    /// The pattern's shader for a piece whose region frame is <paramref name="frame"/>
    /// (figure space), for a character <paramref name="height"/> tall, over
    /// <paramref name="ground"/>; null if there's no pattern (or its tile is missing).
    /// </summary>
    public static SKShader? Pattern(PatternFill pattern, SKColor ground, SKMatrix frame, double height, Func<string, SKPicture?>? tiles = null)
    {
        var size = (float)(Math.Max(pattern.Size ?? PatternFill.DefaultSize, 0.005) * height);
        var local = SKMatrix.CreateScale(size, size)
            .PostConcat(SKMatrix.CreateRotationDegrees((float)(pattern.Angle ?? 0)))
            .PostConcat(frame);
        if (pattern.Kind == PatternKind.Tile)
        {
            // Tiles are cached by whoever loaded them - not ours to dispose.
            var tile = pattern.Tile is { } name ? tiles?.Invoke(name) : null;
            return tile is null ? null : SKShader.CreatePicture(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, SKFilterMode.Linear, local, Unit);
        }
        using var picture = Record(canvas => DrawPattern(canvas, pattern, ground));
        return SKShader.CreatePicture(picture, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, SKFilterMode.Linear, local, Unit);
    }

    /// <summary>The texture's greyscale shader, to multiply over the fill; null for none.</summary>
    public static SKShader? Texture(TextureFill texture, SKMatrix frame, double height, Func<string, SKPicture?>? tiles = null)
    {
        var size = (float)(Math.Max(texture.Size ?? TextureFill.DefaultSize, 0.003) * height);
        var local = SKMatrix.CreateScale(size, size).PostConcat(frame);
        if (texture.Kind == TextureKind.Tile)
        {
            var tile = texture.Tile is { } name ? tiles?.Invoke(name) : null;
            return tile is null ? null : SKShader.CreatePicture(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, SKFilterMode.Linear, local, Unit);
        }
        using var lines = Record(canvas => DrawTexture(canvas, texture.Kind));
        using var weave = SKShader.CreatePicture(lines, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, SKFilterMode.Linear, SKMatrix.Identity, Unit);
        // Noise gives it life; its frequency is per tile, so it scales with the texture.
        var (frequency, octaves, depth) = texture.Kind switch
        {
            TextureKind.Wool => (1.2f, 3, 0.35f),
            TextureKind.Felt => (3.5f, 4, 0.28f),
            TextureKind.Leather => (0.9f, 3, 0.3f),
            TextureKind.Denim => (2.5f, 2, 0.18f),
            _ => (2f, 2, 0.12f)
        };
        using var noise = SKShader.CreatePerlinNoiseFractalNoise(frequency, frequency, octaves, 7);
        using var grey = SKColorFilter.CreateColorMatrix(GreyMatrix(depth));
        using var greyNoise = SKShader.CreateColorFilter(noise, grey);
        using var combined = SKShader.CreateBlend(SKBlendMode.Multiply, weave, greyNoise);
        return SKShader.CreateLocalMatrix(combined, local);
    }

    /// <summary>Fills <paramref name="path"/> with <paramref name="ground"/>, then the fabric's pattern and texture in <paramref name="frame"/>.</summary>
    public static void Fill(SKCanvas canvas, SKPath path, SKColor ground, Fabric? fabric, SKMatrix frame, double height, Func<string, SKPicture?>? tiles = null)
    {
        using (var paint = new SKPaint { Color = ground, Style = SKPaintStyle.Fill, IsAntialias = true })
            canvas.DrawPath(path, paint);
        if (fabric?.Pattern is { } pattern)
        {
            using var shader = Pattern(pattern, ground, frame, height, tiles);
            if (shader != null)
            {
                using var paint = new SKPaint { Shader = shader, Style = SKPaintStyle.Fill, IsAntialias = true };
                canvas.DrawPath(path, paint);
            }
        }
        if (fabric?.Texture is { } texture)
        {
            using var shader = Texture(texture, frame, height, tiles);
            if (shader != null)
            {
                var strength = (float)Math.Clamp(texture.Strength ?? TextureFill.DefaultStrength, 0, 1);
                using var paint = new SKPaint { Shader = shader, Style = SKPaintStyle.Fill, IsAntialias = true, BlendMode = SKBlendMode.Multiply, Color = SKColors.White.WithAlpha((byte)(strength * 255)) };
                canvas.DrawPath(path, paint);
            }
        }
    }

    private static SKPicture Record(Action<SKCanvas> draw)
    {
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(Unit);
        draw(canvas);
        return recorder.EndRecording();
    }

    /// <summary>One repeat of a generated pattern in the unit square, over its ground.</summary>
    private static void DrawPattern(SKCanvas canvas, PatternFill pattern, SKColor ground)
    {
        var first = pattern.Colors.Count > 0 ? FigureGeometry.ToSk(pattern.Colors[0]) : Contrast(ground);
        var second = pattern.Colors.Count > 1 ? FigureGeometry.ToSk(pattern.Colors[1]) : first;
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        using (var background = new SKPaint { Color = ground })
            canvas.DrawRect(Unit, background);
        var weight = (float)Math.Clamp(pattern.Weight ?? DefaultWeight(pattern.Kind), 0.02, 0.95);
        switch (pattern.Kind)
        {
            case PatternKind.Stripes:
            case PatternKind.Pinstripes:
                fill.Color = first;
                canvas.DrawRect(new SKRect(0, 0, 1, weight), fill);
                break;
            case PatternKind.Checks:
                // Gingham: a half-tone band each way, darker where they cross.
                fill.Color = first.WithAlpha(150);
                canvas.DrawRect(new SKRect(0, 0, weight, 1), fill);
                canvas.DrawRect(new SKRect(0, 0, 1, weight), fill);
                break;
            case PatternKind.Plaid:
                fill.Color = first.WithAlpha(140);
                canvas.DrawRect(new SKRect(0, 0, weight, 1), fill);
                canvas.DrawRect(new SKRect(0, 0, 1, weight), fill);
                fill.Color = second.WithAlpha(210);
                var line = weight * 0.18f;
                canvas.DrawRect(new SKRect(0.62f, 0, 0.62f + line, 1), fill);
                canvas.DrawRect(new SKRect(0, 0.62f, 1, 0.62f + line), fill);
                break;
            case PatternKind.Dots:
                // Polka dots, half-dropped so they don't line up in rows.
                fill.Color = first;
                var r = weight * 0.35f;
                canvas.DrawCircle(0.25f, 0.25f, r, fill);
                canvas.DrawCircle(0.75f, 0.75f, r, fill);
                break;
            case PatternKind.Chevron:
                fill.Color = first;
                using (var builder = new SKPathBuilder())
                {
                    builder.MoveTo(0, 0);
                    builder.LineTo(0.5f, 0.5f);
                    builder.LineTo(1, 0);
                    builder.LineTo(1, weight);
                    builder.LineTo(0.5f, 0.5f + weight);
                    builder.LineTo(0, weight);
                    builder.Close();
                    using var zigzag = builder.Detach();
                    canvas.DrawPath(zigzag, fill);
                }
                break;
        }
    }

    public static double DefaultWeight(PatternKind kind) => kind switch
    {
        PatternKind.Pinstripes => 0.07,
        PatternKind.Checks => 0.5,
        PatternKind.Plaid => 0.36,
        PatternKind.Dots => 0.5,
        PatternKind.Chevron => 0.25,
        _ => 0.5
    };

    /// <summary>One repeat of a generated texture's weave, light grey on white - what gets multiplied in.</summary>
    private static void DrawTexture(SKCanvas canvas, TextureKind kind)
    {
        using (var white = new SKPaint { Color = SKColors.White })
            canvas.DrawRect(Unit, white);
        using var line = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, Color = new SKColor(0, 0, 0, 55) };
        switch (kind)
        {
            case TextureKind.Denim:
                // A diagonal twill: fine lines at 45 degrees.
                line.StrokeWidth = 0.18f;
                for (var i = -1f; i <= 1; i += 0.5f)
                    canvas.DrawLine(i, 0, i + 1, 1, line);
                break;
            case TextureKind.Knit:
                // Rows of little Vs.
                line.StrokeWidth = 0.1f;
                line.Color = new SKColor(0, 0, 0, 45);
                canvas.DrawLine(0.1f, 0.2f, 0.5f, 0.8f, line);
                canvas.DrawLine(0.5f, 0.8f, 0.9f, 0.2f, line);
                break;
            case TextureKind.Corduroy:
                // Ribs: a soft shade across each one.
                using (var rib = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0), [new SKColor(0, 0, 0, 70), new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 70)], SKShaderTileMode.Clamp) })
                    canvas.DrawRect(Unit, rib);
                break;
            case TextureKind.Canvas:
                line.StrokeWidth = 0.12f;
                line.Color = new SKColor(0, 0, 0, 35);
                canvas.DrawLine(0, 0.5f, 1, 0.5f, line);
                canvas.DrawLine(0.5f, 0, 0.5f, 1, line);
                break;
            // Wool, leather and felt are the noise alone.
        }
    }

    /// <summary>A colour matrix taking noise to a light grey (1 - depth .. 1), opaque, for multiplying.</summary>
    private static float[] GreyMatrix(float depth)
    {
        // Luminance of the noise, squeezed into [1 - depth, 1].
        var (r, g, b) = (0.3f * depth, 0.59f * depth, 0.11f * depth);
        var offset = 1 - depth;
        return
        [
            r, g, b, 0, offset,
            r, g, b, 0, offset,
            r, g, b, 0, offset,
            0, 0, 0, 0, 1,
        ];
    }

    /// <summary>A default pattern colour that reads on <paramref name="ground"/>: white on dark, dark on light.</summary>
    private static SKColor Contrast(SKColor ground) =>
        0.3 * ground.Red + 0.59 * ground.Green + 0.11 * ground.Blue < 140 ? new SKColor(0xF4, 0xF4, 0xF4) : new SKColor(0x2B, 0x2B, 0x2B);
}
