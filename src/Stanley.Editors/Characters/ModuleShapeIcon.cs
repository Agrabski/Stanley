using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Stanley.ProjectModel.Characters;

namespace Stanley.Editors;

/// <summary>
/// A module's outline as a small filled, inked icon (docs/shape-characters.md §9.2): the
/// shape galleries' choices and the Shapes tab's part buttons. The outline is drawn from its
/// unit box at <see cref="Aspect"/> (width over height), centred and as big as fits; with no
/// <see cref="Shape"/>, a dashed circle with a + (a part the character hasn't got).
/// </summary>
public sealed class ModuleShapeIcon : Control
{
    public static readonly StyledProperty<ModuleShape?> ShapeProperty = AvaloniaProperty.Register<ModuleShapeIcon, ModuleShape?>(nameof(Shape));

    public static readonly StyledProperty<double> AspectProperty = AvaloniaProperty.Register<ModuleShapeIcon, double>(nameof(Aspect), 1);

    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<ModuleShapeIcon, IBrush?>(nameof(Fill));

    static ModuleShapeIcon()
    {
        AffectsRender<ModuleShapeIcon>(ShapeProperty, AspectProperty, FillProperty);
    }

    public ModuleShape? Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public double Aspect
    {
        get => GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    private static readonly IPen Ink = new Pen(Brushes.Black, 1.2, lineJoin: PenLineJoin.Round);
    private static readonly IPen Dashed = new Pen(new SolidColorBrush(Color.FromArgb(140, 128, 128, 128)), 1.2, new DashStyle([3, 2], 0));

    public override void Render(DrawingContext context)
    {
        var (width, height) = (Bounds.Width - 3, Bounds.Height - 3);
        if (width < 2 || height < 2)
            return;
        if (Shape is not { Anchors.Count: >= 3 } shape)
        {
            var r = Math.Min(width, height) / 2 - 1;
            var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
            context.DrawEllipse(null, Dashed, c, r, r);
            context.DrawLine(Dashed, new Point(c.X - r / 2, c.Y), new Point(c.X + r / 2, c.Y));
            context.DrawLine(Dashed, new Point(c.X, c.Y - r / 2), new Point(c.X, c.Y + r / 2));
            return;
        }
        var aspect = double.IsFinite(Aspect) && Aspect > 0 ? Math.Clamp(Aspect, 0.08, 12) : 1;
        var (w, h) = width / height > aspect ? (height * aspect, height) : (width, width / aspect);
        var (left, top) = ((Bounds.Width - w) / 2, (Bounds.Height - h) / 2);
        Point P(ProjectModel.Geometry.Point2D p) => new(left + p.X * w, top + p.Y * h);

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var anchors = shape.Anchors;
            g.BeginFigure(P(anchors[0].Point), true);
            for (var i = 0; i < anchors.Count; i++)
            {
                var next = anchors[(i + 1) % anchors.Count];
                g.CubicBezierTo(P(anchors[i].OutHandle), P(next.InHandle), P(next.Point));
            }
            g.EndFigure(true);
        }
        context.DrawGeometry(Fill ?? Brushes.BurlyWood, Ink, geometry);
    }
}
