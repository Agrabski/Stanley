using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Issues;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>
/// Draws a character standing on the ground, fitted to the control - through the same
/// <see cref="CharacterRenderers"/> as the page. With a <see cref="LineUp"/>, the other
/// characters stand faded beside it at the same scale, so heights compare at a glance;
/// with <see cref="ShowGuides"/>, a ground line and height marks (percent of an average
/// adult) run behind them. Small ones are the thumbnails in the Characters pane and the
/// Insert tab's gallery; the big one is the character editor.
/// </summary>
public sealed class CharacterFigure : Control
{
    public static readonly StyledProperty<CharacterDefinition?> CharacterProperty =
        AvaloniaProperty.Register<CharacterFigure, CharacterDefinition?>(nameof(Character));

    public static readonly StyledProperty<IReadOnlyList<CharacterDefinition>?> LineUpProperty =
        AvaloniaProperty.Register<CharacterFigure, IReadOnlyList<CharacterDefinition>?>(nameof(LineUp));

    public static readonly StyledProperty<bool> ShowGuidesProperty =
        AvaloniaProperty.Register<CharacterFigure, bool>(nameof(ShowGuides));

    public static readonly StyledProperty<ViewAngle> AngleProperty =
        AvaloniaProperty.Register<CharacterFigure, ViewAngle>(nameof(Angle));

    public static readonly StyledProperty<ProjectModel.Poses.PoseData?> PoseProperty =
        AvaloniaProperty.Register<CharacterFigure, ProjectModel.Poses.PoseData?>(nameof(Pose));

    public static readonly StyledProperty<ProjectModel.Ids.StickerId?> HighlightProperty =
        AvaloniaProperty.Register<CharacterFigure, ProjectModel.Ids.StickerId?>(nameof(Highlight));

    public static readonly StyledProperty<bool> CloseupProperty =
        AvaloniaProperty.Register<CharacterFigure, bool>(nameof(Closeup));

    static CharacterFigure()
    {
        AffectsRender<CharacterFigure>(CharacterProperty, LineUpProperty, ShowGuidesProperty, AngleProperty, PoseProperty, HighlightProperty, CloseupProperty);
    }

    /// <summary>Head and shoulders only, filling the control - for hair, face and hat galleries.</summary>
    public bool Closeup
    {
        get => GetValue(CloseupProperty);
        set => SetValue(CloseupProperty, value);
    }

    /// <summary>A worn sticker to outline on the main character (the character editor's selection).</summary>
    public ProjectModel.Ids.StickerId? Highlight
    {
        get => GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    /// <summary>The worn sticker on the main character under <paramref name="point"/> (control coordinates), or null.</summary>
    public ProjectModel.Ids.StickerId? StickerAt(Point point)
    {
        if (Character is not { } character)
            return null;
        var angle = Pose?.ViewAngle ?? Angle;
        var main = Arrange(new(Bounds.Size), character, LineUp ?? [], ShowGuides, angle, Pose, Closeup).FirstOrDefault(f => !f.Faded);
        return main.Character is null ? null : CharacterRenderers.Default.StickerAt(character, main.Placement, new(point.X, point.Y), angle, Pose);
    }

    /// <summary>Where the main character stands in the control now (control coordinates are page millimetres to it), or null if nothing is drawn.</summary>
    public CharacterPlacement? MainPlacement
    {
        get
        {
            if (Character is not { } character)
                return null;
            var main = Arrange(new(Bounds.Size), character, LineUp ?? [], ShowGuides, Pose?.ViewAngle ?? Angle, Pose, Closeup).FirstOrDefault(f => !f.Faded);
            return main.Character is null ? null : main.Placement;
        }
    }

    /// <summary>The faded (line-up) character whose drawn extent contains <paramref name="point"/> (control coordinates), or null. Returns null in closeup mode.</summary>
    public CharacterDefinition? LineUpCharacterAt(Point point)
    {
        if (Character is not { } character || Closeup)
            return null;
        var angle = Pose?.ViewAngle ?? Angle;
        var figures = Arrange(new(Bounds.Size), character, LineUp ?? [], ShowGuides, angle, Pose, Closeup);
        foreach (var (figure, placement, faded, _) in figures)
        {
            if (!faded)
                continue;
            var extent = CharacterRenderers.Default.Extent(figure, angle, null);
            var pageExtent = placement.ToPage(extent);
            if (pageExtent.Left <= point.X && point.X <= pageExtent.Right &&
                pageExtent.Top <= point.Y && point.Y <= pageExtent.Bottom)
                return figure;
        }
        return null;
    }

    /// <summary>Where each figure stands in <paramref name="bounds"/>: everyone to one scale, the main character in the middle, the others alternating right and left of it.</summary>
    private static List<(CharacterDefinition Character, CharacterPlacement Placement, bool Faded, double Unit)> Arrange(Rect bounds, CharacterDefinition main,
        IReadOnlyList<CharacterDefinition> others, bool guides, ViewAngle angle, ProjectModel.Poses.PoseData? pose, bool closeup = false)
    {
        var result = new List<(CharacterDefinition, CharacterPlacement, bool, double)>();
        if (closeup)
        {
            // The head, what's worn on it, and a little of the shoulders, centred.
            var head = BodyRig.Build(main.Body, angle, main.Skeleton, pose).Regions.Head;
            var r = head.RadiusY;
            var worn = CharacterRenderers.Default.Extent(main, angle, pose);
            var top = Math.Max(worn.Top, head.Center.Y - 2.4 * r) - 0.15 * r;
            var bottom = head.Center.Y + 1.9 * r;
            var halfWidth = 1.9 * r;
            var scale = Math.Min((bounds.Height - 4) / (bottom - top), (bounds.Width - 4) / (2 * halfWidth));
            if (scale <= 0)
                return result;
            var ground = new Point2D(bounds.Width / 2 - head.Center.X * scale, 2 + (bounds.Height - 4 - (bottom - top) * scale) / 2 - top * scale);
            result.Add((main, new(ground, scale, Mirrored: false), false, scale));
            return result;
        }
        var extents = new[] { main }.Concat(others).Select(c => (Character: c, Extent: CharacterRenderers.Default.Extent(c, angle, ReferenceEquals(c, main) ? pose : null))).ToList();
        var tallest = extents.Max(e => e.Extent.Height);
        var padTop = guides ? 18.0 : 3.0;
        var padBottom = guides ? 10.0 : 3.0;
        var gap = 0.08;
        var totalWidth = extents.Sum(e => e.Extent.Width) + gap * (extents.Count - 1);
        var unit = Math.Min((bounds.Height - padTop - padBottom) / tallest, (bounds.Width - 6) / totalWidth);
        if (unit <= 0)
            return result;
        var groundY = bounds.Height - padBottom;
        var order = new List<(CharacterDefinition Character, Rect2D Extent, bool Faded)> { (main, extents[0].Extent, false) };
        var right = new List<(CharacterDefinition, Rect2D, bool)>();
        var left = new List<(CharacterDefinition, Rect2D, bool)>();
        for (var i = 1; i < extents.Count; i++)
            (i % 2 == 1 ? right : left).Add((extents[i].Character, extents[i].Extent, true));
        left.Reverse();
        var x = (bounds.Width - totalWidth * unit) / 2;
        foreach (var (character, extent, faded) in left.Concat(order).Concat(right))
        {
            result.Add((character, new(new(x - extent.Left * unit, groundY), unit, Mirrored: false), faded, unit));
            x += (extent.Width + gap) * unit;
        }
        return result;
    }

    /// <summary>The pose to show the main character in (its view wins over <see cref="Angle"/>); null stands at rest.</summary>
    public ProjectModel.Poses.PoseData? Pose
    {
        get => GetValue(PoseProperty);
        set => SetValue(PoseProperty, value);
    }

    /// <summary>Front or side view, for every figure drawn (the line-up too).</summary>
    public ViewAngle Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public CharacterDefinition? Character
    {
        get => GetValue(CharacterProperty);
        set => SetValue(CharacterProperty, value);
    }

    public IReadOnlyList<CharacterDefinition>? LineUp
    {
        get => GetValue(LineUpProperty);
        set => SetValue(LineUpProperty, value);
    }

    public bool ShowGuides
    {
        get => GetValue(ShowGuidesProperty);
        set => SetValue(ShowGuidesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Character is not { } character || Bounds.Width < 2 || Bounds.Height < 2)
            return;
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        context.Custom(new FigureDrawOperation(new(Bounds.Size), character, LineUp ?? [], ShowGuides, dark, Pose?.ViewAngle ?? Angle, Pose, Highlight, Closeup));
    }

    private sealed class FigureDrawOperation(Rect bounds, CharacterDefinition main, IReadOnlyList<CharacterDefinition> others, bool guides, bool dark, ViewAngle angle,
        ProjectModel.Poses.PoseData? pose, ProjectModel.Ids.StickerId? highlight, bool closeup)
        : ICustomDrawOperation
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
            canvas.Save();
            canvas.ClipRect(new(0, 0, (float)bounds.Width, (float)bounds.Height));

            var figures = Arrange(bounds, main, closeup ? [] : others, guides && !closeup, angle, pose, closeup);
            if (figures.Count == 0)
            {
                canvas.Restore();
                return;
            }
            var unit = figures[0].Unit;
            var groundY = figures[0].Placement.Ground.Y;
            if (guides && !closeup)
                DrawGuides(canvas, groundY, unit);

            foreach (var (character, placement, faded, _) in figures)
            {
                if (faded)
                {
                    using var alpha = new SKPaint { Color = SKColors.White.WithAlpha(90) };
                    canvas.SaveLayer(alpha);
                }
                CharacterRenderers.Default.Draw(canvas, character, placement, (float)Math.Clamp(unit * 0.004, 0.8, closeup ? 1.6 : 2), angle, faded ? null : pose);
                if (faded)
                {
                    canvas.Restore();
                    if (guides)
                        Label(canvas, character.Name, (float)placement.Ground.X, (float)(groundY - character.Body.Height * unit - 4), SKTextAlign.Center, 10, 140);
                }
                else if (highlight is { } sticker)
                {
                    using var outline = CharacterRenderers.Default.BuildStickerOutline(character, placement, sticker, angle, pose);
                    using var selection = new SKPaint { Color = new(0x1E, 0x5A, 0xA8), Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, IsAntialias = true, PathEffect = SKPathEffect.CreateDash([6f, 3f], 0) };
                    canvas.DrawPath(outline, selection);
                }
            }
            canvas.Restore();
        }

        private void DrawGuides(SKCanvas canvas, double groundY, double unit)
        {
            using var line = new SKPaint { Color = (dark ? SKColors.White : SKColors.Black).WithAlpha(40), StrokeWidth = 1, IsAntialias = true };
            using var ground = new SKPaint { Color = (dark ? SKColors.White : SKColors.Black).WithAlpha(110), StrokeWidth = 1.5f, IsAntialias = true };
            for (var h = 0.25; h * unit < groundY - 4; h += 0.25)
            {
                var y = (float)(groundY - h * unit);
                canvas.DrawLine(0, y, (float)bounds.Width, y, line);
                Label(canvas, $"{h * 100:0}%", 4, y - 3, SKTextAlign.Left, 10, 120);
            }
            canvas.DrawLine(0, (float)groundY, (float)bounds.Width, (float)groundY, ground);
        }

        private void Label(SKCanvas canvas, string text, float x, float y, SKTextAlign align, float size, byte alpha)
        {
            using var font = new SKFont(SKTypeface.Default, size);
            using var paint = new SKPaint { Color = (dark ? SKColors.White : SKColors.Black).WithAlpha(alpha), IsAntialias = true };
            canvas.DrawText(text, x, y, align, font, paint);
        }
    }
}
