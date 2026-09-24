using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;

namespace Stanley.ProjectModel.Issues;

/// <summary>
/// Where a character stands on the page and how big it is drawn.
/// </summary>
/// <param name="Ground">Page millimetres: the point on the floor between the character's feet. Resizing scales about it, so the character stays standing where it was; it may lie below the panel for a waist-up shot.</param>
/// <param name="UnitHeightMm">How tall, in page millimetres, a character of <see cref="BodyShape.Height"/> 1.0 is drawn - the panel's "camera distance". Characters sharing a value keep their true relative heights; by default every character in a panel shares one.</param>
/// <param name="Mirrored">Flipped left-right about <see cref="Ground"/> - two characters facing each other.</param>
public sealed record CharacterPlacement(Point2D Ground, double UnitHeightMm, bool Mirrored)
{
    /// <summary>A point in figure space (see <see cref="BodyFigure"/>) on the page.</summary>
    public Point2D ToPage(Point2D figure) =>
        new(Ground.X + (Mirrored ? -figure.X : figure.X) * UnitHeightMm, Ground.Y + figure.Y * UnitHeightMm);

    /// <summary>A page point in figure space - the inverse of <see cref="ToPage(Point2D)"/>.</summary>
    public Point2D ToFigure(Point2D page)
    {
        var unit = UnitHeightMm <= 0 ? 1 : UnitHeightMm;
        var x = (page.X - Ground.X) / unit;
        return new Point2D(Mirrored ? -x : x, (page.Y - Ground.Y) / unit);
    }

    /// <summary>A figure-space rectangle (e.g. <see cref="BodyFigure.Extent"/>) on the page.</summary>
    public Rect2D ToPage(Rect2D figure)
    {
        var a = ToPage(new Point2D(figure.Left, figure.Top));
        var b = ToPage(new Point2D(figure.Right, figure.Bottom));
        return Rect2D.FromEdges(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }
}
