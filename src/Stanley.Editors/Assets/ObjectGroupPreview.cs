using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Stanley.ProjectModel.Issues;
using Stanley.ProjectModel.Objects;
using Stanley.Rendering;

namespace Stanley.Editors;

/// <summary>A kept object group drawn to fit, centred - its tile in Insert › From My Assets and on File › My Assets.</summary>
public sealed class ObjectGroupPreview : Control
{
    public static readonly StyledProperty<ObjectGroup?> GroupProperty =
        AvaloniaProperty.Register<ObjectGroupPreview, ObjectGroup?>(nameof(Group));

    static ObjectGroupPreview() => AffectsRender<ObjectGroupPreview>(GroupProperty);

    public ObjectGroup? Group
    {
        get => GetValue(GroupProperty);
        set => SetValue(GroupProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Group is not { Children.Count: > 0 } group || Bounds.Width < 2 || Bounds.Height < 2)
            return;
        context.Custom(new GroupDrawOperation(new(Bounds.Size), group));
    }

    private sealed class GroupDrawOperation(Rect bounds, ObjectGroup group) : ICustomDrawOperation
    {
        private const double Inset = 4;

        public Rect Bounds => bounds;

        public void Dispose() { }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                return;
            var box = PanelElements.Bounds(new GroupElement(default, ElementLayer.Foreground, group.Children));
            if (box.Width <= 0 && box.Height <= 0)
                return;

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
            var scale = Math.Min((bounds.Width - 2 * Inset) / Math.Max(box.Width, 1), (bounds.Height - 2 * Inset) / Math.Max(box.Height, 1));
            canvas.Translate((float)((bounds.Width - box.Width * scale) / 2), (float)((bounds.Height - box.Height * scale) / 2));
            canvas.Scale((float)scale);
            canvas.Translate((float)-box.X, (float)-box.Y);
            foreach (var child in group.Children)
                ElementRenderer.Draw(canvas, child, pictures: group.ArtFiles);
            canvas.Restore();
        }
    }
}
