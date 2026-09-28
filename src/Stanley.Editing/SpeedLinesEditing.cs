using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>
/// Placing and restyling a burst of speed lines (<see cref="SpeedLinesElement"/>): pure
/// functions, one implementation for live preview and commit alike, like <see cref="ShapeEditing"/>.
/// </summary>
public static class SpeedLinesEditing
{
    /// <summary>Smallest the focus ellipse (the clear area in the middle) can be resized down to, each way.</summary>
    public const double MinFocusSizeMm = 4;

    public const int MinCount = 8;
    public const int MaxCount = 300;
    public const double MaxWidthMm = 10;

    /// <summary>How much of the panel the focus ellipse starts as, each way - small enough that most of the burst clears the panel on every side.</summary>
    public const double DefaultPanelFraction = 0.38;

    /// <summary>The style new speed lines start with: black, a middling count and thickness - restyle, thin out or thicken from there.</summary>
    public static readonly SpeedLinesStyle DefaultStyle = new(ColorValue.FromHex("#1c1c1c"));

    /// <summary>
    /// A new burst centred in <paramref name="panel"/>, its focus ellipse roughly
    /// <see cref="DefaultPanelFraction"/> of the panel's own size - behind the characters
    /// (<see cref="ElementLayer.Background"/>) by default, like scenery, but still over the
    /// panel's own background.
    /// </summary>
    public static SpeedLinesElement Place(Rect2D panel, ElementLayer layer = ElementLayer.Background) =>
        new(ElementId.New(), layer, DefaultFocus(panel), DefaultStyle);

    private static Rect2D DefaultFocus(Rect2D panel)
    {
        var width = Math.Max(panel.Width * DefaultPanelFraction, MinFocusSizeMm);
        var height = Math.Max(panel.Height * DefaultPanelFraction, MinFocusSizeMm);
        return new Rect2D(panel.MidX - width / 2, panel.MidY - height / 2, width, height);
    }

    /// <summary>Moves the focus ellipse - and so the point the lines radiate from.</summary>
    public static SpeedLinesElement Move(SpeedLinesElement speedLines, double dx, double dy) =>
        speedLines with { Focus = speedLines.Focus with { X = speedLines.Focus.X + dx, Y = speedLines.Focus.Y + dy } };

    /// <summary>Resizes the focus ellipse (a handle drag) - the lines themselves always reach past the panel, however big or small the clear area gets.</summary>
    public static EditResult<SpeedLinesElement> Resize(SpeedLinesElement speedLines, Rect2D bounds) =>
        bounds.Width < MinFocusSizeMm - 1e-9 || bounds.Height < MinFocusSizeMm - 1e-9
            ? EditResult<SpeedLinesElement>.Failure($"The clear area must be at least {MinFocusSizeMm}x{MinFocusSizeMm}mm.")
            : EditResult<SpeedLinesElement>.Success(speedLines with { Focus = bounds });

    public static EditResult<SpeedLinesElement> SetStyle(SpeedLinesElement speedLines, SpeedLinesStyle style)
    {
        if (style.Count < MinCount || style.Count > MaxCount)
            return EditResult<SpeedLinesElement>.Failure($"Speed lines must number between {MinCount} and {MaxCount}.");
        if (style.WidthMm < 0 || style.WidthMm > MaxWidthMm)
            return EditResult<SpeedLinesElement>.Failure($"A speed line can be at most {MaxWidthMm}mm thick.");
        return EditResult<SpeedLinesElement>.Success(speedLines with { Style = style with { Jitter = Math.Clamp(style.Jitter, 0, 1) } });
    }

    /// <summary>A fresh, differently-jittered burst with the same style otherwise - "Shuffle" just moves on to the next seed.</summary>
    public static SpeedLinesElement Shuffle(SpeedLinesElement speedLines) =>
        speedLines with { Style = speedLines.Style with { Seed = speedLines.Style.Seed + 1 } };
}
