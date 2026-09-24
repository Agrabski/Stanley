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

    static FabricSwatch()
    {
        AffectsRender<FabricSwatch>(ColorProperty, FabricProperty);
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
        context.Custom(new SwatchDrawOperation(new Rect(Bounds.Size), Color, Fabric));
    }

    private sealed class SwatchDrawOperation(Rect bounds, ColorValue color, Fabric? fabric) : ICustomDrawOperation
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
            var shown = fabric is null ? null : fabric with
            {
                Pattern = fabric.Pattern,
                Texture = fabric.Texture is { } t ? t with { Size = repeat / 2 / patternHeight } : null
            };
            canvas.Save();
            canvas.ClipPath(path, antialias: true);
            FabricShaders.Fill(canvas, path, ToSk(color), shown, SKMatrix.Identity, patternHeight);
            canvas.Restore();
            using var border = new SKPaint { Color = new SKColor(0, 0, 0, 96), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
            canvas.DrawPath(path, border);
        }

        private static SKColor ToSk(ColorValue c) =>
            SKColor.TryParse(c.Hex is { Length: 9 } h ? "#" + h[7..] + h[1..7] : c.Hex ?? "#9a9a9a", out var parsed) ? parsed : SKColors.Gray;
    }
}
