namespace Stanley.ProjectModel.Geometry;

/// <summary>A plain axis-aligned rectangle in whichever local space its containing entity defines.</summary>
public readonly record struct Rect2D(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;

    public static Rect2D FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);
}
