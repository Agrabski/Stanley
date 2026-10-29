using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Characters;

/// <summary>
/// Where a module's unit box lands in figure space: its centre at <paramref name="Center"/>,
/// <paramref name="Width"/> by <paramref name="Height"/> figure units, turned
/// <paramref name="Rotation"/> degrees clockwise about the centre. Flips aren't part of it -
/// they're applied to the shape itself first (<see cref="ModuleSockets.Flipped"/>), so its
/// sockets are derived the right way up - which makes this a plain affine map: an outline's
/// anchors and handles map through it exactly.
/// </summary>
public readonly record struct ModuleFrame(Point2D Center, double Width, double Height, double Rotation = 0)
{
    /// <summary>A unit-box point in figure space.</summary>
    public Point2D ToFigure(Point2D unit) => Offset(Center, Turn(new Point2D((unit.X - 0.5) * Width, (unit.Y - 0.5) * Height)));

    /// <summary>A figure-space point back in the unit box (the unit box's centre for a box with no size).</summary>
    public Point2D ToUnit(Point2D figure)
    {
        var back = Turn(new Point2D(figure.X - Center.X, figure.Y - Center.Y), -Rotation);
        return new Point2D(Width == 0 ? 0.5 : back.X / Width + 0.5, Height == 0 ? 0.5 : back.Y / Height + 0.5);
    }

    /// <summary>An outline's anchors, handles and all, in figure space.</summary>
    public IReadOnlyList<ShapeAnchor> Place(IReadOnlyList<ShapeAnchor> anchors)
    {
        var frame = this;
        return anchors.Select(a => a with { Point = frame.ToFigure(a.Point), InHandle = frame.ToFigure(a.InHandle), OutHandle = frame.ToFigure(a.OutHandle) }).ToList();
    }

    /// <summary>An axis-aligned ellipse of the unit box (centre and radii there) in figure space: turned with the frame.</summary>
    public BodyEllipse Place((Point2D Center, double RadiusX, double RadiusY) ellipse) =>
        new(ToFigure(ellipse.Center), ellipse.RadiusX * Math.Abs(Width), ellipse.RadiusY * Math.Abs(Height), Rotation);

    /// <summary>The same frame moved so that unit-box point <paramref name="unit"/> lands on <paramref name="figure"/>.</summary>
    public ModuleFrame Pinned(Point2D unit, Point2D figure)
    {
        var at = ToFigure(unit);
        return this with { Center = new Point2D(Center.X + figure.X - at.X, Center.Y + figure.Y - at.Y) };
    }

    /// <summary>The same frame moved by <paramref name="by"/>.</summary>
    public ModuleFrame Moved(Point2D by) => this with { Center = Offset(Center, by) };

    /// <summary>
    /// The frame for <paramref name="module"/>'s size and turn with its unit point
    /// <paramref name="socket"/> on <paramref name="at"/>, before the module's own nudge
    /// (<see cref="ShapeModule.Offset"/>).
    /// </summary>
    public static ModuleFrame Of(ShapeModule module, Point2D socket, Point2D at) =>
        new ModuleFrame(default, module.Width, module.Height, module.Rotation ?? 0).Pinned(socket, at);

    private Point2D Turn(Point2D v) => Turn(v, Rotation);

    private static Point2D Turn(Point2D v, double degrees)
    {
        if (degrees == 0)
            return v;
        var r = degrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(r), Math.Sin(r));
        return new Point2D(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    private static Point2D Offset(Point2D p, Point2D by) => new(p.X + by.X, p.Y + by.Y);
}
