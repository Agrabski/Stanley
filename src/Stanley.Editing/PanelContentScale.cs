using Stanley.ProjectModel.Geometry;

namespace Stanley.Editing;

/// <summary>
/// What resizing a panel does to everything in it (issue #58): one uniform scale and shift,
/// the same for every character, bubble, tail, drawing, picture and thought trail in the
/// panel, so the panel comes out as a bigger or smaller copy of itself. Giving each kind of
/// thing its own rule - a character's feet keeping their place in the panel, a bubble's
/// centre keeping its - pulled them apart: a character standing below the frame dived out of
/// it while the bubble above it barely moved.
/// <para>
/// The scale is the smaller of the panel's width and height ratios: as big as the picture
/// can get with everything that was in frame still in it, never squashed. A corner drag
/// scales it; narrowing a panel shrinks it to fit; widening just one way leaves it its size
/// and makes room. The picture holds on to the edges the resize didn't move - drag the
/// right edge and it stays put against the left. Along an axis where neither or both edges
/// moved, it's centred across and stands on the panel's floor, so characters keep their
/// footing (the same reason <see cref="ProjectModel.Issues.CharacterPlacement.Ground"/> is a
/// character's anchor).
/// </para>
/// <para>
/// Sizes scale with it - a character's height, a bubble's or a text's lettering - but line
/// weights don't: ink is drawn the same thickness at any size, as characters' outlines and
/// bubble borders already are.
/// </para>
/// </summary>
/// <param name="Scale">How many times bigger everything is drawn; exactly 1 when the panel only moved.</param>
/// <param name="OffsetX">Added to every X after scaling.</param>
/// <param name="OffsetY">Added to every Y after scaling.</param>
public readonly record struct PanelContentScale(double Scale, double OffsetX, double OffsetY)
{
    /// <summary>Leaves everything where it is.</summary>
    public static PanelContentScale None { get; } = new(1, 0, 0);

    /// <summary>What a panel's contents go through when it's resized from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static PanelContentScale Between(Rect2D from, Rect2D to)
    {
        if (from.Width <= 0 || from.Height <= 0)
            return None;

        var scale = Math.Min(to.Width / from.Width, to.Height / from.Height);
        // A boundary drag rebuilds the untouched sides from their edges, which can round a
        // hair off the size; that mustn't turn every bubble's 10pt into 9.999999999999998pt.
        if (Math.Abs(scale - 1) < 1e-9)
            scale = 1;
        return new PanelContentScale(
            scale,
            Offset(from.Left, from.Right, to.Left, to.Right, scale, unpinned: 0.5),
            Offset(from.Top, from.Bottom, to.Top, to.Bottom, scale, unpinned: 1));
    }

    /// <summary>
    /// Where the scaled picture goes along one axis, as the offset added after scaling: against
    /// the edge that stayed put, else <paramref name="unpinned"/> of the way along the room left
    /// over (0 the near edge, 1 the far one).
    /// </summary>
    private static double Offset(double oldLow, double oldHigh, double newLow, double newHigh, double scale, double unpinned)
    {
        const double same = 1e-6;
        var lowKept = Math.Abs(newLow - oldLow) < same;
        var highKept = Math.Abs(newHigh - oldHigh) < same;
        var along = lowKept && !highKept ? 0 : highKept && !lowKept ? 1 : unpinned;
        var room = newHigh - newLow - (oldHigh - oldLow) * scale;
        return newLow + room * along - oldLow * scale;
    }

    public Point2D Map(Point2D point) => new(OffsetX + point.X * Scale, OffsetY + point.Y * Scale);

    public Rect2D Map(Rect2D rect) => new(OffsetX + rect.X * Scale, OffsetY + rect.Y * Scale, rect.Width * Scale, rect.Height * Scale);

    public ShapeAnchor Map(ShapeAnchor anchor) =>
        anchor with { Point = Map(anchor.Point), InHandle = Map(anchor.InHandle), OutHandle = Map(anchor.OutHandle) };

    /// <summary>Every anchor and handle mapped - point by point, so a flat ring (a straight line) moves too, which <see cref="AnchorRing.Rescale"/> wouldn't do.</summary>
    public IReadOnlyList<ShapeAnchor> Map(IReadOnlyList<ShapeAnchor> anchors) => anchors.Select(Map).ToList();
}
