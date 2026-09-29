using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>A colour with its fabric, as a small swatch: the ribbon's colour buttons and the pattern and texture galleries.</summary>
public sealed class FabricSwatch : Control
{
    public static readonly StyledProperty<ColorValue> ColorProperty = AvaloniaProperty.Register<FabricSwatch, ColorValue>(nameof(Color));

    public static readonly StyledProperty<Fabric?> FabricProperty = AvaloniaProperty.Register<FabricSwatch, Fabric?>(nameof(Fabric));

    public static readonly StyledProperty<IReadOnlyDictionary<string, ArtFile>?> TilesProperty =
        AvaloniaProperty.Register<FabricSwatch, IReadOnlyDictionary<string, ArtFile>?>(nameof(Tiles));

    static FabricSwatch()
    {
        AffectsRender<FabricSwatch>(ColorProperty, FabricProperty, TilesProperty);
    }

    /// <summary>The tile files a tile pattern or texture draws from, by name.</summary>
    public IReadOnlyDictionary<string, ArtFile>? Tiles
    {
        get => GetValue(TilesProperty);
        set => SetValue(TilesProperty, value);
    }

    public ColorValue Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public Fabric? Fabric
    {
        get => GetValue(FabricProperty);
        set => SetValue(FabricProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < 1 || Bounds.Height < 1)
            return;
        context.Custom(new SwatchDrawOperation(new Rect(Bounds.Size), Color, Fabric, Tiles));
    }

    private sealed class SwatchDrawOperation(Rect bounds, ColorValue color, Fabric? fabric, IReadOnlyDictionary<string, ArtFile>? tiles) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public void Dispose() { }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            using var rect = new SKPathBuilder();
            rect.AddRoundRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height), 2, 2);
            using var path = rect.Detach();
            // A few repeats across the swatch, whatever the fabric's own size.
            var repeat = (float)Math.Max(bounds.Height / 2.2, 4);
            var patternHeight = repeat / (fabric?.Pattern?.Size ?? PatternFill.DefaultSize);
            // A dye (docs: modular hair) isn't a repeat: it's drawn here across the whole swatch, on top of the ground and any texture.
            var dye = fabric?.Pattern is { IsDye: true } d ? d : null;
            var shown = fabric is null ? null : fabric with
            {
                Pattern = dye is null ? fabric.Pattern : null,
                Texture = fabric.Texture is { } t ? t with { Size = repeat / 2 / patternHeight } : null
            };
            canvas.Save();
            canvas.ClipPath(path, antialias: true);
            FabricShaders.Fill(canvas, path, ToSk(color), shown, SKMatrix.Identity, patternHeight, name => tiles is not null && tiles.TryGetValue(name, out var file) ? file : null);
            if (dye is not null)
                DrawDye(canvas, (float)bounds.Width, (float)bounds.Height, dye, ToSk(color));
            canvas.Restore();
            using var border = new SKPaint { Color = new SKColor(0, 0, 0, 96), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
            canvas.DrawPath(path, border);
        }

        /// <summary>
        /// A simple, recognisable drawing of a dye over the ground colour: the ends for Tips,
        /// the roots for Roots, a fade for Ombré, uneven stripes for Streaks, and side-by-side
        /// bands of every colour for Rainbow.
        /// </summary>
        private static void DrawDye(SKCanvas canvas, float width, float height, PatternFill dye, SKColor ground)
        {
            var ink = ToSk(dye.Colors.Count > 0 ? dye.Colors[0] : ColorSlotEditor.DefaultDyeColor);
            var weight = Math.Clamp((float)(dye.Weight ?? ColorSlotEditor.DefaultDyeWeight(dye.Kind)), 0.05f, 1f);
            using var paint = new SKPaint { IsAntialias = true, Color = ink };
            switch (dye.Kind)
            {
                case PatternKind.Tips:
                    canvas.DrawRect(new SKRect(0, height * (1 - weight), width, height), paint);
                    break;
                case PatternKind.Roots:
                    canvas.DrawRect(new SKRect(0, 0, width, height * weight), paint);
                    break;
                case PatternKind.Ombre:
                    // Ground down to where the fade starts, then into the dye at the bottom.
                    using (var fade = SKShader.CreateLinearGradient(new SKPoint(0, height * Math.Min(weight, 0.95f)), new SKPoint(0, height), [ground, ink], SKShaderTileMode.Clamp))
                    {
                        paint.Shader = fade;
                        canvas.DrawRect(new SKRect(0, 0, width, height), paint);
                    }
                    break;
                case PatternKind.Streaks:
                    // Uneven on purpose: different widths at uneven spacing, whatever the swatch's size.
                    foreach (var (at, factor) in new[] { (0.12f, 0.7f), (0.34f, 1.1f), (0.5f, 0.6f), (0.74f, 1f), (0.9f, 0.5f) })
                    {
                        var stripe = Math.Max(1.5f, width * 0.3f * weight * factor);
                        canvas.DrawRect(new SKRect(width * at - stripe / 2, 0, width * at + stripe / 2, height), paint);
                    }
                    break;
                case PatternKind.Rainbow:
                    var bands = dye.Colors.Count > 1 ? dye.Colors : PatternFill.RainbowColors;
                    for (var i = 0; i < bands.Count; i++)
                    {
                        paint.Color = ToSk(bands[i]);
                        canvas.DrawRect(new SKRect(width * i / bands.Count, 0, width * (i + 1) / bands.Count, height), paint);
                    }
                    break;
            }
        }

        private static SKColor ToSk(ColorValue c) =>
            SKColor.TryParse(c.Hex is { Length: 9 } h ? "#" + h[7..] + h[1..7] : c.Hex ?? "#9a9a9a", out var parsed) ? parsed : SKColors.Gray;
    }
}
