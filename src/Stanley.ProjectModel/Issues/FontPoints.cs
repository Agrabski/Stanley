namespace Stanley.ProjectModel.Issues;

/// <summary>
/// Type sizes are in points, as in Word - 10pt dialogue, 72pt headlines - the one place
/// Stanley isn't metric: it's how everyone already sizes type. Everything else on the page
/// (and the renderer, which draws a size in millimetres) stays in millimetres.
/// </summary>
public static class FontPoints
{
    /// <summary>One point, 1/72 inch, in millimetres.</summary>
    public const double MmPerPoint = 25.4 / 72;

    public static double ToMm(double points) => points * MmPerPoint;

    public static double FromMm(double millimetres) => millimetres / MmPerPoint;
}
