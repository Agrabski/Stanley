using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editing;

/// <summary>Placing and sizing imported pictures (<see cref="PictureElement"/>) - always at their own shape, never squashed.</summary>
public static class PictureEditing
{
    public const double MinSizeMm = 3;

    /// <summary>How much of the panel a newly placed picture may fill, each way.</summary>
    public const double DefaultPanelFill = 0.8;

    /// <summary>
    /// A new picture of the given pixel (or view box) <paramref name="size"/> in the middle
    /// of <paramref name="panel"/>, as big as fits in <see cref="DefaultPanelFill"/> of it
    /// at its own shape.
    /// </summary>
    public static EditResult<PictureElement> Place(Rect2D panel, (double Width, double Height) size, string artFileName, ElementLayer layer)
    {
        if (size.Width <= 0 || size.Height <= 0)
            return EditResult<PictureElement>.Failure("That picture has no size.");
        var scale = Math.Min(panel.Width * DefaultPanelFill / size.Width, panel.Height * DefaultPanelFill / size.Height);
        var width = size.Width * scale;
        var height = size.Height * scale;
        return EditResult<PictureElement>.Success(new PictureElement(ElementId.New(), layer,
            new Rect2D(panel.MidX - width / 2, panel.MidY - height / 2, width, height), artFileName));
    }

    public static PictureElement Move(PictureElement picture, double dx, double dy) =>
        picture with { Bounds = picture.Bounds with { X = picture.Bounds.X + dx, Y = picture.Bounds.Y + dy } };

    /// <summary>
    /// Resizes towards <paramref name="newBounds"/> (what a handle drag asks for) while
    /// keeping the picture's shape: it scales by whichever side changed more, and stays
    /// pinned to the edges that didn't move - a corner drag grows away from the opposite
    /// corner, an edge drag grows both ways across it.
    /// </summary>
    public static EditResult<PictureElement> Resize(PictureElement picture, Rect2D newBounds)
    {
        var old = picture.Bounds;
        if (old.Width <= 0 || old.Height <= 0)
            return EditResult<PictureElement>.Success(picture with { Bounds = newBounds });

        var sx = newBounds.Width / old.Width;
        var sy = newBounds.Height / old.Height;
        var scale = Math.Abs(sx - 1) >= Math.Abs(sy - 1) ? sx : sy;
        var width = old.Width * scale;
        var height = old.Height * scale;
        if (width < MinSizeMm || height < MinSizeMm)
            return EditResult<PictureElement>.Failure($"A picture must be at least {MinSizeMm}x{MinSizeMm}mm.");

        var left = Pin(old.Left, old.Right, newBounds.Left, newBounds.Right, width);
        var top = Pin(old.Top, old.Bottom, newBounds.Top, newBounds.Bottom, height);
        return EditResult<PictureElement>.Success(picture with { Bounds = new Rect2D(left, top, width, height) });
    }

    /// <summary>Where the new near edge goes along one axis: after the edge that stayed put, or centred where both (or neither) did.</summary>
    private static double Pin(double oldLow, double oldHigh, double newLow, double newHigh, double size)
    {
        const double same = 1e-9;
        var lowKept = Math.Abs(newLow - oldLow) < same;
        var highKept = Math.Abs(newHigh - oldHigh) < same;
        if (lowKept && highKept)
            return (oldLow + oldHigh) / 2 - size / 2;
        if (lowKept)
            return oldLow;
        if (highKept)
            return oldHigh - size;
        return (newLow + newHigh) / 2 - size / 2;
    }
}
